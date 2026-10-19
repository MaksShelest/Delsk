using System.Collections.Immutable;

namespace Simulation.Core;

/// <summary>Natural stock is not harvested inventory. Null rules retain public fixture geography.</summary>
public sealed record ResourceSiteRules(bool Public = true, long Capacity = 1000000,
    long RegenerationQuantity = 1000, int RegenerationTicks = 120,
    long PurchasePriceMinor = 10000, long LeasePriceMinor = 1000, int LeaseTicks = 1200,
    long? InitialOwner = null);
public sealed record ResourceSiteView(Cell Position, string ItemId, bool Public, long Stock, long Reserved,
    long Capacity, long RegenerationQuantity, int RegenerationTicks, long PurchasePriceMinor,
    long LeasePriceMinor, int LeaseTicks, ParticipantId? Owner, ParticipantId? Tenant, long? LeaseUntil);
internal sealed record ResourceClaim(long JobId, Cell Position, string ItemId, long Quantity);
internal sealed class ResourceSite(ResourceNodeDefinition definition)
{
    public ResourceNodeDefinition Definition { get; } = definition;
    public ResourceSiteRules Rules { get; } = definition.Rules ?? new();
    public long Stock { get; set; } = (definition.Rules ?? new()).Capacity;
    public long? Owner { get; set; } = definition.Rules?.InitialOwner;
    public long? Tenant { get; set; }
    public long? LeaseUntil { get; set; }
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<(Cell Position,string Item), ResourceSite> _resources = [];
    private readonly Dictionary<long, ResourceClaim> _resourceClaims = [];
    public ImmutableArray<ResourceSiteView> ResourceSites() => [.. _resources.Values
        .OrderBy(r => r.Definition.Position.Z).ThenBy(r => r.Definition.Position.X).Select(r => new ResourceSiteView(
            r.Definition.Position, r.Definition.ItemId, r.Rules.Public, r.Stock, ResourceReserved(r.Definition.Position,r.Definition.ItemId),
            r.Rules.Capacity, r.Rules.RegenerationQuantity, r.Rules.RegenerationTicks, r.Rules.PurchasePriceMinor,
            r.Rules.LeasePriceMinor, r.Rules.LeaseTicks, r.Owner is { } owner ? new(owner) : null,
            r.Tenant is { } tenant ? new(tenant) : null, r.LeaseUntil))];
    private long ResourceReserved(Cell cell,string item) => _resourceClaims.Values.Where(c => c.Position == cell && c.ItemId==item).Sum(c => c.Quantity);
    private bool ResourceAccess(long actor, ResourceSite site, bool machine = false) => machine
        ? site.Owner == actor
        : site.Rules.Public || site.Owner == actor || site.Tenant == actor && Tick < site.LeaseUntil;
    private ResourceSite RequireResource(long actor, Cell cell, string item, long quantity, bool machine = false)
    {
        if (!_resources.TryGetValue((cell,item), out var site) || site.Definition.ItemId != item) throw new RuleViolation("NoResourceNode");
        if (!ResourceAccess(actor, site, machine)) throw new RuleViolation("NoResourceRights");
        if (quantity <= 0 || quantity > checked(site.Stock - ResourceReserved(cell,item))) throw new RuleViolation("ResourceDepleted");
        return site;
    }
    private void AcquireResource(ActorState actor, CommandEnvelope command, bool lease)
    {
        RequireIdle(actor); RequireActionLocation(actor, command.Action);
        var candidates=_resources.Values.Where(r=>r.Definition.Position==command.Action.Target && (command.Action.ItemId=="" || r.Definition.ItemId==command.Action.ItemId)).Take(2).ToArray();
        if(candidates.Length!=1)throw new RuleViolation("NoResourceNode");
        var site=candidates[0];
        if (site is null) throw new RuleViolation("NoResourceNode");
        if (site.Rules.Public || site.Owner.HasValue || site.Tenant.HasValue) throw new RuleViolation("ResourceUnavailable");
        // Outstanding public work cannot be invalidated by a competing acquisition.
        if (ResourceReserved(command.Action.Target,site.Definition.ItemId) != 0) throw new RuleViolation("ResourceBusy");
        long until = checked(Tick + site.Rules.LeaseTicks);
        long price = lease ? site.Rules.LeasePriceMinor : site.Rules.PurchasePriceMinor;
        var tx = _economy.Begin(Tick); tx.Pay(actor.Account, _treasury, price, lease ? "ResourceLease" : "ResourcePurchase"); tx.Commit();
        if (lease) { site.Tenant = actor.Id; site.LeaseUntil = until; } else site.Owner = actor.Id;
        Emit(lease ? "ResourceLeased" : "ResourcePurchased", actor.Id, item: site.Definition.ItemId,
            money: price, reason: $"{command.Action.Target.X},{command.Action.Target.Z}", command: command.CommandId);
    }
    private void ClaimResource(long job, Cell cell, long quantity) => _resourceClaims.Add(job, new(job, cell, _actors.Values.FirstOrDefault(a=>a.Job?.Id==job)?.Job?.Item ?? _equipment.Values.Single(e=>e.Batch?.Id==job || e.PersonalJobId==job).Type.ItemId, quantity));
    private void ConsumeResource(long job)
    {
        var claim = _resourceClaims[job];
        _resources[(claim.Position,claim.ItemId)].Stock = checked(_resources[(claim.Position,claim.ItemId)].Stock - claim.Quantity);
        _resourceClaims.Remove(job);
        Emit("ResourceConsumed",entity:job,item:claim.ItemId,quantity:claim.Quantity,reason:$"{claim.Position.X},{claim.Position.Z}");
    }
    private void AdvanceResourceIntervals()
    {
        foreach (var site in _resources.Values.OrderBy(r => r.Definition.Position.Z).ThenBy(r => r.Definition.Position.X))
        {
            if (site.LeaseUntil is { } until && Tick >= until)
            {
                foreach (var actor in _actors.Values.Where(a => a.Job is { } j && IsHarvest(j) &&
                    _resourceClaims.TryGetValue(j.Id, out var claim) && claim.Position == site.Definition.Position && claim.ItemId==site.Definition.ItemId).ToArray())
                    CancelHarvest(actor, "ResourceLeaseExpired");
                Emit("ResourceLeaseExpired", site.Tenant, item: site.Definition.ItemId);
                site.Tenant = null; site.LeaseUntil = null;
            }
            if (Tick % site.Rules.RegenerationTicks == 0)
            {
                long growth=Math.Min(site.Rules.Capacity-site.Stock,site.Rules.RegenerationQuantity);
                site.Stock=checked(site.Stock+growth);
                if(growth>0)Emit("ResourceRegenerated",item:site.Definition.ItemId,quantity:growth,reason:$"{site.Definition.Position.X},{site.Definition.Position.Z}");
            }
        }
    }
    private void RestoreResources(ImmutableArray<ResourceSiteView> saved, ResourceClaim[] claims)
    {
        if (saved.IsDefault || saved.Length != _resources.Count) throw new CheckpointException("InvalidResourceSites");
        var seen = new HashSet<(Cell,string)>();
        foreach (var v in saved)
        {
            if (!seen.Add((v.Position,v.ItemId)) || !_resources.TryGetValue((v.Position,v.ItemId), out var site)) throw new CheckpointException("InvalidResourceSite");
            var initial = ResourceSites().Single(r => r.Position == v.Position && r.ItemId==v.ItemId);
            if (initial with { Stock = v.Stock, Reserved = v.Reserved, Owner = v.Owner, Tenant = v.Tenant, LeaseUntil = v.LeaseUntil } != v)
                throw new CheckpointException("InvalidResourceDefinition");
            site.Stock = v.Stock; site.Owner = v.Owner?.Value; site.Tenant = v.Tenant?.Value; site.LeaseUntil = v.LeaseUntil;
        }
        _resourceClaims.Clear(); foreach (var c in claims) _resourceClaims.Add(c.JobId, c);
        if (!ResourceSites().SequenceEqual(saved)) throw new CheckpointException("ResourceReservationMismatch");
    }
    private void ValidateResources()
    {
        foreach (var r in _resources.Values)
            if (r.Stock < 0 || r.Stock > r.Rules.Capacity || ResourceReserved(r.Definition.Position,r.Definition.ItemId) > r.Stock ||
                r.Owner.HasValue && (!_actors.ContainsKey(r.Owner.Value) || r.Rules.Public || r.Tenant.HasValue) ||
                r.Tenant.HasValue && (!_actors.ContainsKey(r.Tenant.Value) || r.Rules.Public || r.LeaseUntil <= Tick) ||
                r.Tenant.HasValue != r.LeaseUntil.HasValue)
                throw new InvalidOperationException("Resource stock or rights invariant");
        foreach (var c in _resourceClaims.Values)
        {
            var actor = _actors.Values.SingleOrDefault(a => a.Job?.Id == c.JobId && IsHarvest(a.Job));
            var machine = _equipment.Values.SingleOrDefault(e => e.Batch?.Id == c.JobId);
            if (c.Quantity <= 0 || !_resources.TryGetValue((c.Position,c.ItemId), out var site) ||
                (actor is null) == (machine is null) ||
                actor is not null && (actor.Job!.Quantity != c.Quantity || actor.Position != c.Position || !ResourceAccess(actor.Id, site, actor.Job.EquipmentId != 0)) ||
                machine is not null && (machine.Position != c.Position || machine.Characteristics.CycleQuantity != c.Quantity || !ResourceAccess(machine.Owner, site, true)))
                throw new InvalidOperationException("Orphan natural resource claim");
        }
        if (_actors.Values.Count(a => IsHarvest(a.Job)) + _equipment.Values.Count(e => e.Batch is not null) != _resourceClaims.Count)
            throw new InvalidOperationException("Missing natural resource claim");
    }
}

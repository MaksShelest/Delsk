using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void SetLogisticsInbound(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id ||
            !IsOwnLogisticsStorage(actor.Id, c.Action.Source.Value)) throw new RuleViolation("NotAvailable");
        if (c.Action.Quantity != 0) throw new RuleViolation("QuantityFromCapacity");
        if (c.Action.ItemId != "" || c.Action.ContractId != 0 || c.Action.Destination.Value != 0)
            throw new RuleViolation("ItemFromProduction");
        if (l.Trip is { } t && t.InboundSource.Value != 0 && t.InboundSource.Value != c.Action.Source.Value)
            throw new RuleViolation("LogisticsBusy");
        if (l.InboundSource != 0 && l.InboundSource != c.Action.Source.Value)
            l.Queue.RemoveAll(j => j.Key == JobKey(0, 0, l.InboundSource));
        UpsertLogisticsJob(l, 0, 0, c.Action.Source.Value, c.Action.Enabled);
        l.InboundSource = c.Action.Source.Value; l.InboundEnabled = c.Action.Enabled;
        Emit("LogisticsInboundSet", actor.Id, l.Id, reason: c.Action.Enabled ? "Waiting" : "UserPaused", command: c.CommandId);
    }
    // Keep one feasible complete kit in a shared input, including all already admitted cargo.
    private long ProductionInputRoom(long input,string item,long free)
    {
        var e=_enterprises.Values.SingleOrDefault(e=>e.Input==input);
        if(e is null || e.Recipe.Ingredients.Length==1)return free;
        var destination=_economy.Inventories[input];
        long missing=e.Recipe.Ingredients.Where(i=>i.ItemId!=item).Sum(i=>checked(Math.Max(0,i.Quantity-destination.Free(i.ItemId)-IncomingIngredient(input,i.ItemId))*_items[i.ItemId].UnitVolume));
        return Math.Max(0,free-missing);
    }
    private long IncomingIngredient(long destination,string item)=>_logistics.Values.Where(l=>l.Trip is {} t && t.ItemId==item &&
        (t.Freight?.Destination.Value??t.OwnDestination.Value)==destination).Sum(l=>Math.Max(0,l.Trip!.Quantity-(l.Trip.Freight?.Received??0)-(l.Trip.Freight?.Cancelled??0)));
    private LogisticsPlanData InboundLogisticsPlan(LogisticsState l, long origin)
    {
        var e = _enterprises[l.Enterprise]; var source = _economy.Inventories[origin];
        var destination = _economy.Inventories[e.Input];
        // Serve the least supplied component first. Ratio uses current + incoming stock and no PRNG.
        var available=e.Recipe.Ingredients.Where(i=>source.Free(i.ItemId)>0).ToArray();
        if(available.Length==0)throw new RuleViolation("NoStock");
        var ingredient=available.Aggregate((a,b)=>
            (Int128)(destination.Free(a.ItemId)+IncomingIngredient(e.Input,a.ItemId))*b.Quantity<=
            (Int128)(destination.Free(b.ItemId)+IncomingIngredient(e.Input,b.ItemId))*a.Quantity?a:b);
        string item=ingredient.ItemId;
        long volume = _items[item].UnitVolume;
        if (l.CargoCapacity < volume) throw new RuleViolation("NoCargoSpace");
        long free = checked(destination.Capacity - destination.Occupied(_items) - destination.Incoming);
        if (free < volume) throw new RuleViolation("NoDestinationSpace");
        long quantity = Math.Min(source.Free(item), Math.Min(l.CargoCapacity / volume, free / volume));
        quantity=Math.Min(quantity,ProductionInputRoom(e.Input,item,free)/volume);
        if (quantity <= 0) throw new RuleViolation("NoStock");
        var path = _map.FindPath(_economy.Inventories[l.Source].Position, source.Position);
        long leg = Math.Max(1, checked((path.Length - 1L) * Logistics.MoveTicksPerCell));
        return new(-1, item, path, quantity, leg, origin, e.Input, origin);
    }
}

using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record EquipmentMotion(ImmutableArray<Cell> Path, int Index, int SegmentTicks, long StartedTick);
public sealed record ExtractionRoute(ImmutableArray<Cell> Stops, bool Repeat, bool Enabled, int Cursor = 0);

public sealed partial class SimulationEngine
{
    private bool EquipmentFull(EquipmentState e) =>
        _economy.Inventories[e.Storage].Capacity - _economy.Inventories[e.Storage].Occupied(_items) -
        _economy.Inventories[e.Storage].Incoming < _items[e.Type.ItemId].UnitVolume * e.Characteristics.CycleQuantity;
    private bool EquipmentTransferBusy(EquipmentState e) => _transfers.Values.Any(t => t.Source == e.Storage || t.Destination == e.Storage) ||
        _logistics.Values.Any(l=>l.Trip?.Freight?.GoodsSource.Value==e.Storage);
    private void SetExtractionRoute(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor, c.Action.Equipment);
        var stops = c.Action.Stops ?? [];
        if (stops.IsDefault || stops.Length > 32 || c.Action.Enabled && stops.Length == 0) throw new RuleViolation("InvalidRoute");
        foreach (var cell in stops)
            if (!_resources.TryGetValue((cell,e.Type.ItemId), out var site) || site.Owner != actor.Id || site.Definition.ItemId != e.Type.ItemId)
                throw new RuleViolation("NoResourceRights");
        e.Route = new(stops, c.Action.Repeat, c.Action.Enabled);
        // The current leg and its goods survive edits; the next choice uses the new route.
        Emit("ExtractionRouteSet", actor.Id, e.Id, reason: c.Action.Enabled ? "Enabled" : "UserPaused", command: c.CommandId);
    }
    private void MoveEquipment(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor, c.Action.Equipment);
        if (e.Mode != EquipmentMode.Automatic || e.Motion is not null || e.Batch is not null || e.PersonalJobId != 0 || EquipmentTransferBusy(e))
            throw new RuleViolation("EquipmentBusy");
        if (EquipmentFull(e)) throw new RuleViolation("NoOutputSpace");
        BeginEquipmentMotion(e, c.Action.Target);
        Emit("EquipmentMovementStarted", actor.Id, e.Id, command: c.CommandId);
    }
    private void BeginEquipmentMotion(EquipmentState e, Cell destination)
    {
        if (!_resources.TryGetValue((destination,e.Type.ItemId), out var site) || site.Owner != e.Owner || site.Definition.ItemId != e.Type.ItemId)
            throw new RuleViolation("NoResourceRights");
        var path = _map.FindPath(e.Position, destination);
        if (path.Length == 1) return;
        if(Roads is not null)CancelTrafficRoute(e.Id);
        e.Motion = new([.. path], 0, 0, Tick); e.Reason = "MovingEquipment";
    }
    private void ConsiderExtractionRoute(EquipmentState e)
    {
        if (e.Route is not { Enabled: true } route || route.Stops.IsDefaultOrEmpty || e.Motion is not null ||
            e.Batch is not null || e.PersonalJobId != 0 || EquipmentTransferBusy(e) || EquipmentFull(e)) return;
        for (int reviewed = 0; reviewed < route.Stops.Length; reviewed++)
        {
            int cursor = route.Cursor;
            if (cursor == route.Stops.Length)
            {
                if (!route.Repeat) { e.Route = route with { Enabled = false }; e.Reason = "RouteCompleted"; return; }
                cursor = 0;
            }
            var stop = route.Stops[cursor];
            route = route with { Cursor = cursor + 1 }; e.Route = route;
            if (!_resources.TryGetValue((stop,e.Type.ItemId), out var site) || site.Owner != e.Owner || site.Definition.ItemId != e.Type.ItemId)
            { e.Reason = "NoResourceRights"; continue; }
            if (site.Stock - ResourceReserved(stop,e.Type.ItemId) < e.Characteristics.CycleQuantity) { e.Reason = "ResourceDepleted"; continue; }
            try { BeginEquipmentMotion(e, stop); return; }
            catch (RuleViolation ex) { e.Reason = ex.ReasonCode; }
        }
        // A repeat route waits until the next tick; never spins through empty stops forever.
    }
    private void AdvanceEquipmentMovements()
    {
        foreach (var e in _equipment.Values.OrderBy(e => e.Id))
        {
            if (e.Motion is not { } motion || motion.StartedTick >= Tick) continue;
            if (e.Mode != EquipmentMode.Automatic) { e.Reason = "UserPaused"; continue; }
            if (e.Group.Headcount < e.Characteristics.MinimumCrew) { e.Reason = "BelowMinimumCrew"; continue; }
            if(Roads is not null){AdvanceRoadEquipment(e,motion);continue;}
            try
            {
                long wage = checked(e.Group.Headcount * e.Group.Wage);
                var tx = _economy.Begin(Tick); if (wage > 0) tx.Pay(_actors[e.Owner].Account, _wages, wage, "EquipmentTravelWages"); tx.Commit();
                if (wage > 0) Emit("EquipmentTravelWagesPaid", e.Owner, e.Id, money: wage);
                int segment = motion.SegmentTicks + 1, index = motion.Index;
                if (segment == _config.MoveTicksPerCell) { ++index; segment = 0; }
                e.Position = motion.Path[index]; _economy.Inventories[e.Storage].Position = e.Position;
                e.MovedAtTick = Tick;
                e.Motion = index == motion.Path.Length - 1 ? null : motion with { Index = index, SegmentTicks = segment };
                e.Reason = e.Motion is null ? "Ready" : "MovingEquipment";
                if (e.Motion is null) Emit("EquipmentMovementArrived", e.Owner, e.Id);
            }
            catch (RuleViolation ex) { e.Reason = ex.ReasonCode; }
        }
    }
    private void ValidateExtractionRoutes()
    {
        foreach (var e in _equipment.Values)
        {
            if (e.Route is { } r && (r.Stops.IsDefault || r.Stops.Length > 32 || r.Cursor < 0 || r.Cursor > r.Stops.Length ||
                r.Stops.Any(c => !_resources.TryGetValue((c,e.Type.ItemId), out var s) || s.Owner != e.Owner || s.Definition.ItemId != e.Type.ItemId)))
                throw new InvalidOperationException("Invalid extraction route");
            if (e.Motion is { } m && (e.Batch is not null || e.PersonalJobId != 0 || m.Path.IsDefaultOrEmpty ||
                m.Index < 0 || m.Index >= m.Path.Length - 1 || (Roads is null && m.Path[m.Index] != e.Position) || m.Path.Any(c => !_map.Walkable(c)) ||
                m.SegmentTicks < 0 || m.SegmentTicks >= _config.MoveTicksPerCell || m.StartedTick < 1 || m.StartedTick > Tick ||
                m.Path.Zip(m.Path.Skip(1)).Any(p => Math.Abs(p.First.X-p.Second.X)+Math.Abs(p.First.Z-p.Second.Z)!=1) ||
                !_resources.TryGetValue((m.Path[^1],e.Type.ItemId), out var target) || target.Owner != e.Owner || target.Definition.ItemId != e.Type.ItemId))
                throw new InvalidOperationException("Invalid equipment movement");
            if (e.MovedAtTick < 0 || e.MovedAtTick > Tick) throw new InvalidOperationException("Invalid equipment interval");
        }
    }
}

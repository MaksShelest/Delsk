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
    private LogisticsPlanData InboundLogisticsPlan(LogisticsState l, long origin)
    {
        var e = _enterprises[l.Enterprise]; var source = _economy.Inventories[origin];
        var destination = _economy.Inventories[e.Input]; string item = e.Recipe.InputItem;
        long volume = _items[item].UnitVolume;
        if (l.CargoCapacity < volume) throw new RuleViolation("NoCargoSpace");
        long free = checked(destination.Capacity - destination.Occupied(_items) - destination.Incoming);
        if (free < volume) throw new RuleViolation("NoDestinationSpace");
        long quantity = Math.Min(source.Free(item), Math.Min(l.CargoCapacity / volume, free / volume));
        if (quantity <= 0) throw new RuleViolation("NoStock");
        var path = _map.FindPath(_economy.Inventories[l.Source].Position, source.Position);
        long leg = Math.Max(1, checked((path.Length - 1L) * Logistics.MoveTicksPerCell));
        return new(-1, item, path, quantity, leg, origin, e.Input, origin);
    }
    private void AcceptInboundArrival(LogisticsState l, LogisticsTripView t)
    {
        if (t.State == "Arrived")
        {
            var tx = _economy.Begin(Tick); tx.ConsumeReserved(t.InboundSource.Value, t.StockHold, t.Quantity);
            tx.AddStock(l.Cargo, t.ItemId, t.Quantity); tx.ReserveStock(l.Cargo, t.CargoHold, t.ItemId, t.Quantity);
            tx.ReserveCapacity(t.InboundSource.Value, t.ReturnHold, t.ItemId, t.Quantity); tx.Commit();
            l.Trip = t with { State = "ReturningInput", Path = t.Path.Reverse().ToImmutableArray(), PathIndex = 0, SegmentTicks = 0 };
            l.Reason = "ReturningInput";
            Emit("LogisticsInputLoaded", l.Owner, t.Id, t.ItemId, t.Quantity);
        }
        else if (t.State == "Finishing")
        {
            var tx = _economy.Begin(Tick); tx.ConsumeReserved(l.Cargo, t.CargoHold, t.Quantity);
            tx.ReceiveReservedCapacity(t.OwnDestination.Value, t.DestinationHold, t.ItemId, t.Quantity);
            tx.ReleaseCapacity(t.InboundSource.Value, t.ReturnHold); tx.Commit();
            Emit("LogisticsInputReceived", l.Owner, t.Id, t.ItemId, t.Quantity);
        }
    }
}

using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, LogisticsState> _logistics = [];
    private LogisticsSettings Logistics => _config.Logistics ?? new();
    private void BuyLogistics(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        if (!_enterprises.TryGetValue(c.Action.Enterprise.Value, out var enterprise) || enterprise.Definition.OwnerId != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (actor.Position != enterprise.Definition.Position) throw new RuleViolation("NotAtLocation");
        if (_logistics.Values.Any(l => l.Enterprise == enterprise.Definition.Id)) throw new RuleViolation("LogisticsAlreadyOwned");
        long id = _nextId, cargo = checked(id + 1), next = checked(id + 2);
        var system = new LogisticsState(id, actor.Id, enterprise.Definition.Id, enterprise.Output, cargo, Logistics);
        var tx = _economy.Begin(Tick); tx.Pay(actor.Account, _treasury, Logistics.PurchasePriceMinor, "LogisticsPurchase");
        tx.CreateInventory(cargo, actor.Id, actor.Position, Logistics.CargoCapacity); tx.Commit();
        _nextId = next; _logistics.Add(id, system);
        Emit("LogisticsPurchased", actor.Id, id, money: Logistics.PurchasePriceMinor, command: c.CommandId);
    }
    private void SetLogisticsContract(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (c.Action.Quantity != 0) throw new RuleViolation("QuantityFromContract");
        if (!_deliveryContracts.Any(d => d.Id == c.Action.ContractId && d.Supplier.Value == actor.Id && (!c.Action.Enabled || d.ClosedTick is null)))
            throw new RuleViolation("NotAvailable");
        if (l.Trip is not null && l.Contract != c.Action.ContractId) throw new RuleViolation("LogisticsBusy");
        l.Contract = c.Action.ContractId; l.Enabled = c.Action.Enabled;
        l.Reason = l.Enabled ? "Waiting" : "UserPaused";
        Emit("LogisticsContractSet", actor.Id, l.Id, reason: l.Reason, command: c.CommandId);
    }
    private (int Index, Cell[] Path, long Quantity, long LegTicks) LogisticsPlan(LogisticsState l)
    {
        if (!l.Enabled) throw new RuleViolation("UserPaused");
        int index = _deliveryContracts.FindIndex(d => d.Id == l.Contract && d.Supplier.Value == l.Owner && d.ClosedTick is null);
        if (index < 0) throw new RuleViolation("NoActiveContract");
        var d = _deliveryContracts[index]; var source = _economy.Inventories[l.Source];
        long quantity = Math.Min(source.Free(d.ItemId), Math.Min(d.UnprovidedQuantity, Logistics.CargoCapacity / _items[d.ItemId].UnitVolume));
        if (quantity <= 0) throw new RuleViolation(d.UnprovidedQuantity == 0 ? "NoUnassignedQuota" : "NoStock");
        Cell[] path = _map.FindPath(source.Position, d.Position);
        long leg = Math.Max(1, checked((path.Length - 1L) * Logistics.MoveTicksPerCell));
        if (checked(Tick + 1 + leg) > d.DeadlineTick) throw new RuleViolation("ArrivalAfterDeadline");
        return (index, path, quantity, leg);
    }
    private string LogisticsVacancyReason(LogisticsState l)
    {
        if (l.Trip is not null) return "Ready"; // Payroll already funded, even when free cash is zero.
        if ((l.Group.PendingWage ?? l.Group.Wage) <= 0 || l.Group.AttractionWage <= 0) return "NoPaidOffer";
        try { LogisticsPlan(l); return "Ready"; } catch (RuleViolation ex) { return ex.ReasonCode; }
    }
    private long NextLogisticsCost(LogisticsState l)
    {
        if (l.Trip is not null) return 0;
        var plan = LogisticsPlan(l);
        return checked(l.Group.Capacity * (l.Group.PendingWage ?? l.Group.Wage) * checked(2 * plan.LegTicks));
    }
    private void AdvanceLogisticsWork(LogisticsState l)
    {
        if (l.Trip is { } trip)
        {
            if (Tick <= trip.LoadTick) return;
            long wages = checked(l.Group.Headcount * trip.CommittedWageMinor);
            long paid = checked(trip.WagesPaidMinor + wages); var tx = _economy.Begin(Tick);
            if (wages > 0) tx.Pay(_actors[l.Owner].Account, _wages, wages, "LogisticsWages", trip.WageHold);
            if (trip.State == "Finishing" && paid < trip.WageBudgetMinor) tx.ReleaseMoney(_actors[l.Owner].Account, trip.WageHold);
            tx.Commit();
            if (wages > 0) Emit("LogisticsWagesPaid", l.Owner, trip.Id, money: wages);
            if (trip.State == "Finishing")
            {
                l.Trip = null; ++l.Completed; l.Reason = "Ready";
                Emit("LogisticsTripCompleted", l.Owner, trip.Id, trip.ItemId, trip.Quantity, paid);
            }
            else l.Trip = trip with { WagesPaidMinor = paid };
            return;
        }
        string reason = LogisticsVacancyReason(l);
        if (reason != "Ready") { l.Reason = reason; return; }
        if (l.Group.Headcount < Logistics.MinimumCrew) { l.Reason = "NoStaff"; return; }
        try
        {
            var plan = LogisticsPlan(l); var d = _deliveryContracts[plan.Index]; long id = _nextId, next = checked(id + 1);
            long load = checked(Tick + 1), arrival = checked(load + plan.LegTicks), end = checked(arrival + plan.LegTicks);
            long budget = checked(l.Group.Capacity * l.Group.Wage * checked(2 * plan.LegTicks));
            var preparedTrip = new LogisticsTripView(id, d.Id, d.ItemId, plan.Quantity, "Loading", Tick, load, arrival, end,
                plan.Path[0], [.. plan.Path], 0, 0, l.Group.Wage, budget, 0,
                $"logistics-stock:{id}", $"logistics-return:{id}", $"logistics-wage:{id}");
            var updated = d with { PreparedQuantity = checked(d.PreparedQuantity + plan.Quantity) };
            var tx = _economy.Begin(Tick); tx.ReserveStock(l.Source, preparedTrip.StockHold, d.ItemId, plan.Quantity);
            tx.HoldMoney(_actors[l.Owner].Account, preparedTrip.WageHold, budget); tx.Commit();
            _nextId = next; _deliveryContracts[plan.Index] = updated; l.Trip = preparedTrip; l.Reason = "Loading";
            Emit("LogisticsTripPrepared", l.Owner, id, d.ItemId, plan.Quantity, budget);
        }
        catch (RuleViolation ex) { l.Reason = ex.ReasonCode; }
    }
    private void AdvanceLogisticsMovements()
    {
        foreach (var l in _logistics.Values.OrderBy(l => l.Id))
        {
            if (l.Trip is not { } t) continue;
            if (t.State == "Loading")
            {
                int index = _deliveryContracts.FindIndex(d => d.Id == t.ContractId); var d = _deliveryContracts[index];
                var next = d with { PreparedQuantity = checked(d.PreparedQuantity - t.Quantity), InTransitQuantity = checked(d.InTransitQuantity + t.Quantity) };
                var tx = _economy.Begin(Tick); tx.ConsumeReserved(l.Source, t.StockHold, t.Quantity);
                tx.AddStock(l.Cargo, t.ItemId, t.Quantity); tx.ReserveStock(l.Cargo, t.CargoHold, t.ItemId, t.Quantity); tx.ReserveCapacity(l.Source, t.ReturnHold, t.ItemId, t.Quantity); tx.Commit();
                _deliveryContracts[index] = next; l.Trip = t with { State = "Outbound" }; l.Reason = "Outbound";
                Emit("LogisticsCargoLoaded", l.Owner, t.Id, t.ItemId, t.Quantity); continue;
            }
            if (t.State is not ("Outbound" or "ReturningEmpty" or "ReturningCargo")) continue;
            int indexPath = t.PathIndex, segment = checked(t.SegmentTicks + 1);
            if (t.Path.Length == 1) segment = 0;
            else if (segment == Logistics.MoveTicksPerCell) { ++indexPath; segment = 0; }
            Cell position = t.Path[indexPath]; long arrival = t.State == "Outbound" ? t.ArrivalTick : t.EndTick;
            string state = Tick == arrival ? (t.State == "Outbound" ? "Arrived" : "Finishing") : t.State;
            l.Trip = t with { Position = position, PathIndex = indexPath, SegmentTicks = segment, State = state };
            _economy.Inventories[l.Cargo].Position = position;
        }
    }
    private void AcceptLogisticsArrivals()
    {
        foreach (var l in _logistics.Values.OrderBy(l => l.Id))
        {
            if (l.Trip is not { } t) continue;
            if (t.State == "Arrived")
            {
                int index = _deliveryContracts.FindIndex(d => d.Id == t.ContractId); var d = _deliveryContracts[index];
                bool accepted = d.ClosedTick is null && Tick <= d.DeadlineTick;
                if (accepted) ReceiveDelivery(index, l.Cargo, t.Quantity, true, l);
                l.Trip = t with { State = accepted ? "ReturningEmpty" : "ReturningCargo",
                    Path = t.Path.Reverse().ToImmutableArray(), PathIndex = 0, SegmentTicks = 0 };
                l.Reason = l.Trip.State;
                Emit(accepted ? "LogisticsDeliveryAccepted" : "LogisticsDeliveryRejected", l.Owner, t.Id, t.ItemId, t.Quantity);
            }
            else if (t.State == "Finishing" && _economy.Inventories[l.Cargo].Count(t.ItemId) > 0)
            {
                var tx = _economy.Begin(Tick); tx.ConsumeReserved(l.Cargo, t.CargoHold, t.Quantity);
                tx.ReceiveReservedCapacity(l.Source, t.ReturnHold, t.ItemId, t.Quantity); tx.Commit();
                Emit("LogisticsCargoReturned", l.Owner, t.Id, t.ItemId, t.Quantity);
            }
        }
    }
    private void ReleasePreparedLogistics(long contract)
    {
        foreach (var l in _logistics.Values.Where(l => l.Trip?.ContractId == contract && l.Trip.State == "Loading"))
        {
            var t = l.Trip!; var tx = _economy.Begin(Tick); tx.ReleaseStock(l.Source, t.StockHold);
            tx.ReleaseMoney(_actors[l.Owner].Account, t.WageHold); tx.Commit(); l.Trip = null;
        }
    }
    private LogisticsView[] LogisticsViews() => _logistics.Values.OrderBy(l => l.Id).Select(l => l.View()).ToArray();
    private SavedLogistics[] SaveLogistics() => _logistics.Values.OrderBy(l => l.Id).Select(l => new SavedLogistics(l.View(), l.Group.View())).ToArray();
    private void RestoreLogistics(SavedLogistics[] saved)
    {
        _logistics.Clear();
        foreach (var s in saved)
        {
            var v = s.View; var l = new LogisticsState(v.Id.Value, v.OwnerId.Value, v.EnterpriseId.Value,
                v.SourceInventory.Value, v.CargoInventory.Value, Logistics)
                { Enabled = v.Enabled, Contract = v.ContractId, Reason = v.ReasonCode, Completed = v.CompletedTrips, Trip = v.Trip };
            LoadGroup(l.Group, s.Group); _logistics.Add(l.Id, l);
        }
    }
}

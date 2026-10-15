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
        if (l.Trip is { InboundSource.Value: 0 } && (l.Contract != c.Action.ContractId || l.Destination != 0)) throw new RuleViolation("LogisticsBusy");
        l.Contract = c.Action.ContractId; l.Destination = 0; l.Enabled = c.Action.Enabled;
        l.Reason = l.Enabled ? "Waiting" : "UserPaused";
        Emit("LogisticsContractSet", actor.Id, l.Id, reason: l.Reason, command: c.CommandId);
    }
    private void UpgradeLogistics(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        RequireIdle(actor);
        var enterprise = _enterprises[l.Enterprise];
        if (actor.Position != enterprise.Definition.Position) throw new RuleViolation("NotAtLocation");
        if (l.Enabled || l.InboundEnabled || l.Trip is not null) throw new RuleViolation("LogisticsNotSafe");
        var upgrade = Logistics.Upgrade;
        if (l.Level != 1 || upgrade is null) throw new RuleViolation("MaxLogisticsLevel");
        long maximumTicks = Math.Max(2L, 2L * (_config.MapWidth * _config.MapHeight - 1) * Logistics.MoveTicksPerCell);
        _ = checked(upgrade.CrewCapacity * l.Group.Wage * maximumTicks);
        if (l.Group.PendingWage is { } pending) _ = checked(upgrade.CrewCapacity * pending * maximumTicks);
        var materials = upgrade.Materials.ToArray();
        var consumed = PrepareMaterialFlows(materials);
        var tx = _economy.Begin(Tick);
        foreach (var material in materials)
        {
            long fromInput = Math.Min(material.Quantity, _economy.Inventories[enterprise.Input].Free(material.ItemId));
            if (fromInput > 0) tx.RemoveFreeStock(enterprise.Input, material.ItemId, fromInput);
            long remainder = checked(material.Quantity - fromInput);
            if (remainder > 0) tx.RemoveFreeStock(enterprise.Output, material.ItemId, remainder);
        }
        tx.Pay(actor.Account, _treasury, upgrade.PriceMinor, "LogisticsUpgrade");
        tx.SetCapacity(l.Cargo, upgrade.CargoCapacity); tx.Commit();
        l.Level = 2; l.Group.Capacity = upgrade.CrewCapacity;
        foreach (var pair in consumed) _flows[pair.Key].ConstructionInput = pair.Value;
        Emit("LogisticsUpgraded", actor.Id, l.Id, money: upgrade.PriceMinor, command: c.CommandId);
        EmitMaterialConsumption(actor.Id, l.Id, materials, c.CommandId);
    }
    private bool IsOwnLogisticsStorage(long owner, long inventory) => _buildings.Values.Any(b =>
        b.Owner == owner && b.Input == inventory && b.Type.Kind is BuildingKind.Warehouse or BuildingKind.Shop);
    private void SetLogisticsStorage(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id ||
            !IsOwnLogisticsStorage(actor.Id, c.Action.Destination.Value)) throw new RuleViolation("NotAvailable");
        if (c.Action.Quantity != 0) throw new RuleViolation("QuantityFromCapacity");
        if (c.Action.ItemId != "" || c.Action.ContractId != 0) throw new RuleViolation("ItemFromProduction");
        if (l.Trip is { InboundSource.Value: 0 } && (l.Contract != 0 || l.Destination != c.Action.Destination.Value))
            throw new RuleViolation("LogisticsBusy");
        l.Contract = 0; l.Destination = c.Action.Destination.Value; l.Enabled = c.Action.Enabled;
        l.Reason = l.Enabled ? "Waiting" : "UserPaused";
        Emit("LogisticsStorageSet", actor.Id, l.Id, reason: l.Reason, command: c.CommandId);
    }
    private record LogisticsPlanData(int Index, string ItemId, Cell[] Path, long Quantity, long LegTicks,
        long InboundSource = 0, long OwnDestination = 0, long GoodsSource = 0);
    private LogisticsPlanData OutputLogisticsPlan(LogisticsState l)
    {
        if (!l.Enabled) throw new RuleViolation("UserPaused");
        var source = _economy.Inventories[l.Source];
        if (l.Destination != 0)
        {
            var destination = _economy.Inventories[l.Destination];
            string item = _enterprises[l.Enterprise].Recipe.OutputItem;
            long volume = _items[item].UnitVolume;
            if (l.CargoCapacity < volume) throw new RuleViolation("NoCargoSpace");
            long free = checked(destination.Capacity - destination.Occupied(_items) - destination.Incoming);
            if (free < volume) throw new RuleViolation("NoDestinationSpace");
            long q = Math.Min(source.Free(item), Math.Min(l.CargoCapacity / volume, free / volume));
            if (q <= 0) throw new RuleViolation("NoStock");
            var ownPath = _map.FindPath(source.Position, destination.Position);
            return new(-1, item, ownPath, q, Math.Max(1, checked((ownPath.Length - 1L) * Logistics.MoveTicksPerCell)));
        }
        int index = _deliveryContracts.FindIndex(d => d.Id == l.Contract && d.Supplier.Value == l.Owner && d.ClosedTick is null);
        if (index < 0) throw new RuleViolation("NoActiveContract");
        var d = _deliveryContracts[index];
        long quantity = Math.Min(source.Free(d.ItemId), Math.Min(d.UnprovidedQuantity, l.CargoCapacity / _items[d.ItemId].UnitVolume));
        if (quantity <= 0) throw new RuleViolation(d.UnprovidedQuantity == 0 ? "NoUnassignedQuota" : "NoStock");
        Cell[] path = _map.FindPath(source.Position, d.Position);
        long leg = Math.Max(1, checked((path.Length - 1L) * Logistics.MoveTicksPerCell));
        if (checked(Tick + 1 + leg) > d.DeadlineTick) throw new RuleViolation("ArrivalAfterDeadline");
        return new(index, d.ItemId, path, quantity, leg);
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
        if (l.Group.Headcount < l.MinimumCrew) { l.Reason = "NoStaff"; return; }
        try
        {
            var plan = LogisticsPlan(l); long id = _nextId, next = checked(id + 1);
            var d = plan.Index >= 0 ? _deliveryContracts[plan.Index] : null;
            long load = checked(Tick + 1), arrival = checked(load + plan.LegTicks), end = checked(arrival + plan.LegTicks);
            long budget = checked(l.Group.Capacity * l.Group.Wage * checked(2 * plan.LegTicks));
            long? deferredPay = l.Group.PayEffectiveTick is { } pay && pay <= end ? checked(end + 1) : null;
            long? deferredAttraction = deferredPay.HasValue ? Math.Max(l.Group.AttractionEffectiveTick!.Value, deferredPay.Value) : null;
            var preparedTrip = new LogisticsTripView(id, d?.Id ?? 0, plan.ItemId, plan.Quantity, "Loading", Tick, load, arrival, end,
                plan.Path[0], [.. plan.Path], 0, 0, l.Group.Wage, budget, 0,
                $"logistics-stock:{id}", $"logistics-return:{id}", $"logistics-wage:{id}", new(plan.OwnDestination), new(plan.InboundSource));
            var updated = d is null ? null : d with { PreparedQuantity = checked(d.PreparedQuantity + plan.Quantity) };
            var tx = _economy.Begin(Tick); tx.ReserveStock(plan.GoodsSource, preparedTrip.StockHold, plan.ItemId, plan.Quantity);
            if (plan.OwnDestination != 0) tx.ReserveCapacity(plan.OwnDestination, preparedTrip.DestinationHold, plan.ItemId, plan.Quantity);
            tx.HoldMoney(_actors[l.Owner].Account, preparedTrip.WageHold, budget); tx.Commit();
            _nextId = next; if (updated is not null) _deliveryContracts[plan.Index] = updated;
            // A wage command can precede preparation in this same tick (phase1 -> phase4).
            // Its previously scheduled next-tick pay must not change a committed trip.
            if (deferredPay.HasValue)
            {
                l.Group.PayEffectiveTick = deferredPay;
                l.Group.AttractionEffectiveTick = deferredAttraction;
                Emit("LogisticsWageDeferred", l.Owner, l.Id, money: l.Group.PendingWage!.Value);
            }
            l.Trip = preparedTrip; l.Reason = "Loading";
            Emit("LogisticsTripPrepared", l.Owner, id, plan.ItemId, plan.Quantity, budget);
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
                if (t.InboundSource.Value != 0)
                {
                    l.Trip = t with { State = "FetchingEmpty" }; l.Reason = "FetchingEmpty";
                    Emit("LogisticsInputDeparted", l.Owner, t.Id, t.ItemId, t.Quantity); continue;
                }
                int index = _deliveryContracts.FindIndex(d => d.Id == t.ContractId);
                var d = index >= 0 ? _deliveryContracts[index] : null;
                var next = d is null ? null : d with { PreparedQuantity = checked(d.PreparedQuantity - t.Quantity), InTransitQuantity = checked(d.InTransitQuantity + t.Quantity) };
                var tx = _economy.Begin(Tick); tx.ConsumeReserved(l.Source, t.StockHold, t.Quantity);
                tx.AddStock(l.Cargo, t.ItemId, t.Quantity); tx.ReserveStock(l.Cargo, t.CargoHold, t.ItemId, t.Quantity); tx.ReserveCapacity(l.Source, t.ReturnHold, t.ItemId, t.Quantity); tx.Commit();
                if (next is not null) _deliveryContracts[index] = next;
                l.Trip = t with { State = "Outbound" }; l.Reason = "Outbound";
                Emit("LogisticsCargoLoaded", l.Owner, t.Id, t.ItemId, t.Quantity); continue;
            }
            if (t.State is not ("Outbound" or "ReturningEmpty" or "ReturningCargo" or "FetchingEmpty" or "ReturningInput")) continue;
            int indexPath = t.PathIndex, segment = checked(t.SegmentTicks + 1);
            if (t.Path.Length == 1) segment = 0;
            else if (segment == Logistics.MoveTicksPerCell) { ++indexPath; segment = 0; }
            bool firstLeg = t.State is "Outbound" or "FetchingEmpty";
            Cell position = t.Path[indexPath]; long arrival = firstLeg ? t.ArrivalTick : t.EndTick;
            string state = Tick == arrival ? (firstLeg ? "Arrived" : "Finishing") : t.State;
            l.Trip = t with { Position = position, PathIndex = indexPath, SegmentTicks = segment, State = state };
            _economy.Inventories[l.Cargo].Position = position;
        }
    }
    private void AcceptLogisticsArrivals()
    {
        foreach (var l in _logistics.Values.OrderBy(l => l.Id))
        {
            if (l.Trip is not { } t) continue;
            if (t.InboundSource.Value != 0)
            {
                AcceptInboundArrival(l, t); continue;
            }
            if (t.State == "Arrived")
            {
                bool accepted;
                if (t.OwnDestination.Value != 0)
                {
                    var tx = _economy.Begin(Tick); tx.ConsumeReserved(l.Cargo, t.CargoHold, t.Quantity);
                    tx.ReceiveReservedCapacity(t.OwnDestination.Value, t.DestinationHold, t.ItemId, t.Quantity);
                    tx.ReleaseCapacity(l.Source, t.ReturnHold); tx.Commit(); accepted = true;
                    Emit("LogisticsStorageReceived", l.Owner, t.Id, t.ItemId, t.Quantity);
                }
                else
                {
                    int index = _deliveryContracts.FindIndex(d => d.Id == t.ContractId); var d = _deliveryContracts[index];
                    accepted = d.ClosedTick is null && Tick <= d.DeadlineTick;
                    if (accepted) ReceiveDelivery(index, l.Cargo, t.Quantity, true, l);
                }
                l.Trip = t with { State = accepted ? "ReturningEmpty" : "ReturningCargo",
                    Path = t.Path.Reverse().ToImmutableArray(), PathIndex = 0, SegmentTicks = 0 };
                l.Reason = l.Trip.State;
                if (t.OwnDestination.Value == 0)
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
                v.SourceInventory.Value, v.CargoInventory.Value, Logistics, v.Level)
                { Enabled = v.Enabled, Contract = v.ContractId, Destination = v.OwnDestination.Value,
                    InboundSource = v.InboundSource.Value, InboundEnabled = v.InboundEnabled,
                    Reason = v.ReasonCode, Completed = v.CompletedTrips, Trip = v.Trip };
            LoadGroup(l.Group, s.Group); _logistics.Add(l.Id, l);
        }
    }
}

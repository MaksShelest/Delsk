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
        RequireActionLocation(actor, c.Action);
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
        ReplacePrimaryOutput(l, c.Action.ContractId, 0, c.Action.Enabled);
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
        RequireActionLocation(actor, c.Action);
        if (l.Enabled || l.InboundEnabled || l.Trip is not null) throw new RuleViolation("LogisticsNotSafe");
        var upgrade = Logistics.Upgrade;
        if (l.Level != 1 || upgrade is null) throw new RuleViolation("MaxLogisticsLevel");
        long maximumTicks = MaximumFreightTicks(_config,upgrade.CargoCapacity,2);
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
    private EnterpriseState? OwnProductionDestination(LogisticsState l, long inventory) => _enterprises.Values.SingleOrDefault(e =>
        e.Definition.OwnerId == l.Owner && e.Definition.Id != l.Enterprise && e.Input == inventory);
    private bool IsOwnLogisticsDestination(LogisticsState l, long inventory) =>
        IsOwnLogisticsStorage(l.Owner, inventory) || OwnProductionDestination(l, inventory) is not null;
    private void SetLogisticsStorage(ActorState actor, CommandEnvelope c) => SetLogisticsOwnDestination(actor, c, production: false);
    private void SetLogisticsOwnDestination(ActorState actor, CommandEnvelope c, bool production)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        var destination = OwnProductionDestination(l, c.Action.Destination.Value);
        if (production ? destination is null : !IsOwnLogisticsStorage(actor.Id, c.Action.Destination.Value))
            throw new RuleViolation("NotAvailable");
        if (c.Action.Quantity != 0) throw new RuleViolation("QuantityFromCapacity");
        if (c.Action.ItemId != "" || c.Action.ContractId != 0) throw new RuleViolation("ItemFromProduction");
        if (l.Trip is { InboundSource.Value: 0 } && (l.Contract != 0 || l.Destination != c.Action.Destination.Value))
            throw new RuleViolation("LogisticsBusy");
        if (production && c.Action.Enabled && !destination!.Recipe.Uses(_enterprises[l.Enterprise].Recipe.OutputItem))
            throw new RuleViolation("IncompatibleRecipe");
        ReplacePrimaryOutput(l, 0, c.Action.Destination.Value, c.Action.Enabled);
        l.Contract = 0; l.Destination = c.Action.Destination.Value; l.Enabled = c.Action.Enabled;
        l.Reason = l.Enabled ? "Waiting" : "UserPaused";
        Emit(production ? "LogisticsProductionSet" : "LogisticsStorageSet", actor.Id, l.Id, reason: l.Reason, command: c.CommandId);
    }
    private record LogisticsPlanData(int Index, string ItemId, Cell[] Path, long Quantity, long LegTicks,
        long InboundSource = 0, long OwnDestination = 0, long GoodsSource = 0, string JobKey = "");
    private LogisticsPlanData OutputLogisticsPlan(LogisticsState l, long contract, long target)
    {
        var source = _economy.Inventories[l.Source];
        if (target != 0)
        {
            var destination = _economy.Inventories[target];
            string item = _enterprises[l.Enterprise].Recipe.OutputItem;
            // Recipe edits affect future trips; a prepared trip keeps its item and capacity hold.
            if (OwnProductionDestination(l, target) is { } production && !production.Recipe.Uses(item))
                throw new RuleViolation("IncompatibleRecipe");
            long volume = _items[item].UnitVolume;
            if (l.CargoCapacity < volume) throw new RuleViolation("NoCargoSpace");
            long free = checked(destination.Capacity - destination.Occupied(_items) - destination.Incoming);
            if (free < volume) throw new RuleViolation("NoDestinationSpace");
            long q = Math.Min(source.Free(item), Math.Min(l.CargoCapacity / volume, free / volume));
            if (q <= 0) throw new RuleViolation("NoStock");
            var ownPath = _map.FindPath(source.Position, destination.Position);
            return new(-1, item, ownPath, q, Math.Max(1, checked((ownPath.Length - 1L) * Logistics.MoveTicksPerCell)));
        }
        int index = _deliveryContracts.FindIndex(d => d.Id == contract && d.Supplier.Value == l.Owner && d.ClosedTick is null);
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
        return FreightCost(l,plan);
    }
    private void AdvanceLogisticsWork(LogisticsState l)
    { if(l.Trip is null) PrepareFreight(l); else PayFreightWork(l); }
    private void AdvanceLogisticsMovements()
    { foreach(var l in _logistics.Values.OrderBy(l=>l.Id)) AdvanceFreightMovement(l); }
    private void AcceptLogisticsArrivals()
    { foreach(var l in _logistics.Values.OrderBy(l=>l.Id)) AdvanceFreightHandling(l); }
    private void ReleasePreparedLogistics(long contract) => CancelPreparedFreight(contract);
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
                    Reason = v.ReasonCode, Completed = v.CompletedTrips, Trip = v.Trip, DispatchCount = v.DispatchCount };
            l.Queue.AddRange(v.Queue);
            LoadGroup(l.Group, s.Group); _logistics.Add(l.Id, l);
        }
    }
}

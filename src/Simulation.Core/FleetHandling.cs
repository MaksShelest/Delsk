namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private long VehicleKitInvestment(long vehicle)
    {
        var commands=_events.Where(e=>e.Type=="VehicleKitStarted"&&e.EntityId==vehicle).Select(e=>e.CommandId).ToHashSet(StringComparer.Ordinal);
        return checked(_events.Where(e=>e.Type=="VehicleKitStarted"&&e.EntityId==vehicle).Sum(e=>e.AmountMinor)+_events.Where(e=>e.Type=="ConstructionMaterialConsumed"&&e.CommandId is {} id&&commands.Contains(id)).Sum(e=>checked(e.Quantity*_items[e.ItemId].BasePriceMinor)));
    }
    private void PayHandlingUpgrade(ActorState actor, long inventory, HandlingUpgrade upgrade, long entity, string reason, string command)
    {
        var consumed = PrepareMaterialFlows(upgrade.Materials.ToArray());
        var tx = _economy.Begin(Tick); tx.Pay(actor.Account, _treasury, upgrade.PriceMinor, reason);
        foreach (var m in upgrade.Materials) tx.RemoveFreeStock(inventory, m.ItemId, m.Quantity);
        tx.Commit(); foreach (var pair in consumed) _flows[pair.Key].ConstructionInput = pair.Value;
        EmitMaterialConsumption(actor.Id, entity, upgrade.Materials.ToArray(), command);
    }
    private void InstallVehicleKit(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); var v = OwnVehicle(actor, c.Action.VehicleId); RequireActionLocation(actor, c.Action);
        var kit = Fleet!.Kits.SingleOrDefault(k => k.Level == c.Action.EquipmentLevel) ?? throw new RuleViolation("NotAvailable");
        if (!CompatibleVehicleKit(v,kit.Level) || kit.Throughput > Model(v.ModelId).HandlingMax || kit.Level <= v.HandlingLevel) throw new RuleViolation("IncompatibleEquipment");
        if (v.Driver != 0 || v.Installation is not null || VehicleTransferring(v.CargoInventory.Value) || VehicleLogistics(v.Id) is { } l && (l.Trip is not null || l.Enabled || l.InboundEnabled)) throw new RuleViolation("VehicleBusy");
        long inventory = c.Action.Source.Value == 0 ? actor.Inventory : c.Action.Source.Value;
        if (!_economy.Inventories.TryGetValue(inventory, out var source) || source.Owner != actor.Id || source.Position != actor.Position ||
            inventory != actor.Inventory && inventory != v.CargoInventory.Value && !_buildings.Values.Any(b => b.Owner == actor.Id && b.Input == inventory && !Demolished(b.Id))) throw new RuleViolation("NotAvailable");
        PayHandlingUpgrade(actor, inventory, kit, v.Id, "VehicleHandlingKit", c.CommandId);
        _vehicles[v.Id] = v with { Installation = new(kit.Level, Tick, checked(Tick + kit.Ticks)) };
        Emit("VehicleKitStarted", actor.Id, v.Id, quantity: kit.Level, money: kit.PriceMinor, command: c.CommandId);
    }
    private void BuildLoadingPlatform(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); RequireActionLocation(actor, c.Action);
        if (PreparingDemolition(c.Action.Building.Value)) throw new RuleViolation("PreparingDemolition");
        if (Fleet is null || !_buildings.TryGetValue(c.Action.Building.Value, out var b) || b.Owner != actor.Id || Demolished(b.Id)) throw new RuleViolation("NotAvailable");
        var p = _platforms.Values.SingleOrDefault(p => p.Building == b.Id);
        int level = c.Action.EquipmentLevel;
        if (p is null)
        {
            if (level != 1) throw new RuleViolation("PlatformRequired");
            p = new(_nextId++, b.Id, actor.Id, Fleet.WageMinor); _platforms.Add(p.Id, p);
            Emit("LoadingPlatformCreated", actor.Id, p.Id, quantity: b.Id, command: c.CommandId); return;
        }
        var upgrade = Fleet.Platforms.SingleOrDefault(u => u.Level == level) ?? throw new RuleViolation("NotAvailable");
        if (p.Installation is not null || level != p.Level + 1 || PlatformBusy(p)) throw new RuleViolation("PlatformBusy");
        PayHandlingUpgrade(actor, b.Input, upgrade, p.Id, "LoadingPlatformUpgrade", c.CommandId);
        p.Installation = new(level, Tick, checked(Tick + upgrade.Ticks));
        Emit("LoadingPlatformStarted", actor.Id, p.Id, quantity: level, money: upgrade.PriceMinor, command: c.CommandId);
    }
    private bool PlatformBusy(PlatformState p)
    {
        var b = _buildings[p.Building];
        return _transfers.Values.Any(t => t.Source == b.Input || t.Destination == b.Input || t.Source == b.Output || t.Destination == b.Output) ||
            _logistics.Values.Any(l => l.Trip is { } t && (t.Freight!.GoodsSource.Value == b.Input || t.Freight.GoodsSource.Value == b.Output || t.Freight.Destination.Value == b.Input));
    }
    private long PlatformPower(long inventory)
    {
        var p = _platforms.Values.SingleOrDefault(p => !Demolished(p.Building) && (_buildings[p.Building].Input == inventory || _buildings[p.Building].Output == inventory));
        if (p is null || p.Installation is not null || p.LastWorkedTick == Tick) return 0;
        var definition = Fleet!.Platforms.Single(u => u.Level == p.Level);
        if (definition.Staff > 0 && p.Group.Headcount < definition.Staff) return 0;
        long cost = definition.Staff == 0 ? definition.ServiceMinor : checked(p.Group.Headcount * p.Group.Wage);
        if (cost > _economy.Accounts[_actors[p.Group.Owner].Account].Available) return 0;
        if (cost > 0) { var tx = _economy.Begin(Tick); tx.Pay(_actors[p.Group.Owner].Account, definition.Staff == 0 ? _treasury : _wages, cost, definition.Staff == 0 ? "HandlingService" : "HandlingWages"); tx.Commit(); }
        p.LastWorkedTick = Tick; Emit("LoadingPlatformWorked", p.Group.Owner, p.Id, quantity: definition.Throughput, money: cost);
        return definition.Throughput;
    }
    private bool CompatibleVehicleKit(OwnedVehicle v,int level) => level==2 ? v.ModelId=="vector200" : level==1 && v.ModelId is "muravey70" or "eger70" or "trakt_d1";
    private long VehicleHandlingPower(OwnedVehicle v) => v.Installation is not null ? 0 : v.HandlingLevel == 0 ? 10 : Fleet!.Kits.Single(k => k.Level == v.HandlingLevel).Throughput;
    private long FreightHandlingPower(LogisticsState l, bool loading, bool returning, FreightState f)
    {
        if (l.VehicleId == 0) return checked(Handling.WorkerVolumePerTick * l.Level * l.Group.Headcount);
        if (l.Group.Headcount == 0) return 0;
        // This crew has one driver/operator. A separate platform is a separate numeric workplace.
        return checked(VehicleHandlingPower(_vehicles[l.VehicleId]) + PlatformPower(loading || returning ? f.GoodsSource.Value : f.Destination.Value));
    }
    private long TransferHandlingPower(TransferOperation t)
    {
        if (Fleet is null) return Handling.PersonalVolumePerTick;
        var v = _vehicles.Values.SingleOrDefault(v => v.CargoInventory.Value == t.Source || v.CargoInventory.Value == t.Destination);
        long driver = v is not null && v.Driver == t.Owner.Value ? VehicleHandlingPower(v) : 10;
        return checked(driver + PlatformPower(t.Source) + PlatformPower(t.Destination));
    }
    private bool MaterialAllowed(OwnedVehicle v, string item) => !Model(v.ModelId).MaterialsOnly || item is "gravel" or "asphalt" or "concrete";
    private bool CanTransferVehicle(ActorState actor, long inventory, string item)
    {
        if (_vehicles.Values.SingleOrDefault(v => v.CargoInventory.Value == inventory) is not { } v) return true;
        return v.Owner.Value == actor.Id && (v.Driver == 0 || v.Driver == actor.Id) && !VehicleInUse(inventory) &&
            (VehicleLogistics(v.Id) is not { } l || !l.Enabled && !l.InboundEnabled) && MaterialAllowed(v, item);
    }
    private bool TransferCarrier(ActorState actor, long inventory) => inventory == actor.Inventory ||
        _vehicles.Values.Any(v => v.Owner.Value == actor.Id && v.CargoInventory.Value == inventory);
}

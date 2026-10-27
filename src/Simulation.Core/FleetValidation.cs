namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateFleetRules(ScenarioDefinition s)
    {
        if (s.Economy?.Fleet is not { } f) return;
        void Require(bool value) { if (!value) throw new ArgumentException("Invalid fleet rules"); }
        Require(s.Layout is not null && !f.Models.IsDefaultOrEmpty && f.Models.Length == 5 && f.Models.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() == 5);
        Require(f.Models.Select(m => m.Id).Order(StringComparer.Ordinal).SequenceEqual(new[] { "eger70", "muravey70", "sputnik20", "trakt_d1", "vector200" }));
        Require(f.SupplyTicks is >=1 and <=100000 && f.MinimumStockTicks>=f.SupplyTicks && f.MinimumStockTicks<=1000000 && f.PurchaseWindowTicks is >=1 and <=100 && f.WageMinor is >=1 and <=100000);
        var dealer = s.Layout!.CivicObjects.Single(c => c.Kind == "dealership");
        Require(dealer.Parking is { Length: 4 } && dealer.Parking.All(c => !s.BlockedCells.Contains(c)));
        foreach (var m in f.Models) Require(!string.IsNullOrWhiteSpace(m.Name) && m.Speed is >=1 and <=60 && m.PriceMinor is >=1 and <=100000000 && m.CapacityVolume is >=1 and <=100000 && m.HandlingMax is >=10 and <=100 && m.MaterialsOnly==(m.Id=="trakt_d1"));
        Require(f.Kits.Select(k=>k.Level).SequenceEqual(new[]{1,2}) && f.Platforms.Select(p=>p.Level).SequenceEqual(new[]{1,2,3}));
        foreach (var u in f.Kits.Concat(f.Platforms))
        {
            Require(u.Throughput is >=1 and <=100 && u.PriceMinor>=0 && u.PriceMinor<=100000000 && u.Ticks is >=0 and <=100000 && u.Staff is >=0 and <=1 && u.ServiceMinor is >=0 and <=100000 && !u.Materials.IsDefault && u.Materials.Select(m=>m.ItemId).Distinct().Count()==u.Materials.Length);
            foreach (var m in u.Materials) Require(m.Quantity is >=1 and <=100000 && s.Items.Any(i=>i.Id==m.ItemId));
        }
    }
}

public sealed partial class SimulationEngine
{
    private void ValidateFleet()
    {
        if (Fleet is null) { if (_vehicles.Count+_dealerStock.Count+_vehicleRequests.Count+_platforms.Count!=0) throw new InvalidOperationException("Unexpected fleet"); return; }
        void Require(bool value) { if (!value) throw new InvalidOperationException("Invalid fleet state"); }
        Require(_nextVehicleSupply == checked((Tick/Fleet.SupplyTicks+1)*Fleet.SupplyTicks) && _dealerStock.Count<=6);
        var ids=new HashSet<long>(); var cargos=new HashSet<long>(); var drivers=new HashSet<long>();
        foreach (var s in _dealerStock) Require(ids.Add(s.Id) && s.Id>=1000 && s.Id<_nextId && s.DeliveredTick>=0 && s.DeliveredTick<=Tick && Fleet.Models.Any(m=>m.Id==s.ModelId) && Dealer.Parking!.Contains(s.Position));
        foreach (var v in _vehicles.Values)
        {
            Require(ids.Add(v.Id) && v.Id>=1000 && v.Id<_nextId && _actors.ContainsKey(v.Owner.Value) && cargos.Add(v.CargoInventory.Value));
            Require(_economy.Inventories.TryGetValue(v.CargoInventory.Value,out var cargo) && cargo.Owner==v.Owner.Value && cargo.Capacity==Model(v.ModelId).CapacityVolume && _map.Walkable(cargo.Position));
            Require(v.HandlingLevel is >=0 and <=2 && (v.HandlingLevel==0||CompatibleVehicleKit(v,v.HandlingLevel)&&Fleet.Kits.Single(k=>k.Level==v.HandlingLevel).Throughput<=Model(v.ModelId).HandlingMax));
            Require(cargo!.Stock.Keys.All(i=>MaterialAllowed(v,i)) && v.OffsetX is >=0 and <TravelUnitsPerCell && v.OffsetZ is >=0 and <TravelUnitsPerCell && v.FacingX is >=-1 and <=1 && v.FacingZ is >=-1 and <=1 && (v.FacingX!=0||v.FacingZ!=0));
            if (v.Driver!=0) Require(v.Driver==v.Owner.Value && drivers.Add(v.Driver) && _actors[v.Driver].Position==cargo.Position && _actors[v.Driver].OffsetX==v.OffsetX && _actors[v.Driver].OffsetZ==v.OffsetZ && _actors[v.Driver].FacingX==v.FacingX && _actors[v.Driver].FacingZ==v.FacingZ && VehicleLogistics(v.Id)?.Trip is null);
            if (v.Installation is {} installation) Require(v.Driver==0 && installation.Level>v.HandlingLevel && Fleet.Kits.Any(k=>k.Level==installation.Level && CompatibleVehicleKit(v,k.Level) && k.Throughput<=Model(v.ModelId).HandlingMax && installation.CompletionTick==installation.StartedTick+k.Ticks) && installation.StartedTick>=1 && installation.StartedTick<=Tick && installation.CompletionTick>Tick && !VehicleTransferring(cargo.Id) && VehicleLogistics(v.Id)?.Trip is null);
            var receipt=_vehiclePurchases.SingleOrDefault(p=>p.VehicleId==v.Id);
            Require(receipt is not null && receipt.Owner==v.Owner && receipt.ModelId==v.ModelId && receipt.PriceMinor==Model(v.ModelId).PriceMinor && receipt.Tick>=1 && receipt.Tick<=Tick && _events.Any(e=>e.Type=="VehiclePurchased"&&e.EntityId==v.Id&&e.ActorId==v.Owner&&e.CommandId==receipt.CommandId));
        }
        Require(_vehiclePurchases.Count==_vehicles.Count && _vehicleRequests.Select(r=>r.Actor).Distinct().Count()==_vehicleRequests.Count);
        foreach (var r in _vehicleRequests)
        {
            Require(_dealerStock.Any(s=>s.Id==r.VehicleId) && _actors.ContainsKey(r.Actor.Value) && r.CutoffTick>Tick && r.CutoffTick<=Tick+Fleet.PurchaseWindowTicks && _seenCommands.TryGetValue(r.CommandId,out var c) && c.ActorId==r.Actor && c.Action.Kind==ActionKind.BuyVehicle && c.Action.VehicleId==r.VehicleId);
            Require(_economy.Accounts[_actors[r.Actor.Value].Account].Holds.GetValueOrDefault(r.MoneyHold)==Model(_dealerStock.Single(s=>s.Id==r.VehicleId).ModelId).PriceMinor);
        }
        Require(_logistics.Values.Where(l=>l.VehicleId!=0).Select(l=>l.VehicleId).Distinct().Count()==_logistics.Values.Count(l=>l.VehicleId!=0));
        foreach (var l in _logistics.Values.Where(l=>l.VehicleId!=0)) Require(_vehicles.TryGetValue(l.VehicleId,out var v) && v.Owner.Value==l.Owner && v.CargoInventory.Value==l.Cargo && Model(v.ModelId)==l.Vehicle && !l.Vehicle!.MaterialsOnly);
        Require(_platforms.Values.Select(p=>p.Building).Distinct().Count()==_platforms.Count);
        foreach (var p in _platforms.Values)
        {
            Require(p.Id>=1000&&p.Id<_nextId && _buildings.TryGetValue(p.Building,out var b)&&b.Owner==p.Group.Owner && p.Group.Role==EmploymentRole.Handling && p.Group.Capacity==1 && p.Level is >=1 and <=3 && p.LastWorkedTick>=0&&p.LastWorkedTick<=Tick);
            if(p.Installation is {} i) Require(i.Level==p.Level+1 && i.StartedTick>=1&&i.StartedTick<=Tick&&i.CompletionTick>Tick&&i.CompletionTick==i.StartedTick+Fleet.Platforms.Single(u=>u.Level==i.Level).Ticks);
        }
    }
    private void AddExpectedFleetHolds(Dictionary<(long Account,string Hold),long> money)
    { foreach(var r in _vehicleRequests)money.Add((_actors[r.Actor.Value].Account,r.MoneyHold),Model(_dealerStock.Single(s=>s.Id==r.VehicleId).ModelId).PriceMinor); }
}

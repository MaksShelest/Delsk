using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private FleetRules? Fleet => _config.Economy?.Fleet;
    private readonly Dictionary<long, OwnedVehicle> _vehicles = [];
    private readonly List<DealerVehicle> _dealerStock = [];
    private readonly List<VehiclePurchaseRequest> _vehicleRequests = [];
    private readonly List<VehiclePurchase> _vehiclePurchases = [];
    private readonly Dictionary<long, PlatformState> _platforms = [];
    private DeterministicRandom? _fleetRandom;
    private long _nextVehicleSupply;
    private CivicSite Dealer => _config.Layout!.CivicObjects.Single(c => c.Kind == "dealership");
    private VehicleModel Model(string id) => Fleet!.Models.Single(m => m.Id == id);
    private Inventory VehicleCargo(OwnedVehicle v) => _economy.Inventories[v.CargoInventory.Value];
    private LogisticsState? VehicleLogistics(long id) => _logistics.Values.SingleOrDefault(l => l.VehicleId == id);
    private OwnedVehicle? DrivenVehicle(long actor) => _vehicles.Values.SingleOrDefault(v => v.Driver == actor);
    private bool VehicleInUse(long cargo) => _vehicles.Values.Any(v => v.CargoInventory.Value == cargo &&
        (RoadVehicleBusy(v.Id) || v.Installation is not null || VehicleLogistics(v.Id) is { Trip: not null } || v.Driver != 0 && _actors[v.Driver].Job is not null));
    private bool VehicleTransferring(long cargo) => _transfers.Values.Any(t => t.Source == cargo || t.Destination == cargo);
    private void InitializeFleet()
    {
        if (Fleet is null) return;
        _fleetRandom = new(unchecked(_config.Seed ^ 0xF1EE7123UL));
        _nextVehicleSupply = Fleet.SupplyTicks;
        foreach (string id in new[] { "sputnik20", "muravey70", "muravey70", "eger70", "vector200", "trakt_d1" })
        {
            AddDealerVehicle(id);var listing=_dealerStock[^1];
            Emit("DealerInitialStock",entity:listing.Id,item:id,reason:$"{listing.Position.X},{listing.Position.Z}");
        }
    }
    private Cell? DealerPlace(string model)
    {
        var parking = Dealer.Parking!;
        bool tracked = Model(model).MaterialsOnly;
        foreach (var cell in tracked ? parking.TakeLast(1) : parking.Take(3))
        {
            int count = _dealerStock.Count(v => v.Position == cell) + _vehicles.Values.Count(v => VehicleCargo(v).Position == cell);
            if (count < (tracked ? 1 : 2)) return cell;
        }
        return null;
    }
    private bool AddDealerVehicle(string model)
    {
        if (DealerPlace(model) is not { } cell) return false;
        _dealerStock.Add(new(_nextId++, model, cell, Tick));
        return true;
    }
    private void AdvanceFleet()
    {
        if (Fleet is null) return;
        foreach (var v in _vehicles.Values.Where(v => v.Installation?.CompletionTick <= Tick).ToArray())
        {
            _vehicles[v.Id] = v with { HandlingLevel = v.Installation!.Level, Installation = null };
            Emit("VehicleKitInstalled", v.Owner.Value, v.Id, quantity: v.Installation.Level);
        }
        foreach (var p in _platforms.Values.Where(p => p.Installation?.CompletionTick <= Tick))
        { p.Level = p.Installation!.Level; p.Installation = null; Emit("LoadingPlatformInstalled", p.Group.Owner, p.Id, quantity: p.Level); }
        if (Tick < _nextVehicleSupply) return;
        // One delivery per active interval, never a catch-up burst. Purchased cars still occupy their parking place.
        _nextVehicleSupply = checked(_nextVehicleSupply + Fleet.SupplyTicks);
        var sales = _vehiclePurchases.Where(p => p.Tick > Tick - Fleet.MinimumStockTicks).ToArray();
        var candidates = Fleet.Models.OrderBy(m => _dealerStock.Any(v => v.ModelId == m.Id) ? 1 : 0)
            .ThenByDescending(m => sales.Count(p => p.ModelId == m.Id)).ToArray();
        // Shuffle ties without exposing either the PRNG or future supply to controllers.
        var ties = Fleet.Models.Select(m => m.Id).Order(StringComparer.Ordinal).ToArray(); _fleetRandom!.Shuffle(ties);
        var ranks = ties.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        candidates = candidates.OrderBy(m => _dealerStock.Any(v => v.ModelId == m.Id) ? 1 : 0)
            .ThenByDescending(m => sales.Count(p => p.ModelId == m.Id)).ThenBy(m => ranks[m.Id]).ToArray();
        foreach (var model in candidates)
        {
            if (_dealerStock.Count < 6 && AddDealerVehicle(model.Id))
            { Emit("DealerSupply", entity: _dealerStock[^1].Id, item: model.Id, reason:$"{_dealerStock[^1].Position.X},{_dealerStock[^1].Position.Z}"); return; }
        }
        // Pending competitive windows protect an old listing until their inclusive close.
        var old = _dealerStock.Where(v => Tick - v.DeliveredTick >= Fleet.MinimumStockTicks && !_vehicleRequests.Any(r => r.VehicleId == v.Id))
            .OrderBy(v => v.DeliveredTick).ThenBy(v => v.Id).ToArray();
        foreach (var listing in old)
        {
            _dealerStock.Remove(listing);
            var replacement = candidates.FirstOrDefault(m => DealerPlace(m.Id) is not null);
            if (replacement is not null && AddDealerVehicle(replacement.Id))
            { Emit("DealerReplaced", entity: listing.Id, item: listing.ModelId); Emit("DealerSupply", entity: _dealerStock[^1].Id, item: replacement.Id, reason:$"{_dealerStock[^1].Position.X},{_dealerStock[^1].Position.Z}"); return; }
            _dealerStock.Add(listing);
        }
    }
    private void BuyVehicle(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); RequireActionLocation(actor, c.Action);
        var listing = _dealerStock.SingleOrDefault(v => v.Id == c.Action.VehicleId) ?? throw new RuleViolation("NotAvailable");
        if (_vehicleRequests.Any(r => r.Actor.Value == actor.Id)) throw new RuleViolation("VehiclePurchasePending");
        long cutoff = _vehicleRequests.FirstOrDefault(r => r.VehicleId == listing.Id)?.CutoffTick ?? checked(Tick + Fleet!.PurchaseWindowTicks);
        var request = new VehiclePurchaseRequest(listing.Id, new(actor.Id), cutoff, c.CommandId);
        var tx = _economy.Begin(Tick); tx.HoldMoney(actor.Account, request.MoneyHold, Model(listing.ModelId).PriceMinor); tx.Commit();
        _vehicleRequests.Add(request); Emit("VehiclePurchaseRequested", actor.Id, listing.Id, command: c.CommandId);
    }
    private void CloseVehiclePurchases()
    {
        if (Fleet is null) return;
        foreach (var window in _vehicleRequests.Where(r => r.CutoffTick <= Tick).GroupBy(r => r.VehicleId).OrderBy(g => g.Key).ToArray())
        {
            var listing = _dealerStock.Single(v => v.Id == window.Key); var model = Model(listing.ModelId);
            var requests = window.OrderBy(r => r.Actor.Value).ToArray(); _fleetRandom!.Shuffle(requests);
            var winner = requests[0]; long cargo = _nextId, next = checked(cargo + 1);
            var tx = _economy.Begin(Tick);
            foreach (var request in requests)
                if (request == winner) tx.Pay(_actors[request.Actor.Value].Account, _treasury, model.PriceMinor, "VehiclePurchase", request.MoneyHold);
                else tx.ReleaseMoney(_actors[request.Actor.Value].Account, request.MoneyHold);
            tx.CreateInventory(cargo, winner.Actor.Value, listing.Position, model.CapacityVolume); tx.Commit();
            _nextId = next; _vehicles.Add(listing.Id, new(listing.Id, winner.Actor, model.Id, new(cargo)));
            _vehiclePurchases.Add(new(listing.Id, winner.Actor, model.Id, Tick, model.PriceMinor, winner.CommandId));
            _dealerStock.Remove(listing);
            foreach (var r in requests)
            { _vehicleRequests.Remove(r); Emit(r == winner ? "VehiclePurchased" : "VehicleNotAllocated", r.Actor.Value, listing.Id, model.Id, money: r == winner ? model.PriceMinor : 0, command: r.CommandId); }
        }
    }
    private OwnedVehicle OwnVehicle(ActorState actor, long id) => _vehicles.TryGetValue(id, out var v) && v.Owner.Value == actor.Id ? v : throw new RuleViolation("NotAvailable");
    private void BoardVehicle(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); RequireActionLocation(actor, c.Action); var v = OwnVehicle(actor, c.Action.VehicleId);
        if (RoadVehicleBusy(v.Id)&&!ServiceBoardAllowed(actor.Id,v.Id) || HasTransfer(actor.Id) || DrivenVehicle(actor.Id) is not null || v.Driver != 0 || v.Installation is not null || VehicleTransferring(v.CargoInventory.Value) ||
            VehicleLogistics(v.Id) is { } l && (l.Trip is not null || l.Enabled || l.InboundEnabled)) throw new RuleViolation("VehicleBusy");
        _vehicles[v.Id] = v with { Driver = actor.Id };
        actor.OffsetX = v.OffsetX; actor.OffsetZ = v.OffsetZ; actor.FacingX = v.FacingX; actor.FacingZ = v.FacingZ; actor.HeadingX = actor.HeadingZ = 0;
        Emit("VehicleBoarded", actor.Id, v.Id, command: c.CommandId);
    }
    private void LeaveVehicle(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); var v = DrivenVehicle(actor.Id) ?? throw new RuleViolation("NotAvailable");
        if (RoadVehicleBusy(v.Id)&&!ServiceBoardAllowed(actor.Id,v.Id) || HasTransfer(actor.Id) || VehicleTransferring(v.CargoInventory.Value)) throw new RuleViolation("VehicleBusy");
        actor.HeadingX = actor.HeadingZ = 0; _vehicles[v.Id] = v with { Driver = 0 };
        Emit("VehicleLeft", actor.Id, v.Id, command: c.CommandId);
    }
    private void BindVehicle(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor); var v = OwnVehicle(actor, c.Action.VehicleId); RequireActionLocation(actor, c.Action);
        if (!_enterprises.TryGetValue(c.Action.Enterprise.Value, out var e) || e.Definition.OwnerId != actor.Id || Demolished(e.Definition.Id) || PreparingDemolition(e.Definition.Id)) throw new RuleViolation("NotAvailable");
        var cargo = VehicleCargo(v);
        if (Model(v.ModelId).MaterialsOnly) throw new RuleViolation("RoadVehicleOnly");
        if (RoadVehicleBusy(v.Id) || v.Driver != 0 || v.Installation is not null || VehicleLogistics(v.Id) is not null || cargo.Position != e.Definition.Position || cargo.Stock.Count != 0 || VehicleTransferring(cargo.Id)) throw new RuleViolation("VehicleBusy");
        var l = _logistics.Values.SingleOrDefault(l => l.Enterprise == e.Definition.Id);
        if (l is not null && (l.Trip is not null || l.Enabled || l.InboundEnabled || _economy.Inventories[l.Cargo].Stock.Count != 0 || _vehicles[l.VehicleId].Driver != 0)) throw new RuleViolation("LogisticsNotSafe");
        if (l is null) { l = new(_nextId++, actor.Id, e.Definition.Id, e.Output, cargo.Id, Logistics, vehicle: Model(v.ModelId), vehicleId: v.Id); _logistics.Add(l.Id, l); }
        else { l.Cargo = cargo.Id; l.Vehicle = Model(v.ModelId); l.VehicleId = v.Id; }
        Emit("VehicleAssigned", actor.Id, v.Id, quantity: l.Id, command: c.CommandId);
    }
    private void SyncDrivenVehicle(ActorState actor)
    {
        if (DrivenVehicle(actor.Id) is not { } v) return;
        VehicleCargo(v).Position = actor.Position;
        _vehicles[v.Id] = v with { OffsetX = actor.OffsetX, OffsetZ = actor.OffsetZ, FacingX = actor.FacingX, FacingZ = actor.FacingZ };
    }
    private int FleetSpeed(Cell from, Cell to, VehicleModel? model = null)
    {
        if(Roads is not null)
        {
            int a=RoadLevel(from),b=RoadLevel(to);
            int limit=a>0&&b>0?Roads.Stages.Single(s=>s.Level==Math.Min(a,b)).Speed:8;
            return model is null?a>0&&b>0?20:16:Math.Min(model.Speed,limit);
        }
        bool road = _config.Layout!.InitialCityDirtCells.Contains(from) && _config.Layout.InitialCityDirtCells.Contains(to);
        return model is null ? road ? 20 : 16 : Math.Min(model.Speed, road ? 20 : 8);
    }
    private int PersonalMoveTicks(ActorState actor, Cell from, Cell to) => Fleet is null ? _config.MoveTicksPerCell : 80 / FleetSpeed(from, to, DrivenVehicle(actor.Id) is { } v ? Model(v.ModelId) : null);
    private int FreightMoveTicks(LogisticsState l, Cell from, Cell to) => l.Vehicle is null ? Logistics.MoveTicksPerCell : Roads is null?80/FleetSpeed(from,to,l.Vehicle):(80+FleetSpeed(from,to,l.Vehicle)-1)/FleetSpeed(from,to,l.Vehicle);
    private long FreightTravelTicks(LogisticsState l, IEnumerable<Cell> path) { var cells = path.ToArray(); return cells.Zip(cells.Skip(1)).Sum(p => (long)FreightMoveTicks(l, p.First, p.Second)); }
    public FleetState? InspectFleet() => Fleet is null ? null : new(_fleetRandom!.State, _nextVehicleSupply,
        [.. _dealerStock.OrderBy(v => v.Id)], [.. _vehicles.Values.OrderBy(v => v.Id)], [.. _vehicleRequests.OrderBy(r => r.VehicleId).ThenBy(r => r.Actor.Value)],
        [.. _vehiclePurchases], [.. _platforms.Values.OrderBy(p => p.Id).Select(p => p.View())]);
    public FleetObservation? FleetOf(ParticipantId actor) => Fleet is null ? null : new(Dealer.Entry, Fleet.Models,
        [.. _dealerStock.OrderBy(v => v.Id)], [.. _vehicles.Values.Where(v => v.Owner == actor).OrderBy(v => v.Id).Select(v => new VehicleView(v, Model(v.ModelId), VehicleCargo(v).Position,
            ServiceVehicleBusy(v.Id) ? "ServiceReserved" : v.Installation is not null ? "Installing" : v.Driver != 0 ? "Personal" : VehicleLogistics(v.Id)?.Trip?.State ?? "Parked", new(VehicleLogistics(v.Id)?.Id ?? 0)))],
        [.. _vehicleRequests.Where(r => r.Actor == actor)], [.. _platforms.Values.Where(p => p.Group.Owner == actor.Value).OrderBy(p => p.Id).Select(p => p.View())], Fleet.Kits, Fleet.Platforms);
    private void RestoreFleet(FleetState? state)
    {
        _vehicles.Clear(); _dealerStock.Clear(); _vehicleRequests.Clear(); _vehiclePurchases.Clear(); _platforms.Clear();
        if (Fleet is null) { if (state is not null) throw new CheckpointException("UnexpectedFleet"); return; }
        if (state is null) throw new CheckpointException("MissingFleet");
        _fleetRandom!.RestoreState(state.RandomState); _nextVehicleSupply = state.NextSupplyTick;
        _dealerStock.AddRange(state.Stock); foreach (var v in state.Vehicles) _vehicles.Add(v.Id, v);
        _vehicleRequests.AddRange(state.Requests); _vehiclePurchases.AddRange(state.Purchases);
        foreach (var v in state.Platforms) { var p = new PlatformState(v.Id, v.Building, v.Group.OwnerId.Value, Fleet.WageMinor) { Level = v.Level, Installation = v.Installation, LastWorkedTick = v.LastWorkedTick }; LoadGroup(p.Group, v.Group); _platforms.Add(p.Id, p); }
    }
}

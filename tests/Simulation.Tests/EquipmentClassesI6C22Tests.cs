using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class EquipmentClassesI6C22Tests
{
    internal static EconomyProfile Profile() => VNextEquipmentClassesProfile.Create(MobileEquipmentI4Tests.Profile());
    internal static GameSession Session(long cash = 2_000_000)
    {
        var prepared = MobileEquipmentI4Tests.Prepared(cash: cash);
        var p = Profile();
        var d = prepared with { EconomyProfileId = p.Id, Economy = p.World.Rules, EquipmentTypes = p.World.EquipmentTypes, Items = p.World.Items,
            Participants = prepared.Participants.Select(a => a with { CashMinor = cash }).ToArray() };
        return new(new(d));
    }
    internal static OwnedVehicle BuyModel(GameSession s, string model, long actor = 1)
    {
        var fleet = s.Engine.FleetOf(new(actor))!;
        FleetE1Tests.Move(s, fleet.Dealer, actor);
        var listing = s.Engine.FleetOf(new(actor))!.Stock.Single(v => v.ModelId == model);
        FleetE1Tests.Step(s, new(ActionKind.BuyVehicle, VehicleId: listing.Id), actor: actor);
        s.AdvanceTo(s.Engine.Tick + 5);
        return s.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == listing.Id);
    }
    internal static OwnedVehicle Buy(GameSession s, string item, long actor = 1)
    {
        var fleet = s.Engine.FleetOf(new(actor))!;
        var type = fleet.EquipmentTypes.Single(t => t.Accepts(item));
        return BuyModel(s, fleet.Models.Single(m => m.EquipmentTypeId == type.Id).Id, actor);
    }
    private static void Order(GameSession s, string model, long actor = 1)
    {
        FleetE1Tests.Move(s, s.Engine.FleetOf(new(actor))!.Dealer, actor);
        FleetE1Tests.Step(s, new(ActionKind.BuyVehicle, VehicleId: 0, EquipmentTypeId: model), actor: actor);
    }
    private static Cell Site(GameSession s, string item) => s.Engine.Configuration().ResourceNodes.First(n => n.ItemId == item && !n.Rules!.Public).Position;
    private static void Drive(GameSession s, OwnedVehicle v, Cell cell, long actor = 1)
    {
        var at = s.Engine.FleetOf(new(actor))!.Vehicles.Single(x => x.Vehicle.Id == v.Id).Position;
        FleetE1Tests.Move(s, at, actor);
        FleetE1Tests.Step(s, new(ActionKind.BoardVehicle, VehicleId: v.Id), actor: actor);
        FleetE1Tests.Move(s, cell, actor);
    }
    private static long Harvest(GameSession s, OwnedVehicle v, string item, int quantity, long actor = 1)
    {
        FleetE1Tests.Step(s, new(ActionKind.StartHarvesting, Equipment: new(v.Id), ItemId: item, Quantity: quantity), actor: actor);
        s.AdvanceTo(s.Engine.Tick + 12);
        FleetE1Tests.Step(s, new(ActionKind.SetEquipmentMode, Equipment: new(v.Id), Mode: EquipmentMode.Off), actor: actor);
        return FleetE1Tests.Stock(s, v.CargoInventory, item);
    }

    [Fact]
    public void NewPricesApplyOnceAndOnlyLevelOneClassesAreSold()
    {
        var fleet = Profile().World.Rules.Fleet!;
        Assert.Equal(2, fleet.EquipmentDealerVersion);
        Assert.Equal(9, fleet.Models.Length);
        Assert.DoesNotContain(fleet.Models, m => m.EquipmentLevel > 1);
        Assert.Equal(90000, fleet.Models.Single(m => m.Id == "sputnik20").PriceMinor);
        Assert.Equal(130000, fleet.Models.Single(m => m.Id == "muravey70").PriceMinor);
        Assert.Equal(220000, fleet.Models.Single(m => m.Id == "eger70").PriceMinor);
        Assert.Equal(440000, fleet.Models.Single(m => m.Id == "vector200").PriceMinor);
        Assert.Equal(590000, fleet.Models.Single(m => m.Id == "trakt_d1").PriceMinor);
        Assert.Equal(230000, fleet.Models.Single(m => m.Id == "extractor_field_1").PriceMinor);
        Assert.Equal(15000, fleet.Models.Single(m => m.EquipmentTypeId == "extract_forest").PriceMinor);
        Assert.Equal(15000, fleet.Models.Single(m => m.EquipmentTypeId == "extract_dairy").PriceMinor);
        Assert.Equal(15000, fleet.Models.Single(m => m.EquipmentTypeId == "extract_mine").PriceMinor);
        var i4 = MobileEquipmentI4Tests.Profile().World.Rules.Fleet!;
        Assert.Equal(130000, i4.Models.Single(m => m.Id == "sputnik20").PriceMinor);
        Assert.Equal(16, i4.Models.Count(m => m.EquipmentTypeId is not null));
        Assert.Equal(32, Session().Engine.ActiveCheckpointSchemaVersion);
    }

    [Fact]
    public void ValleyShowsFourVehiclesAndRefreshesWithoutStacking()
    {
        var s = I6C2MapsTests.Session(PlayMaps.CompactValley);
        var fleet = s.Engine.InspectFleet()!;
        Assert.Equal(new[] { "extractor_field_1", "extractor_forest_1", "muravey70", "sputnik20" }, fleet.Stock.Select(v => v.ModelId).Order(StringComparer.Ordinal));
        Assert.Equal(4, fleet.Stock.Select(v => v.Position).Distinct().Count());
        Assert.Equal(2, fleet.Stock.Count(v => v.ModelId.StartsWith("extractor_", StringComparison.Ordinal)));
        Assert.NotEqual(fleet.Stock.First(v => v.ModelId == "extractor_field_1").Position, fleet.Stock.First(v => v.ModelId == "extractor_forest_1").Position);
        long next = fleet.NextSupplyTick;
        var saved = CheckpointStore.Encode(s);
        var copy = CheckpointStore.Decode(saved);
        Assert.Equal(s.Engine.ComputeStateHash(), copy.Engine.ComputeStateHash());
        s.AdvanceTo(next); copy.AdvanceTo(next);
        Assert.Equal(5, s.Engine.InspectFleet()!.Stock.Length);
        Assert.Equal(s.Engine.ComputeStateHash(), copy.Engine.ComputeStateHash());
        Assert.Equal(5, s.Engine.InspectFleet()!.Stock.Select(v => v.Position).Distinct().Count());
    }

    [Fact]
    public void FieldHarvesterKeepsItsCargoAndIdentityWhenItChangesCropAndLevel()
    {
        var s = Session();
        var grain = Site(s, "grain"); var beet = Site(s, "beet");
        var v = Buy(s, "grain"); long id = v.Id; long owner = v.Owner.Value;
        Drive(s, v, grain);
        FleetE1Tests.Step(s, new(ActionKind.PlaceEquipment, Equipment: new(id), ItemId: "grain"));
        long grown = Harvest(s, v, "grain", 2);
        Assert.True(grown >= 2);
        FleetE1Tests.Step(s, new(ActionKind.UpgradeEquipment, Equipment: new(id)));
        var machine = s.Engine.Inspect().Equipment.Single(e => e.Id.Value == id);
        Assert.Equal(2, machine.Level); Assert.Equal(owner, machine.OwnerId.Value);
        Assert.Equal(grown, FleetE1Tests.Stock(s, v.CargoInventory, "grain"));
        Assert.Equal(200, StorageC1Tests.Inventory(s, v.CargoInventory).Capacity);
        Assert.Contains(s.Engine.Ledger(), e => e.Reason == "EquipmentUpgrade");
        string second = s.Send(new(1), new(ActionKind.UpgradeEquipment, Equipment: new(id)));
        s.Step(); Assert.Equal("MaxEquipmentLevel", s.Engine.Result(second)!.ReasonCode);
        FleetE1Tests.Step(s, new(ActionKind.PackEquipment, Equipment: new(id)));
        v = s.Engine.InspectFleet()!.Vehicles.Single(x => x.Id == id);
        Drive(s, v, beet);
        FleetE1Tests.Step(s, new(ActionKind.PlaceEquipment, Equipment: new(id), ItemId: "beet"));
        long beets = Harvest(s, v, "beet", 6);
        Assert.True(beets >= 6);
        Assert.Equal(grown, FleetE1Tests.Stock(s, v.CargoInventory, "grain"));
        Assert.Equal(id, s.Engine.Inspect().Equipment.Single().Id.Value);
        s.Engine.ValidateInvariants();
    }

    [Fact]
    public void ForestryDairyAndMinerUseOneMachinePerClass()
    {
        var s = Session();
        var forest = Buy(s, "wood");
        Drive(s, forest, Site(s, "wood"));
        FleetE1Tests.Step(s, new(ActionKind.PlaceEquipment, Equipment: new(forest.Id), ItemId: "wood"));
        Assert.True(Harvest(s, forest, "wood", 2) >= 2);
        string wrong = s.Send(new(1), new(ActionKind.PlaceEquipment, Equipment: new(forest.Id), ItemId: "grain"));
        s.Step(); Assert.Equal(ResultStatus.Rejected, s.Engine.Result(wrong)!.Status);

        Order(s, "extractor_dairy_1");
        var mineWait = Session();
        var shownMine = mineWait.Engine.InspectFleet()!.Stock.First(v => v.ModelId.StartsWith("extractor_", StringComparison.Ordinal));
        var moved = BuyModel(mineWait, shownMine.ModelId);
        Drive(mineWait, moved, mineWait.Engine.Configuration().Layout!.InitialCityDirtCells[0]);
        FleetE1Tests.Step(mineWait, new(ActionKind.LeaveVehicle));
        Order(mineWait, "extractor_mine_1");
        s.AdvanceTo(s.Engine.InspectFleet()!.NextSupplyTick);
        var dairy = s.Engine.InspectFleet()!.Vehicles.Single(v => v.ModelId == "extractor_dairy_1");
        Drive(s, dairy, Site(s, "milk"));
        FleetE1Tests.Step(s, new(ActionKind.PlaceEquipment, Equipment: new(dairy.Id), ItemId: "milk"));
        Assert.True(Harvest(s, dairy, "milk", 2) >= 2);
        string foreign = s.Send(new(1), new(ActionKind.StartHarvesting, Equipment: new(dairy.Id), ItemId: "wood", Quantity: 2));
        s.Step(); Assert.Equal(ResultStatus.Rejected, s.Engine.Result(foreign)!.Status);

        mineWait.AdvanceTo(mineWait.Engine.InspectFleet()!.NextSupplyTick);
        var miner = mineWait.Engine.InspectFleet()!.Vehicles.Single(v => v.ModelId == "extractor_mine_1");
        foreach (string item in new[] { "stone", "sand", "iron_ore", "copper_ore" })
        {
            if (mineWait.Engine.Inspect().Equipment.Single(e => e.Id.Value == miner.Id).Deployed == true)
                FleetE1Tests.Step(mineWait, new(ActionKind.PackEquipment, Equipment: new(miner.Id)));
            miner = mineWait.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == miner.Id);
            Drive(mineWait, miner, Site(mineWait, item));
            FleetE1Tests.Step(mineWait, new(ActionKind.PlaceEquipment, Equipment: new(miner.Id), ItemId: item));
            Assert.True(Harvest(mineWait, miner, item, 2) >= 2, item);
        }
        Assert.Equal(4, new[] { "stone", "sand", "iron_ore", "copper_ore" }.Count(item => FleetE1Tests.Stock(mineWait, miner.CargoInventory, item) >= 2));
        mineWait.Engine.ValidateInvariants();
    }

    [Fact]
    public void OneCatalogBayGoesToOneBuyerAndAParkedExtractorStopsCarsOnly()
    {
        var s = Session();
        var sputnik = BuyModel(s, "sputnik20");
        Drive(s, sputnik, s.Engine.Configuration().Layout!.InitialCityDirtCells[3]);
        FleetE1Tests.Step(s, new(ActionKind.LeaveVehicle, VehicleId: sputnik.Id));
        Order(s, "eger70", 1); Order(s, "eger70", 2);
        long supply = s.Engine.InspectFleet()!.NextSupplyTick;
        s.AdvanceTo(supply);
        var buyers = s.Engine.InspectFleet()!.Vehicles.Where(v => v.ModelId == "eger70").ToArray();
        Assert.Single(buyers);
        Assert.Single(s.Engine.Ledger(), e => e.Reason == "VehiclePurchase" && e.AmountMinor == 220000);
        Assert.True(s.Engine.InspectFleet()!.CatalogWaits.IsDefaultOrEmpty);
        var saved = CheckpointStore.Encode(s);
        Assert.Equal(s.Engine.ComputeStateHash(), CheckpointStore.Decode(saved).Engine.ComputeStateHash());

        var road = Session();
        var machine = Buy(road, "wood");
        var site = Site(road, "wood");
        Drive(road, machine, site);
        FleetE1Tests.Step(road, new(ActionKind.PlaceEquipment, Equipment: new(machine.Id), ItemId: "wood"));
        FleetE1Tests.Move(road, new(site.X, site.Z - 1), 1);
        FleetE1Tests.Move(road, site, 2);
        Assert.Equal(site, road.Engine.Observe(new(2)).Participant.Position);
        var car = BuyModel(road, "sputnik20", 2);
        var parked = road.Engine.FleetOf(new(2))!.Vehicles.Single(x => x.Vehicle.Id == car.Id).Position;
        FleetE1Tests.Move(road, parked, 2);
        FleetE1Tests.Step(road, new(ActionKind.BoardVehicle, VehicleId: car.Id), actor: 2);
        FleetE1Tests.Step(road, new(ActionKind.MoveTo, Target: site), accepted: false, actor: 2);
        Assert.NotEqual(site, StorageC1Tests.Inventory(road, car.CargoInventory).Position);
        Assert.Equal(machine.Id, road.Engine.Inspect().Equipment.Single().Id.Value);
        road.Engine.ValidateInvariants();
    }

    [Fact]
    public void PreviousValleySaveKeepsHistoricalModelsPricesAndReplay()
    {
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(OrdinaryNewGameTests.Text("compact-valley-i6c2.json"), SimulationJson.Options)!;
        var s = new GameSession(new(d));
        Assert.Equal(1, s.Engine.Configuration().Economy!.Fleet!.EquipmentDealerVersion);
        Assert.Equal(22, s.Engine.InspectFleet()!.Stock.Length);
        Assert.Equal(130000, d.Economy!.Fleet!.Models.Single(m => m.Id == "sputnik20").PriceMinor);
        Assert.Contains(d.EquipmentTypes!, t => t.Id == "extract_wood");
        Assert.DoesNotContain(d.EquipmentTypes!, t => t.Id == "extract_field");
        Assert.Equal(31, s.Engine.ActiveCheckpointSchemaVersion);
        var copy = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(), copy.Engine.ComputeStateHash());
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class HouseholdManagementTests
{
    private static ScenarioDefinition Resource(string name) => JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text(name), SimulationJson.Options)!;
    private static CityEconomy City(GameSession s) => s.Engine.PublicEconomy();

    [Fact]
    public void EveryActionHasAnExplicitPolicyAndAHumanInputPath()
    {
        foreach (var kind in Enum.GetValues<ActionKind>())
        {
            Assert.True(Enum.IsDefined(ActionPolicy.Reach(kind)));
            if (kind != ActionKind.SetTravelHeading) Assert.Equal(kind, PlayerDesk.Check(new(kind)).Kind);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ActionPolicy.Reach((ActionKind)999));
        Assert.Equal(ActionReach.Remote, ActionPolicy.Reach(ActionKind.SetProductionEnabled));
        Assert.Equal(ActionReach.AtLocation, ActionPolicy.Reach(ActionKind.SetWage));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void ChangingWageRequiresTheSameWorkplaceForEveryActor(long actor)
    {
        var d = Fixtures.Cycle(); d = d with { Enterprises = d.Enterprises.Select(e => e with { OwnerId = actor }).ToArray() };
        var s = Fixtures.Session(d); var command = PlayerDesk.Wage(1, 105);
        string before = s.Engine.ComputeStateHash(); var access = s.Engine.AccessFor(new(actor), command);
        Assert.Equal("NotAtLocation", access.ReasonCode); Assert.Equal(new Cell(10, 0), Assert.Single(access.Locations));
        Assert.Equal(before, s.Engine.ComputeStateHash());
        Assert.Equal("NotAtLocation", LandContentFixtures.Reason(s, command, actor));
        Assert.Null(s.Engine.Observe(new(actor)).OwnEmploymentGroups.Single().PendingWageMinor);
        Fixtures.Command(s, PlayerDesk.Move(10, 0), actor); Fixtures.UntilIdle(s, actor);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, command, actor));
        Assert.Equal(105, s.Engine.Observe(new(actor)).OwnEmploymentGroups.Single().PendingWageMinor);
    }
    [Fact]
    public void ForeignAndUnknownWageTargetsHaveIdenticalPrivateErrors()
    {
        var s = Fixtures.Session();
        var foreign = s.Engine.AccessFor(new(2), PlayerDesk.Wage(1, 50));
        var absent = s.Engine.AccessFor(new(2), PlayerDesk.Wage(99999, 50));
        Assert.Equal("NotAvailable", foreign.ReasonCode); Assert.Empty(foreign.Locations);
        Assert.Equal(foreign, absent);
    }
    [Fact]
    public void ExtractionAndLogisticsWagesResolveToTheirOwnBase()
    {
        var s = Fixtures.Session(Resource("equipment-cycle"));
        Fixtures.Command(s, new(ActionKind.BuyEquipment, EquipmentTypeId: "wood-extractor", Target: new(0, 0)));
        var machine = Assert.Single(s.Engine.Observe(new(1)).OwnEquipment);
        Assert.True(s.Engine.AccessFor(new(1), PlayerDesk.Wage(machine.GroupId.Value, 30)).Allowed);
        LandContentFixtures.Move(s, new(1, 0));
        Assert.Equal("NotAtLocation", LandContentFixtures.Reason(s, PlayerDesk.Wage(machine.GroupId.Value, 30)));
        var logistics = Fixtures.Session(Resource("logistics-cycle")); LandContentFixtures.Move(logistics, new(2, 0));
        Fixtures.Command(logistics, PlayerDesk.BuyLogistics(1)); var system = Assert.Single(logistics.Engine.Observe(new(1)).OwnLogistics);
        Assert.True(logistics.Engine.AccessFor(new(1), PlayerDesk.Wage(system.GroupId.Value, 30)).Allowed);
        LandContentFixtures.Move(logistics, new(0, 0));
        Assert.Equal("NotAtLocation", LandContentFixtures.Reason(logistics, PlayerDesk.Wage(system.GroupId.Value, 30)));
    }
    [Fact]
    public void WageAverageUsesCurrentWorkersAndDoesNotSubstitutePendingOrAttraction()
    {
        var d = Fixtures.Cycle(); d = d with { Population = 6, Enterprises = [
            new(1,1,new(0,0),"sawmill",40,40,1,2,20), new(2,2,new(0,0),"sawmill",40,40,3,4,40)] };
        var s = Fixtures.Session(d);
        Assert.Equal(new WeightedWage(140, 4), City(s).Labor.EmployedRate); Assert.Equal(2, City(s).Unemployed);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, PlayerDesk.Wage(1, 100)));
        Assert.Equal(new WeightedWage(140, 4), City(s).Labor.EmployedRate);
        s.Step(); Assert.Equal(new WeightedWage(220, 4), City(s).Labor.EmployedRate);
        Assert.Empty(City(s).Labor.Offers); // Stopped facilities do not advertise available vacancies.
    }
    [Fact]
    public void NoWorkersOrVacanciesHaveAnEmptyDenominatorRatherThanAFalseZeroWage()
    {
        var d = Fixtures.Cycle(staff: 0); var s = Fixtures.Session(d);
        Assert.Equal(new WeightedWage(0, 0), City(s).Labor.EmployedRate);
        Assert.Equal(new WeightedWage(0, 0), City(s).Labor.VacancyRate); Assert.Empty(City(s).Labor.Offers);
    }
    [Fact]
    public void VacancyAverageUsesAffordableReadyPlacesAndEffectiveAttraction()
    {
        var d = Fixtures.Cycle(staff: 0); d = d with { Population = 10, Enterprises = [
            new(1,1,new(0,0),"sawmill",40,40,0,2,20), new(2,2,new(0,0),"sawmill",40,40,0,4,40)],
            Participants = d.Participants.Select(p => p with { CashMinor = 100000 }).ToArray() };
        var s = Fixtures.Session(d);
        foreach (long actor in new long[] { 1, 2 })
        {
            Fixtures.Command(s, PlayerDesk.Harvest("wood", 2), actor); Fixtures.UntilIdle(s, actor);
            var o = s.Engine.Observe(new(actor));
            Fixtures.Command(s, PlayerDesk.Transfer(o.Participant.InventoryId.Value, o.OwnEnterprises.Single().InputId.Value, "wood", 2), actor);
            Fixtures.Command(s, PlayerDesk.Work(actor, true), actor);
        }
        Assert.Equal(new WeightedWage(200, 6), City(s).Labor.VacancyRate);
        Fixtures.Command(s, PlayerDesk.Wage(1, 80)); s.Step();
        Assert.Equal(80, s.Engine.Observe(new(1)).OwnEmploymentGroups.Single().WageMinor);
        Assert.Equal(new WeightedWage(200, 6), City(s).Labor.VacancyRate); // attraction delay still active
        Assert.Equal(2, City(s).Labor.Offers.Length);
        Assert.Equal(JsonSerializer.Serialize(City(s)), JsonSerializer.Serialize(s.Engine.ObserveController(new(2)).City));
    }
    [Fact]
    public void ProductionWipAndInternalTransferDoNotDuplicateCapital()
    {
        var s = Fixtures.Session(); Fixtures.Supply(s, 2);
        long before = s.Engine.CapitalOf(new(1)).TotalMinor;
        var output = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        Fixtures.Command(s, PlayerDesk.Work(1, true));
        var capital = s.Engine.CapitalOf(new(1)); Assert.Equal(1000, capital.WorkInProgressMinor); Assert.Equal(0, capital.StockMinor);
        Assert.Equal(before - 250, capital.TotalMinor); // 2*100 wages + half the 100 energy cost
        Fixtures.Command(s, PlayerDesk.Work(1, false));
        Assert.Equal(capital, s.Engine.CapitalOf(new(1)));
    }
    [Fact]
    public void PickupEscrowRemainsBuyersMoneyAndReservedGoodsRemainSellersUntilReceipt()
    {
        var d = Resource("market-window"); var s = Fixtures.Session(d);
        LandContentFixtures.Harvest(s, "wood", 10); LandContentFixtures.Move(s, new(2, 0));
        var own = s.Engine.Observe(new(1)); var shop = own.OwnBuildings.Single();
        long beforeTransfer = City(s).CapitalMinor;
        Fixtures.Command(s, PlayerDesk.Transfer(own.Participant.InventoryId.Value, shop.InputId.Value, "wood", 10));
        Assert.Equal(beforeTransfer, City(s).CapitalMinor);
        Fixtures.Command(s, new(ActionKind.CreateSellOffer, Building: shop.Id, ItemId: "wood", Quantity: 10, UnitPriceMinor: 700));
        long offer = Assert.Single(s.Engine.Inspect().MarketOffers).Id;
        long sellerCapital = s.Engine.CapitalOf(new(1)).TotalMinor, buyerCapital = s.Engine.CapitalOf(new(2)).TotalMinor;
        Fixtures.Command(s, new(ActionKind.SubmitBuyRequest, MarketOfferId: offer, Quantity: 10, MaximumPriceMinor: 700), 2);
        s.AdvanceTo(s.Engine.Inspect().MarketOffers.Single().CutoffTick);
        Assert.Equal(sellerCapital, s.Engine.CapitalOf(new(1)).TotalMinor); Assert.Equal(buyerCapital, s.Engine.CapitalOf(new(2)).TotalMinor);
        var purchase = Assert.Single(s.Engine.Inspect().MarketAllocations);
        Fixtures.Command(s, PlayerDesk.Move(2, 0), 2); Fixtures.UntilIdle(s, 2);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.CollectPurchase, PurchaseId: purchase.Id, Quantity: 3), 2));
        Assert.Equal(sellerCapital + 600, s.Engine.CapitalOf(new(1)).TotalMinor); // 3*(700-500)
        Assert.Equal(buyerCapital - 600, s.Engine.CapitalOf(new(2)).TotalMinor);
        Assert.Equal(4900, s.Engine.Inspect().Accounts.Single(a => a.Id == purchase.EscrowAccount).BalanceMinor);
    }
    [Fact]
    public void ReferenceCapitalDoesNotFollowThePriceOfYourOwnOffer()
    {
        var s = Fixtures.Session(Resource("market-window")); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(2, 0));
        var o = s.Engine.Observe(new(1)); var shop = o.OwnBuildings.Single();
        Fixtures.Command(s, PlayerDesk.Transfer(o.Participant.InventoryId.Value, shop.InputId.Value, "wood", 2));
        long before = City(s).CapitalMinor;
        Fixtures.Command(s, new(ActionKind.CreateSellOffer, Building: shop.Id, ItemId: "wood", Quantity: 2, UnitPriceMinor: 999999));
        Assert.Equal(before, City(s).CapitalMinor);
    }
    [Fact]
    public void CityStageWaitsForConfirmationAndItsPendingChangeSurvivesLoadAndReplay()
    {
        var city = new CityProgression(3, new(1, 1, 0), new(long.MaxValue, 100, 10), new(long.MaxValue, 100, 10));
        var d = Fixtures.Cycle() with { Economy = new(City: city) }; var s = Fixtures.Session(d);
        s.AdvanceTo(2); Assert.Equal(0, City(s).Stage);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.Step(); loaded.Step(); Assert.Equal(1, City(s).Stage); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(), GameSession.Replay(d, s.CommandTape, 3).Engine.ComputeStateHash());
        var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; json["cityStage"]!["sinceTick"] = 0;
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString()));
    }
    [Fact]
    public void NormalProfileUsesTwentyAndFiveWithoutRewritingHistoricOrSavedWages()
    {
        var profile = EconomyProfileTests.Profile(); Assert.Equal(5, profile.World.Rules.PlayerWageStepMinor);
        Assert.All(profile.World.EquipmentTypes.SelectMany(t => t.Levels), l => Assert.Equal(20, l.WageMinor));
        Assert.All(profile.World.BuildingTypes.Where(t => t.Kind == BuildingKind.Production).SelectMany(t => t.Levels), l => Assert.Equal(20, l.WageMinor));
        Assert.Equal(20, profile.World.Logistics.WageMinor);
        var old = Fixtures.Session(); old.Step(); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(old));
        Assert.Equal(100, loaded.Engine.Observe(new(1)).OwnEmploymentGroups.Single().WageMinor);
    }
    [Fact]
    public void GlobalTabletSurvivesTravelWhileLocalObjectCardLosesItsContext()
    {
        var s = Fixtures.Session(Resource("equipment-cycle"));
        Fixtures.Command(s, new(ActionKind.BuyEquipment, EquipmentTypeId: "wood-extractor", Target: new(0, 0)));
        var view = PlayerDesk.Read(s.Engine, new(1)); var machine = Assert.Single(view.Equipment!);
        var local = new TabletState(); Assert.True(local.OpenObject(view, "equipment", machine.Id.Value));
        var global = new TabletState(); global.OpenPlayer(view); Assert.True(global.BlocksTime);
        LandContentFixtures.Move(s, new(1, 0)); view = PlayerDesk.Read(s.Engine, new(1));
        Assert.False(local.ValidateLocation(view, s.Engine.Configuration())); Assert.False(local.IsOpen);
        Assert.True(global.ValidateLocation(view, s.Engine.Configuration())); Assert.True(global.IsOpen);
        global.Close(); Assert.True(global.IsPaused(true));
    }
    [Fact]
    public void MoreShopSitesThanParticipantsPreserveTheSingleOwnedOrReservedQuota()
    {
        foreach (string map in new[] { "baseline-economy", "presentation-map-two" })
        {
            var definition = Resource(map);
            Assert.True(definition.Plots!.Count(p => p.IsShopSite) > definition.Participants.Length);
            ScenarioValidation.Validate(definition);
        }
    }
    [Fact]
    public void DeliveryEscrowAndSupplierCollateralAreCountedOnceAndExternalCapitalIsExcluded()
    {
        var d = Resource("delivery-lifecycle"); var s = Fixtures.Session(d);
        var destination = s.Engine.Observe(new(2)).OwnBuildings.Single().InputId;
        long supplier = s.Engine.CapitalOf(new(1)).TotalMinor, buyer = s.Engine.CapitalOf(new(2)).TotalMinor;
        Fixtures.Command(s, new(ActionKind.CreateBuyOffer, Destination: destination, ItemId: "wood", Quantity: 10, UnitPriceMinor: 500), 2);
        long offer = Assert.Single(s.Engine.Inspect().BuyOffers).Id;
        Fixtures.Command(s, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: 10));
        s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Assert.Equal(supplier, s.Engine.CapitalOf(new(1)).TotalMinor); Assert.Equal(buyer, s.Engine.CapitalOf(new(2)).TotalMinor);
        Assert.Equal(5250, s.Engine.Inspect().Accounts.Where(a => a.Kind is "ContractEscrow" or "SupplierBond").Sum(a => a.BalanceMinor));
        var withCapital = d with { CapitalOffers = [new(1, "wood", 100, 500, new(5, 1))] };
        Assert.Equal(new SimulationEngine(d).PublicEconomy().CapitalMinor, new SimulationEngine(withCapital).PublicEconomy().CapitalMinor);
    }
    [Fact]
    public void CargoAndReturnCapacityDoNotDuplicateThePublicStockValue()
    {
        var d = Resource("logistics-cycle"); var recording = Fixtures.Session(d); ScriptedScenarios.LogisticsCycle(recording, d);
        var s = Fixtures.Session(d); s.ConfigurePlayback(recording.CommandTape, 75); s.AdvanceTo(67);
        Assert.Equal(30000, s.Engine.CapitalOf(new(1)).StockMinor);
        s.Step(); Assert.Equal("Outbound", Assert.Single(s.Engine.Inspect().Logistics).Trip!.State);
        Assert.Equal(30000, s.Engine.CapitalOf(new(1)).StockMinor);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.AdvanceTo(75); loaded.AdvanceTo(75);
        Assert.Equal(JsonSerializer.Serialize(City(s)), JsonSerializer.Serialize(City(loaded)));
        Assert.Equal(0, s.Engine.CapitalOf(new(1)).StockMinor); Assert.Equal(30000, s.Engine.CapitalOf(new(2)).StockMinor);
    }
    [Fact]
    public void NpcTravelsToChangeWagesAndRestoresItsPlanWithoutRemotePrivilege()
    {
        var d = Resource("baseline-politics");
        var s = Fixtures.Session(d); var settings = new ControllerSettings(2, "wood", BaseWageMinor: 40, MaximumWageMinor: 50, PoliticsEnabled: false);
        s.AttachControllers([new AlgorithmicController(settings, 42, d.Participants.Select(p => p.Id))]);
        s.AdvanceTo(6); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); loaded.RestoreControllers(AlgorithmicController.Restore);
        while (s.Engine.Tick < 80) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(s.ComputeControllerStateHash(), loaded.ComputeControllerStateHash()); }
        Assert.Contains(s.CommandTape, i => i.Command.Action.Kind == ActionKind.SetWage);
        Assert.DoesNotContain(s.ControllerTurns.SelectMany(t => t.Feedback), r => r.ReasonCode == "NotAtLocation");
        Assert.Equal(s.Engine.ComputeStateHash(), GameSession.Replay(d, s.CommandTape, 80).Engine.ComputeStateHash());
    }
    [Fact]
    public void RunnerSeededPoliticsStillDemonstratesBothAcceptanceAndRefusalAfterTravelRules()
    {
        var d = Resource("baseline-politics");
        var controllers = JsonSerializer.Deserialize<AutonomousConfiguration>(EconomyProfileTests.Text("baseline-politics-controllers"), SimulationJson.Options)!;
        var s = Fixtures.Session(d);
        s.AttachControllers(controllers.Npcs.Select(n => new AlgorithmicController(n, AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId), d.Participants.Select(p=>p.Id))));
        s.AdvanceTo(160);
        Assert.Contains(s.Engine.InspectNegotiations(),n=>n.Current.Terms.Kind==TradeKind.Support && n.Status==NegotiationStatus.Rejected);
        Assert.Contains(s.Engine.InspectSupport().Commitments,c=>c.Status==PoliticalCommitmentStatus.Executed);
        Assert.DoesNotContain(s.ControllerTurns.SelectMany(t=>t.Feedback),r=>r.ReasonCode=="NotAtLocation");
    }
    [Fact]
    public void BriefThresholdChangesDoNotFlickerTheCityStageOrConsumeRandomnessOnRead()
    {
        var city = new CityProgression(3,new(1,1,1),new(long.MaxValue,100,10),new(long.MaxValue,100,10));
        var s = Fixtures.Session(Fixtures.Cycle() with {Economy=new(City:city)});
        Fixtures.Command(s,PlayerDesk.Work(1,true));s.Step();Assert.Equal(0,City(s).Stage);
        Fixtures.Command(s,PlayerDesk.Work(1,false));Assert.Equal(0,City(s).Stage);
        Fixtures.Command(s,PlayerDesk.Work(1,true));s.Step();s.Step();Assert.Equal(1,City(s).Stage);
        Fixtures.Command(s,PlayerDesk.Work(1,false));s.Step();Assert.Equal(1,City(s).Stage);
        s.Step();Assert.Equal(0,City(s).Stage);
        string hash=s.Engine.ComputeStateHash();
        for(int i=0;i<3;i++)s.Engine.PublicEconomy();
        Assert.Equal(hash,s.Engine.ComputeStateHash());
    }
}

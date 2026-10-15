using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class OwnStorageTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(OwnStorageTests).Assembly.GetManifestResourceStream("Fixture.own-storage-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Run()
    { var d = Scenario(); var s = new GameSession(new SimulationEngine(d)); ScriptedScenarios.OwnStorageCycle(s, d); return s; }
    private static readonly Lazy<GameSession> Baseline = new(Run);
    private static long RouteTick => Baseline.Value.CommandTape.Single(c => c.Command.Action.Kind == ActionKind.SetLogisticsStorage).SubmittedAtTick;
    private static long PreparedTick => Baseline.Value.Engine.EventsSince(0).First(e => e.Type == "LogisticsTripPrepared").Tick;
    private static GameSession BeforeRoute(ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        foreach (var c in Baseline.Value.CommandTape.Where(c => c.SubmittedAtTick < RouteTick))
        { s.AdvanceTo(c.SubmittedAtTick); s.Submit(c.AuthenticatedActor, c.Command); }
        s.AdvanceTo(RouteTick); return s;
    }
    private static GameSession At(long tick, ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        s.ConfigurePlayback(Baseline.Value.CommandTape, Baseline.Value.Engine.Tick, "own-storage"); s.AdvanceTo(tick); return s;
    }
    private static LogisticsView System(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static InventoryId Storage(GameSession s) => s.Engine.Inspect().Buildings.Single(b => b.Id.Value == 2).InputId;
    private static string Route(GameSession s, bool enabled = true, InventoryId? destination = null, long actor = 1,
        long quantity = 0, string item = "") => LandContentFixtures.Reason(s, new(ActionKind.SetLogisticsStorage,
            Logistics: System(s).Id, Destination: destination ?? Storage(s), Enabled: enabled, Quantity: quantity, ItemId: item), actor);
    private static long Wages(GameSession s) => s.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor);
    private static void WaitTrips(GameSession s, long count)
    {
        long limit = s.Engine.Tick + 100;
        while (System(s).CompletedTrips < count && s.Engine.Tick < limit) s.Step();
        Assert.Equal(count, System(s).CompletedTrips);
    }
    [Fact]
    public void TwoPhysicalTripsFillOwnStorageWithoutSaleEmissionOrGoodsLoss()
    {
        var s = Run(); var w = s.Engine.Inspect(); var source = System(s).SourceInventory;
        Assert.Equal(2, System(s).CompletedTrips); Assert.Null(System(s).Trip); Assert.Equal(0, System(s).ContractId);
        Assert.Equal(Storage(s), System(s).OwnDestination); Assert.Equal("NoDestinationSpace", System(s).ReasonCode);
        Assert.Equal(15, Fixtures.Count(s, Storage(s), "plank")); Assert.Equal(15, Fixtures.Count(s, source, "plank"));
        Assert.Equal(600, Wages(s)); Assert.Equal(74900, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(23000, w.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(2100, w.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor); Assert.Equal(200000, w.Accounts.Sum(a => a.BalanceMinor));
        Assert.Empty(w.DeliveryContracts); Assert.Empty(w.MarketAllocations); Assert.Equal(0, w.ExternalMoneyInMinor);
        Assert.Equal(30, w.Flows.Single(f => f.ItemId == "plank").Produced); Assert.All(w.Flows, f => Assert.Equal(0, f.Exported));
        Assert.Equal(new long[] {10,5}, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsStorageReceived").Select(e => e.Quantity));
        Assert.Equal(0, w.Accounts.Sum(a => a.ReservedMinor)); Assert.Equal(0, w.Inventories.Sum(i => i.ReservedCapacity));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(s.Engine.Tick + 10); Assert.Equal(ledger, s.Engine.Ledger());
    }
    [Fact]
    public void DestinationIsReservedBeforeLoadingAndCargoMovesBeforeStockArrives()
    {
        var s = At(PreparedTick); var t = System(s).Trip!;
        var w = s.Engine.Inspect(); Assert.Equal("Loading", t.State); Assert.Equal(0, t.ContractId);
        Assert.Equal(10, w.Inventories.Single(i => i.Id == Storage(s)).ReservedCapacity);
        Assert.Equal(0, Fixtures.Count(s, Storage(s), "plank")); Assert.Equal(30, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        s.Step(); Assert.Equal(20, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).SourceInventory).ReservedCapacity);
        s.Step(); Assert.Equal(new Cell(3,0), System(s).Trip!.Position); Assert.Equal(0, Fixtures.Count(s, Storage(s), "plank"));
        s.AdvanceTo(t.ArrivalTick); Assert.Equal(10, Fixtures.Count(s, Storage(s), "plank"));
        Assert.Equal("ReturningEmpty", System(s).Trip!.State);
        Assert.Equal(0, s.Engine.Inspect().Inventories.Sum(i => i.ReservedCapacity));
    }
    [Fact]
    public void ReservedDestinationSpaceCannotBeFilledByManualTransferBeforeTruckArrives()
    {
        var d = Scenario() with { Logistics = new(MoveTicksPerCell: 2) };
        var s = BeforeRoute(d); Assert.Equal("Accepted", Route(s)); s.Step(); Assert.NotNull(System(s).Trip);
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Fixtures.Command(s, new(ActionKind.Transfer, Source: System(s).SourceInventory, Destination: carrier, ItemId: "plank", Quantity: 6));
        LandContentFixtures.Move(s, new(5,0)); Assert.Equal(0, Fixtures.Count(s, Storage(s), "plank"));
        Assert.Equal("NoCapacity", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: carrier, Destination: Storage(s), ItemId: "plank", Quantity: 6)));
        Assert.Equal(6, Fixtures.Count(s, carrier, "plank")); Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == Storage(s)).ReservedCapacity);
        s.AdvanceTo(System(s).Trip!.ArrivalTick); Assert.Equal(10, Fixtures.Count(s, Storage(s), "plank"));
    }
    [Fact]
    public void FreeingSpaceAutomaticallyResumesWithBoundedCargoAndNoReassignment()
    {
        var s = At(Baseline.Value.Engine.Tick); LandContentFixtures.Move(s, new(5,0));
        Fixtures.Command(s, new(ActionKind.Transfer, Source: Storage(s), Destination: s.Engine.Observe(new(1)).Participant.InventoryId, ItemId: "plank", Quantity: 5));
        WaitTrips(s, 3); s.Step(); Assert.Equal(15, Fixtures.Count(s, Storage(s), "plank"));
        Assert.Equal(10, Fixtures.Count(s, System(s).SourceInventory, "plank")); Assert.Equal(900, Wages(s));
        Assert.Equal(5, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "plank"));
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsStorageSet");
    }
    [Fact]
    public void StopAndLoadInFlightFinishPaidTripButPreventNextTripUntilResumed()
    {
        var s = At(PreparedTick + 1); Assert.Equal("Accepted", Route(s, false));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end = System(s).Trip!.EndTick + 5; s.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(1, System(s).CompletedTrips);
        Assert.Equal(10, Fixtures.Count(s, Storage(s), "plank")); Assert.Equal(20, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        Assert.Equal(300, Wages(s)); Assert.False(System(s).Enabled);
        var paused = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal("Accepted", Route(s)); Assert.Equal("Accepted", Route(paused)); WaitTrips(s,2); WaitTrips(paused,2);
        Assert.Equal(s.Engine.ComputeStateHash(), paused.Engine.ComputeStateHash()); Assert.Equal(600, Wages(s));
    }
    [Theory]
    [InlineData(BuildingKind.Warehouse)] [InlineData(BuildingKind.Shop)]
    public void WarehouseAndShopAreAllowedWhileStockAndMoneyRemainOwners(BuildingKind kind)
    {
        var d = Scenario(); d = d with { BuildingTypes = d.BuildingTypes!.Select(t => t.Id == "warehouse" ? t with { Kind = kind } : t).ToArray(),
            Plots = d.Plots!.Select(p => p.Id == 2 ? p with { ShopParticipantId = kind == BuildingKind.Shop ? 1 : null } : p).ToArray() };
        var s = At(Baseline.Value.Engine.Tick,d); Assert.Equal(15, Fixtures.Count(s,Storage(s),"plank")); Assert.Equal(600,Wages(s));
        Assert.Equal(new ParticipantId(1),s.Engine.Inspect().Inventories.Single(i=>i.Id==Storage(s)).OwnerId);
    }
    [Theory]
    [InlineData("foreign")] [InlineData("personal")] [InlineData("production")] [InlineData("cargo")] [InlineData("unknown")]
    public void DestinationRequiresOwnStaticWarehouseOrShop(string kind)
    {
        var d = Scenario();
        if (kind == "foreign") d = d with { Buildings = d.Buildings!.Select(b=>b.Id==2?b with{OwnerId=2}:b).ToArray(),
            Plots = d.Plots!.Select(p=>p.Id==2?p with{OwnerId=2}:p).ToArray() };
        var s = BeforeRoute(d); var o = s.Engine.Observe(new(1));
        var target = kind switch { "personal"=>o.Participant.InventoryId, "production"=>o.OwnEnterprises.Single().InputId,
            "cargo"=>System(s).CargoInventory, "unknown"=>new InventoryId(999999), _=>Storage(s) };
        var ledger = s.Engine.Ledger(); Assert.Equal("NotAvailable", Route(s,destination:target));
        Assert.Equal(ledger,s.Engine.Ledger()); Assert.Null(System(s).Trip); Assert.Equal(0,System(s).OwnDestination.Value);
    }
    [Fact]
    public void ForeignActorOrCallerQuantityAndItemCannotReconfigureSystem()
    {
        var s = BeforeRoute(); Assert.Equal("NotAvailable", Route(s,actor:2));
        Assert.Equal("QuantityFromCapacity", Route(s,quantity:1000)); Assert.Equal("ItemFromProduction", Route(s,item:"wood"));
        Assert.False(System(s).Enabled); Assert.Null(System(s).Trip);
    }
    [Fact]
    public void StartedTruckCannotBeRetargetedToAnotherOwnStorage()
    {
        var d=Scenario(); d=d with{Plots=[..d.Plots!,new(3,new(2,1),10000,OwnerId:1)]};
        var s=BeforeRoute(d);LandContentFixtures.Move(s,new(2,1));
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,new(ActionKind.Build,Target:new(2,1),BuildingTypeId:"warehouse")));
        var second=s.Engine.Inspect().Buildings.Single(b=>b.Id.Value>1000).InputId;
        Assert.Equal("Accepted",Route(s));s.Step();var trip=System(s).Trip!.Id;
        Assert.Equal("LogisticsBusy",Route(s,destination:second));Assert.Equal(Storage(s),System(s).OwnDestination);
        Assert.Equal(trip,System(s).Trip!.Id);Assert.Equal(Storage(s),System(s).Trip!.OwnDestination);
    }
    [Fact]
    public void RepeatedAssignmentAndDuplicateCommandCannotReserveOrReceiveTwice()
    {
        var s = At(PreparedTick + 1); long trip = System(s).Trip!.Id;
        var id = s.Send(new(1),new(ActionKind.SetLogisticsStorage,Logistics:System(s).Id,Destination:Storage(s),Enabled:true)); s.Step();
        var c = s.CommandTape.Single(c=>c.Command.CommandId==id).Command;
        Assert.Equal(ResultStatus.Accepted,s.Engine.Enqueue(new(1),c)!.Status); Assert.Equal(trip,System(s).Trip!.Id);
        s.AdvanceTo(Baseline.Value.Engine.Tick); Assert.Equal(2,System(s).CompletedTrips);
        Assert.Equal(15,s.Engine.EventsSince(0).Where(e=>e.Type=="LogisticsStorageReceived").Sum(e=>e.Quantity));
    }
    [Theory]
    [InlineData(24799,0)] [InlineData(24800,1)]
    public void FullPayrollBudgetIsRequiredAndFundedTripSurvivesZeroFreeCash(long cash,long trips)
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p=>p.Id==1?p with{CashMinor=cash}:p).ToArray() };
        var s = At(Baseline.Value.Engine.Tick,d); Assert.Equal(trips,System(s).CompletedTrips); Assert.Equal(trips*300,Wages(s));
        Assert.Equal(trips*10,Fixtures.Count(s,Storage(s),"plank")); Assert.Equal(0,s.Engine.Observe(new(1)).Account.ReservedMinor);
        Assert.Equal(trips==1?0:299,s.Engine.Observe(new(1)).Account.AvailableMinor);
    }
    [Fact]
    public void CargoAndDestinationLimitsUseItemVolumeAndLeaveFractionalSpaceUnused()
    {
        var d = Scenario(); d = d with { Items = d.Items.Select(i=>i.Id=="plank"?i with{UnitVolume=2}:i).ToArray(),
            BuildingTypes=d.BuildingTypes!.Select(t=>t.Id=="sawmill"?t with{Levels=t.Levels.Select(l=>l with{OutputCapacity=80}).ToArray()}:t).ToArray() };
        var s=At(Baseline.Value.Engine.Tick,d); Assert.Equal(7,Fixtures.Count(s,Storage(s),"plank"));
        Assert.Equal(23,Fixtures.Count(s,System(s).SourceInventory,"plank")); Assert.Equal("NoDestinationSpace",System(s).ReasonCode);
        Assert.Equal(new long[]{5,2},s.Engine.EventsSince(0).Where(e=>e.Type=="LogisticsStorageReceived").Select(e=>e.Quantity));
    }
    [Fact]
    public void UnreachableDestinationDoesNotReserveGoodsSpaceOrPayWages()
    {
        var d=Scenario() with{BlockedCells=[new(3,0),new(3,1),new(3,2)]}; var s=At(Baseline.Value.Engine.Tick,d);
        Assert.Null(System(s).Trip); Assert.Equal("NoPath",System(s).ReasonCode); Assert.Equal(0,Wages(s));
        Assert.Equal(30,Fixtures.Count(s,System(s).SourceInventory,"plank")); Assert.Equal(0,s.Engine.Inspect().Inventories.Sum(i=>i.ReservedCapacity));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(6)] [InlineData(10)] [InlineData(15)]
    public void DirectCheckpointAtPreparationTravelReceiptReturnAndFullWaitMatchesEachTick(int offset)
    {
        var s=At(PreparedTick+offset); var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.Tick<Baseline.Value.Engine.Tick+5){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(s.Engine.Ledger(),loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0),loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("route")] [InlineData("trip")] [InlineData("hold")] [InlineData("contract")]
    public void CheckpointRejectsDestinationCrossReferencesOrMissingHold(string field)
    {
        var s=At(PreparedTick+1);var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;var l=json["logistics"]![0]!["view"]!;
        if(field=="route")l["ownDestination"]!["value"]=System(s).SourceInventory.Value;
        else if(field=="trip")l["trip"]!["ownDestination"]!["value"]=System(s).CargoInventory.Value;
        else if(field=="contract")l["trip"]!["contractId"]=999999;
        else foreach(var i in json["inventories"]!.AsArray())if(i!["id"]!.GetValue<long>()==Storage(s).Value)i["capacityHolds"]=new JsonArray();
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }
    [Fact]
    public void PrivateDestinationIsOwnerOnlyAndPreviousSchemaIsRejected()
    {
        var s=At(PreparedTick+1);Assert.Single(s.Engine.ObserveController(new(1)).OwnLogistics);Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);
        Assert.DoesNotContain(s.Engine.ObserveController(new(2)).Inventories,i=>i.Id==Storage(s));
        var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;json["schemaVersion"]=6;
        Assert.Equal("UnknownWorldSchema",Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
}

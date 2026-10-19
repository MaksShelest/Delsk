using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class InboundTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream=typeof(InboundTests).Assembly.GetManifestResourceStream("Fixture.inbound-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
    }
    private static GameSession Run(){var d=Scenario();var s=new GameSession(new SimulationEngine(d));ScriptedScenarios.InboundCycle(s,d);return s;}
    private static readonly Lazy<GameSession> Baseline=new(Run);
    private static long InputCommandTick=>Baseline.Value.CommandTape.First(c=>c.Command.Action.Kind==ActionKind.SetLogisticsInbound).SubmittedAtTick;
    private static long PreparedTick=>Baseline.Value.Engine.EventsSince(0).First(e=>e.Type=="LogisticsTripPrepared").Tick;
    private static long OutputCommandTick=>Baseline.Value.CommandTape.Single(c=>c.Command.Action.Kind==ActionKind.SetLogisticsStorage).SubmittedAtTick;
    private static GameSession Prefix(long tick,ScenarioDefinition? d=null)
    {
        var s=new GameSession(new SimulationEngine(d??Scenario()));
        foreach(var c in Baseline.Value.CommandTape.Where(c=>c.SubmittedAtTick<tick))
        {s.AdvanceTo(c.SubmittedAtTick);s.Submit(c.AuthenticatedActor,c.Command);}
        s.AdvanceTo(tick);return s;
    }
    private static GameSession At(long tick)
    {
        var s=new GameSession(new SimulationEngine(Scenario()));
        s.ConfigurePlayback(Baseline.Value.CommandTape,Baseline.Value.Engine.Tick,"inbound");s.AdvanceTo(tick);return s;
    }
    private static LogisticsView System(GameSession s)=>s.Engine.Inspect().Logistics.Single();
    private static EnterpriseView Factory(GameSession s)=>s.Engine.Observe(new(1)).OwnEnterprises.Single();
    private static InventoryId Storage(GameSession s)=>s.Engine.Inspect().Buildings.Single(b=>b.Id.Value==2).InputId;
    private static EmploymentView Crew(GameSession s)=>s.Engine.Inspect().EmploymentGroups.Single(g=>g.Role==EmploymentRole.Logistics);
    private static long Wages(GameSession s)=>s.Engine.Ledger().Where(l=>l.Reason=="LogisticsWages").Sum(l=>l.AmountMinor);
    private static string Input(GameSession s,bool enabled=true,InventoryId? source=null,long actor=1,long quantity=0,string item="")
        =>LandContentFixtures.Reason(s,new(ActionKind.SetLogisticsInbound,Logistics:System(s).Id,Source:source??Storage(s),Enabled:enabled,Quantity:quantity,ItemId:item),actor);
    private static void WaitTrips(GameSession s,long count)
    {long end=s.Engine.Tick+100;while(System(s).CompletedTrips<count&&s.Engine.Tick<end)s.Step();Assert.Equal(count,System(s).CompletedTrips);}
    [Fact]
    public void RawStockFeedsProductionAndFinishedOutputUsesTheSameTransportAndCrew()
    {
        var s=Run();var w=s.Engine.Inspect();Assert.Equal(3,System(s).CompletedTrips);Assert.Null(System(s).Trip);
        Assert.Equal(1,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(15,Fixtures.Count(s,Storage(s),"wood"));
        Assert.Equal(7,Fixtures.Count(s,Storage(s),"plank"));Assert.Equal(0,Fixtures.Count(s,Factory(s).OutputId,"plank"));
        Assert.Equal(77750,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(20700,w.Accounts.Single(a=>a.Kind=="Treasury").BalanceMinor);
        Assert.Equal(1550,w.Accounts.Single(a=>a.Kind=="WagesSink").BalanceMinor);Assert.Equal(1200,Wages(s));Assert.Equal(200000,w.Accounts.Sum(a=>a.BalanceMinor));
        Assert.Single(w.Logistics);Assert.Single(w.EmploymentGroups,g=>g.Role==EmploymentRole.Logistics);Assert.Equal(4,w.Population);
        Assert.Equal(30,w.Flows.Single(f=>f.ItemId=="wood").Harvested);Assert.Equal(14,w.Flows.Single(f=>f.ItemId=="wood").RecipeInput);
        Assert.Equal(7,w.Flows.Single(f=>f.ItemId=="plank").Produced);Assert.Equal(0,w.ExternalMoneyInMinor);Assert.Empty(w.DeliveryContracts);
        Assert.Equal(new long[]{10,5},s.Engine.EventsSince(0).Where(e=>e.Type=="LogisticsInputReceived").Select(e=>e.Quantity));
        Assert.Equal(0,w.Accounts.Sum(a=>a.ReservedMinor));Assert.Equal(0,w.Inventories.Sum(i=>i.ReservedCapacity));
    }
    [Fact]
    public void EmptyApproachKeepsReservedGoodsAtWarehouseUntilPhysicalCollection()
    {
        var s=At(PreparedTick);var trip=System(s).Trip!;
        Assert.Equal(30,Fixtures.Count(s,Storage(s),"wood"));Assert.Equal(0,Fixtures.Count(s,System(s).CargoInventory,"wood"));
        Assert.Equal(10,s.Engine.Inspect().Inventories.Single(i=>i.Id==Storage(s)).Stock.Single().Reserved);
        Assert.Equal(10,s.Engine.Inspect().Inventories.Single(i=>i.Id==Factory(s).InputId).ReservedCapacity);
        s.Step();Assert.Equal("FetchingEmpty",System(s).Trip!.State);Assert.Equal(new Cell(3,0),System(s).Trip!.Position);
        Assert.Equal(30,Fixtures.Count(s,Storage(s),"wood"));Assert.Equal(0,Fixtures.Count(s,Factory(s).InputId,"wood"));
        s.AdvanceTo(trip.LoadTick);Assert.Equal("ReturningInput",System(s).Trip!.State);Assert.Equal(new Cell(5,0),System(s).Trip!.Position);
        Assert.Equal(20,Fixtures.Count(s,Storage(s),"wood"));Assert.Equal(10,Fixtures.Count(s,System(s).CargoInventory,"wood"));
        Assert.Equal(10,s.Engine.Inspect().Inventories.Single(i=>i.Id==Storage(s)).ReservedCapacity);
        s.AdvanceTo(trip.EndTick);Assert.Equal(10,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(0,Fixtures.Count(s,System(s).CargoInventory,"wood"));
        Assert.Equal(0,s.Engine.Inspect().Inventories.Sum(i=>i.ReservedCapacity));Assert.Equal(400,Wages(s));
    }
    [Fact]
    public void StopInboundDoesNotStopCommittedTripAndPausedSaveCanResumeIt()
    {
        var s=Prefix(PreparedTick+2);Assert.Equal("Accepted",Input(s,false));var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end=System(s).Trip!.EndTick+5;s.AdvanceTo(end);loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(1,System(s).CompletedTrips);
        Assert.Equal(10,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(400,Wages(s));Assert.False(System(s).InboundEnabled);
        Assert.Equal("Accepted",Input(s));Assert.Equal("Accepted",Input(loaded));WaitTrips(s,2);WaitTrips(loaded,2);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(15,Fixtures.Count(s,Factory(s).InputId,"wood"));
    }
    [Fact]
    public void ProductionAndOutputSwitchesDoNotDisableRunningInbound()
    {
        var s=Prefix(PreparedTick+1);var id=System(s).Trip!.Id;
        Fixtures.Command(s,new(ActionKind.SetProductionEnabled,Enterprise:Factory(s).Id,Enabled:false));
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,new(ActionKind.SetLogisticsStorage,Logistics:System(s).Id,Destination:Storage(s),Enabled:false)));
        Assert.True(System(s).InboundEnabled);Assert.Equal(id,System(s).Trip!.Id);WaitTrips(s,1);
        Assert.Equal(10,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(400,Wages(s));Assert.Equal(0,Factory(s).CompletedBatches);
    }
    [Fact]
    public void PausedProductionRefillFollowsOwnOutput()
    {
        var s=Prefix(OutputCommandTick);Assert.Equal(7,Fixtures.Count(s,Factory(s).OutputId,"plank"));
        Assert.Equal("Accepted",Input(s));
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,new(ActionKind.SetLogisticsStorage,Logistics:System(s).Id,Destination:Storage(s),Enabled:true)));
        var first=System(s).Trip!;Assert.Equal(0,first.InboundSource.Value);Assert.Equal("plank",first.ItemId);WaitTrips(s,3);
        s.Step();Assert.NotEqual(0,System(s).Trip!.InboundSource.Value);Assert.Equal("wood",System(s).Trip!.ItemId);
        WaitTrips(s,4);Assert.Equal(7,Fixtures.Count(s,Storage(s),"plank"));Assert.Single(s.Engine.Inspect().Logistics);
    }
    [Fact]
    public void InboundPauseAllowsOutputToContinueUsingItsIndependentSetting()
    {
        var s=Prefix(OutputCommandTick);Assert.Equal("Accepted",Input(s,false));
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,new(ActionKind.SetLogisticsStorage,Logistics:System(s).Id,Destination:Storage(s),Enabled:true)));
        WaitTrips(s,3);Assert.False(System(s).InboundEnabled);Assert.True(System(s).Enabled);
        Assert.Equal(7,Fixtures.Count(s,Storage(s),"plank"));
    }
    [Theory]
    [InlineData("foreign")] [InlineData("personal")] [InlineData("production")] [InlineData("cargo")] [InlineData("unknown")]
    public void InputSourceRequiresOwnStaticWarehouseOrShop(string kind)
    {
        var d=Scenario();if(kind=="foreign")d=d with{Buildings=d.Buildings!.Select(b=>b.Id==2?b with{OwnerId=2}:b).ToArray(),Plots=d.Plots!.Select(p=>p.Id==2?p with{OwnerId=2}:p).ToArray()};
        // Foreign storage cannot receive the scripted deposit: test direct configuration on the new world instead.
        if(kind=="foreign")
        {
            var s0=new GameSession(new SimulationEngine(d));LandContentFixtures.Move(s0,new(2,0));Fixtures.Command(s0,new(ActionKind.BuyLogistics,Enterprise:Factory(s0).Id));
            Assert.Equal("NotAvailable",Input(s0));return;
        }
        var s=Prefix(InputCommandTick,d);var target=kind switch{"personal"=>s.Engine.Observe(new(1)).Participant.InventoryId,"production"=>Factory(s).InputId,"cargo"=>System(s).CargoInventory,"unknown"=>new InventoryId(99999),_=>Storage(s)};
        Assert.Equal("NotAvailable",Input(s,source:target));Assert.False(System(s).InboundEnabled);Assert.Null(System(s).Trip);
    }
    [Fact]
    public void ActorItemAndQuantityAreValidatedAndRepeatedSettingCannotDuplicateTrip()
    {
        var s=Prefix(InputCommandTick);Assert.Equal("NotAvailable",Input(s,actor:2));Assert.Equal("QuantityFromCapacity",Input(s,quantity:99));Assert.Equal("ItemFromProduction",Input(s,item:"plank"));
        Assert.Equal("Accepted",Input(s));s.Step();long trip=System(s).Trip!.Id;Assert.Equal("Accepted",Input(s));Assert.Equal(trip,System(s).Trip!.Id);
        WaitTrips(s,2);Assert.Equal(15,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(800,Wages(s));
    }
    [Theory]
    [InlineData(20399,0)] [InlineData(20400,1)]
    public void UpperBudgetIsReservedBeforeDepartureAndZeroFreeCashCannotStrandTruck(long cash,long trips)
    {
        var d=Scenario();d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=cash}:p).ToArray()};
        var s=Prefix(InputCommandTick,d);Assert.Equal("Accepted",Input(s));s.AdvanceTo(s.Engine.Tick+30);
        Assert.Equal(trips,System(s).CompletedTrips);Assert.Equal(trips*10,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(trips*400,Wages(s));
        Assert.Equal(trips==1?0:399,s.Engine.Observe(new(1)).Account.AvailableMinor);Assert.Equal(0,s.Engine.Observe(new(1)).Account.ReservedMinor);
    }
    [Fact]
    public void ReservedWarehouseStockCannotBeTakenDuringEmptyApproach()
    {
        var s=Prefix(InputCommandTick,Scenario() with{Logistics=new(MoveTicksPerCell:2)});Assert.Equal("Accepted",Input(s));s.Step();
        LandContentFixtures.Move(s,new(5,0));Assert.Equal("FetchingEmpty",System(s).Trip!.State);
        Assert.Equal("NoStock",LandContentFixtures.Reason(s,new(ActionKind.Transfer,Source:Storage(s),Destination:s.Engine.Observe(new(1)).Participant.InventoryId,ItemId:"wood",Quantity:21)));
        Assert.Equal(30,Fixtures.Count(s,Storage(s),"wood"));Assert.Equal(0,Fixtures.Count(s,s.Engine.Observe(new(1)).Participant.InventoryId,"wood"));
    }
    [Fact]
    public void ReservedFactorySpaceCannotBeFilledBeforeLoadedTruckReturns()
    {
        var s=Prefix(InputCommandTick,Scenario() with{Logistics=new(MoveTicksPerCell:2)});Assert.Equal("Accepted",Input(s));s.Step();
        LandContentFixtures.Move(s,new(5,0));var carrier=s.Engine.Observe(new(1)).Participant.InventoryId;
        Fixtures.Command(s,new(ActionKind.Transfer,Source:Storage(s),Destination:carrier,ItemId:"wood",Quantity:6));
        LandContentFixtures.Move(s,new(2,0));
        Assert.Equal("NoCapacity",LandContentFixtures.Reason(s,new(ActionKind.Transfer,Source:carrier,Destination:Factory(s).InputId,ItemId:"wood",Quantity:6)));
        Assert.Equal(6,Fixtures.Count(s,carrier,"wood"));Assert.Equal(0,Fixtures.Count(s,Factory(s).InputId,"wood"));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(10)] [InlineData(15)]
    public void CheckpointReproducesBothLegsPartialFillProductionAndOutput(int offset)
    {
        var s=At(PreparedTick+offset);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.Tick<Baseline.Value.Engine.Tick){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(s.Engine.Ledger(),loaded.Engine.Ledger());Assert.Equal(s.Engine.EventsSince(0),loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("source")] [InlineData("recipient")] [InlineData("state")] [InlineData("hold")]
    public void CorruptedInputReferencesStatesAndCapacityHoldAreRejected(string field)
    {
        var s=At(PreparedTick+2);var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;var trip=json["logistics"]![0]!["view"]!["trip"]!;
        if(field=="source")trip["inboundSource"]!["value"]=Factory(s).OutputId.Value;
        else if(field=="recipient")trip["ownDestination"]!["value"]=Storage(s).Value;
        else if(field=="state")trip["state"]="Outbound";
        else foreach(var i in json["inventories"]!.AsArray())if(i!["id"]!.GetValue<long>()==Factory(s).InputId.Value)i["capacityHolds"]=new JsonArray();
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }
    [Fact]
    public void PrivateInputConfigurationAndCurrentSchemaRemainExplicit()
    {
        var s=At(PreparedTick+2);Assert.Equal(Storage(s),s.Engine.ObserveController(new(1)).OwnLogistics.Single().InboundSource);
        Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;json["schemaVersion"]=7;
        Assert.Equal("UnknownWorldSchema",Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
}

using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class I1IntegrationTests
{
    [Theory][InlineData(30)][InlineData(150)]
    public void RealServiceUsesNewSpeedKeepsHandlingAndPaysOnceWithExactEtaRestoreAndReplay(long quantity)
    {
        var(s,v,o)=ServiceF2Tests.Prepare(quantity,movement:MovementRules.I1);
        ServiceF2Tests.Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));
        var quote=s.Engine.InspectServices()!.Orders.Single().Quote;long started=s.Engine.Tick;
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));int guard=2000;
        while(s.Engine.InspectServices()!.Orders.Single().State!="Completed"&&guard-->0)
        {s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.True(guard>0);Assert.Equal(quote.EtaTicks,s.Engine.Tick-started);
        Assert.Equal(quantity,ServiceF2Tests.Run(s).Delivered);Assert.Equal(quote.FeeMinor,ServiceF2Tests.Run(s).PaidMinor);
        Assert.Equal(quantity==30?12:60,quote.HandlingTicks);
        ServiceF2Tests.Exact(s,"i1-service-"+quantity);
        I1Fixture.Evidence(new{kind="service",quantity,quote.TravelTicks,quote.HandlingTicks,quote.EtaTicks,actual=s.Engine.Tick-started-3});
    }
    [Fact]public void FreightKeepsOneCargoAndRealDriverAcrossBypass()=>RoadFreightE2Tests.CheckFreight(false,MovementRules.I1);
    [Fact]public void ExtractionKeepsMiningTimersAcrossBypass()=>RoadFreightE2Tests.CheckExtraction(false,MovementRules.I1);
    [Fact]public void RoadCompletionInsideWalkingEdgeRetainsEnteredDurationAcrossRestoreAndReplay()
    {
        var d=RoadE2Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=MovementRules.I1}}));var paver=RoadE2Tests.Paver(s);
        var work=new Cell(27,2);StorageC1Tests.Move(s,work);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,target:work));
        long started=s.Engine.InspectRoads()!.Works.Single().Batch!.StartedTick;s.AdvanceTo(started+15);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,enabled:false,target:work));
        StorageC1Tests.Move(s,new(27,1),2);s.Send(new(2),new(ActionKind.MoveTo,Target:new(27,3)));s.Step();long move=s.Engine.Tick;
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,target:work));Assert.Contains(s.Engine.InspectRoads()!.Cells,c=>c.Cell==work&&c.Level==1);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));while(s.Engine.Observe(new(2)).Participant.Busy){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(4,s.Engine.Tick-move);RoadE2Tests.Exact(s,"i1-foot-road-change");
    }
    [Theory][InlineData(15)][InlineData(30)][InlineData(144)]
    public void EqualActiveSecondsHaveEqualPhysicsRegardlessOfFrameRate(int fps)
    {
        var s=new GameSession(new(I1Fixture.Definition()));var car=BillingE3Tests.Car(s);StorageC1Tests.Move(s,new(8,1));
        if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==car.Id).FacingX==-1)RoadE2Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.SetTravelHeading(new(1),1,0);copy.SetTravelHeading(new(1),1,0);
        var scheduler=new TickScheduler(s);long prior=0;
        for(int frame=1;frame<=fps*4;frame++){long now=TimeSpan.TicksPerSecond*4*frame/(fps*4);scheduler.AdvanceElapsed(TimeSpan.FromTicks(now-prior));prior=now;}
        for(int n=0;n<40;n++)copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(new Cell(14,1),s.Engine.Observe(new(1)).Participant.Position);
    }
    [Fact]public void BareNewWorldUsesNominalRunningEvenWithoutFleetAndIgnoresLegacyCellTickSetting()
    {
        var d=new ScenarioDefinition("i1-bare",42,128,2,[],[new("wood",1,1)],[],[new(1,100,new(2,1),20)],[],[],[],MoveTicksPerCell:100000,MaxTicks:1000,Economy:new(Movement:MovementRules.I1));
        var s=new GameSession(new(d));s.Send(new(1),new(ActionKind.MoveTo,Target:new(122,1)));s.Step();long start=s.Engine.Tick;
        while(s.Engine.Observe(new(1)).Participant.Busy)s.Step();Assert.Equal(200,s.Engine.Tick-start);
        Assert.Equal(200,MovementTiming.PersonalTicks(s.Engine.ObserveController(new(1)),new(2,1),new(122,1)));
        s.SetTravelHeading(new(1),-1,0);for(int n=0;n<10;n++)s.AdvanceTravelQuantum();
        var at=s.Engine.Observe(new(1)).Participant.Position;Assert.Equal(121900,at.X*1000+s.Engine.TravelOf(new(1)).OffsetX);
    }
    [Fact]public void LegacyAndMovement1KeepExactSerializedConfigAndSpeedWhileInvalidProfilesAreRejected()
    {
        foreach(int profile in new[]{0,1,2})
        {
            var s=new GameSession(new(I1Fixture.Definition(profile)));s.Send(new(1),new(ActionKind.MoveTo,Target:new(122,1)));s.Step();s.Step();
            var saved=s.Engine.CreateCheckpointJson();var copy=SimulationEngine.FromCheckpointJson(saved);Assert.Equal(saved,copy.CreateCheckpointJson());
            Assert.Equal(I1Fixture.Rules(profile),copy.Configuration().Economy!.Movement);
            for(int n=0;n<10;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.ComputeStateHash());}
            if(profile==2)
            {
                var node=JsonNode.Parse(saved)!;node["config"]!["economy"]!["movement"]!["speedMultiplier"]=2;
                Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(node.ToJsonString()));
                node=JsonNode.Parse(saved)!;node["schemaVersion"]=28;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(node.ToJsonString()));
            }
        }
    }
}

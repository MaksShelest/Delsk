using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class RoadTrafficE2Tests
{
    private static OwnedVehicle Car(GameSession s,string model,Cell position,long actor)
    {
        var v=FleetE1Tests.Buy(s,model,actor);StorageC1Tests.Move(s,s.Engine.FleetOf(new(actor))!.Vehicles.Single(x=>x.Vehicle.Id==v.Id).Position,actor);
        RoadE2Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),actor);StorageC1Tests.Move(s,position,actor);return v;
    }
    private static void RunUntil(GameSession s,Func<bool> done,int bound=600)
    {while(!done()&&bound-->0)s.Step();Assert.True(bound>0,"Traffic failed to finish its legal routes");}
    [Theory]
    [InlineData(1,20)]
    [InlineData(2,40)]
    [InlineData(3,60)]
    public void ActualRoadStagesLimitTheSameCarToTwentyFortyAndSixty(int level,int expectedSpeed)
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var paver=RoadE2Tests.Paver(s);
        for(int stage=2;stage<=level;stage++)
        {
            foreach(var cell in new[]{new Cell(10,1),new Cell(11,1)})
            {
                StorageC1Tests.Move(s,cell);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,stage,target:cell));
                int wanted=stage;RunUntil(s,()=>s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==cell).Level==wanted);s.Step();
            }
        }
        StorageC1Tests.Move(s,new(14,2));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        var car=Car(s,"sputnik20",new(8,1),2);StorageC1Tests.Move(s,new(10,1),2);
        s.SetTravelHeading(new(2),1,0);s.AdvanceTravelQuantum();
        Assert.Equal(expectedSpeed,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int i=0;i<8;i++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"speed-"+level);
    }
    [Fact]public void OpposingVehiclesYieldThroughDeclaredCorridorWithoutOverlapAndRestoreExactly()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var a=Car(s,"muravey70",new(8,1),1);StorageC1Tests.Move(s,new(10,1));var b=Car(s,"eger70",new(11,1),2);
        string x=s.Send(new(1),new(ActionKind.MoveTo,Target:new(16,1))),y=s.Send(new(2),new(ActionKind.MoveTo,Target:new(6,1)));s.Step();
        Assert.Equal(ResultStatus.Accepted,s.Engine.Result(x)!.Status);Assert.Equal(ResultStatus.Accepted,s.Engine.Result(y)!.Status);
        bool yielding=false;int bound=500;
        while((s.Engine.Observe(new(1)).Participant.Busy||s.Engine.Observe(new(2)).Participant.Busy)&&bound-->0)
        {
            s.Step();var vehicles=s.Engine.FleetOf(new(1))!.Vehicles.Concat(s.Engine.FleetOf(new(2))!.Vehicles).ToArray();Assert.NotEqual(vehicles[0].Position,vehicles[1].Position);
            if(!yielding&&s.Engine.EventsSince(0).Any(e=>e.Type=="TrafficYielded")){yielding=true;RoadE2Tests.Exact(s);}
        }
        if(bound<=0&&Environment.GetEnvironmentVariable("DELSK_E2_DEBUG") is {} debug){System.IO.Directory.CreateDirectory(debug);System.IO.File.WriteAllText(System.IO.Path.Combine(debug,"head-on.json"),s.Engine.CreateCheckpointJson());}
        Assert.True(bound>0,"Head-on routes did not finish");Assert.True(yielding,"Expected an actual passing-bay manoeuvre");Assert.Equal(new Cell(16,1),s.Engine.Observe(new(1)).Participant.Position);Assert.Equal(new Cell(6,1),s.Engine.Observe(new(2)).Participant.Position);
        Assert.All(s.Engine.EventsSince(0).Where(e=>e.Type=="TrafficYielded"),e=>Assert.DoesNotContain("4,4",e.ReasonCode));RoadE2Tests.Exact(s);
    }
    [Fact]public void VehicleTakesRealBypassAndCompletionRetainsItsCommittedPath()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var paver=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,RoadE2Tests.Work(paver));
        // Pause paid progress while bringing the other participant, without removing the real work zone.
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,enabled:false));
        var car=Car(s,"muravey70",new(8,1),2);s.Send(new(2),new(ActionKind.MoveTo,Target:new(13,1)));s.Step();
        RunUntil(s,()=>s.Engine.FleetOf(new(2))!.Vehicles.Single().Position.Z==2);
        var traffic=s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id);Assert.DoesNotContain(new Cell(10,1),traffic.Path);
        RoadE2Tests.Exact(s);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver));
        RunUntil(s,()=>s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(10,1)).Level==2);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int i=0;i<90;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(new Cell(13,1),s.Engine.Observe(new(2)).Participant.Position);Assert.Empty(s.Engine.InspectRoads()!.Bypasses);RoadE2Tests.Exact(s);
    }
    [Theory]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(19UL)]
    public void OppositeBypassEntrancesDoNotEnterAnOccupiedExitOrDeadlock(ulong seed)
    {
        var s=new GameSession(new(RoadE2Tests.Prepared(seed)));var paver=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic));RunUntil(s,()=>s.Engine.InspectRoads()!.Works.Single().Batch is not null);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic,enabled:false));
        Car(s,"muravey70",new(6,1),1);StorageC1Tests.Move(s,new(8,1));Car(s,"eger70",new(12,1),2);
        s.Send(new(1),new(ActionKind.MoveTo,Target:new(14,1)));s.Send(new(2),new(ActionKind.MoveTo,Target:new(6,1)));s.Step();
        int bound=700;while((s.Engine.Observe(new(1)).Participant.Busy||s.Engine.Observe(new(2)).Participant.Busy)&&bound-->0)
        {
            s.Step();var vehicles=s.Engine.InspectFleet()!.Vehicles;var inventories=s.Engine.Inspect().Inventories;
            Assert.Equal(vehicles.Length,vehicles.Select(v=>inventories.Single(i=>i.Id==v.CargoInventory).Position).Distinct().Count());
        }
        if(bound<=0&&Environment.GetEnvironmentVariable("DELSK_E2_DEBUG") is {} debug){System.IO.Directory.CreateDirectory(debug);System.IO.File.WriteAllText(System.IO.Path.Combine(debug,"bypass-head-on-"+seed+".json"),s.Engine.CreateCheckpointJson());}
        Assert.True(bound>0,"Opposing bypass entrances did not clear");RoadE2Tests.Exact(s,"narrow-"+seed);
    }
    [Fact]public void SteeringRequiresWholeTickExitAndReleaseCancelsWithoutEconomicSteps()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var v=Car(s,"sputnik20",new(12,1),1);
        long tick=s.Engine.Tick;s.SetTravelHeading(new(1),0,1);
        for(int i=0;i<9;i++)s.AdvanceTravelQuantum();Assert.Equal(tick,s.Engine.Tick);Assert.Equal("RoadExitPending",s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==v.Id).Reason);
        Assert.Equal(new Cell(12,1),s.Engine.Observe(new(1)).Participant.Position);s.SetTravelHeading(new(1),0,0);s.AdvanceTravelQuantum();
        Assert.Equal(0,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==v.Id).ExitQuanta);
        s.SetTravelHeading(new(1),0,1);for(int i=0;i<10;i++)s.AdvanceTravelQuantum();Assert.Equal(0,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==v.Id).Progress);
        s.AdvanceTravelQuantum();Assert.Equal(8,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==v.Id).Progress);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int i=0;i<12;i++){s.AdvanceTravelQuantum();restored.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());}
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s);
    }
    [Fact]public void StoppedPassageWarnsAtThirtyAndClearsWithForwardMotionAfterSixty()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var v=Car(s,"sputnik20",new(10,1),1);long at=s.Engine.Tick;
        s.AdvanceTo(at+29);Assert.DoesNotContain(s.Engine.EventsSince(0),e=>e.Type=="TrafficPassageWarning"&&e.EntityId==v.Id&&e.Tick>=at);
        s.AdvanceTo(at+31);Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="TrafficPassageWarning"&&e.EntityId==v.Id&&e.Tick>=at);
        s.AdvanceTo(at+90);Assert.DoesNotContain(s.Engine.FleetOf(new(1))!.Vehicles,v=>s.Engine.Configuration().Layout!.RoadCenterCells.Contains(v.Position));RoadE2Tests.Exact(s);
    }
    [Fact]public void PersonalReverseInputBrakesAndExplicitTurnNeedsFreeDesignatedPlace()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var v=Car(s,"sputnik20",new(10,1),1);var before=s.Engine.FleetOf(new(1))!.Vehicles.Single().Position;
        var facing=s.Engine.InspectFleet()!.Vehicles.Single().FacingX;s.SetTravelHeading(new(1),-facing,0);s.CompleteTravelTick();s.SetTravelHeading(new(1),0,0);Assert.Equal(before,s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);
        RoadE2Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:v.Id),accepted:false);StorageC1Tests.Move(s,new(12,1));
        RoadE2Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:v.Id));RoadE2Tests.Exact(s);
    }
}

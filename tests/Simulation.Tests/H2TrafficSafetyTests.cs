using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class H2TrafficSafetyTests
{
    [Fact]public void UnbilledNewProfileRestoresAfterManualCellEntryBetweenEconomicTicks()
    {
        var d=H2MovementTests.LongRoad(true);d=d with{Economy=d.Economy! with{Billing=null}};
        var s=new GameSession(new(d));var car=BillingE3Tests.Car(s);
        if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==car.Id).FacingX==-1)
            RoadE2Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id));
        s.SetTravelHeading(new(1),1,0);for(int q=0;q<17;q++)s.AdvanceTravelQuantum();
        Assert.Equal(7,s.Engine.TravelQuantum);Assert.Equal(new Cell(7,1),s.Engine.Observe(new(1)).Participant.Position);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int q=0;q<33;q++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Null(s.Engine.InspectBilling());s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"h2-unbilled-manual-entry");
    }
    [Fact]public void OpposingAcceleratedVehiclesYieldWithoutPushingAndRestoreWhileWaiting()
    {
        var d=BillingE3Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=new()}}));
        var a=BillingE3Tests.Car(s,"muravey70");StorageC1Tests.Move(s,new(10,1));var b=BillingE3Tests.Car(s,"eger70",2);StorageC1Tests.Move(s,new(11,1),2);
        s.Send(new(1),new(ActionKind.MoveTo,Target:new(16,1)));s.Send(new(2),new(ActionKind.MoveTo,Target:new(6,1)));s.Step();
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));int guard=500;
        while((s.Engine.Observe(new(1)).Participant.Busy||s.Engine.Observe(new(2)).Participant.Busy)&&guard-->0)
        {
            s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
            var positions=s.Engine.InspectFleet()!.Vehicles.Select(v=>s.Engine.FleetOf(v.Owner)!.Vehicles.Single(x=>x.Vehicle.Id==v.Id).Position).ToArray();Assert.Equal(positions.Length,positions.Distinct().Count());
        }
        Assert.True(guard>0);Assert.Equal(new Cell(16,1),s.Engine.Observe(new(1)).Participant.Position);Assert.Equal(new Cell(6,1),s.Engine.Observe(new(2)).Participant.Position);
        RoadE2Tests.Exact(s,"h2-opposing");
    }
    [Fact]public void ClosingBypassKeepsAnAlreadyCommittedFastVehicleUntilItLeaves()
    {
        var d=RoadE2Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=new()}}));var paver=RoadE2Tests.Paver(s);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver));long start=s.Engine.InspectRoads()!.Works.Single().Batch!.StartedTick;s.AdvanceTo(start+23);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,enabled:false));var car=FleetE1Tests.Buy(s,"muravey70",2);
        StorageC1Tests.Move(s,s.Engine.FleetOf(new(2))!.Vehicles.Single().Position,2);RoadE2Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:car.Id),2);StorageC1Tests.Move(s,new(8,1),2);
        s.Send(new(2),new(ActionKind.MoveTo,Target:new(13,1)));s.Step();int guard=200;
        while(s.Engine.FleetOf(new(2))!.Vehicles.Single().Position.Z!=2&&guard-->0)s.Step();Assert.True(guard>0);
        Assert.DoesNotContain(new Cell(10,1),s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Path);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver));Assert.Contains(s.Engine.InspectRoads()!.Bypasses,b=>b.Closing&&b.Users.Contains(car.Id));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int n=0;n<60;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.NotEqual(new Cell(10,1),s.Engine.FleetOf(new(2))!.Vehicles.Single().Position);}
        Assert.Equal(new Cell(13,1),s.Engine.Observe(new(2)).Participant.Position);Assert.Empty(s.Engine.InspectRoads()!.Bypasses);RoadE2Tests.Exact(s,"h2-closing-bypass");
    }
    [Fact]public void ExitStillRequiresOneActiveSecondAndReleaseCancelsTheIntent()
    {
        var d=BillingE3Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=new()}}));var car=BillingE3Tests.Car(s);StorageC1Tests.Move(s,new(12,1));long tick=s.Engine.Tick;
        s.SetTravelHeading(new(1),0,1);for(int n=0;n<9;n++)s.AdvanceTravelQuantum();Assert.Equal(tick,s.Engine.Tick);Assert.Equal(0,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        s.SetTravelHeading(new(1),0,0);s.AdvanceTravelQuantum();Assert.Equal(0,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).ExitQuanta);
        s.SetTravelHeading(new(1),0,1);for(int n=0;n<10;n++)s.AdvanceTravelQuantum();Assert.Equal(0,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        s.AdvanceTravelQuantum();Assert.Equal(20,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"h2-exit-hold");
    }
}

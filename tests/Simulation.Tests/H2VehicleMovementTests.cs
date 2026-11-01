using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class H2VehicleMovementTests
{
    [Theory][InlineData("sputnik20")][InlineData("muravey70")][InlineData("vector200")][InlineData("trakt_d1")]
    public void AllBodiesUseSameConversionAndDistanceBillingAcrossLongDirtRoutes(string model)
    {
        long[] times=new long[2],charges=new long[2];
        for(int fast=0;fast<2;fast++)
        {
            var s=new GameSession(new(H2MovementTests.LongRoad(fast==1)));var car=BillingE3Tests.Car(s,model);
            long before=BillingE3Tests.TotalUnits(s);long eta=fast==1?MovementTiming.PersonalTicks(s.Engine.ObserveController(new(1),true),new(6,1),new(106,1)):0;
            times[fast]=H2MovementTests.MoveTime(s,new(106,1));charges[fast]=BillingE3Tests.TotalUnits(s)-before;
            if(fast==1){Assert.Equal(eta,times[fast]);RoadE2Tests.Exact(s,"h2-body-"+model);}
        }
        Assert.Equal((long)Math.Ceiling(times[0]/2.5),times[1]);Assert.Equal(charges[0],charges[1]);
        H2MovementTests.Evidence(new{kind=model,level=1,distance=100,legacyTicks=times[0],fastTicks=times[1],roadUnits=charges[1],ratio=(double)times[0]/times[1]});
    }
    [Fact]public void FastManualVehicleStopsAtNextCentreAndRestoresWithinQuantum()
    {
        var s=new GameSession(new(H2MovementTests.LongRoad(true)));var car=BillingE3Tests.Car(s);StorageC1Tests.Move(s,new(8,1));
        s.SetTravelHeading(new(1),1,0);s.AdvanceTravelQuantum();Assert.Equal(50,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int n=0;n<33;n++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        s.SetTravelHeading(new(1),0,0);int guard=100;
        while(s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress>0&&guard-->0)s.AdvanceTravelQuantum();Assert.True(guard>0);
        var at=s.Engine.Observe(new(1)).Participant.Position;s.CompleteTravelTick();s.Step();Assert.Equal(at,s.Engine.Observe(new(1)).Participant.Position);
        RoadE2Tests.Exact(s,"h2-manual-car");
    }
}

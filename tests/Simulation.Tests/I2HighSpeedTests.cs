using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I2HighSpeedTests
{
    [Fact]public void HighwayKeepsFourPointFiveCellsPerSecondAndCannotJumpAParkedBody()
    {
        var s=I1Fixture.Surface(3,3);var car=I1Fixture.Car(s,"sputnik20",2,new(8,2));var paver=s.Engine.InspectFleet()!.Vehicles.Single(v=>v.ModelId=="trakt_d1");
        I1Fixture.Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);I1Fixture.Step(s,new(ActionKind.BoardVehicle,VehicleId:paver.Id));
        I1Fixture.Move(s,new(25,1));I1Fixture.Step(s,new(ActionKind.LeaveVehicle));I1Fixture.Move(s,new(2,9));
        I1Fixture.Move(s,new(8,1),2);s.SetTravelHeading(new(2),1,0);I2Fixture.Quanta(s,20);
        var start=s.Engine.Observe(new(2)).Participant.Position;var first=s.Engine.VehicleTravelOf(car.Id);long bill=BillingE3Tests.TotalUnits(s,2);
        I2Fixture.Quanta(s,10);var end=s.Engine.Observe(new(2)).Participant.Position;var last=s.Engine.VehicleTravelOf(car.Id);
        Assert.Equal(4500,(end.X-start.X)*1000+last.OffsetX-first.OffsetX);
        Assert.Equal(27L*SimulationEngine.RoadAccrualScale/2,BillingE3Tests.TotalUnits(s,2)-bill);
        I2Fixture.Evidence("highway",new{distance=4500,quanta=10,nominal=60,units=BillingE3Tests.TotalUnits(s,2)-bill});
        I2Fixture.ExactQuanta(s,35);Assert.InRange(s.Engine.Observe(new(2)).Participant.Position.X,20,24);
        Assert.Equal(new Cell(25,1),s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);
        s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();I2Fixture.Save(s,"highway-blocked");RoadE2Tests.Exact(s,"i2-highway-blocked");
    }
}

using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I2LaneTrafficTests
{
    private static OwnedVehicle Buy(GameSession s,long actor,string model,Cell target)
    {
        I2Fixture.Move(s,s.Engine.FleetOf(new(actor))!.Dealer,actor);var stock=s.Engine.FleetOf(new(actor))!.Stock.First(v=>v.ModelId==model);
        I2Fixture.Step(s,new(ActionKind.BuyVehicle,VehicleId:stock.Id),actor);for(int n=0;n<5;n++)s.Step();
        var car=s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Owner.Value==actor);I2Fixture.Move(s,s.Engine.FleetOf(new(actor))!.Vehicles.Single().Position,actor);
        I2Fixture.Step(s,new(ActionKind.BoardVehicle,VehicleId:car.Id),actor);I2Fixture.Move(s,target,actor);return car;
    }
    private static (GameSession,OwnedVehicle,OwnedVehicle) Pair()
    {
        var s=new GameSession(new(I2Fixture.Definition()));var a=Buy(s,1,"sputnik20",new(8,2));var b=Buy(s,2,"eger70",new(14,1));
        I2Fixture.Move(s,new(10,1));return(s,a,b);
    }
    [Fact]public void OppositeCarsPassInTheirRealRightLanesAndRestoreInASharedCell()
    {
        var(s,a,b)=Pair();s.SetTravelHeading(new(1),1,0);s.SetTravelHeading(new(2),-1,0);bool shared=false;
        for(int q=0;q<32;q++)
        {
            s.AdvanceTravelQuantum();var pa=s.Engine.Observe(new(1)).Participant.Position;var pb=s.Engine.Observe(new(2)).Participant.Position;
            Assert.Equal(1,pa.Z);Assert.Equal(1,pb.Z);Assert.Equal(700,s.Engine.VehicleTravelOf(a.Id).OffsetZ);Assert.Equal(300,s.Engine.VehicleTravelOf(b.Id).OffsetZ);
            Assert.Null(I2Fixture.Traffic(s,a.Id).Resume);Assert.Null(I2Fixture.Traffic(s,b.Id).Resume);
            if(pa==pb&&!shared){shared=true;I2Fixture.Save(s,"opposing-shared");I2Fixture.ExactQuanta(s,1);}
        }
        Assert.True(shared);Assert.True(s.Engine.Observe(new(1)).Participant.Position.X>s.Engine.Observe(new(2)).Participant.Position.X);
        s.SetTravelHeading(new(1),0,0);s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"i2-opposing-lanes");
    }
    [Fact]public void OccupiedOppositeLaneBlocksARealUTurnThenReleasesIt()
    {
        var(s,a,b)=Pair();I2Fixture.Move(s,new(12,1));I2Fixture.Move(s,new(12,1),2);
        Assert.Equal(s.Engine.Observe(new(1)).Participant.Position,s.Engine.Observe(new(2)).Participant.Position);
        s.SetTravelHeading(new(1),-1,0);I2Fixture.Quanta(s,5);Assert.Equal("TurnSpaceOccupied",I2Fixture.Traffic(s,a.Id).Reason);
        Assert.Equal(700,s.Engine.VehicleTravelOf(a.Id).OffsetZ);I2Fixture.Save(s,"opposing-turn-wait");I2Fixture.ExactQuanta(s,3);
        s.SetTravelHeading(new(2),-1,0);I2Fixture.Quanta(s,25);
        Assert.Equal(300,s.Engine.VehicleTravelOf(a.Id).OffsetZ);Assert.True(s.Engine.Observe(new(1)).Participant.Position.X<12);
        s.SetTravelHeading(new(1),0,0);s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"i2-occupied-opposite-turn");
    }
}

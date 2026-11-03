using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I2IntegrationTests
{
    [Theory]
    [InlineData("compact_valley",3)][InlineData("compact_valley",6)][InlineData("compact_valley",10)]
    [InlineData("small_river",3)][InlineData("small_river",6)][InlineData("small_river",10)]
    [InlineData("large_districts",3)][InlineData("large_districts",6)][InlineData("large_districts",10)]
    public void OrdinaryMapsKeepAllNpcControllersAndSavedMovement2(string map,int count)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),NpcEconomyG1Tests.Profile(),new(count),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));Assert.Equal(MovementRules.I1,d.Economy!.Movement);Assert.Equal(VehicleControlRules.I2,d.Economy.VehicleControl);
        Assert.Equal(30,s.Engine.ActiveCheckpointSchemaVersion);
        s.AttachControllers(d.Participants.Skip(1).Select(p=>NpcEconomyG1Tests.Controller(p.Id,p.PublicPreferenceItem)));s.AdvanceTo(15);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));H2StorageTests.Restore(copy);s.AdvanceTo(25);copy.AdvanceTo(25);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,25).Engine.ComputeStateHash());
        I2Fixture.Evidence(map+"-"+count,new{map,count,tick=s.Engine.Tick,hash=s.Engine.ComputeStateHash()});
    }
    [Fact]public void LoadedRealServiceKeepsReservationsCargoAndPaymentAcrossSteering()
    {
        var(s,v,o)=ServiceF2Tests.Prepare(freeControl:true);StorageC1Tests.Move(s,new(2,9));ServiceF2Tests.Board(s,v);ServiceF2Tests.Load(s,o.Id);
        long cargo=ServiceF2Tests.Run(s).Cargo;Assert.True(cargo>0);
        I2Fixture.Save(s,"loaded-service");
        ServiceF2Tests.Exact(s,"i2-loaded-service");ServiceF2Tests.Deliver(s,o.Id);
        Assert.Equal(cargo,ServiceF2Tests.Run(s).Delivered);Assert.True(ServiceF2Tests.Run(s).PaidMinor>0);
        I2Fixture.Save(s,"paid-service");
        ServiceF2Tests.Exact(s,"i2-paid-service");
    }
    [Theory][InlineData("T")][InlineData("cross")][InlineData("corner")][InlineData("end")]
    public void BranchesTurnWithoutAnExitHoldAndEndsPermitAFreeExit(string topology)
    {
        var d=I2Fixture.Definition();var branch=Enumerable.Range(2,topology=="cross"?6:2).Select(z=>new Cell(8,z)).ToList();
        if(topology!="end")branch.Add(new(9,3));if(topology is "T" or "cross")branch.Add(new(7,3));
        d=d with{Layout=d.Layout! with{InitialCityDirtCells=[..d.Layout.InitialCityDirtCells.Concat(branch).Distinct()],RoadCenterCells=[..d.Layout.RoadCenterCells.Concat(branch).Distinct()],RoadCorridorCells=[..d.Layout.RoadCorridorCells.Concat(branch).Distinct()]}};
        var(s,car)=I2Fixture.Car(definition:d);I2Fixture.Move(s,new(8,3));s.SetTravelHeading(new(1),1,0);
        s.AdvanceTravelQuantum();Assert.Equal(topology=="end"?1:0,I2Fixture.Traffic(s,car.Id).ExitQuanta);
        I2Fixture.ExactQuanta(s,23);Assert.True(s.Engine.Observe(new(1)).Participant.Position.X>8);
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"i2-intersection-"+topology);
    }
    [Fact]public void TractD1UsesTheSameManualInputAndRetainsItsTenKmhCap()
    {
        var(s,car)=I2Fixture.Car(model:"trakt_d1");s.SetTravelHeading(new(1),1,0);I2Fixture.Quanta(s,30);
        var start=s.Engine.Observe(new(1)).Participant.Position;var first=s.Engine.VehicleTravelOf(car.Id);
        I2Fixture.Quanta(s,20);var end=s.Engine.Observe(new(1)).Participant.Position;var last=s.Engine.VehicleTravelOf(car.Id);
        Assert.Equal(1500,(end.X-start.X)*1000+last.OffsetX-first.OffsetX);I2Fixture.ExactQuanta(s,7);
    }
}

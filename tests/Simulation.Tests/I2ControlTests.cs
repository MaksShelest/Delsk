using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using System.Text.Json;

namespace Simulation.Tests;

internal static class I2Fixture
{
    internal static ScenarioDefinition Definition(bool offroad=false)
    {
        var d=I1Fixture.Definition(offroad:offroad);
        return d with{Economy=d.Economy! with{VehicleControl=VehicleControlRules.I2},Layout=d.Layout! with{TurnCells=[new(0,1)]}};
    }
    internal static void Move(GameSession s,Cell at,long actor=1)
    {
        Step(s,new(ActionKind.MoveTo,Target:at),actor);int bound=100;
        while(s.Engine.Observe(new(actor)).Participant.Busy&&bound-->0)s.Step();
        Assert.True(bound>0,JsonSerializer.Serialize(s.Engine.InspectRoads()!.Traffic));
    }
    internal static (GameSession Session,OwnedVehicle Car) Car(bool offroad=false,string model="sputnik20",ScenarioDefinition? definition=null)
    {
        var s=new GameSession(new(definition??Definition(offroad)));
        Move(s,s.Engine.FleetOf(new(1))!.Dealer);var stock=s.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId==model);
        Step(s,new(ActionKind.BuyVehicle,VehicleId:stock.Id));for(int n=0;n<5;n++)s.Step();
        var v=s.Engine.InspectFleet()!.Vehicles.Single();
        Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);
        Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));Move(s,new(12,1));
        return(s,s.Engine.InspectFleet()!.Vehicles.Single());
    }
    internal static void Step(GameSession s,GameAction action,long actor=1)
    {var id=s.Send(new(actor),action);s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);}
    internal static TrafficVehicle Traffic(GameSession s,long id)=>s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==id);
    internal static void Quanta(GameSession s,int n){for(int q=0;q<n;q++)s.AdvanceTravelQuantum();}
    internal static void ExactQuanta(GameSession s,int n)
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int q=0;q<n;q++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
    }
    internal static void Evidence(string name,object data)
    {
        if(Environment.GetEnvironmentVariable("DELSK_I2_EVIDENCE") is not {} dir)return;
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(data,SimulationJson.Options));
    }
    internal static void Save(GameSession s,string name)
    {
        if(Environment.GetEnvironmentVariable("DELSK_I2_EVIDENCE") is not {} dir)return;
        Directory.CreateDirectory(dir);CheckpointStore.Save(s,Path.Combine(dir,name+".save.json"));
        File.WriteAllText(Path.Combine(dir,name+".scenario.json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(dir,name+".commands.jsonl"),string.Join('\n',s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        Evidence(name+"-boundary",new{tick=s.Engine.Tick,quantum=s.Engine.TravelQuantum,hash=s.Engine.ComputeStateHash()});
    }
}

public class I2SteeringTests
{
    [Fact]public void RightLaneConvergesWithoutOscillationAndKeepsI1StraightSpeed()
    {
        var(s,car)=I2Fixture.Car();s.SetTravelHeading(new(1),1,0);I2Fixture.Quanta(s,10);
        var first=s.Engine.VehicleTravelOf(car.Id);Assert.Equal(700,first.OffsetZ);
        int x=s.Engine.Observe(new(1)).Participant.Position.X;int offset=first.OffsetX;
        for(int q=0;q<10;q++){s.AdvanceTravelQuantum();Assert.Equal(700,s.Engine.VehicleTravelOf(car.Id).OffsetZ);}
        var last=s.Engine.VehicleTravelOf(car.Id);
        Assert.Equal(1500,(s.Engine.Observe(new(1)).Participant.Position.X-x)*1000+last.OffsetX-offset);
        I2Fixture.Evidence("straight",new{distance=1500,quanta=10,lane=last.OffsetZ});
        I2Fixture.ExactQuanta(s,7);
    }
    [Theory][InlineData(1,0)][InlineData(-1,0)][InlineData(0,1)][InlineData(0,-1)]
    public void OppositeDirectionTurnsLocallyIntoTheNewRightLane(int x,int z)
    {
        var(s,car)=I2Fixture.Car(offroad:true);I2Fixture.Move(s,new(12,5));s.SetTravelHeading(new(1),x,z);I2Fixture.Quanta(s,35);
        s.SetTravelHeading(new(1),0,0);I2Fixture.Quanta(s,20);
        var start=s.Engine.Observe(new(1)).Participant.Position;
        s.SetTravelHeading(new(1),-x,-z);s.AdvanceTravelQuantum();
        Assert.NotNull(I2Fixture.Traffic(s,car.Id).Steering);
        I2Fixture.ExactQuanta(s,25);
        var now=s.Engine.Observe(new(1)).Participant.Position;
        Assert.True((now.X-start.X)*x+(now.Z-start.Z)*z<0);
        Assert.InRange(Math.Abs(now.X-start.X)+Math.Abs(now.Z-start.Z),1,3);
    }
    [Fact]public void RoadUTurnHasARealBilledArcAndRestoresInTheMiddle()
    {
        var(s,car)=I2Fixture.Car();var before=s.Engine.VehicleTravelOf(car.Id);long units=BillingE3Tests.TotalUnits(s);
        var at=s.Engine.Observe(new(1)).Participant.Position;s.SetTravelHeading(new(1),-car.FacingX,-car.FacingZ);
        s.AdvanceTravelQuantum();var steering=I2Fixture.Traffic(s,car.Id).Steering!;
        I2Fixture.Save(s,"mid-turn");
        Assert.True(steering.TurnQuanta>0);Assert.NotEqual(before,s.Engine.VehicleTravelOf(car.Id));
        int duration=steering.TurnQuanta;I2Fixture.ExactQuanta(s,duration-1);
        Assert.Equal(at,s.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal(500-car.FacingX*200,s.Engine.VehicleTravelOf(car.Id).OffsetZ);
        Assert.True(BillingE3Tests.TotalUnits(s)>units);
        I2Fixture.Evidence("uturn",new{quanta=duration,units=BillingE3Tests.TotalUnits(s)-units,start=at,finish=s.Engine.Observe(new(1)).Participant.Position});
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"i2-uturn");
    }
    [Fact]public void ShortExitInputCancelsAndACommittedExitReentersTheRoad()
    {
        var(s,car)=I2Fixture.Car();s.SetTravelHeading(new(1),0,1);I2Fixture.Quanta(s,3);
        Assert.Equal(3,I2Fixture.Traffic(s,car.Id).ExitQuanta);
        s.SetTravelHeading(new(1),0,0);Assert.Equal(0,I2Fixture.Traffic(s,car.Id).ExitQuanta);
        I2Fixture.Quanta(s,15);Assert.Equal(1,s.Engine.Observe(new(1)).Participant.Position.Z);
        s.SetTravelHeading(new(1),0,1);I2Fixture.Quanta(s,5);Assert.Equal(5,I2Fixture.Traffic(s,car.Id).ExitQuanta);
        I2Fixture.Save(s,"mid-exit");I2Fixture.ExactQuanta(s,25);Assert.True(s.Engine.Observe(new(1)).Participant.Position.Z>1);
        s.SetTravelHeading(new(1),0,0);I2Fixture.Quanta(s,20);
        var lane=I2Fixture.Traffic(s,car.Id).Steering!;Assert.Equal(0,lane.LaneX);Assert.Equal(0,lane.LaneZ);
        s.SetTravelHeading(new(1),0,-1);int bound=100;while(s.Engine.Observe(new(1)).Participant.Position.Z>1&&bound-->0)s.AdvanceTravelQuantum();
        Assert.True(bound>0);s.SetTravelHeading(new(1),0,0);I2Fixture.Quanta(s,20);
        Assert.Equal(1,s.Engine.Observe(new(1)).Participant.Position.Z);
    }
}

public class I2SafetyTests
{
    [Fact]public void OccupiedTurnSpaceStopsAndResumesAfterThePedestrianLeaves()
    {
        var(s,car)=I2Fixture.Car();var at=s.Engine.Observe(new(1)).Participant.Position;
        var trial=CheckpointStore.Decode(CheckpointStore.Encode(s));trial.SetTravelHeading(new(1),-car.FacingX,-car.FacingZ);trial.AdvanceTravelQuantum();
        var blocked=I2Fixture.Traffic(trial,car.Id).Steering!.ReservedCells.First(c=>c!=at);
        I2Fixture.Move(s,blocked,2);
        s.SetTravelHeading(new(2),Math.Sign(at.X-blocked.X),Math.Sign(at.Z-blocked.Z));I2Fixture.Quanta(s,8);s.SetTravelHeading(new(2),0,0);
        s.SetTravelHeading(new(1),-car.FacingX,-car.FacingZ);
        I2Fixture.Quanta(s,10);Assert.Equal("TurnSpaceOccupied",I2Fixture.Traffic(s,car.Id).Reason);
        Assert.Equal(at,s.Engine.Observe(new(1)).Participant.Position);I2Fixture.ExactQuanta(s,3);
        I2Fixture.Move(s,new(2,9),2);I2Fixture.Quanta(s,25);
        Assert.NotEqual(at,s.Engine.Observe(new(1)).Participant.Position);
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();RoadE2Tests.Exact(s,"i2-blocked-turn");
    }
    [Fact]public void PauseCancelsExitAndFrameRateCannotChangeNormalizedInputs()
    {
        var(s,car)=I2Fixture.Car();s.SetTravelHeading(new(1),0,1);I2Fixture.Quanta(s,3);
        s.SetPaused(true);I2Fixture.Quanta(s,100);Assert.Equal(0,I2Fixture.Traffic(s,car.Id).ExitQuanta);
        s.SetPaused(false);string saved=CheckpointStore.Encode(s);string? expected=null;
        foreach(int fps in new[]{10,15,30,144})
        {
            var run=CheckpointStore.Decode(saved);run.SetTravelHeading(new(1),-car.FacingX,-car.FacingZ);var clock=new TickScheduler(run);
            long last=0;
            for(int frame=1;frame<=fps*2;frame++){long now=TimeSpan.TicksPerSecond*frame/fps;clock.AdvanceElapsed(TimeSpan.FromTicks(now-last));last=now;}
            string hash=run.Engine.ComputeStateHash();expected??=hash;Assert.Equal(expected,hash);
        }
    }
    [Fact]public void JoystickNoiseHasHysteresisAndNeutralIsExplicit()
    {
        var stick=new TravelStick();Assert.Equal(default,stick.Sample(500,200));Assert.Equal(new TravelHeading(1,0),stick.Sample(800,100));
        foreach(int value in new[]{500,530,400,540,360})Assert.Equal(new TravelHeading(1,0),stick.Sample(value,100));
        Assert.Equal(default,stick.Sample(0,0));Assert.Equal(new TravelHeading(-1,0),stick.Sample(-800,50));stick.Clear();Assert.Equal(default,stick.Heading);
    }
    [Fact]public void ManualOffroadCannotEnterForeignClosedLand()
    {
        var d=I2Fixture.Definition();var closed=new Cell(30,3);
        d=d with{Plots=[..d.Plots!.Select(p=>p.Position==closed?p with{OwnerId=2}:p)]};
        var(s,car)=I2Fixture.Car(definition:d);I2Fixture.Move(s,new(30,2));
        s.SetTravelHeading(new(1),0,1);I2Fixture.Quanta(s,20);
        Assert.Equal(new Cell(30,2),s.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal(0,I2Fixture.Traffic(s,car.Id).Progress);Assert.Equal("ObstacleBraking",I2Fixture.Traffic(s,car.Id).Reason);
        Assert.False(s.Engine.PreviewMovement(new(1),closed).Allowed);
        I2Fixture.ExactQuanta(s,5);
    }
}

public class I2LegacyControlTests
{
    [Fact]
    public void ReproduceI1OppositeInputStopsAtTurnRequiredOnAStraightRoad()
    {
        var d=I1Fixture.Definition();
        d=d with{Layout=d.Layout! with{TurnCells=[new(0,1)]}};
        var s=new GameSession(new(d));var car=I1Fixture.Car(s,"sputnik20",start:new(12,1));
        var before=s.Engine.FleetOf(new(1))!.Vehicles.Single().Position;
        var facing=s.Engine.InspectFleet()!.Vehicles.Single();
        s.SetTravelHeading(new(1),-facing.FacingX,-facing.FacingZ);
        for(int q=0;q<10;q++)s.AdvanceTravelQuantum();
        var t=s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id);
        Assert.Equal("TurnRequired",t.Reason);Assert.Equal(0,t.Progress);
        Assert.Equal(before,s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();
        RoadE2Tests.Exact(s,"i2-old-turn-required");
    }
}

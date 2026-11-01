using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
using Xunit;
namespace Simulation.Tests;

public class H2MovementTests
{
    internal static ScenarioDefinition LongRoad(bool fast,bool offroad=false)
    {
        var s=BillingE3Tests.Prepared();var road=Enumerable.Range(0,128).Select(x=>new Cell(x,1)).ToArray();
        return s with{MapWidth=128,Economy=s.Economy! with{Movement=fast?new():null},
            Participants=[..s.Participants.Select((p,n)=>p with{Position=new(2,n==0?(offroad?2:1):9)})],
            Layout=s.Layout! with{RoadCenterCells=road,InitialCityDirtCells=road,RoadCorridorCells=[..s.Layout.RoadCorridorCells.Concat(road).Concat(Enumerable.Range(0,128).Select(x=>new Cell(x,2))).Distinct()],TurnCells=road}};
    }
    internal static long MoveTime(GameSession s,Cell target,long actor=1)
    {
        string id=s.Send(new(actor),new(ActionKind.MoveTo,Target:target));s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);
        long start=s.Engine.Tick;int bound=2000;
        while(s.Engine.Observe(new(actor)).Participant.Busy&&bound-->0)s.Step();Assert.True(bound>0);Assert.Equal(target,s.Engine.Observe(new(actor)).Participant.Position);
        return s.Engine.Tick-start;
    }
    [Theory][InlineData(1,false)][InlineData(5,false)][InlineData(25,false)][InlineData(100,false)][InlineData(100,true)]
    public void WalkingLongRoutesCarryFractionalTimeAndMatchRouteForecast(int distance,bool offroad)
    {
        long[] times=new long[2];
        for(int profile=0;profile<2;profile++)
        {
            var s=new GameSession(new(LongRoad(profile==1,offroad)));var target=new Cell(2+distance,offroad?2:1);
            long eta=profile==1?MovementTiming.PersonalTicks(s.Engine.ObserveController(new(1),true),new(2,target.Z),target):0;
            times[profile]=MoveTime(s,target);if(profile==1)Assert.Equal(eta,times[profile]);
            if(profile==1&&distance==100)RoadE2Tests.Exact(s,"h2-walk-"+offroad);
        }
        Assert.InRange(times[1],(long)Math.Ceiling(times[0]/2.5), (long)Math.Ceiling(times[0]/2.5));
        Evidence(new{kind="walking",distance,offroad,legacyTicks=times[0],fastTicks=times[1],ratio=(double)times[0]/times[1]});
    }
    [Fact]public void FastFootMotionRestoresExactlyInsideEdgeAndKeepsEconomicClockAndWalls()
    {
        var s=new GameSession(new(LongRoad(true)));s.Send(new(1),new(ActionKind.MoveTo,Target:new(102,1)));s.Step();s.Step();
        Assert.Equal(new Cell(2,1),s.Engine.Observe(new(1)).Participant.Position);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int n=0;n<30;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(s.Engine.Tick,s.Samples.Last().Tick);
        var manual=new GameSession(new(LongRoad(true) with{Participants=[..LongRoad(true).Participants.Select((p,n)=>n==0?p with{Position=new(2,3)}:p)]}));manual.SetTravelHeading(new(1),1,0);manual.AdvanceTo(50);Assert.Equal(9,manual.Engine.Observe(new(1)).Participant.Position.X);
        manual.SetTravelHeading(new(1),0,0);var p=manual.Engine.TravelOf(new(1));manual.Step();Assert.Equal(p,manual.Engine.TravelOf(new(1)));
        RoadE2Tests.Exact(manual,"h2-manual-wall");
    }
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void OnlyNewGamesOptInAndTheirControllerContinuationAndReplayAreExact(string key)
    {
        Assert.False(H2StorageTests.Frozen().Engine.UsesAcceleratedMovement);
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(key),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));s.AttachControllers(d.Participants.Skip(1).Select(p=>NpcEconomyG1Tests.Controller(p.Id,p.PublicPreferenceItem)));s.AdvanceTo(40);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));H2StorageTests.Restore(copy);s.AdvanceTo(80);copy.AdvanceTo(80);
        Assert.True(s.Engine.UsesAcceleratedMovement);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,80).Engine.ComputeStateHash());
    }
    internal static void Evidence(object data)
    {if(Environment.GetEnvironmentVariable("DELSK_H2_EVIDENCE") is {} dir){Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"movement.jsonl"),JsonSerializer.Serialize(data,SimulationJson.Options)+"\n");}}
}

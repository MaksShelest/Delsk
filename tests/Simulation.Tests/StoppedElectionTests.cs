using System.Collections.Immutable;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class StoppedElectionTests
{
    internal static ScenarioDefinition Definition() => JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("elections"),SimulationJson.Options)! with { Elections=new(3,1,true) };
    private static GameAction Vote(long actor=1,long election=1) => new(ActionKind.SetVoteChoice,Election:new(election),Candidate:new(actor));
    private static void CastAll(GameSession s, bool unite=false)
    { foreach(var p in s.Engine.Observe(new(1)).Elections.Participants) s.Send(p,Vote(unite?1:p.Value,s.Engine.Observe(p).Elections.NextElectionId!.Value.Value)); }

    [Fact]
    public void DelayedHumanFreezesEveryEconomicAndTravelPhaseWithoutHiddenTick()
    {
        var s=new GameSession(new(Definition()));s.AdvanceTo(3);
        var hash=s.Engine.ComputeStateHash();var events=s.Engine.EventsSince(0).Length;
        for(int i=0;i<50;i++){s.Step();s.Engine.Step();s.AdvanceTravelQuantum();}
        Assert.Equal(3,s.Engine.Tick);Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Equal(events,s.Engine.EventsSince(0).Length);
        Assert.Throws<InvalidOperationException>(()=>s.Engine.SetTravelHeading(new(1),1,0));
        var rejected=s.Submit(new(1),new("blocked",new(1),1,3,4,5,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true)));
        Assert.Equal("ElectionInProgress",rejected!.ReasonCode);Assert.Equal(hash,s.Engine.ComputeStateHash());
        Assert.Equal("ElectionInProgress",s.Engine.AccessFor(new(1),new(ActionKind.MoveTo)).ReasonCode);
    }
    [Fact]
    public void LastBallotTalliesOnceOnFrozenWorkforceAndThenRestoresTheClock()
    {
        var s=new GameSession(new(Definition()));s.AdvanceTo(3);
        foreach(long actor in new long[]{2,3,4})s.Send(new(actor),Vote());
        Assert.Empty(s.Engine.Inspect().Elections.Results);s.Send(new(1),Vote());
        var result=Assert.Single(s.Engine.Inspect().Elections.Results);
        Assert.Equal(64,result.TotalWeight);Assert.Equal(new ParticipantId(1),result.Winner);Assert.Equal(3,result.VotingTick);
        Assert.False(s.Engine.WaitingForBallots);s.Step();Assert.Equal(4,s.Engine.Tick);
        Assert.Equal(1,s.Engine.EventsSince(0).Count(e=>e.Type=="ElectionCompleted"));
    }
    [Fact]
    public void SeparateReceiptsCannotDoubleVoteAndStaleOrForgedBallotsDoNotMutate()
    {
        var s=new GameSession(new(Definition()));s.AdvanceTo(3);string id=s.Send(new(1),Vote());
        var receipt=s.CommandTape.Single().Command;var hash=s.Engine.ComputeStateHash();
        Assert.Equal(s.Engine.Result(id),s.Submit(new(1),receipt));Assert.Equal(hash,s.Engine.ComputeStateHash());
        string duplicate=s.Send(new(1),Vote(2));Assert.Equal("BallotAlreadyCast",s.Engine.Result(duplicate)!.ReasonCode);
        var forged=receipt with {CommandId="forged",ActorId=new(2),LocalSequence=2};
        Assert.Equal("NotAvailable",s.Submit(new(1),forged)!.ReasonCode);
        string stale=s.Send(new(2),Vote(2,2));Assert.Equal("ElectionClosed",s.Engine.Result(stale)!.ReasonCode);
        Assert.Equal(3,s.Engine.Tick);Assert.Single(s.Engine.Inspect().Elections.Voting!.Submitted);
    }
    [Fact]
    public void HalfDoesNotWinAndNextBoundaryDoesNotReuseOldBallots()
    {
        var d=Definition();d=d with {Enterprises=d.Enterprises.Select((e,i)=>e with {Staff=i<2?30:0,Capacity=30}).ToArray()};
        var s=new GameSession(new(d));s.AdvanceTo(3);
        s.Send(new(1),Vote());s.Send(new(2),Vote(2));s.Send(new(3),Vote());s.Send(new(4),Vote(2));
        Assert.Null(Assert.Single(s.Engine.Inspect().Elections.Results).Winner);
        s.AdvanceTo(6);Assert.Empty(s.Engine.Inspect().Elections.Voting!.Submitted);Assert.Equal(new ElectionId(2),s.Engine.Inspect().Elections.NextElectionId);
    }
    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void WaitingSnapshotRestoresAndRecordedBallotsReplayExactly(int submitted)
    {
        var d=Definition();var s=new GameSession(new(d));s.AdvanceTo(3);
        for(int a=1;a<=submitted;a++)s.Send(new(a),Vote(a));
        var restored=SimulationEngine.FromCheckpointJson(s.Engine.CreateCheckpointJson());
        Assert.Equal(s.Engine.ComputeStateHash(),restored.ComputeStateHash());Assert.True(restored.WaitingForBallots);
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            CheckpointStore.Save(s,path);var loaded=CheckpointStore.Load(path).Session;
            for(int a=submitted+1;a<=4;a++){s.Send(new(a),Vote(a));loaded.Send(new(a),Vote(a));}
            s.AdvanceTo(5);loaded.AdvanceTo(5);
            Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
            Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,5).Engine.ComputeStateHash());
        }
        finally {File.Delete(path);File.Delete(path+".bak");}
    }
    [Fact]
    public void PrivatePendingBallotsAreNotExposedToOtherObservers()
    {
        var s=new GameSession(new(Definition()));s.AdvanceTo(3);var before=s.Engine.Observe(new(2)).Elections;
        s.Send(new(1),Vote(3));var after=s.Engine.Observe(new(2)).Elections;
        Assert.Null(after.OwnChoice);Assert.Equal(before.Voting!.OwnWeight,after.Voting!.OwnWeight);
        Assert.DoesNotContain(new ParticipantId(1),after.Voting.Awaiting);Assert.Empty(after.Results);
        Assert.DoesNotContain("submitted",JsonSerializer.Serialize(after,SimulationJson.Options),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void HeadcountAtBoundaryIncludesLaborChangeAndIsCountedOnlyOnce()
    {
        var d=Definition() with {Labor=new(StepTicks:3,MobilityNumerator:1,MobilityDenominator:2,GrowthPerPeriod:0)};
        var s=new GameSession(new(d));s.AdvanceTo(3);var snapshot=s.Engine.Inspect().Elections.Voting!;
        CastAll(s);
        var r=Assert.Single(s.Engine.Inspect().Elections.Results);
        Assert.Equal(snapshot.Employment.Sum(g=>(long)g.Headcount)+4,r.TotalWeight);
        Assert.Equal(snapshot.Employment.ToArray(),r.Employment.ToArray());Assert.Equal(snapshot.Population,r.Unemployed+r.Ballots.Sum(b=>b.WorkerWeight));
    }
    [Theory]
    [InlineData(2,true)] [InlineData(4,false)]
    public void SignedSupportLocksTheBallotAndExecutesEvenWithoutAWinner(long seller,bool winner)
    {
        var d=TradeFixtures.WithShops(Definition() with {Elections=new(20,1,true),Support=new(new(0,0),80),ResourceNodes=[new("wood",new(0,0))]});
        var s=new GameSession(new(d));ParticipantId buyer=new(1),voter=new(seller);
        void Execute(ParticipantId actor,GameAction action){string id=s.Send(actor,action);s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);}
        Execute(buyer,new(ActionKind.Harvest,ItemId:"wood",Quantity:2));
        while(s.Engine.Observe(buyer).Participant.Busy)s.Step();
        Execute(buyer,new(ActionKind.Transfer,Source:s.Engine.Observe(buyer).Participant.InventoryId,Destination:s.Engine.Observe(buyer).Support.OwnSection,ItemId:"wood",Quantity:2));
        Execute(buyer,new(ActionKind.ProposeSupport,Election:new(1),SupportSeller:voter,Compensation:[new("wood",2)]));
        Execute(voter,new(ActionKind.AcceptSupport,SupportProposalId:1));Execute(buyer,new(ActionKind.SignSupport,SupportProposalId:1));
        s.AdvanceTo(20);Assert.Equal(buyer,s.Engine.Observe(voter).Elections.Voting!.RequiredCandidate);
        string rejected=s.Send(voter,Vote(seller));Assert.Equal("CommittedVote",s.Engine.Result(rejected)!.ReasonCode);
        foreach(var actor in d.Participants)s.Send(new(actor.Id),Vote(actor.Id==seller?1:actor.Id));
        Assert.Equal(winner,Assert.Single(s.Engine.Inspect().Elections.Results).Winner.HasValue);
        Assert.Equal(PoliticalCommitmentStatus.Executed,Assert.Single(s.Engine.InspectSupport().Commitments).Status);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,20).Engine.ComputeStateHash());
    }
    [Fact]
    public void SchedulerDropsElapsedWaitingTimeAndIndependentPauseSurvivesTheLastBallot()
    {
        var s=new GameSession(new(Definition()));s.AdvanceTo(3);var clock=new TickScheduler(s);clock.SetPaused(true);
        clock.AdvanceElapsed(TimeSpan.FromDays(1));Assert.Equal(3,s.Engine.Tick);CastAll(s);Assert.True(s.Paused);
        clock.AdvanceElapsed(TimeSpan.FromSeconds(10));Assert.Equal(3,s.Engine.Tick);
        clock.SetPaused(false);clock.AdvanceElapsed(TimeSpan.FromMilliseconds(50));Assert.Equal(3,s.Engine.Tick);
    }
}

using System.Diagnostics;
using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class BallotModelTests
{
    private sealed class Adapter(Func<ModelRequest,Task<ModelReply>> reply):ILocalModelAdapter
    {public Task<ModelReply> CompleteAsync(ModelRequest request,CancellationToken cancel)=>reply(request);}
    private static ModelReply Reply(ModelRequest r,string option="vote-2")=>new(JsonSerializer.Serialize(new
    {decisionId=r.DecisionId,actorId=r.ActorId,observationVersion=r.ObservationVersion,sessionEpoch=r.SessionEpoch,optionId=option,reasonSummary="test ballot"}));
    private static (GameSession Session,HybridController Hybrid) Start(ModelRuntime runtime)
    {
        var d=StoppedElectionTests.Definition();var s=new GameSession(new(d));s.AdvanceTo(3);
        var h=new HybridController(new(new(1,"wood"),1,[1,2,3,4]),runtime);h.BindSession(new(s.SessionId,s.SessionEpoch,s.Engine.Tick,false));
        return(s,h);
    }
    private static async Task<BallotDecision> Poll(GameSession s,HybridController h,int limit=2000)
    {
        var watch=Stopwatch.StartNew();long barrier=1;
        while(watch.ElapsedMilliseconds<limit)
        {
            h.BeginBallotBarrier(barrier);var d=h.DecideBallot(s.Engine.ObserveController(new(1)));h.EndBallotBarrier(barrier++,1);
            if(d.Candidate.HasValue)return d;await Task.Delay(1,TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("Ballot failed to resolve within test deadline");
    }
    [Fact]
    public async Task RealRuntimeAppliesAValidBallotWithoutAnyEconomicTick()
    {
        using var runtime=new ModelRuntime(new Adapter(r=>Task.FromResult(Reply(r))),[1],new(TimeoutMilliseconds:100));
        var (s,h)=Start(runtime);string hash=s.Engine.ComputeStateHash();var ballot=await Poll(s,h);
        Assert.Equal(new ParticipantId(2),ballot.Candidate);Assert.Equal("ModelResolved",ballot.ModelTrace!.Resolution);
        Assert.Equal(3,s.Engine.Tick);Assert.Equal(hash,s.Engine.ComputeStateHash());
        s.Send(new(1),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:ballot.Candidate!.Value));
        Assert.Single(s.Engine.Inspect().Elections.Voting!.Submitted);
    }
    [Theory]
    [InlineData("invalid")] [InlineData("forged")] [InlineData("timeout")]
    public async Task InvalidForgedAndTimedOutRepliesFallBackWithoutAdvancingEconomy(string mode)
    {
        using var runtime=new ModelRuntime(new Adapter(r=>mode=="timeout"?new TaskCompletionSource<ModelReply>().Task:
            Task.FromResult(mode=="invalid"?new ModelReply("{oops}"):Reply(r with {SessionEpoch=r.SessionEpoch+1}))),[1],new(TimeoutMilliseconds:15));
        var (s,h)=Start(runtime);var hash=s.Engine.ComputeStateHash();var ballot=await Poll(s,h);
        Assert.Equal(new ParticipantId(1),ballot.Candidate);Assert.Equal("FallbackResolved",ballot.ModelTrace!.Resolution);
        Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Equal(3,s.Engine.Tick);
    }
    [Fact]
    public async Task PendingModelAfterRestoreUsesFallbackAndLateReplyCannotReplaceIt()
    {
        var source=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime=new ModelRuntime(new Adapter(_=>source.Task),[1],new(TimeoutMilliseconds:100));
        var (s,h)=Start(runtime);h.BeginBallotBarrier(1);var pending=h.DecideBallot(s.Engine.ObserveController(new(1)));h.EndBallotBarrier(1,1);
        Assert.Null(pending.Candidate);var state=h.Capture();
        var restored=HybridController.Restore(state,runtime);restored.BindSession(new(s.SessionId,2,3,true));
        var decision=await Poll(s,restored);Assert.Equal(new ParticipantId(1),decision.Candidate);
        Assert.Equal("SessionRestored",decision.ModelTrace!.ReasonCode);source.SetResult(new("{}"));
        await Task.Delay(5,TestContext.Current.CancellationToken);Assert.Equal(3,s.Engine.Tick);Assert.Empty(s.Engine.Inspect().Elections.Voting!.Submitted);
    }
    [Fact]
    public void BallotPromptOnlyContainsPublicCandidatesAndOwnFrozenWeight()
    {
        using var runtime=new ModelRuntime(new Adapter(_=>new TaskCompletionSource<ModelReply>().Task),[1]);
        var(s,h)=Start(runtime);var decision=h.DecideBallot(s.Engine.ObserveController(new(1)));var request=JsonSerializer.Deserialize<ModelRequest>(decision.ModelTrace!.RequestJson,SimulationJson.Options)!.Prompt;
        Assert.Contains("frozenWeight",request);Assert.DoesNotContain("random",request,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compensation",request,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("seed",request,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SessionCheckpointDuringModelWaitRestoresTheBarrierAndCastsOneFallbackWithoutRequery()
    {
        int calls=0;using var runtime=new ModelRuntime(new Adapter(_=>{Interlocked.Increment(ref calls);return new TaskCompletionSource<ModelReply>().Task;}),[2],new(TimeoutMilliseconds:100));
        var s=new GameSession(new(StoppedElectionTests.Definition()));
        s.AttachControllers([new HybridController(new(new(2,"wood"),1,[1,2,3,4]),runtime)]);s.AdvanceTo(3);
        for(int i=0;i<5 && !s.ControllerStates.Single().StateJson.Contains("\"trigger\":\"FrozenBallot\"",StringComparison.Ordinal);i++)s.PumpVoting();
        Assert.Contains("FrozenBallot",s.ControllerStates.Single().StateJson);
        var watch=Stopwatch.StartNew();while(Volatile.Read(ref calls)==0 && watch.ElapsedMilliseconds<500)await Task.Delay(1,TestContext.Current.CancellationToken);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        loaded.RestoreControllers(c=>HybridController.Restore(c,runtime));int before=calls;
        loaded.PumpVoting();Assert.Equal(3,loaded.Engine.Tick);
        Assert.Single(loaded.Engine.Inspect().Elections.Voting!.Submitted);Assert.Equal(before,calls);
        Assert.Contains(loaded.ControllerTurns,t=>t.Decision.ModelTrace?.ReasonCode=="SessionRestored" && t.Voting);
    }
}

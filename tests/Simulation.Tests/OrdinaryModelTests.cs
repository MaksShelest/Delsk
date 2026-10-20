using System.Net;
using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class OrdinaryModelTests
{
    private static (GameSession S,HybridController C,ModelRuntime R) Start(TestModelAdapter adapter,int timeout=1000)
    {
        var d=Fixtures.Cycle();var config=LocalGameModel.Default with {Runtime=new(3,timeout)};
        var runtime=new ModelRuntime(adapter,[1],config.Runtime);
        var policy=new HybridController(new(new(1,"wood",TradeEnabled:false,PoliticsEnabled:false),42,d.Participants.Select(p=>p.Id)),runtime,gameModel:config);
        var session=Fixtures.Session(d);session.AttachControllers([policy]);return(session,policy,runtime);
    }
    [Fact]
    public async Task HostBarrierStopsCatchupQuantaWithoutBlockingOrAccumulatingPausedTime()
    {
        var gate=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);var adapter=new TestModelAdapter((_,_)=>gate.Task);
        var(s,c,r)=Start(adapter);using var runtime=r;
        var clock=new TickScheduler(s,controllerBarrier:()=>s.WaitingForModel);
        clock.AdvanceElapsed(TimeSpan.FromSeconds(50));Assert.Equal(1,s.Engine.Tick);
        string hash=s.Engine.ComputeStateHash();for(int i=0;i<20;i++)clock.AdvanceElapsed(TimeSpan.FromSeconds(50));
        Assert.Equal(hash,s.Engine.ComputeStateHash());await ModelTestFixtures.Until(()=>adapter.Calls.Count==1);
        gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single())));await ModelTestFixtures.Completed(r,adapter);
        clock.AdvanceElapsed(TimeSpan.FromSeconds(1));Assert.Equal(2,s.Engine.Tick);
        Assert.Equal("ModelResolved",s.ControllerTurns.Last().Decision.ModelTrace!.Resolution);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(s.Engine.Configuration(),s.CommandTape,2).Engine.ComputeStateHash());
    }
    [Fact]
    public async Task QueueDrainsOnHostFramesWithoutAnotherEconomicTickAndUsesOnePhysicalSlot()
    {
        var adapter=new TestModelAdapter((_,_)=>Task.FromResult(new ModelReply("{}")));
        using var runtime=new ModelRuntime(adapter,[1,2,3]);runtime.Bind("s1",1);
        var requests=new[]{ModelTestFixtures.Request(1),ModelTestFixtures.Request(2),ModelTestFixtures.Request(3)};
        foreach(var r in requests)Assert.True(runtime.Enqueue(r));runtime.Pump(0);
        await ModelTestFixtures.Until(()=>{runtime.ProgressWaiting();return requests.All(runtime.HasReply);});
        Assert.Equal(3,adapter.Calls.Count);Assert.All(requests,r=>Assert.NotNull(runtime.Peek(r)));
    }
    [Fact]
    public async Task RetryingQueueFullCannotBypassTheRuntimeQueueBudget()
    {
        var adapter=new TestModelAdapter((_,_)=>Task.FromResult(new ModelReply("{}")));
        using var runtime=new ModelRuntime(adapter,[1,2,3],new(0,1000));runtime.Bind("s1",1);
        var requests=new[]{ModelTestFixtures.Request(1),ModelTestFixtures.Request(2),ModelTestFixtures.Request(3)};
        foreach(var request in requests)Assert.True(runtime.Enqueue(request));runtime.Pump(0);
        await ModelTestFixtures.Completed(runtime,adapter);
        Assert.Equal("QueueFull",runtime.Peek(requests[1])!.Outcome);
        Assert.True(runtime.Retry(requests[1]));Assert.False(runtime.Retry(requests[2]));Assert.Equal(1,runtime.WaitingRequests);
        runtime.ProgressWaiting();await ModelTestFixtures.Completed(runtime,adapter,2);Assert.True(runtime.HasReply(requests[1]));
    }
    [Fact]
    public async Task RetryIsExplicitBoundedAndUserFallbackIsSavedAndRecordedOnce()
    {
        int calls=0;var adapter=new TestModelAdapter((r,_)=>Task.FromResult(Interlocked.Increment(ref calls)==1?new ModelReply(null,"ModelUnavailable"):new ModelReply("{}")));
        var(s,c,r)=Start(adapter);using var runtime=r;s.Step();await ModelTestFixtures.Completed(r,adapter);
        Assert.Equal("ModelUnavailable",c.WaitingFailure);Assert.True(c.RetryWaiting());r.ProgressWaiting();await ModelTestFixtures.Completed(r,adapter,2);
        Assert.False(c.RetryWaiting());c.ContinueAlgorithmically();Assert.False(s.WaitingForModel);
        s.Step();var trace=Assert.Single(s.ControllerTurns,t=>t.Decision.ModelTrace?.Resolution=="FallbackResolved").Decision.ModelTrace!;
        Assert.Equal("UserFallback",trace.ReasonCode);Assert.Equal(1,trace.RetryCount);
        var state=JsonSerializer.Deserialize<HybridState>(c.Capture().StateJson,SimulationJson.Options)!;
        Assert.False(state.Settings.Enabled);Assert.Equal(LocalGameModel.Default.Adapter,state.GameModel!.Adapter);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));loaded.RestoreControllers(cp=>HybridController.Restore(cp,r));
        loaded.AdvanceTo(15);Assert.Equal(2,calls);Assert.Equal(loaded.Engine.ComputeStateHash(),GameSession.Replay(loaded.Engine.Configuration(),loaded.CommandTape,15).Engine.ComputeStateHash());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SaveAtWaitRestoresMetadataAndInterruptsWithoutRequery(bool ready)
    {
        var gate=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);var adapter=new TestModelAdapter((_,_)=>gate.Task);
        var(s,c,r)=Start(adapter);using var runtime=r;s.Step();await ModelTestFixtures.Until(()=>adapter.Calls.Count==1);
        if(ready){gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single())));await ModelTestFixtures.Completed(r,adapter);}
        string saved=CheckpointStore.Encode(s);var loaded=CheckpointStore.Decode(saved);Assert.Equal(LocalGameModel.Default with {Runtime=new(3,1000)},LocalGameModel.FromCheckpoint(loaded.ControllerStates.Single()));
        loaded.RestoreControllers(cp=>HybridController.Restore(cp,r));Assert.False(loaded.WaitingForModel);loaded.Step();
        Assert.Equal("SessionRestored",loaded.ControllerTurns.Last().Decision.ModelTrace!.ReasonCode);Assert.Single(adapter.Calls);
        if(!ready)gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single())));
        await ModelTestFixtures.Completed(r,adapter);Assert.Equal(loaded.Engine.ComputeStateHash(),GameSession.Replay(loaded.Engine.Configuration(),loaded.CommandTape,2).Engine.ComputeStateHash());
    }
    [Fact]
    public async Task TimeoutKeepsUIChoiceAvailableAndLateCallbackCannotApplyAfterUserFallback()
    {
        var gate=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);var adapter=new TestModelAdapter((_,_)=>gate.Task);
        var(s,c,r)=Start(adapter,20);using var runtime=r;s.Step();await ModelTestFixtures.Until(()=>c.WaitingFailure=="HttpTimeout");
        Assert.Equal(1,s.Engine.Tick);c.ContinueAlgorithmically();s.Step();string hash=s.Engine.ComputeStateHash();gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single())));
        await ModelTestFixtures.Completed(r,adapter);Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Equal("UserFallback",s.ControllerTurns.Last().Decision.ModelTrace!.ReasonCode);
    }
    private sealed class Tags(string text,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Assert.EndsWith("/api/tags",request.RequestUri!.ToString());return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(text)});}}
    [Theory][InlineData("{\"models\":[{\"name\":\"qwen3.5:4b\"}]}","Available")][InlineData("{\"models\":[]}","ModelMissing")][InlineData("bad","RuntimeUnavailable")]
    public async Task AvailabilityChecksExactModelWithoutInference(string json,string expected)
    {using var adapter=new OllamaAdapter(new(),new Tags(json));Assert.Equal(expected,await adapter.CheckAvailabilityAsync(CancellationToken.None));}
    [Fact]
    public void OrdinaryConfigurationRejectsNonBarrierPolicyAndNonLocalEndpoint()
    {Assert.Throws<ArgumentException>(()=>(LocalGameModel.Default with {Policy=new()}).Validate());Assert.Throws<ArgumentException>(()=>(LocalGameModel.Default with {Adapter=new("https://example.com/")}).Validate());}
}

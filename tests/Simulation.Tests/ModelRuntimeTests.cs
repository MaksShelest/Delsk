using Agents.Llm;
using Xunit;

namespace Simulation.Tests;

public class ModelRuntimeTests
{
    [Fact]
    public async Task RepliesArrivingAfterFreezeWaitForTheNextSharedBarrier()
    {
        var gate = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new TestModelAdapter((_, _) => gate.Task); using var runtime = new ModelRuntime(adapter, [1]); runtime.Bind("s1", 1);
        var r = ModelTestFixtures.Request(); Assert.True(runtime.Enqueue(r)); runtime.Pump(0); runtime.Freeze(1);
        gate.SetResult(new(ModelTestFixtures.Intent(r))); await ModelTestFixtures.Completed(runtime, adapter);
        Assert.Null(runtime.Read(r)); runtime.Freeze(1); Assert.Null(runtime.Read(r));
        runtime.Freeze(2); Assert.NotNull(runtime.Read(r)); runtime.Cancel(r);
    }
    [Fact]
    public async Task TimeoutIgnoringCancellationRetainsSinglePhysicalInferenceAndBoundedQueue()
    {
        var gate = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new TestModelAdapter((_, _) => gate.Task); using var runtime = new ModelRuntime(adapter, [1, 2, 3], new(1, 20)); runtime.Bind("s1", 1);
        var a = ModelTestFixtures.Request(); runtime.Enqueue(a); runtime.Pump(0);
        await ModelTestFixtures.Until(() => adapter.Calls.Count == 1); await Task.Delay(40, TestContext.Current.CancellationToken);
        runtime.Freeze(1); Assert.Equal("HttpTimeout", runtime.Read(a)!.Outcome); runtime.Cancel(a);
        var b = ModelTestFixtures.Request(2, 1); var c = ModelTestFixtures.Request(3, 1);
        runtime.Enqueue(b); runtime.Enqueue(c); runtime.Pump(1); runtime.Freeze(2);
        Assert.Equal(1, runtime.ActiveRequests); Assert.Equal(1, runtime.WaitingRequests); Assert.Single(adapter.Calls);
        Assert.Equal("QueueFull", runtime.Read(c)!.Outcome);
        gate.SetResult(new("late")); await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0);
        runtime.Freeze(3); Assert.Null(runtime.Read(a));
        runtime.Pump(3); await ModelTestFixtures.Completed(runtime, adapter, 2); Assert.Equal(2, adapter.Calls.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BatchAdmissionRotatesAndDoesNotDependOnSubmissionOrder(bool reverse)
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        using var runtime = new ModelRuntime(adapter, [1, 2, 3], new(0)); runtime.Bind("s1", 1);
        foreach (long tick in new long[] { 0, 30, 60 })
        {
            var requests = new long[] { 1, 2, 3 }.Select(a => ModelTestFixtures.Request(a, tick)).ToArray();
            foreach (var r in reverse ? requests.Reverse() : requests) runtime.Enqueue(r);
            runtime.Pump(tick); await ModelTestFixtures.Completed(runtime, adapter, (int)(tick / 30 + 1));
            runtime.Freeze(tick); long winner = tick / 30 + 1;
            foreach (var r in requests) { Assert.Equal(r.ActorId == winner ? "Completed" : "QueueFull", runtime.Read(r)!.Outcome); runtime.Cancel(r); }
        }
        Assert.Equal(new long[] { 1, 2, 3 }, adapter.Calls.Select(r => r.ActorId));
    }
    [Fact]
    public async Task OldEpochCallbackCannotPopulateNewSessionMailboxOrDuplicateRequest()
    {
        var gate = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new TestModelAdapter((_, _) => gate.Task); using var runtime = new ModelRuntime(adapter, [1]); runtime.Bind("s1", 1);
        var r = ModelTestFixtures.Request(); Assert.True(runtime.Enqueue(r)); Assert.False(runtime.Enqueue(r)); runtime.Pump(0);
        await ModelTestFixtures.Until(() => adapter.Calls.Count == 1); runtime.Bind("s1", 2);
        Assert.False(runtime.Enqueue(r)); gate.SetResult(new(ModelTestFixtures.Intent(r))); await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0);
        runtime.Freeze(1); Assert.Null(runtime.Read(r));
    }
}

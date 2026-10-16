using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

internal sealed class TestModelAdapter(Func<ModelRequest, CancellationToken, Task<ModelReply>> respond) : ILocalModelAdapter
{
    public ConcurrentQueue<ModelRequest> Calls { get; } = new();
    public Task<ModelReply> CompleteAsync(ModelRequest r, CancellationToken token)
    { Calls.Enqueue(r); return respond(r, token); }
}
internal static class ModelTestFixtures
{
    public static string Intent(ModelRequest r, string? option = null) => JsonSerializer.Serialize(new ModelIntent(
        r.DecisionId, r.ActorId, r.ObservationVersion, r.SessionEpoch, option ?? r.Options.First(p => p.Id != "hold").Id, "Choose a feasible plan"), SimulationJson.Options);
    public static ModelRequest Request(long actor = 1, long tick = 0, string session = "s1", long epoch = 1)
    {
        var d = Fixtures.Cycle(); var o = new SimulationEngine(d).ObserveController(new(1));
        var policy = new AlgorithmicController(new(1, "wood"), 42, [1, 2]); policy.Decide(o, []);
        return ModelPrompt.Build(o, policy.ModelOptions(o), $"{session}:model:{actor}:1", new(session, epoch, tick, false), new(), "Initial", "")
            with { ActorId = actor, RequestedTick = tick, ObservationVersion = tick, ApplyTick = tick + 5, ExpiresTick = tick + 5 };
    }
    public static (GameSession Session, HybridController Controller, ModelRuntime Runtime) Session(ILocalModelAdapter adapter,
        HybridSettings? settings = null, ScenarioDefinition? scenario = null)
    {
        var d = scenario ?? Fixtures.Cycle(); var runtime = new ModelRuntime(adapter, [1]);
        var policy = new HybridController(new AlgorithmicController(new(1, "wood", TradeEnabled: false, PoliticsEnabled: false), 42, d.Participants.Select(p => p.Id)), runtime,
            settings ?? new(MinimumIntervalTicks: 30, ApplyDelayTicks: 5, DecisionTtlTicks: 5, MaximumRequestsPerPeriod: 1));
        var session = new GameSession(new SimulationEngine(d)); session.AttachControllers([policy]); return (session, policy, runtime);
    }
    public static async Task Until(Func<bool> condition, int milliseconds = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds) await Task.Delay(1);
        Assert.True(condition(), "Bounded asynchronous fixture did not complete");
    }
    public static async Task Completed(ModelRuntime runtime, TestModelAdapter adapter, int calls = 1)
        => await Until(() => adapter.Calls.Count >= calls && runtime.ActiveRequests == 0);
}

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class HybridControllerTests
{
    [Fact]
    public async Task ValidIntentWaitsForApplyTickThenUsesOrdinaryValidatorExactlyOnce()
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Completed(runtime, adapter); s.AdvanceTo(4); Assert.Empty(s.CommandTape);
        s.Step(); var resolution = Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "ModelResolved");
        Assert.Equal(4, resolution.Tick); Assert.Equal(5, resolution.Decision.ModelTrace!.ApplyTick);
        Assert.Single(resolution.CommandIds); Assert.Equal(ResultStatus.Accepted, s.Engine.Result(resolution.CommandIds[0])!.Status);
        s.AdvanceTo(12); Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "ModelResolved"); Assert.Single(adapter.Calls);
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, 12);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash()); Assert.False(replay.HasControllers);
    }
    [Fact]
    public async Task SlotTimeoutAndLateReplyNeverBlockOrAddASecondAction()
    {
        var gate = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new TestModelAdapter((_, _) => gate.Task); var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Until(() => adapter.Calls.Count == 1); s.AdvanceTo(5);
        var fallback = Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "FallbackResolved");
        Assert.Equal("DecisionDeadline", fallback.Decision.ModelTrace!.ReasonCode); Assert.Single(fallback.CommandIds);
        gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single()))); await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0);
        s.AdvanceTo(12); Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution is "FallbackResolved" or "ModelResolved");
        Assert.Equal(s.Engine.ComputeStateHash(), GameSession.Replay(s.Engine.Configuration(), s.CommandTape, 12).Engine.ComputeStateHash());
    }
    [Theory]
    [InlineData("{}")] [InlineData("{\"quantity\":-1}")] [InlineData("{\"actorId\":2}")]
    public async Task InvalidIntentIsNotSubmittedAndOneAlgorithmicFallbackIsRecorded(string reply)
    {
        var adapter = new TestModelAdapter((_, _) => Task.FromResult(new ModelReply(reply)));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Completed(runtime, adapter); s.AdvanceTo(5);
        var turn = Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "FallbackResolved");
        Assert.Equal("InvalidIntent", turn.Decision.ModelTrace!.ReasonCode); Assert.Single(turn.CommandIds);
        Assert.DoesNotContain(s.Engine.EventsSince(0), e => e.Type == "CommandRejected");
    }
    [Fact]
    public async Task RelevantWageChangeRejectsFrozenExactPlanWhileOtherActorsHarvestDoesNot()
    {
        foreach (bool relevant in new[] { true, false })
        {
            var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
            var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
            s.Step(); await ModelTestFixtures.Completed(runtime, adapter);
            if (relevant) s.Send(new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 150));
            else s.Send(new(2), new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
            s.AdvanceTo(5);
            var trace = s.ControllerTurns.Last(t => t.Decision.ModelTrace is not null).Decision.ModelTrace!;
            Assert.Equal(relevant ? "StaleRelevantState" : "Accepted", trace.ReasonCode);
            Assert.Equal(relevant ? "FallbackResolved" : "ModelResolved", trace.Resolution);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LoadPendingOrReadySlotChangesEpochAndFallsBackWithoutRequeryOrOldCallback(bool ready)
    {
        var gate = new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new TestModelAdapter((_, _) => gate.Task); var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Until(() => adapter.Calls.Count == 1);
        if (ready) { gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single()))); await ModelTestFixtures.Completed(runtime, adapter); }
        s.Step(); var saved = CheckpointStore.Encode(s); var loaded = CheckpointStore.Decode(saved);
        loaded.RestoreControllers(c => HybridController.Restore(c, runtime));
        Assert.Equal(2, loaded.Engine.Tick); Assert.Equal(s.SessionEpoch + 1, loaded.SessionEpoch);
        if (!ready) { gate.SetResult(new(ModelTestFixtures.Intent(adapter.Calls.Single()))); await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0); }
        loaded.AdvanceTo(12); Assert.Single(adapter.Calls);
        var trace = Assert.Single(loaded.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "FallbackResolved").Decision.ModelTrace!;
        Assert.Equal("SessionRestored", trace.ReasonCode);
        Assert.Equal(loaded.Engine.ComputeStateHash(), GameSession.Replay(loaded.Engine.Configuration(), loaded.CommandTape, 12).Engine.ComputeStateHash());
    }
    [Fact]
    public async Task LoadAfterResolutionRetainsPlanPendingCommandAndFullTraceWithoutDuplicateInference()
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Completed(runtime, adapter); s.AdvanceTo(6);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); loaded.RestoreControllers(c => HybridController.Restore(c, runtime));
        while (s.Engine.Tick < 40)
        { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(s.ComputeControllerStateHash(), loaded.ComputeControllerStateHash()); }
        Assert.Single(adapter.Calls); Assert.Equal(s.CommandTape, loaded.CommandTape);
        Assert.Equal(JsonSerializer.Serialize(s.ControllerTurns, SimulationJson.Options), JsonSerializer.Serialize(loaded.ControllerTurns, SimulationJson.Options));
    }
    [Fact]
    public void SynchronousBlockingAdapterNeverRunsInsideStep()
    {
        using var block = new ManualResetEventSlim(false);
        var adapter = new TestModelAdapter((_, _) => { block.Wait(TimeSpan.FromSeconds(3)); return Task.FromResult(new ModelReply("{}")); });
        var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        try { var watch = Stopwatch.StartNew(); s.AdvanceTo(100); Assert.Equal(100, s.Engine.Tick); Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2)); }
        finally { block.Set(); }
    }
    [Fact]
    public void DisabledModelIsExactlyTheOriginalAlgorithmAndNeverCallsAdapter()
    {
        var adapter = new TestModelAdapter((_, _) => throw new InvalidOperationException("Model must not be invoked"));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter, new(Enabled: false)); using var owner = runtime;
        var d = s.Engine.Configuration(); var baseline = new GameSession(new SimulationEngine(d));
        baseline.AttachControllers([new AlgorithmicController(new(1, "wood", TradeEnabled: false, PoliticsEnabled: false), 42, d.Participants.Select(p => p.Id))]);
        s.AdvanceTo(120); baseline.AdvanceTo(120); Assert.Empty(adapter.Calls);
        Assert.Equal(baseline.Engine.ComputeStateHash(), s.Engine.ComputeStateHash()); Assert.Equal(baseline.CommandTape, s.CommandTape);
    }
    [Fact]
    public void PreviewIsPureAndCannotExposeForeignSecretSupportOrFutureState()
    {
        var d = SupportTests.Scenario(); var e = new SimulationEngine(d); var s = new GameSession(e);
        LandContentFixtures.Reason(s, new(ActionKind.ProposeSupport, SupportSeller: new(3), Election: new(1), Compensation: [new("wood", 7)]), 2);
        var o = e.ObserveController(new(1)); var policy = new AlgorithmicController(new(1, "wood"), 42, [1, 2, 3, 4]); policy.Decide(o, []);
        string before = JsonSerializer.Serialize(policy.Capture(), SimulationJson.Options); string world = e.ComputeStateHash();
        var options = policy.ModelOptions(o); var request = ModelPrompt.Build(o, options, "s1:model:1:1", new("s1", 1, s.Engine.Tick, false), new(), "Initial", "");
        Assert.Equal(before, JsonSerializer.Serialize(policy.Capture(), SimulationJson.Options)); Assert.Equal(world, e.ComputeStateHash());
        Assert.DoesNotContain("700000000", request.Prompt); Assert.DoesNotContain("randomState", request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", request.Prompt, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("configHash", request.Prompt);
        Assert.Empty(o.Support.Proposals); Assert.DoesNotContain(e.InspectSupport().Proposals.Single().Id.ToString(), request.Prompt);
        Assert.DoesNotContain("Support", JsonSerializer.Serialize(e.PublicEventsSince(0), SimulationJson.Options));
    }
    [Fact]
    public async Task BusyFallbackIsAnExplicitHoldAndExistingPhysicalWorkContinues()
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Completed(runtime, adapter);
        s.Send(new(1), new(ActionKind.Harvest, ItemId: "wood", Quantity: 5)); s.AdvanceTo(5);
        var turn = Assert.Single(s.ControllerTurns, t => t.Decision.ModelTrace?.Resolution == "FallbackResolved");
        Assert.Equal("Hold", turn.Decision.Goal); Assert.Empty(turn.CommandIds); Assert.True(s.Engine.Observe(new(1)).Participant.Busy);
        s.AdvanceTo(12); Assert.Equal(5, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }
    [Fact]
    public void ChangedReferencedPriceIsStaleButAnotherProductsPriceIsIrrelevant()
    {
        var o = new SimulationEngine(Fixtures.Cycle()).ObserveController(new(1));
        var policy = new AlgorithmicController(new(1, "wood"), 42, [1, 2]); policy.Decide(o, []);
        var offered = policy.ModelOptions(o).Single(p => p.Goal == "HarvestForSale");
        var unrelated = o with { CapitalOffers = o.CapitalOffers.Select(p => p.ItemId == "plank" ? p with { UnitPriceMinor = p.UnitPriceMinor + 1 } : p).ToImmutableArray() };
        Assert.True(policy.TrySelectModelOption(unrelated, offered, out _));
        var changed = o with { CapitalOffers = o.CapitalOffers.Select(p => p.ItemId == "wood" ? p with { UnitPriceMinor = p.UnitPriceMinor + 1 } : p).ToImmutableArray() };
        Assert.False(policy.TrySelectModelOption(changed, offered, out _));
    }
    [Fact]
    public void ExpiredElectionProposalCannotBeRepairedIntoAnotherPoliticalDecision()
    {
        var s = new GameSession(new SimulationEngine(SupportTests.Scenario(period: 3)));
        LandContentFixtures.Reason(s, new(ActionKind.ProposeSupport, SupportSeller: new(1), Election: new(1), Compensation: [new("wood", 1)]), 2);
        var policy = new AlgorithmicController(new(1, "wood"), 42, [1, 2, 3, 4]); var o = s.Engine.ObserveController(new(1)); policy.Decide(o, []);
        var option = policy.ModelOptions(o).Single(p => p.Goal == "RejectSupport");
        s.AdvanceTo(3); string before = s.Engine.ComputeStateHash();
        Assert.False(policy.TrySelectModelOption(s.Engine.ObserveController(new(1)), option, out _)); Assert.Equal(before, s.Engine.ComputeStateHash());
    }
    [Fact]
    public async Task MissedApplyTickCannotApplyAnOtherwiseValidUnexpiredReplyLater()
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var (s, policy, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime;
        s.Step(); await ModelTestFixtures.Completed(runtime, adapter);
        // The controller itself is not called during this explicit host-interruption fixture.
        while (s.Engine.Tick < 6) s.Engine.Step();
        policy.BeginDecisionBarrier(6); var decision = policy.Decide(s.Engine.ObserveController(new(1)), []);
        Assert.Equal("ApplyTickMissed", decision.ModelTrace!.ReasonCode); Assert.Equal("FallbackResolved", decision.ModelTrace.Resolution);
    }
    [Fact]
    public async Task SameSeedAndControlledRepliesProduceTheSamePlansCommandsAndWorld()
    {
        var adapterA = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var adapterB = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        var (a, _, runtimeA) = ModelTestFixtures.Session(adapterA); using var ownerA = runtimeA;
        var (b, _, runtimeB) = ModelTestFixtures.Session(adapterB); using var ownerB = runtimeB;
        a.Step(); b.Step(); await ModelTestFixtures.Completed(runtimeA, adapterA); await ModelTestFixtures.Completed(runtimeB, adapterB);
        while (a.Engine.Tick < 60) { a.Step(); b.Step(); Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash()); }
        Assert.Equal(a.CommandTape, b.CommandTape); Assert.Equal(a.ComputeControllerStateHash(), b.ComputeControllerStateHash());
    }
    [Fact]
    public async Task CooldownAndPeriodBudgetBoundCallsEvenWhenPlansKeepChanging()
    {
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r, "hold"))));
        var (s, _, runtime) = ModelTestFixtures.Session(adapter, new(MinimumIntervalTicks: 5, ApplyDelayTicks: 2, DecisionTtlTicks: 2, BudgetPeriodTicks: 30, MaximumRequestsPerPeriod: 1)); using var owner = runtime;
        while (s.Engine.Tick < 120) { s.Step(); if (runtime.ActiveRequests > 0) await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0); }
        var requests = adapter.Calls.ToArray(); Assert.NotEmpty(requests);
        Assert.All(requests.GroupBy(r => r.RequestedTick / 30), g => Assert.Single(g));
        Assert.All(requests.Zip(requests.Skip(1)), p => Assert.True(p.Second.RequestedTick - p.First.RequestedTick >= 5));
    }
    [Theory]
    [InlineData("actorId")] [InlineData("applyTick")] [InlineData("promptHash")] [InlineData("decisionId")]
    public void CorruptPendingSlotIdentityTimingAndPromptFailDirectControllerRestore(string field)
    {
        var adapter = new TestModelAdapter(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new(null); });
        var (s, policy, runtime) = ModelTestFixtures.Session(adapter); using var owner = runtime; s.Step();
        var saved = policy.Capture(); var state = JsonNode.Parse(saved.StateJson)!; var request = state["slot"]!["request"]!;
        request[field] = field is "actorId" or "applyTick" ? JsonValue.Create(999) : JsonValue.Create("corrupt");
        Assert.Throws<CheckpointException>(() => HybridController.Restore(saved with { StateJson = state.ToJsonString() }, runtime));
    }
    private static ControllerDecision TickPolicy(GameSession session, HybridController policy)
    {
        var feedback = policy.Capture().PendingCommandIds.Select(session.Engine.Result)
            .Where(r => r is not null).Select(r => r!).ToImmutableArray();
        policy.BeginDecisionBarrier(session.Engine.Tick);
        var decision = policy.Decide(session.Engine.ObserveController(policy.Actor), feedback);
        foreach (var action in decision.Actions) { string id = session.Send(policy.Actor, action); policy.CommandSubmitted(id); }
        policy.EndDecisionBarrier(session.Engine.Tick); session.Step();
        return decision;
    }
    [Fact]
    public async Task DeferredRemoteWageDecisionDoesNotSuspendADuplicateOfItsOwnUnsubmittedAction()
    {
        var s = Fixtures.Session(Fixtures.Cycle(staff: 0)); Fixtures.Supply(s);
        LandContentFixtures.Reason(s, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        using var runtime = new ModelRuntime(adapter, [1]);
        var policy = new HybridController(new AlgorithmicController(new(1, "wood", BaseWageMinor: 150, MaximumWageMinor: 200), 42, [1, 2]), runtime,
            new(ApplyDelayTicks: 5, DecisionTtlTicks: 5, MaximumRequestsPerPeriod: 1));
        policy.BindSession(new(s.SessionId, s.SessionEpoch, s.Engine.Tick, false));
        var decisions = new List<ControllerDecision> { TickPolicy(s, policy) }; await ModelTestFixtures.Completed(runtime, adapter);
        Assert.Equal("AdjustWage", adapter.Calls.Single().Options[0].Goal);
        for (int i = 0; i < 8; i++) decisions.Add(TickPolicy(s, policy));
        Assert.Contains(decisions, d => d.ModelTrace?.Resolution == "ModelResolved");
        Assert.Single(s.CommandTape, c => c.Command.Action.Kind == ActionKind.SetWage);
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "WageChangeScheduled");
    }
    [Fact]
    public async Task ExistingShortDeadlineContractIsNotHeldWaitingForModelSlot()
    {
        var d = Fixtures.Cycle() with { Delivery = new(ContractTtlTicks: 8), CapitalOffers = [new(1, "wood", 20, 500, new(1, 0))] };
        var s = Fixtures.Session(d); Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(s);
        Fixtures.Command(s, new(ActionKind.AcceptDeliveryQuantity, OfferId: 1, Quantity: 2)); s.AdvanceTo(s.Engine.Tick + 5);
        var adapter = new TestModelAdapter((r, _) => Task.FromResult(new ModelReply(ModelTestFixtures.Intent(r))));
        using var runtime = new ModelRuntime(adapter, [1]);
        var policy = new HybridController(new AlgorithmicController(new(1, "wood"), 42, [1, 2]), runtime);
        policy.BindSession(new(s.SessionId, s.SessionEpoch, s.Engine.Tick, false));
        for (int i = 0; i < 10; i++) { TickPolicy(s, policy); if (runtime.ActiveRequests > 0) await ModelTestFixtures.Until(() => runtime.ActiveRequests == 0); }
        Assert.Equal("Fulfilled", s.Engine.Inspect().DeliveryContracts.Single().Status);
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ControllerTests
{
    [Fact]
    public void ExistingNpcsUsePeriodicCapitalOrdersAndRestoreTheirPlansDuringDemandPeriod()
    {
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(ReadResource("autonomous-demand.json"), SimulationJson.Options)!;
        var s = Session(d); s.AdvanceTo(350);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); loaded.RestoreControllers(AlgorithmicController.Restore);
        s.AdvanceTo(700); loaded.AdvanceTo(700);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(s.ComputeControllerStateHash(), loaded.ComputeControllerStateHash());
        Assert.Equal(s.CommandTape, loaded.CommandTape);
        Assert.Equal(JsonSerializer.Serialize(s.ControllerTurns, SimulationJson.Options), JsonSerializer.Serialize(loaded.ControllerTurns, SimulationJson.Options));
        Assert.True(s.Engine.Inspect().CapitalDemand!.Periods.Length > 10);
        foreach (long actor in new long[] { 2, 3, 4 })
            Assert.Contains(s.Engine.Inspect().Settlements, sale => sale.Seller.Value == actor && sale.PaidMinor > 0);
    }
    private static ScenarioDefinition Scenario() => JsonSerializer.Deserialize<ScenarioDefinition>(
        ReadResource("autonomous-economy.json"), SimulationJson.Options)!;
    private static string ReadResource(string name)
    {
        using var stream = typeof(ControllerTests).Assembly.GetManifestResourceStream("Fixture." + name)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static GameSession Session(ScenarioDefinition? definition = null, bool reverse = false)
    {
        var d = definition ?? Scenario(); var session = new GameSession(new SimulationEngine(d));
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(ReadResource("autonomous-controllers.json"), SimulationJson.Options)!;
        var controllers = config.Npcs.Select(n => new AlgorithmicController(n, d.Seed ^ (ulong)n.ActorId,
            d.Participants.Select(p => p.Id))).ToArray();
        session.AttachControllers(reverse ? controllers.Reverse() : controllers); return session;
    }
    [Fact]
    public void AutonomousThreeNpcsBuildProduceExportAndBuyActualEquipment()
    {
        var session = Session(); session.AdvanceTo(1000); var world = session.Engine.Inspect();
        Assert.Equal(3, world.Enterprises.Length); Assert.Equal(3, world.Equipment.Length);
        foreach (var actor in new long[] { 2, 3, 4 })
        {
            Assert.Contains(world.Enterprises, e => e.OwnerId.Value == actor && e.CompletedBatches > 0);
            Assert.Contains(world.Equipment, e => e.OwnerId.Value == actor && e.CompletedCycles > 0);
            Assert.Contains(world.Settlements, s => s.Seller.Value == actor && s.Accepted > 0 && s.PaidMinor > 0);
            Assert.Contains(session.ControllerSamples, s => s.ActorId == actor && s.Headcount > 0);
        }
        Assert.Contains(world.Flows, f => f.ItemId == "plank" && f.Produced > 0 && f.Exported > 0);
        Assert.Contains(world.Flows, f => f.ItemId == "bread" && f.Produced > 0 && f.Exported > 0);
        Assert.Equal(world.Population, world.Unemployed + world.EmploymentGroups.Sum(g => g.Headcount));
        Assert.DoesNotContain(session.CommandTape, c => c.AuthenticatedActor.Value == 1); // Reserved human slot.
        Assert.DoesNotContain(session.ControllerTurns.SelectMany(t => t.Feedback), r => r.ReasonCode == "NotAtResourceNode");
    }
    [Fact]
    public void ZeroCashRecoversByFreeHarvestAndPhysicalSale()
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p with { CashMinor = 0 }).ToArray() };
        var s = Session(d); s.AdvanceTo(160); var w = s.Engine.Inspect();
        foreach (long id in new long[] { 2, 3, 4 })
        {
            Assert.Contains(w.Settlements, sale => sale.Seller.Value == id && sale.PaidMinor > 0);
            Assert.Contains(s.ControllerTurns, t => t.Actor.Value == id && t.Decision.Goal == "RecoverCash");
        }
        Assert.Contains(s.CommandTape, c => c.Command.Action.Kind == ActionKind.MoveTo);
        Assert.True(w.Flows.Sum(f => f.Harvested) > 0);
    }
    [Fact]
    public void ControllerOrderDoesNotChangeWorldPlansOrDecisions()
    {
        var a = Session(); var b = Session(reverse: true); a.AdvanceTo(400); b.AdvanceTo(400);
        Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash());
        Assert.Equal(a.ComputeControllerStateHash(), b.ComputeControllerStateHash());
        Assert.Equal(JsonSerializer.Serialize(a.ControllerTurns, SimulationJson.Options), JsonSerializer.Serialize(b.ControllerTurns, SimulationJson.Options));
        foreach (var tick in a.ControllerTurns.GroupBy(t => t.Tick))
            Assert.All(tick, t => Assert.Equal(tick.Key, t.Observation.Tick));
    }
    [Theory]
    [InlineData(1)] [InlineData(55)] [InlineData(120)] [InlineData(350)] [InlineData(701)]
    public void LoadedPlanAndInboxContinueExactlyWithoutReplayingDecisions(int tick)
    {
        var a = Session(); a.AdvanceTo(tick);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(a));
        Assert.Equal(a.Engine.Tick, loaded.Engine.Tick);
        Assert.Throws<InvalidOperationException>(() => loaded.Step()); // Policies cannot silently disappear on load.
        loaded.RestoreControllers(AlgorithmicController.Restore);
        Assert.Equal(a.ComputeControllerStateHash(), loaded.ComputeControllerStateHash());
        a.AdvanceTo(800); loaded.AdvanceTo(800);
        Assert.Equal(a.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(a.ComputeControllerStateHash(), loaded.ComputeControllerStateHash());
        Assert.Equal(a.CommandTape, loaded.CommandTape);
        Assert.Equal(JsonSerializer.Serialize(a.ControllerTurns, SimulationJson.Options), JsonSerializer.Serialize(loaded.ControllerTurns, SimulationJson.Options));
        Assert.Equal(a.ControllerSamples, loaded.ControllerSamples);
    }
    [Fact]
    public void ObservationIsImmutableAndDoesNotExposeForeignPrivateStateOrSeed()
    {
        var engine = new SimulationEngine(Scenario()); var observation = engine.ObserveController(new(2));
        string json = JsonSerializer.Serialize(observation, SimulationJson.Options);
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("random", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accounts", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(observation.Inventories); Assert.Empty(observation.Equipment); Assert.Empty(observation.Enterprises);
        string before = engine.ComputeStateHash();
        var altered = observation with { Map = observation.Map with { BlockedCells = observation.Map.BlockedCells.Add(new(1, 1)) },
            Inventories = observation.Inventories.SetItem(0, observation.Inventories[0] with { Stock = [new("wood", 999, 0)] }) };
        Assert.Equal(before, engine.ComputeStateHash()); Assert.Empty(engine.ObserveController(new(2)).Inventories[0].Stock);
        Assert.NotEqual(observation.Map.BlockedCells, altered.Map.BlockedCells);
    }
    [Fact]
    public void UnreachableWorldYieldsBoundedIdleWithoutInvalidCommandLoop()
    {
        var d = Scenario(); d = d with { BlockedCells = [.. Enumerable.Range(0, d.MapHeight).Select(z => new Cell(1, z))] };
        var s = Session(d); s.AdvanceTo(100);
        Assert.Empty(s.CommandTape); Assert.All(s.ControllerTurns, t => Assert.Equal("NoFeasibleGoal", t.Decision.Reason));
        Assert.All(s.ControllerStates, c => Assert.True(JsonSerializer.Deserialize<AlgorithmState>(c.StateJson, SimulationJson.Options)!.Decisions <= 21));
    }
    [Fact]
    public void RejectedLandPlanReevaluatesInsteadOfRepeatingStaleTarget()
    {
        var s = Session(); s.AdvanceTo(500);
        var failed = s.ControllerTurns.Where(t => t.Feedback.Any(r => r.ReasonCode == "PlotOwned")).ToArray();
        Assert.NotEmpty(failed);
        Assert.Equal(3, s.Engine.Inspect().Enterprises.Length);
        foreach (var t in failed)
            Assert.DoesNotContain(t.Decision.Actions, a => a.Kind == ActionKind.BuyPlot);
    }
    [Fact]
    public void CommandTapeReplaysWorldWithoutCallingPolicies()
    {
        var s = Session(); s.AdvanceTo(300);
        var replay = GameSession.Replay(Scenario(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash()); Assert.False(replay.HasControllers);
    }
    [Fact]
    public void InvalidSettingsAndUnknownControllerStateAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlgorithmicController(new(2, "wood", HarvestCoefficient: 2), 0, []));
        var s = Session().ControllerStates[0];
        Assert.Throws<CheckpointException>(() => AlgorithmicController.Restore(s with { Version = 999 }));
        var state = JsonSerializer.Deserialize<AlgorithmState>(s.StateJson, SimulationJson.Options)!;
        Assert.Throws<CheckpointException>(() => AlgorithmicController.Restore(s with { StateJson = JsonSerializer.Serialize(state with { NextStep = 999 }, SimulationJson.Options) }));
    }
    [Fact]
    public void PausePreservesPlansAndCashAndDoesNotCallController()
    {
        var s = Session(); s.AdvanceTo(50); string hash = s.ComputeControllerStateHash(); string world = s.Engine.ComputeStateHash();
        s.SetPaused(true); new TickScheduler(s).Headless(100); Assert.Equal(50, s.Engine.Tick);
        Assert.Equal(hash, s.ComputeControllerStateHash()); Assert.Equal(world, s.Engine.ComputeStateHash());
    }
    [Fact]
    public void ChangingForeignCashRevealsOnlyTheAuthorisedCapitalTotal()
    {
        var d = Scenario();
        var other = d with { Participants = d.Participants.Select(p => p.Id == 3 ? p with { CashMinor = 99999 } : p).ToArray() };
        var a = new SimulationEngine(d).ObserveController(new(2)); var b = new SimulationEngine(other).ObserveController(new(2));
        Assert.Equal(JsonSerializer.Serialize(a with { City = null! }, SimulationJson.Options), JsonSerializer.Serialize(b with { City = null! }, SimulationJson.Options));
        Assert.Equal(79999, b.City.Participants.Single(p=>p.Actor.Value==3).CapitalMinor-a.City.Participants.Single(p=>p.Actor.Value==3).CapitalMinor);
    }
    [Fact]
    public void ExcessPolicyBudgetIsRejectedBeforeEnqueueOrWorldMutation()
    {
        var s = new GameSession(new SimulationEngine(Scenario() with { MaxCommandsPerActorTick = 1 }));
        s.AttachControllers([new BudgetProbe()]); string before = s.Engine.ComputeStateHash();
        Assert.Throws<InvalidOperationException>(() => s.Step()); Assert.Empty(s.CommandTape);
        Assert.Equal(before, s.Engine.ComputeStateHash());
    }
    private sealed class BudgetProbe : IController
    {
        public ParticipantId Actor => new(2);
        public ControllerDecision Decide(ControllerObservation o, ImmutableArray<CommandResult> feedback)
            => new("Probe", "Budget", [new(ActionKind.MoveTo, Target: new(1, 1)), new(ActionKind.MoveTo, Target: new(2, 1))]);
        public void CommandSubmitted(string id) { }
        public ControllerCheckpoint Capture() => new("budget-probe", 1, Actor, "{}", []);
    }

}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class CheckpointTests
{
    private static ScenarioDefinition Equipment()
    {
        using var stream = typeof(CheckpointTests).Assembly.GetManifestResourceStream("Fixture.equipment-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Run(string script, out ScenarioDefinition definition)
    {
        definition = script switch
        {
            "cycle" => Fixtures.Cycle(), "development" => LandContentFixtures.Scenario(),
            "equipment" => Equipment(), _ => throw new ArgumentException("Unknown fixture")
        };
        var s = new GameSession(new SimulationEngine(definition));
        if (script == "cycle") ScriptedScenarios.EconomicCycle(s, definition);
        else if (script == "development") ScriptedScenarios.Development(s, definition);
        else ScriptedScenarios.EquipmentCycle(s, definition);
        return s;
    }
    private static GameSession At(GameSession original, ScenarioDefinition d, long tick)
    {
        var s = new GameSession(new SimulationEngine(d)); s.ConfigurePlayback(original.CommandTape, original.Engine.Tick);
        new TickScheduler(s).Headless(tick); return s;
    }
    private static void ContinueTogether(GameSession a, GameSession b, long finalTick)
    {
        Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash());
        while (a.Engine.Tick < finalTick)
        {
            a.Step(); b.Step(); Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash());
        }
        Assert.Equal(a.Engine.Ledger(), b.Engine.Ledger());
        Assert.Equal(a.CommandTape, b.CommandTape); Assert.Equal(a.Samples, b.Samples);
        Assert.Equal(a.Engine.EventsSince(0), b.Engine.EventsSince(0));
    }

    [Theory]
    [InlineData("cycle", 0)]
    [InlineData("cycle", 12)] // personal harvest and reserved capacity
    [InlineData("cycle", 25)] // path progress
    [InlineData("cycle", 34)] // production WIP/energy/output capacity
    [InlineData("cycle", 55)] // physical cargo on a path
    [InlineData("cycle", 60)] // open sale window, stock/money holds
    [InlineData("development", 90)]
    [InlineData("development", 300)]
    [InlineData("development", 450)]
    [InlineData("equipment", 4)] // personal machine job and mutual links
    [InlineData("equipment", 101)] // automatic extraction WIP
    [InlineData("equipment", 170)] // fractional labor credit/idle departure
    public void CompleteSnapshotRestoresCurrentStateAndEveryRemainingTick(string script, long tick)
    {
        var expected = Run(script, out var d); var current = At(expected, d, tick);
        string snapshot = CheckpointStore.Encode(current);
        var loaded = CheckpointStore.Decode(snapshot);
        Assert.Equal(tick, loaded.Engine.Tick); Assert.Equal(current.SessionEpoch + 1, loaded.SessionEpoch);
        ContinueTogether(current, loaded, expected.Engine.Tick);
        Assert.Equal(expected.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(expected.Samples, loaded.Samples);
    }

    [Fact]
    public void ReservationAndExpiryRemainInclusiveAfterLoad()
    {
        var d = LandContentFixtures.Scenario() with { LandReservationTicks = 5 };
        var s = LandContentFixtures.Session(d); LandContentFixtures.Move(s, new(8, 0));
        Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(3)));
        long until = s.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).ReservationUntilTick!.Value;
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < until - 1) { s.Step(); loaded.Step(); }
        var action = new GameAction(ActionKind.BuyPlot, Plot: new(3));
        string a = s.Send(new(1), action), b = loaded.Send(new(1), action);
        Assert.Equal(a, b); s.Step(); loaded.Step();
        Assert.Equal("Accepted", loaded.Engine.Result(b)!.ReasonCode);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(1, loaded.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).OwnerId!.Value.Value);
    }

    [Fact]
    public void DelayedWageGrowthCreditAndPausedWipSurviveRepeatedLoad()
    {
        var d = Equipment() with { Labor = new(StepTicks: 10, GrowthPerPeriod: 1) };
        var s = new GameSession(new SimulationEngine(d));
        Fixtures.Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor"));
        var e = s.Engine.Observe(new(1)).OwnEquipment.Single();
        Fixtures.Command(s, new(ActionKind.SetEquipmentMode, Equipment: e.Id, Mode: EquipmentMode.Automatic));
        while (s.Engine.Tick < 101) s.Step();
        Fixtures.Command(s, new(ActionKind.SetEquipmentMode, Equipment: e.Id, Mode: EquipmentMode.Off)); // pause after full cycle
        Fixtures.Command(s, new(ActionKind.SetWage, Group: e.GroupId, WageMinor: 40));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        for (int n = 0; n < 80; n++)
        {
            s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
            if (n is 0 or 19 or 46) loaded = CheckpointStore.Decode(CheckpointStore.Encode(loaded));
        }
        Assert.True(loaded.Engine.Inspect().Population > d.Population);
        Assert.Equal(40, loaded.Engine.Observe(new(1)).OwnEmploymentGroups.Single().AttractionWageMinor);
    }

    [Fact]
    public void PendingCommandsDedupNamespaceAndEpochArePreserved()
    {
        var s = Fixtures.Session();
        string first = s.Send(new(1), new(ActionKind.Harvest, ItemId: "wood", Quantity: 2), delayTicks: 5);
        var c = s.CommandTape.Single(); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        string hash = loaded.Engine.ComputeStateHash();
        Assert.Equal("StaleSessionEpoch", loaded.SubmitFromEpoch(s.SessionEpoch, c.AuthenticatedActor, c.Command)!.ReasonCode);
        Assert.Equal(hash, loaded.Engine.ComputeStateHash()); Assert.Single(loaded.CommandTape);
        Assert.Null(loaded.Submit(c.AuthenticatedActor, c.Command));
        string next = loaded.Send(new(1), new(ActionKind.MoveTo, Target: new(10, 0)), delayTicks: 10);
        Assert.Equal("s1:1:2", next);
        new TickScheduler(loaded).Headless(9);
        Assert.Equal("Accepted", loaded.Engine.Result(first)!.ReasonCode);
        Assert.Equal(2, loaded.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
        Assert.Equal(1, loaded.Engine.EventsSince(0).Count(e => e.Type == "HarvestCompleted"));
    }

    [Fact]
    public void PauseAndNewSchedulerNeverAdvanceOfflineTime()
    {
        var s = Fixtures.Session(); Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
        s.SetPaused(true); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        var scheduler = new TickScheduler(loaded); scheduler.AdvanceElapsed(TimeSpan.FromDays(7));
        Assert.Equal(1, loaded.Engine.Tick); Assert.True(loaded.Paused);
        scheduler.SetPaused(false); scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, loaded.Engine.Tick); scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, loaded.Engine.Tick);
    }

    [Fact]
    public void CoreCheckpointDoesNotReplayCommandsOrEmitNewBudgetAndEvents()
    {
        var expected = Run("equipment", out var d); var current = At(expected, d, 101);
        var loaded = SimulationEngine.FromCheckpointJson(current.Engine.CreateCheckpointJson());
        Assert.Equal(current.Engine.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Equal(current.Engine.EventsSince(0), loaded.EventsSince(0));
        Assert.Equal(current.Engine.Ledger(), loaded.Ledger());
        Assert.Equal(current.Engine.Inspect().ExternalMoneyInMinor, loaded.Inspect().ExternalMoneyInMinor);
        current.Engine.Step(); loaded.Step(); Assert.Equal(current.Engine.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("rules")]
    [InlineData("money")]
    [InlineData("orphan")]
    [InlineData("nextid")]
    [InlineData("journal")]
    [InlineData("population")]
    public void CorruptOrUnsupportedWorldIsRejectedBeforePublishing(string fault)
    {
        var s = Fixtures.Session(); var saved = JsonSerializer.Deserialize<WorldCheckpoint>(s.Engine.CreateCheckpointJson(), SimulationJson.Options)!;
        saved = fault switch
        {
            "schema" => saved with { SchemaVersion = 999 }, "rules" => saved with { RulesVersion = "future" },
            "money" => saved with { Accounts = saved.Accounts.Select((a, i) => i == 0 ? a with { Balance = 1 } : a).ToArray() },
            "orphan" => saved with { Inventories = saved.Inventories.Select((a, i) => i == 0 ? a with
                { CapacityHolds = [new("fake", 1)] } : a).ToArray() },
            "nextid" => saved with { NextId = 1000 }, "journal" => saved with { Events = [] },
            _ => saved with { Population = saved.Population + 1, Unemployed = saved.Unemployed + 1 }
        };
        string hash = s.Engine.ComputeStateHash();
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(JsonSerializer.Serialize(saved, SimulationJson.Options)));
        Assert.Equal(hash, s.Engine.ComputeStateHash());
    }

    [Fact]
    public void ChecksumAndFormatRejectDamagedFileWithoutChangingLiveWorld()
    {
        var s = Fixtures.Session(); string json = CheckpointStore.Encode(s);
        var envelope = JsonSerializer.Deserialize<CheckpointEnvelope>(json, SimulationJson.Options)!;
        Assert.Throws<CheckpointException>(() => CheckpointStore.Decode(JsonSerializer.Serialize(envelope with
            { PayloadJson = envelope.PayloadJson + " " }, SimulationJson.Options)));
        Assert.Throws<CheckpointException>(() => CheckpointStore.Decode(JsonSerializer.Serialize(envelope with
            { FormatVersion = 2 }, SimulationJson.Options)));
        Assert.Throws<CheckpointException>(() => CheckpointStore.Decode("{broken"));
    }

    [Fact]
    public void AtomicFileReplacementKeepsValidBackup_AndCorruptMainNeverOverwritesIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), "delsk-checkpoint-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "save.json");
        try
        {
            var s = Fixtures.Session(); CheckpointStore.Save(s, path);
            Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); CheckpointStore.Save(s, path);
            Assert.Equal(1, CheckpointStore.Load(path).Session.Engine.Tick);
            Assert.Equal(0, CheckpointStore.Load(path + ".bak").Session.Engine.Tick);
            string backup = File.ReadAllText(path + ".bak");
            File.WriteAllText(path, "{broken"); Assert.Throws<CheckpointException>(() => CheckpointStore.Load(path));
            var result = CheckpointStore.Load(path, recoverBackup: true);
            Assert.True(result.UsedBackup); Assert.Equal(0, result.Session.Engine.Tick);
            Assert.Throws<CheckpointException>(() => CheckpointStore.Save(s, path));
            Assert.Equal(backup, File.ReadAllText(path + ".bak"));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp-*"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}

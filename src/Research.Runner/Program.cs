using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulation.Core;
using Agents.Algorithmic;
using Simulation.Hosting;

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--scenario" or "--output" or "--script" or "--mode" or "--seed" or
            "--replay" or "--ticks" or "--checkpoint" or "--checkpoint-at" or "--load" or "--recover-backup" or "--controllers"))
            throw new ArgumentException("Usage: --scenario PATH --output DIRECTORY [--script cycle|recovery|development|labor|equipment|autonomous|market|pickup|delivery|capital-delivery|logistics|logistics-upgrade] [--mode headless|paced] [--seed N] [--replay COMMANDS --ticks N] [--checkpoint FILE --checkpoint-at T] [--load FILE --ticks FINAL_TICK --recover-backup true]");
        if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate argument");
    }
    string directory = options.GetValueOrDefault("--output", "artifacts/cycle");
    string script = options.GetValueOrDefault("--script", "cycle");
    string mode = options.GetValueOrDefault("--mode", "headless");
    if (script is not ("cycle" or "recovery" or "development" or "labor" or "equipment" or "autonomous" or "market" or "pickup" or "delivery" or "capital-delivery" or "logistics" or "logistics-upgrade") || mode is not ("headless" or "paced"))
        throw new ArgumentException("Invalid script or mode");
    if (options.ContainsKey("--controllers") && (script != "autonomous" || options.ContainsKey("--load") || options.ContainsKey("--replay")))
        throw new ArgumentException("--controllers applies only to a new autonomous run");
    if (options.ContainsKey("--checkpoint-at") && !options.ContainsKey("--checkpoint"))
        throw new ArgumentException("--checkpoint-at requires --checkpoint");
    long? saveTick = options.TryGetValue("--checkpoint-at", out string? saveAt) ? long.Parse(saveAt, CultureInfo.InvariantCulture) : null;
    if (saveTick < 0) throw new ArgumentException("Checkpoint tick must be nonnegative");
    long nextDue = Stopwatch.GetTimestamp();
    void Pace()
    {
        nextDue = checked(nextDue + Stopwatch.Frequency);
        long remaining = nextDue - Stopwatch.GetTimestamp();
        if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
    }
    var watch = Stopwatch.StartNew();
    GameSession session;
    ScenarioDefinition definition;
    long? target = options.TryGetValue("--ticks", out string? ticks) ? long.Parse(ticks, CultureInfo.InvariantCulture) : null;
    RecordedCommand[]? tape = options.TryGetValue("--replay", out string? replayPath) ?
        File.ReadLines(replayPath).Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)
            ?? throw new ArgumentException("Invalid command tape")).ToArray() : null;
    bool saved = false;
    if (options.TryGetValue("--load", out string? loadPath))
    {
        if (options.ContainsKey("--seed") || options.ContainsKey("--scenario"))
            throw new ArgumentException("Loaded configuration/seed cannot be replaced; start a new scenario instead");
        bool recover = options.TryGetValue("--recover-backup", out string? recovery) && bool.Parse(recovery);
        var loaded = CheckpointStore.Load(loadPath, recover, mode == "paced" ? Pace : null);
        session = loaded.Session; definition = session.Engine.Configuration();
        session.RestoreControllers(AlgorithmicController.Restore);
        if (loaded.UsedBackup) Console.WriteLine($"Recovered backup: {loaded.LoadedPath}; tick={session.Engine.Tick}");
        target ??= session.PlaybackFinalTick ?? throw new ArgumentException("Load without a recorded plan requires --ticks FINAL_TICK");
        if (tape is not null)
        {
            if (session.PlaybackFinalTick.HasValue) throw new ArgumentException("Checkpoint already contains its recorded plan");
            var submitted = session.CommandTape.ToHashSet();
            session.ConfigurePlayback(tape.Where(c => c.SubmittedAtTick >= session.Engine.Tick && !submitted.Contains(c)).ToArray(), target.Value);
        }
        script = session.HasControllers ? "autonomous" : session.PlaybackLabel ?? "loaded-state";
        session.SetPaused(false); mode = "loaded-" + mode;
    }
    else
    {
        definition = RunArtifacts.LoadScenario(options.GetValueOrDefault("--scenario", script == "autonomous" ? "scenarios/autonomous-economy.json" : "scenarios/economic-cycle.json"));
        if (options.TryGetValue("--seed", out string? seed)) definition = definition with { Seed = ulong.Parse(seed, CultureInfo.InvariantCulture) };
        if (target.HasValue && tape is null && !saveTick.HasValue && script != "autonomous")
            throw new ArgumentException("--ticks requires --load, --replay or checkpointed fixture playback");
        session = new(new SimulationEngine(definition), beforeStep: mode == "paced" ? Pace : null);
        if (script == "autonomous" && tape is null)
        {
            long horizon = AutonomousRun.Configure(session, definition, options.GetValueOrDefault("--controllers", "scenarios/autonomous-controllers.json"));
            target ??= horizon;
        }
        else if (tape is not null || saveTick.HasValue)
        {
            if (tape is null)
            {
                // Existing fixture scripts are synchronous. Record their complete plan before checkpointed playback.
                var recording = new GameSession(new SimulationEngine(definition));
                RunScript(recording, definition, script); tape = recording.CommandTape; target ??= recording.Engine.Tick;
            }
            if (!target.HasValue) throw new ArgumentException("Replay requires --ticks FINAL_TICK");
            session.ConfigurePlayback(tape, target.Value, script); mode = "recorded-" + mode;
        }
    }
    try
    {
        if (saveTick.HasValue)
        {
            if (saveTick.Value < session.Engine.Tick) throw new ArgumentException("Checkpoint tick precedes loaded tick");
            if (saveTick.Value == session.Engine.Tick) Save(session);
            session.TickCompleted += s => { if (s.Engine.Tick == saveTick.Value) Save(s); };
        }
        if (target.HasValue) session.AdvanceTo(target.Value);
        else RunScript(session, definition, script);
        if (saveTick.HasValue && !saved) throw new ArgumentException("Checkpoint tick was not reached");
        if (!saveTick.HasValue && options.ContainsKey("--checkpoint")) Save(session);
    }
    catch (Exception ex)
    {
        watch.Stop(); RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds, "failed", ex.Message);
        throw;
    }
    watch.Stop(); RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds);
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "succeeded", Tick = session.Engine.Tick,
        StateHash = session.Engine.ComputeStateHash(), Output = Path.GetFullPath(directory), CheckpointSaved = saved }, SimulationJson.Options));
    return 0;
    void Save(GameSession s) { CheckpointStore.Save(s, options["--checkpoint"]); saved = true; }
    static void RunScript(GameSession s, ScenarioDefinition d, string script)
    {
        if (script == "cycle") ScriptedScenarios.EconomicCycle(s, d);
        else if (script == "recovery") ScriptedScenarios.ZeroCashRecovery(s, d);
        else if (script == "development") ScriptedScenarios.Development(s, d);
        else if (script == "labor") ScriptedScenarios.LaborDynamics(s, d);
        else if (script == "logistics") ScriptedScenarios.LogisticsCycle(s, d);
        else if (script == "logistics-upgrade") ScriptedScenarios.LogisticsUpgradeCycle(s, d);
        else if (script is "delivery" or "capital-delivery" or "logistics") ScriptedScenarios.DeliveryLifecycle(s, d, script == "capital-delivery");
        else if (script == "pickup") ScriptedScenarios.PickupLifecycle(s, d);
        else if (script == "market") ScriptedScenarios.MarketWindow(s, d);
        else ScriptedScenarios.EquipmentCycle(s, d);
    }
}
catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }

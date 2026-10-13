using System.Diagnostics;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--scenario" or "--output" or "--script" or "--mode" or "--seed" or "--replay" or "--ticks"))
            throw new ArgumentException("Usage: --scenario PATH --output DIRECTORY [--script cycle|recovery|development] [--mode headless|paced] [--seed N] [--replay COMMANDS --ticks N]");
        if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate argument");
    }
    var definition = RunArtifacts.LoadScenario(options.GetValueOrDefault("--scenario", "scenarios/economic-cycle.json"));
    if (options.TryGetValue("--seed", out string? seed)) definition = definition with { Seed = ulong.Parse(seed, System.Globalization.CultureInfo.InvariantCulture) };
    string directory = options.GetValueOrDefault("--output", "artifacts/cycle");
    string script = options.GetValueOrDefault("--script", "cycle");
    string mode = options.GetValueOrDefault("--mode", "headless");
    if (script is not ("cycle" or "recovery" or "development") || mode is not ("headless" or "paced")) throw new ArgumentException("Invalid script or mode");
    var watch = Stopwatch.StartNew();
    GameSession session;
    if (options.TryGetValue("--replay", out string? replayPath))
    {
        if (!options.TryGetValue("--ticks", out string? horizon)) throw new ArgumentException("Replay requires --ticks");
        var commands = File.ReadLines(replayPath).Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)
            ?? throw new ArgumentException("Invalid command tape")).ToArray();
        session = GameSession.Replay(definition, commands, long.Parse(horizon, System.Globalization.CultureInfo.InvariantCulture));
        mode = "recorded-inputs";
    }
    else
    {
        long nextDue = Stopwatch.GetTimestamp();
        void Pace()
        {
            nextDue = checked(nextDue + Stopwatch.Frequency);
            long remaining = nextDue - Stopwatch.GetTimestamp();
            if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
        }
        session = new(new SimulationEngine(definition), beforeStep: mode == "paced" ? Pace : null);
        try
        {
            if (script == "cycle") ScriptedScenarios.EconomicCycle(session, definition);
            else if (script == "recovery") ScriptedScenarios.ZeroCashRecovery(session, definition);
            else ScriptedScenarios.Development(session, definition);
        }
        catch (Exception ex)
        {
            watch.Stop();
            RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds, "failed", ex.Message);
            throw;
        }
    }
    watch.Stop(); RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds);
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "succeeded", Tick = session.Engine.Tick,
        StateHash = session.Engine.ComputeStateHash(), Output = Path.GetFullPath(directory) }, SimulationJson.Options));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1;
}

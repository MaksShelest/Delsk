using System.Globalization;
using System.Text;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public static class RunArtifacts
{
    public static ScenarioDefinition LoadScenario(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Scenario file too large");
        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(path), SimulationJson.Options)
            ?? throw new ArgumentException("Scenario is empty");
        ScenarioValidation.Validate(scenario); return scenario;
    }
    public static void Export(GameSession session, ScenarioDefinition definition, string directory,
        string timingPolicy, string script, double elapsedMilliseconds, string status = "succeeded", string? failure = null)
    {
        Directory.CreateDirectory(directory);
        var state = session.Engine.Inspect();
        var json = new JsonSerializerOptions(SimulationJson.Options) { WriteIndented = true };
        WriteJson("manifest.json", new
        {
            ScenarioId = definition.Id, definition.Seed, SimulationEngine.RulesVersion,
            ConfigHash = session.Engine.ConfigHash, TimingPolicy = timingPolicy, Script = script,
            Controller = "scripted", Model = (string?)null, RuntimeVersion = Environment.Version.ToString(),
            Commit = Environment.GetEnvironmentVariable("DELSK_COMMIT"), TickSeconds = 1,
            Stage = "P07-P08", Author = "Шелестов Максим Евгеньевич"
        });
        WriteJson("summary.json", new { Status = status, Failure = failure, state.Tick, StateHash = session.Engine.ComputeStateHash(),
            ElapsedMilliseconds = elapsedMilliseconds, AverageMillisecondsPerTick = state.Tick > 0 ? elapsedMilliseconds / state.Tick : (double?)null,
            state.InitialMoneyMinor, state.ExternalMoneyInMinor, state.ExternalMoneyOutMinor, state.Accounts, state.Flows,
            state.Settlements, RamBytes = (long?)null, RamMeasurementReason = "not measured" });
        WriteJson("final-state.json", state); // Inspection export; this is not a loadable checkpoint (P12).
        WriteLines("events.jsonl", session.Engine.EventsSince(0));
        WriteLines("commands.jsonl", session.CommandTape);
        WriteLines("ledger.jsonl", session.Engine.Ledger());
        using var csv = new StreamWriter(Path.Combine(directory, "metrics.csv"), false, new UTF8Encoding(false));
        csv.WriteLine("tick,participant_cash_minor,wages_minor,energy_minor,harvested,produced,exported,state_hash");
        foreach (var sample in session.Samples)
            csv.WriteLine(string.Join(',', new[] { sample.Tick, sample.ParticipantCashMinor, sample.WageExpenseMinor,
                sample.EnergyExpenseMinor, sample.Harvested, sample.Produced, sample.Exported }.Select(x => x.ToString(CultureInfo.InvariantCulture)))
                + "," + sample.StateHash);
        void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, json), new UTF8Encoding(false));
        void WriteLines<T>(string name, IEnumerable<T> values)
        {
            using var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
            foreach (var value in values) writer.WriteLine(JsonSerializer.Serialize(value, SimulationJson.Options));
        }
    }
}

using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

internal static class AutonomousRun
{
    public static long Configure(GameSession session, ScenarioDefinition scenario, string path, Func<AlgorithmicController, IController>? decorate = null)
    {
        var config = ReadConfiguration(scenario, path);
        session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(n with { HumanActorId=config.HumanActorId }, AlgorithmicController.RuntimeSeed(scenario.Seed, n.ActorId), scenario.Participants.Select(p => p.Id)))
            .Select(c => decorate is null ? c : decorate(c)));
        return config.FinalTick;
    }
    public static AutonomousConfiguration ReadConfiguration(ScenarioDefinition scenario, string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Controller configuration too large");
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(File.ReadAllText(path), SimulationJson.Options)
            ?? throw new ArgumentException("Empty controller configuration");
        var ids = scenario.Participants.Select(p => p.Id).ToHashSet();
        if (config.Version != 1 || !ids.Contains(config.HumanActorId) || config.FinalTick < 1 || config.FinalTick > scenario.MaxTicks ||
            config.Npcs is null || config.Npcs.Length != ids.Count - 1 ||
            config.Npcs.Select(n => n.ActorId).Distinct().Count() != config.Npcs.Length ||
            config.Npcs.Any(n => n.ActorId == config.HumanActorId || !ids.Contains(n.ActorId) ||
                !scenario.ResourceNodes.Any(r => r.ItemId == n.PreferredInput)))
            throw new ArgumentException("Invalid autonomous configuration");
        return config;
    }
}

using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

internal static class AutonomousRun
{
    public static long Configure(GameSession session, ScenarioDefinition scenario, string path, Func<AlgorithmicController, IController>? decorate = null, EconomyProfile? economics = null, bool selfBallots = false, OrdinaryGameResolution? ordinary = null)
    {
        var config = ReadConfiguration(scenario, path, ordinary);
        if (economics is not null) config = config with { Npcs = config.Npcs.Select(n => AlgorithmicController.WithEconomy(n, economics.Npc)).ToArray() };
        session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(n with { HumanActorId=config.HumanActorId }, AlgorithmicController.RuntimeSeed(scenario.Seed, n.ActorId), scenario.Participants.Select(p => p.Id)))
            .Select(c => decorate is null ? (IController)c : decorate(c))
            .Concat(selfBallots ? [new SelfBallotController(new(config.HumanActorId))] : Array.Empty<IController>()));
        return config.FinalTick;
    }
    public static AutonomousConfiguration ReadConfiguration(ScenarioDefinition scenario, string path, OrdinaryGameResolution? ordinary=null)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Controller configuration too large");
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(File.ReadAllText(path), SimulationJson.Options)
            ?? throw new ArgumentException("Empty controller configuration");
        if(ordinary is not null)config=config.ForOrdinary(scenario,ordinary.HumanActorId,ordinary.Controllers);
        config.Validate(scenario);
        return config;

    }
}

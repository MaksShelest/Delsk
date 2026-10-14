using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

internal static class AutonomousRun
{
    public static long Configure(GameSession session, ScenarioDefinition scenario, string path)
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
        session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(n, ControllerSeed(scenario.Seed, n.ActorId), ids)));
        return config.FinalTick;
    }
    private static ulong ControllerSeed(ulong seed, long actor)
        => BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"controller-v1:{seed}:{actor}"))));
}

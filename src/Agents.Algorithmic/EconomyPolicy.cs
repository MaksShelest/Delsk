using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    public static ControllerSettings WithEconomy(ControllerSettings actor, JsonElement economics)
    {
        var merged = JsonSerializer.SerializeToNode(actor, SimulationJson.Options)!.AsObject();
        foreach (var field in economics.EnumerateObject())
        {
            if (field.Name is "actorId" or "preferredInput" or "humanActorId" || !merged.ContainsKey(field.Name))
                throw new ArgumentException("Unknown or identity field in economy.npc: " + field.Name);
            merged[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        }
        var settings = merged.Deserialize<ControllerSettings>(SimulationJson.Options)!;
        ValidateSettings(settings); return settings;
    }
}

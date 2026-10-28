using System.Text.Json;
using System.Text.Json.Nodes;
namespace Simulation.Hosting;
public static class VNextNpcEconomyProfile
{
    public const string ProfileId="vnext-g1-npc-economy-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var prior=VNextServiceRunProfile.Create(basis,balance);var npc=JsonNode.Parse(prior.Npc.GetRawText())!.AsObject();npc["coherentEconomy"]=true;
        var p=prior with{Id=ProfileId,VNext=null,Npc=JsonSerializer.SerializeToElement(npc,Simulation.Core.SimulationJson.Options)};
        return p with{VNext=prior.VNext! with{ProjectionVersion="npc-economy-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),ActiveCapabilities=[..prior.VNext!.ActiveCapabilities,"coherent-npc-economy","persisted-rejection-memory"],Limitations=["G1 policy uses the same finite resources and commands; natural progression is measured separately from prepared feature fixtures.","Difficulty is a policy difference, not a proven Hard win-rate advantage."]}};
    }
}

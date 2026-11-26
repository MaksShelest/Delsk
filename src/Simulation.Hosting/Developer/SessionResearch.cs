using Simulation.Core;

namespace Simulation.Hosting;

public sealed partial class GameSession
{
    public void ConfigureResearchPlayback(ResearchModification[] modifications) => ConfigureElectionAuditPlayback(modifications);
    public ResearchModification DeveloperModify(string kind,ParticipantId actor,long target,InventoryId inventory=default,string item="")
    {
        var edit=Engine.DeveloperModify(kind,actor,target,_tape.Count,inventory,item);
        RefreshResearchHash();return edit;
    }
}

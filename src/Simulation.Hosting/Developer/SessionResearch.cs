using Simulation.Core;

namespace Simulation.Hosting;

public sealed partial class GameSession
{
    public void ConfigureResearchPlayback(ResearchModification[] modifications)
    {
        if(_researchPlayback.Length>0 || modifications.Length>1024 || modifications.Where((m,i)=>m.Sequence!=i+1 || m.Tick<Engine.Tick || (i>0 && m.Tick<modifications[i-1].Tick)).Any())
            throw new ArgumentException("InvalidResearchPlayback");
        _researchPlayback=modifications.ToArray();_researchIndex=0;
    }
    public ResearchModification DeveloperModify(string kind,ParticipantId actor,long target,InventoryId inventory=default,string item="")
    {
        var edit=Engine.DeveloperModify(kind,actor,target,_tape.Count,inventory,item);
        RefreshResearchHash();return edit;
    }
    private void ApplyResearchPlayback()
    {
        while(_researchIndex<_researchPlayback.Length)
        {
            var m=_researchPlayback[_researchIndex];
            if(m.Tick>Engine.Tick || m.AfterCommandCount>_tape.Count || m.Tick==Engine.Tick&&m.TravelQuantum>Engine.TravelQuantum)return;
            if(m.Tick!=Engine.Tick || m.AfterCommandCount!=_tape.Count || m.TravelQuantum!=Engine.TravelQuantum)throw new CheckpointException("MissedResearchBarrier");
            Engine.DeveloperModify(m.Kind,m.Actor,m.NewValue,_tape.Count,m.Inventory,m.Item,m);++_researchIndex;RefreshResearchHash();
        }
    }
}

using Simulation.Core;

namespace Simulation.Hosting;

public sealed partial class GameSession
{
    private ResearchModification[] _researchPlayback=[];
    private int _researchIndex;
    internal void ContinueResearchAfterVictory(ParticipantId researcher)
    {
        Engine.ContinueResearchAfterVictory(researcher, _tape.Count);
        RefreshResearchHash();
    }

    internal void ConfigureElectionAuditPlayback(ResearchModification[] modifications)
    {
        if(_researchPlayback.Length>0 || modifications.Length>1024 || modifications.Where((m,i)=>m.Sequence!=i+1 || m.Tick<Engine.Tick || (i>0 && m.Tick<modifications[i-1].Tick)).Any())
            throw new ArgumentException("InvalidResearchPlayback");
#if !DELSK_DEVELOPER
        if (modifications.Any(m => m.Kind != SimulationEngine.ResearchContinuation)) throw new CheckpointException("DeveloperSessionUnsupported");
#endif
        _researchPlayback=modifications.ToArray();_researchIndex=0;
    }
    private void RefreshResearchHash()
    {if(_samples.Count>0 && _samples[^1].Tick==Engine.Tick)_samples[^1]=_samples[^1] with {StateHash=Engine.ComputeStateHash()};}
    private void PumpResearchPlayback()
    {
        while(_researchIndex<_researchPlayback.Length)
        {
            var m=_researchPlayback[_researchIndex];
            if(m.Tick>Engine.Tick || m.AfterCommandCount>_tape.Count || m.Tick==Engine.Tick&&m.TravelQuantum>Engine.TravelQuantum)return;
            if(m.Tick!=Engine.Tick || m.AfterCommandCount!=_tape.Count || m.TravelQuantum!=Engine.TravelQuantum)throw new CheckpointException("MissedResearchBarrier");
            if (m.Kind == SimulationEngine.ResearchContinuation) Engine.ContinueResearchAfterVictory(m.Actor, _tape.Count, m);
            else
            {
#if DELSK_DEVELOPER
                Engine.DeveloperModify(m.Kind,m.Actor,m.NewValue,_tape.Count,m.Inventory,m.Item,m);
#else
                throw new CheckpointException("DeveloperSessionUnsupported");
#endif
            }
            ++_researchIndex;
            RefreshResearchHash();
        }
    }
}

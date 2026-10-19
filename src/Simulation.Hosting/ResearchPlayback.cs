using Simulation.Core;

namespace Simulation.Hosting;

public sealed partial class GameSession
{
    private ResearchModification[] _researchPlayback=[];
    private int _researchIndex;
    private void RefreshResearchHash()
    {if(_samples.Count>0 && _samples[^1].Tick==Engine.Tick)_samples[^1]=_samples[^1] with {StateHash=Engine.ComputeStateHash()};}
    private void PumpResearchPlayback()
    {
#if DELSK_DEVELOPER
        ApplyResearchPlayback();
#else
        if(_researchPlayback.Length>0)throw new CheckpointException("DeveloperSessionUnsupported");
#endif
    }
}

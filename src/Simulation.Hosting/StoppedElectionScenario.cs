using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void StoppedElections(GameSession s,ScenarioDefinition d)
    {
        void CastAll(bool united)
        {foreach(var actor in d.Participants)s.Send(new(actor.Id),new(ActionKind.SetVoteChoice,Election:s.Engine.Inspect().Elections.NextElectionId!.Value,Candidate:new(united?1:actor.Id)));}
        s.AdvanceTo(3);string hash=s.Engine.ComputeStateHash();
        for(int i=0;i<10;i++)s.Step();
        if(!s.Engine.WaitingForBallots || hash!=s.Engine.ComputeStateHash())throw new InvalidOperationException("S8 economy advanced during ballot wait");
        CastAll(false);s.AdvanceTo(6);CastAll(true);
        if(s.Engine.Inspect().Elections.FirstVictory?.Winner!=new ParticipantId(1))throw new InvalidOperationException("S8 frozen tally differs");
        s.Send(new(1),new(ActionKind.ContinueAfterVictory));s.AdvanceTo(10);
        if(s.Engine.ComputeStateHash()!=GameSession.Replay(d,s.CommandTape,10).Engine.ComputeStateHash())throw new InvalidOperationException("S8 replay differs");
    }
}

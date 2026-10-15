using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void ElectionsCycle(GameSession session, ScenarioDefinition definition)
    {
        // S5 numerical blocks without P20 contracts: current employment 30/20/10/0, unemployed40.
        Execute(session, new(4), new(ActionKind.SetVoteChoice, Election: new(1), Candidate: new(1)));
        session.AdvanceTo(10);
        var first = session.Engine.Inspect().Elections.Results.Single();
        if (first.TotalWeight != 64 || first.Tally.Single(t => t.Candidate.Value == 1).Weight != 32 || first.Winner.HasValue)
            throw new InvalidOperationException("Exactly 50 percent must not elect a winner");
        Execute(session, new(2), new(ActionKind.SetVoteChoice, Election: new(2), Candidate: new(1)));
        Execute(session, new(3), new(ActionKind.SetVoteChoice, Election: new(2), Candidate: new(2)));
        session.AdvanceTo(20);
        Execute(session, new(1), new(ActionKind.ContinueAfterVictory));
        Execute(session, new(1), new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
        Wait(session, () => !session.Engine.Observe(new(1)).Participant.Busy);
        session.AdvanceTo(40);
        var w = session.Engine.Inspect(); var elections = w.Elections; var second = elections.Results.Last();
        if (elections.Results.Length != 2 || elections.Mode != ElectionMode.Sandbox || elections.NextVotingTick.HasValue ||
            elections.FirstVictory != new ElectionVictory(new(2), new(1), 20) || elections.SandboxStartedAtTick != 21 ||
            second.Ballots.Sum(b => b.Weight) != 64 || second.Tally.Single(t => t.Candidate.Value == 1).Weight != 52 ||
            second.Tally.Single(t => t.Candidate.Value == 2).Weight != 11 || w.Flows.Single(f => f.ItemId == "wood").Harvested != 2)
            throw new InvalidOperationException("Election blocks, first victory or sandbox continuation differ");
        var replay = GameSession.Replay(definition, session.CommandTape, session.Engine.Tick);
        if (session.Engine.ComputeStateHash() != replay.Engine.ComputeStateHash())
            throw new InvalidOperationException("Election replay mismatch");
    }
}

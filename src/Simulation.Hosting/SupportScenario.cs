using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void SupportCycle(GameSession s, ScenarioDefinition d)
    {
        ParticipantId a = new(1), b = new(2), c = new(3);
        Execute(s, a, new(ActionKind.Harvest, ItemId: "wood", Quantity: 4));
        Wait(s, () => !s.Engine.Observe(a).Participant.Busy);
        Move(s, a, d.Support!.TradingPoint);
        Execute(s, a, new(ActionKind.Transfer, Source: s.Engine.Observe(a).Participant.InventoryId,
            Destination: s.Engine.Observe(a).Support.OwnSection, ItemId: "wood", Quantity: 4));
        Execute(s, a, new(ActionKind.ProposeSupport, SupportSeller: b, Election: new(1), Compensation: [new("wood",4)]));
        Execute(s, b, new(ActionKind.AcceptSupport, SupportProposalId: 1));
        Execute(s, a, new(ActionKind.SignSupport, SupportProposalId: 1));
        Execute(s, b, new(ActionKind.ProposeSupport, SupportSeller: c, Election: new(1), Compensation: [new("wood",2)]));
        long chain = s.Engine.Observe(b).Support.Proposals.Single(p => p.Candidate == b).Id;
        Execute(s, c, new(ActionKind.AcceptSupport, SupportProposalId: chain));
        Move(s, b, d.Support.TradingPoint);
        Execute(s, b, new(ActionKind.SignSupport, SupportProposalId: chain));
        Execute(s, b, new(ActionKind.SetVoteChoice, Election: new(1), Candidate: b));
        Execute(s, c, new(ActionKind.SetVoteChoice, Election: new(1), Candidate: c));
        Execute(s, a, new(ActionKind.ProposeSupport, SupportSeller: new(4), Election: new(1), Compensation: [new("wood",1)]));
        long pending = s.Engine.Observe(a).Support.Proposals.Max(p => p.Id);
        Execute(s, new(4), new(ActionKind.AcceptSupport, SupportProposalId: pending));
        s.AdvanceTo(100);
        var r = s.Engine.Inspect().Elections.Results.Single(); var politics = s.Engine.InspectSupport();
        if (r.TotalWeight != 64 || r.Winner != a || r.Tally.Single(t=>t.Candidate==a).Weight!=52 ||
            r.Tally.Single(t=>t.Candidate==b).Weight!=11 || politics.Commitments.Length!=2 ||
            politics.Commitments.Any(x=>x.Status!=PoliticalCommitmentStatus.Executed) ||
            politics.Proposals.Single(p=>p.Id==pending).Status!=SupportProposalStatus.Expired ||
            s.Engine.ObserveController(new(4)).Support.Proposals.Length!=1)
            throw new InvalidOperationException("S5 support execution, expiry or direct vote blocks differ");
        Execute(s, a, new(ActionKind.ContinueAfterVictory)); s.AdvanceTo(120);
        var replay = GameSession.Replay(d,s.CommandTape,120);
        if (s.Engine.ComputeStateHash()!=replay.Engine.ComputeStateHash()) throw new InvalidOperationException("Support replay differs");
    }
}

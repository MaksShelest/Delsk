using System.Collections.Immutable;
using System.Globalization;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private ElectionId? _nextElection;
    private long? _nextVotingTick;
    private ElectionMode _electionMode;
    private ElectionVictory? _firstVictory;
    private long? _sandboxStartedAtTick;
    private readonly Dictionary<long, long> _voteChoices = [];
    private readonly List<ElectionResult> _electionResults = [];
    private VotingSnapshot? _voting;
    public bool WaitingForBallots => _voting is not null;
    private ElectionSettings Elections => _config.Elections ?? new();

    private void InitializeElections()
    {
        _nextElection = new(1);
        _nextVotingTick = Elections.PeriodTicks;
    }
    private void SetVoteChoice(ActorState actor, CommandEnvelope c)
    {
        if (_electionMode != ElectionMode.Competitive || c.Action.Election != _nextElection)
            throw new RuleViolation("ElectionClosed");
        if (!_actors.ContainsKey(c.Action.Candidate.Value)) throw new RuleViolation("NotAvailable");
        if (Elections.PauseForBallots && RequiredCandidate(actor.Id) is { } required && required != c.Action.Candidate)
            throw new RuleViolation("CommittedVote");
        if (_voting?.Submitted.Any(v => v.Voter.Value == actor.Id) == true) throw new RuleViolation("BallotAlreadyCast");
        _voteChoices[actor.Id] = c.Action.Candidate.Value;
        // This event belongs to the research journal, never to a controller observation.
        Emit("VoteChoiceChanged", actor.Id, _nextElection!.Value.Value, reason: c.Action.Candidate.Value.ToString(CultureInfo.InvariantCulture), command: c.CommandId);
        if (_voting is { } voting)
        {
            _voting = voting with { Submitted = [.. voting.Submitted.Append(new(new(actor.Id), c.Action.Candidate)).OrderBy(v => v.Voter.Value)] };
            Emit("BallotSubmitted", actor.Id, voting.Election.Value, command: c.CommandId);
            if (_voting.Submitted.Length == _actors.Count) CompleteElection(voting.Employment, voting.Population, voting.Unemployed);
        }
    }
    private void ContinueAfterVictory(ActorState actor, CommandEnvelope c)
    {
        if (_firstVictory is null) throw new RuleViolation("NoVictory");
        if (_firstVictory.Winner.Value != actor.Id) throw new RuleViolation("NotAvailable");
        if (_electionMode == ElectionMode.Sandbox) throw new RuleViolation("AlreadySandbox");
        // The first winner decides to continue, through the same authenticated command path.
        _electionMode = ElectionMode.Sandbox;
        _sandboxStartedAtTick = Tick;
        Emit("SandboxStarted", actor.Id, _firstVictory.Election.Value, command: c.CommandId);
    }
    private void AdvanceElections()
    {
        if (_electionMode != ElectionMode.Competitive || Tick != _nextVotingTick) return;
        var id = _nextElection!.Value;
        // One authoritative source for every role. Neither enterprise.Staff nor route copies are added.
        var groups = EmploymentGroups().OrderBy(g => g.Id)
            .Select(g => new ElectionEmployment(new(g.Id), new(g.Owner), g.Role, g.Headcount)).ToImmutableArray();
        if (Elections.PauseForBallots)
        {
            foreach(var actor in _actors.Values){actor.HeadingX=0;actor.HeadingZ=0;}
            _voting = new(id, Tick, _population, _unemployed, groups, []);
            Emit("VotingOpened", entity: id.Value);
            return;
        }
        CompleteElection(groups, _population, _unemployed);
    }
    private ParticipantId? RequiredCandidate(long actor) => _politicalCommitments.Values
        .Where(c => c.Seller.Value == actor && c.Election == _nextElection).Select(c => (ParticipantId?)c.Candidate).SingleOrDefault();
    private void CompleteElection(ImmutableArray<ElectionEmployment> groups, long population, long unemployed)
    {
        var id = _nextElection!.Value;
        var workers = groups.GroupBy(g => g.Owner.Value).ToDictionary(g => g.Key, g => g.Sum(x => (long)x.Headcount));
        var ballots = _actors.Keys.Order().Select(v => new ElectionBallot(new(v),
            new(VoteRecipient(v, id)), Elections.BaseVote, workers.GetValueOrDefault(v))).ToImmutableArray();
        var tally = TallyBallots(ballots);
        long total = ballots.Sum(b => b.Weight);
        var winner = tally.Where(t => (Int128)2 * t.Weight > total).Select(t => (ParticipantId?)t.Candidate).SingleOrDefault();
        var result = new ElectionResult(id, Tick, population, unemployed, groups, ballots, tally, total, winner);
        _electionResults.Add(result);
        foreach (var ballot in ballots)
            Emit("ElectionBallot", ballot.Voter.Value, id.Value, quantity: ballot.Weight,
                reason: ballot.Candidate.Value.ToString(CultureInfo.InvariantCulture));
        Emit("ElectionCompleted", entity: id.Value, quantity: total,
            reason: winner?.Value.ToString(CultureInfo.InvariantCulture) ?? "NoMajority");
        _voteChoices.Clear();
        _voting = null;
        ExecuteSupport(id);
        if (winner is { } elected)
        {
            _firstVictory = new(id, elected, Tick);
            _electionMode = ElectionMode.Victory;
            _nextElection = null; _nextVotingTick = null;
            Emit("FirstVictory", elected.Value, id.Value);
        }
        else
        {
            _nextElection = new(checked(id.Value + 1));
            _nextVotingTick = checked(Tick + Elections.PeriodTicks);
        }
    }
    private static ImmutableArray<ElectionTally> TallyBallots(ImmutableArray<ElectionBallot> ballots)
        => ballots.GroupBy(b => b.Candidate).OrderBy(g => g.Key.Value)
            .Select(g => new ElectionTally(g.Key, g.Sum(b => b.Weight))).ToImmutableArray();
    private ElectionStateView ElectionState() => new(_nextElection, _nextVotingTick, _electionMode,
        _firstVictory, _sandboxStartedAtTick,
        _voteChoices.OrderBy(x => x.Key).Select(x => new VoteChoice(new(x.Key), new(x.Value))).ToImmutableArray(),
        [.. _electionResults], _voting);
    private ElectionObservation ObserveElections(ParticipantId actor) => new(Elections,
        _actors.Keys.Order().Select(p => new ParticipantId(p)).ToImmutableArray(), _nextElection, _nextVotingTick,
        _electionMode, _firstVictory, _sandboxStartedAtTick,
        _voteChoices.TryGetValue(actor.Value, out var candidate) ? new ParticipantId(candidate) : null, [.. _electionResults],
        _voting is not { } voting ? null : new(voting.Election, voting.Tick,
            [.. _actors.Keys.Where(p => !voting.Submitted.Any(v => v.Voter.Value == p)).Order().Select(p => new ParticipantId(p))],
            checked(Elections.BaseVote + voting.Employment.Where(g => g.Owner == actor).Sum(g => (long)g.Headcount)), RequiredCandidate(actor.Value)));
    private void RestoreElections(ElectionStateView s)
    {
        _nextElection = s.NextElectionId; _nextVotingTick = s.NextVotingTick; _electionMode = s.Mode;
        _firstVictory = s.FirstVictory; _sandboxStartedAtTick = s.SandboxStartedAtTick;
        _voteChoices.Clear(); foreach (var c in s.Choices) _voteChoices.Add(c.Voter.Value, c.Candidate.Value);
        _electionResults.Clear(); _electionResults.AddRange(s.Results);
        _voting = s.Voting;
    }

    private bool IsImmediateBallot(CommandEnvelope c) => Elections.PauseForBallots && c.Action.Kind == ActionKind.SetVoteChoice &&
        c.IssuedAtTick == c.NotBeforeTick && c.ExpiresAtTick == c.NotBeforeTick && c.NotBeforeTick > 0;

    private bool IsElectionControlEvent(SimulationEvent e) => Elections.PauseForBallots && e.Phase == 9 &&
        (e.Type is "VoteChoiceChanged" or "BallotSubmitted" or "ElectionBallot" or "ElectionCompleted" or "FirstVictory" or "SupportExecuted" ||
         e.Type is "CommandAccepted" or "CommandRejected" && e.CommandId is { } id && _seenCommands.TryGetValue(id, out var c) && IsImmediateBallot(c));
}

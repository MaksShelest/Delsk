using System.Collections.Immutable;

namespace Simulation.Core;

public record ElectionSettings(long PeriodTicks = 1200, long BaseVote = 1, bool PauseForBallots = false);
public enum ElectionMode { Competitive, Victory, Sandbox }
public record VoteChoice(ParticipantId Voter, ParticipantId Candidate);
public record ElectionEmployment(EmploymentGroupId Group, ParticipantId Owner, EmploymentRole Role, int Headcount);
public record ElectionBallot(ParticipantId Voter, ParticipantId Candidate, long BaseWeight, long WorkerWeight)
{
    public long Weight => checked(BaseWeight + WorkerWeight);
}
public record ElectionTally(ParticipantId Candidate, long Weight);
public record ElectionResult(ElectionId Id, long VotingTick, long Population, long Unemployed,
    ImmutableArray<ElectionEmployment> Employment, ImmutableArray<ElectionBallot> Ballots,
    ImmutableArray<ElectionTally> Tally, long TotalWeight, ParticipantId? Winner);
public record ElectionVictory(ElectionId Election, ParticipantId Winner, long Tick);
public record VotingSnapshot(ElectionId Election, long Tick, long Population, long Unemployed,
    ImmutableArray<ElectionEmployment> Employment, ImmutableArray<VoteChoice> Submitted);
public record VotingObservation(ElectionId Election, long Tick, ImmutableArray<ParticipantId> Awaiting,
    long OwnWeight, ParticipantId? RequiredCandidate);
// Research/checkpoint only: all pending choices. Public observations expose only the observer's choice.
public record ElectionStateView(ElectionId? NextElectionId, long? NextVotingTick, ElectionMode Mode,
    ElectionVictory? FirstVictory, long? SandboxStartedAtTick, ImmutableArray<VoteChoice> Choices,
    ImmutableArray<ElectionResult> Results, VotingSnapshot? Voting = null);
public record ElectionObservation(ElectionSettings Rules, ImmutableArray<ParticipantId> Participants,
    ElectionId? NextElectionId, long? NextVotingTick, ElectionMode Mode, ElectionVictory? FirstVictory,
    long? SandboxStartedAtTick, ParticipantId? OwnChoice, ImmutableArray<ElectionResult> Results, VotingObservation? Voting = null);

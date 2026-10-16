using System.Collections.Immutable;

namespace Simulation.Core;

public record SupportSettings(Cell TradingPoint, long SectionCapacity = 80);
public enum SupportProposalStatus { Proposed, AcceptedAwaitingResources, Rejected, Expired, Signed }
public enum PoliticalCommitmentStatus { Active, Executed }
public record SupportSection(ParticipantId Owner, InventoryId Inventory);
public record SupportProposal(long Id, ParticipantId Candidate, ParticipantId Seller, ElectionId Election,
    InventoryId Source, InventoryId Destination, ImmutableArray<MaterialCost> Compensation,
    ImmutableArray<MaterialCost> Reserved, long CreatedTick, long ExpiryTick, SupportProposalStatus Status,
    long? AcceptedTick = null, long? ClosedTick = null);
public record PoliticalCommitment(long ProposalId, ParticipantId Seller, ParticipantId Candidate, ElectionId Election,
    InventoryId Source, InventoryId Destination, ImmutableArray<MaterialCost> Compensation, long SignedTick,
    PoliticalCommitmentStatus Status, long? ExecutedTick = null);
public record SupportObservation(SupportSettings? Rules, InventoryId OwnSection,
    ImmutableArray<SupportProposal> Proposals, ImmutableArray<PoliticalCommitment> Commitments);
// Full research/checkpoint state. Never add this record to WorldView or a public/debug DTO.
public record SupportResearchState(ImmutableArray<SupportSection> Sections,
    ImmutableArray<SupportProposal> Proposals, ImmutableArray<PoliticalCommitment> Commitments);
public record SupportEventData(SupportProposal Proposal, PoliticalCommitment? Commitment);
public record PublicSimulationEvent(long Sequence, long Tick, int Phase, string Type, ParticipantId? ActorId,
    long EntityId, string ItemId, long Quantity, long AmountMinor, string ReasonCode);

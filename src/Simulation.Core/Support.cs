using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, long> _supportSections = [];
    private readonly Dictionary<long, SupportProposal> _supportProposals = [];
    private readonly Dictionary<long, PoliticalCommitment> _politicalCommitments = [];
    private static bool IsSupportAction(ActionKind kind) => kind is ActionKind.ProposeSupport or ActionKind.AcceptSupport or ActionKind.RejectSupport or ActionKind.SignSupport;
    private static string SupportHold(long proposal, string item) => $"support:{proposal}:{item}";
    private void InitializeSupport()
    {
        if (_config.Support is not { } settings) return;
        foreach (long actor in _actors.Keys.Order())
        {
            long id = _nextId++;
            _economy.Inventories.Add(id, new(id, actor, settings.TradingPoint, settings.SectionCapacity));
            _supportSections.Add(actor, id);
        }
    }
    public SupportResearchState InspectSupport() => new(
        _supportSections.OrderBy(s => s.Key).Select(s => new SupportSection(new(s.Key), new(s.Value))).ToImmutableArray(),
        _supportProposals.Values.OrderBy(p => p.Id).ToImmutableArray(), _politicalCommitments.Values.OrderBy(c => c.ProposalId).ToImmutableArray());
    private SupportObservation ObserveSupport(ParticipantId actor) => new(_config.Support,
        new(_supportSections.GetValueOrDefault(actor.Value)),
        _supportProposals.Values.Where(p => p.Candidate == actor || p.Seller == actor).OrderBy(p => p.Id).ToImmutableArray(),
        _politicalCommitments.Values.Where(c => c.Candidate == actor || c.Seller == actor).OrderBy(c => c.ProposalId).ToImmutableArray());
    private void EmitSupport(string type, SupportProposal proposal, long? actor = null, string? command = null)
        => _events.Add(new(++_eventSequence, Tick, _phase, type, actor.HasValue ? new(actor.Value) : null,
            CommandId: command, Support: new(proposal, _politicalCommitments.GetValueOrDefault(proposal.Id))));
    /// <summary>Public journal: no private command IDs, raw sequence gaps, choices or support terms.</summary>
    public PublicSimulationEvent[] PublicEventsSince(long afterSequence)
        => _events.Where(e => e.Support is null && e.Type != "VoteChoiceChanged" &&
                !(e.CommandId is { } id && _seenCommands.TryGetValue(id, out var c) &&
                  (IsSupportAction(c.Action.Kind) || c.Action.Kind == ActionKind.SetVoteChoice)))
            .Select((e, i) => new PublicSimulationEvent(i + 1L, e.Tick, e.Phase, e.Type, e.ActorId,
                e.EntityId, e.ItemId, e.Quantity, e.AmountMinor, e.ReasonCode)).Where(e => e.Sequence > afterSequence).ToArray();
    private void ProposeSupport(ActorState actor, CommandEnvelope c)
    {
        if (_config.Support is null || _electionMode != ElectionMode.Competitive || c.Action.Election != _nextElection ||
            c.Action.SupportSeller.Value == actor.Id || !_supportSections.TryGetValue(c.Action.SupportSeller.Value, out long destination) ||
            (c.Action.Candidate.Value != 0 && c.Action.Candidate.Value != actor.Id) ||
            (c.Action.Source.Value != 0 && c.Action.Source.Value != _supportSections[actor.Id]) ||
            (c.Action.Destination.Value != 0 && c.Action.Destination.Value != destination)) throw new RuleViolation("NotAvailable");
        var terms = c.Action.Compensation ?? [];
        if (terms.IsDefaultOrEmpty || terms.Length > 8 || terms.Any(m => m is null || string.IsNullOrWhiteSpace(m.ItemId) || m.Quantity <= 0 || !_items.ContainsKey(m.ItemId)) ||
            terms.Select(m => m.ItemId).Distinct(StringComparer.Ordinal).Count() != terms.Length)
            throw new RuleViolation("InvalidCompensation");
        long volume = terms.Sum(m => checked(m.Quantity * _items[m.ItemId].UnitVolume));
        if (volume > _config.Support.SectionCapacity) throw new RuleViolation("InvalidCompensation");
        if (_supportProposals.Values.Count(p => p.Candidate.Value == actor.Id && p.Election == _nextElection &&
            p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources) >= 16)
            throw new RuleViolation("ProposalLimit");
        long id = checked((actor.Id - 1) * 1_000_000_000L + _supportProposals.Values.Count(p => p.Candidate.Value == actor.Id) + 1);
        var proposal = new SupportProposal(id, new(actor.Id), c.Action.SupportSeller, _nextElection!.Value,
            new(_supportSections[actor.Id]), new(destination), terms.OrderBy(m => m.ItemId, StringComparer.Ordinal).ToImmutableArray(),
            [], Tick, _nextVotingTick!.Value, SupportProposalStatus.Proposed);
        _supportProposals.Add(proposal.Id, proposal); EmitSupport("SupportProposed", proposal, actor.Id, c.CommandId);
    }
    private SupportProposal AvailableSupport(ActorState actor, long id)
    {
        if (!_supportProposals.TryGetValue(id, out var p) || (p.Candidate.Value != actor.Id && p.Seller.Value != actor.Id))
            throw new RuleViolation("NotAvailable");
        return p;
    }
    private void AcceptSupport(ActorState actor, CommandEnvelope c)
    {
        var p = AvailableSupport(actor, c.Action.SupportProposalId);
        if (p.Seller.Value != actor.Id || p.Status != SupportProposalStatus.Proposed || p.Election != _nextElection || Tick > p.ExpiryTick)
            throw new RuleViolation("NotAvailable");
        p = p with { Status = SupportProposalStatus.AcceptedAwaitingResources, AcceptedTick = Tick };
        _supportProposals[p.Id] = p; EmitSupport("SupportAccepted", p, actor.Id, c.CommandId);
    }
    private void RejectSupport(ActorState actor, CommandEnvelope c)
    {
        var p = AvailableSupport(actor, c.Action.SupportProposalId);
        if (p.Status is not (SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)) throw new RuleViolation("NotAvailable");
        CloseUnsignedSupport(p, SupportProposalStatus.Rejected, actor.Id, c.CommandId);
    }
    private void ReserveSupportResources()
    {
        foreach (var old in _supportProposals.Values.OrderBy(p => p.CreatedTick).ThenBy(p => p.Id).ToArray())
        {
            if (old.Status != SupportProposalStatus.AcceptedAwaitingResources || Tick > old.ExpiryTick) continue;
            var reserved = old.Reserved.ToDictionary(m => m.ItemId, m => m.Quantity, StringComparer.Ordinal);
            var tx = _economy.Begin(Tick); bool changed = false;
            foreach (var term in old.Compensation)
            {
                long held = reserved.GetValueOrDefault(term.ItemId);
                long add = Math.Min(term.Quantity - held, _economy.Inventories[old.Source.Value].Free(term.ItemId));
                if (add <= 0) continue;
                if (held > 0) tx.ReleaseStock(old.Source.Value, SupportHold(old.Id, term.ItemId));
                tx.ReserveStock(old.Source.Value, SupportHold(old.Id, term.ItemId), term.ItemId, checked(held + add));
                reserved[term.ItemId] = checked(held + add); changed = true;
            }
            if (!changed) continue;
            var p = old with { Reserved = reserved.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new MaterialCost(x.Key, x.Value)).ToImmutableArray() };
            tx.Commit(); _supportProposals[p.Id] = p; EmitSupport("SupportResourcesReserved", p);
        }
    }
    private void SignSupport(ActorState actor, CommandEnvelope c)
    {
        var p = AvailableSupport(actor, c.Action.SupportProposalId);
        if (p.Status != SupportProposalStatus.AcceptedAwaitingResources || p.Election != _nextElection || Tick > p.ExpiryTick ||
            _politicalCommitments.Values.Any(x => x.Seller == p.Seller && x.Election == p.Election)) throw new RuleViolation("NotAvailable");
        RequireIdle(actor);
        RequireActionLocation(actor, c.Action);
        if (!p.Reserved.SequenceEqual(p.Compensation)) throw new RuleViolation("IncompleteCompensation");
        var commitment = new PoliticalCommitment(p.Id, p.Seller, p.Candidate, p.Election, p.Source, p.Destination,
            p.Compensation, Tick, PoliticalCommitmentStatus.Active);
        var tx = _economy.Begin(Tick);
        foreach (var term in p.Compensation)
        {
            tx.ConsumeReserved(p.Source.Value, SupportHold(p.Id, term.ItemId), term.Quantity);
            tx.AddStock(p.Destination.Value, term.ItemId, term.Quantity);
        }
        p = p with { Status = SupportProposalStatus.Signed, ClosedTick = Tick, Reserved = [] };
        tx.Commit(); _politicalCommitments.Add(p.Id, commitment); _supportProposals[p.Id] = p;
        EmitSupport("SupportSigned", p, actor.Id, c.CommandId);
    }
    private void CloseUnsignedSupport(SupportProposal old, SupportProposalStatus status, long? actor = null, string? command = null)
    {
        var tx = _economy.Begin(Tick);
        foreach (var material in old.Reserved) tx.ReleaseStock(old.Source.Value, SupportHold(old.Id, material.ItemId));
        var p = old with { Status = status, ClosedTick = Tick, Reserved = [] };
        tx.Commit(); _supportProposals[p.Id] = p; EmitSupport(status == SupportProposalStatus.Expired ? "SupportExpired" : "SupportRejected", p, actor, command);
    }
    private void ExpireSupport()
    {
        foreach (var p in _supportProposals.Values.OrderBy(p => p.Id).ToArray())
            if (p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources && p.ExpiryTick <= Tick)
                CloseUnsignedSupport(p, SupportProposalStatus.Expired);
    }
    private long VoteRecipient(long voter, ElectionId election) => _politicalCommitments.Values
        .SingleOrDefault(c => c.Seller.Value == voter && c.Election == election)?.Candidate.Value ?? _voteChoices.GetValueOrDefault(voter, voter);
    private void ExecuteSupport(ElectionId election)
    {
        foreach (var c in _politicalCommitments.Values.Where(c => c.Election == election && c.Status == PoliticalCommitmentStatus.Active).ToArray())
        {
            _politicalCommitments[c.ProposalId] = c with { Status = PoliticalCommitmentStatus.Executed, ExecutedTick = Tick };
            EmitSupport("SupportExecuted", _supportProposals[c.ProposalId]);
        }
    }
    private void RestoreSupport(SupportResearchState saved)
    {
        if (!saved.Sections.OrderBy(s => s.Owner.Value).SequenceEqual(_supportSections.OrderBy(s => s.Key)
            .Select(s => new SupportSection(new(s.Key), new(s.Value)))))
            throw new CheckpointException("InvalidSupportSectionReferences");
        _supportSections.Clear(); _supportProposals.Clear(); _politicalCommitments.Clear();
        foreach (var s in saved.Sections) _supportSections.Add(s.Owner.Value, s.Inventory.Value);
        foreach (var p in saved.Proposals) _supportProposals.Add(p.Id, p);
        foreach (var c in saved.Commitments) _politicalCommitments.Add(c.ProposalId, c);
    }
}

using System.Collections.Immutable;

namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateSupportSettings(ScenarioDefinition s)
    {
        if (s.Support is not { } p) return;
        if (p.SectionCapacity <= 0 || !new GridMap(s).Walkable(p.TradingPoint) ||
            (s.Plots ?? []).Any(x => x.Position == p.TradingPoint && !x.IsCorridor))
            throw new ArgumentException("Invalid support trading point");
    }
}

public sealed partial class SimulationEngine
{
    private void AddExpectedSupportHolds(Dictionary<(long Inventory, string Hold), StockReservation> stock)
    {
        foreach (var p in _supportProposals.Values)
            foreach (var r in p.Reserved) stock.Add((p.Source.Value, SupportHold(p.Id, r.ItemId)), new(r.ItemId, r.Quantity));
    }
    private void ValidateSupportInvariants()
    {
        if (_config.Support is not { } settings)
        {
            if (_supportSections.Count + _supportProposals.Count + _politicalCommitments.Count != 0)
                throw new InvalidOperationException("Unexpected support state");
            return;
        }
        if (!_supportSections.Keys.Order().SequenceEqual(_actors.Keys.Order()) ||
            _supportSections.Values.Distinct().Count() != _actors.Count)
            throw new InvalidOperationException("Invalid support identities");
        foreach (var section in _supportSections)
        {
            var inventory = _economy.Inventories[section.Value];
            if (inventory.Owner != section.Key || inventory.Position != settings.TradingPoint || inventory.Capacity != settings.SectionCapacity ||
                _actors.Values.Any(a => a.Inventory == inventory.Id) || _buildings.Values.Any(b => b.Input == inventory.Id || b.Output == inventory.Id))
                throw new InvalidOperationException("Invalid support section");
        }
        if (_politicalCommitments.Values.GroupBy(c => (c.Seller, c.Election)).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Support block committed twice");
        foreach (var group in _supportProposals.Values.GroupBy(p => p.Candidate))
            if (group.OrderBy(p => p.Id).Where((p, index) => p.Id != checked((group.Key.Value - 1) * 1_000_000_000L + index + 1)).Any())
                throw new InvalidOperationException("Invalid private proposal namespace");
        foreach (var p in _supportProposals.Values)
        {
            if (!Enum.IsDefined(p.Status) || !_actors.ContainsKey(p.Candidate.Value) || !_actors.ContainsKey(p.Seller.Value) ||
                p.Candidate == p.Seller || p.Source.Value != _supportSections[p.Candidate.Value] || p.Destination.Value != _supportSections[p.Seller.Value] ||
                p.CreatedTick < 1 || p.CreatedTick > Tick || p.Election.Value < 1 || p.CreatedTick <= checked((p.Election.Value - 1) * Elections.PeriodTicks) ||
                p.ExpiryTick != checked(p.Election.Value * Elections.PeriodTicks) || p.CreatedTick > p.ExpiryTick ||
                p.Compensation.IsDefaultOrEmpty || p.Compensation.Length > 8 || p.Reserved.IsDefault ||
                p.Compensation.Any(m => m.Quantity <= 0 || !_items.ContainsKey(m.ItemId)) ||
                !p.Compensation.Select(m => m.ItemId).SequenceEqual(p.Compensation.Select(m => m.ItemId).Distinct().Order(StringComparer.Ordinal)) ||
                p.Compensation.Sum(m => checked(m.Quantity * _items[m.ItemId].UnitVolume)) > settings.SectionCapacity ||
                p.AcceptedTick < p.CreatedTick || p.AcceptedTick > Tick || p.AcceptedTick > p.ExpiryTick ||
                (p.Status == SupportProposalStatus.Proposed && p.AcceptedTick.HasValue) ||
                (p.Status is SupportProposalStatus.AcceptedAwaitingResources or SupportProposalStatus.Signed && !p.AcceptedTick.HasValue))
                throw new InvalidOperationException("Invalid support proposal");
            bool open = p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources;
            if (open != !p.ClosedTick.HasValue || p.ClosedTick < p.CreatedTick || p.ClosedTick > Tick || p.ClosedTick > p.ExpiryTick ||
                (open && (p.Election != _nextElection || p.ExpiryTick <= Tick)) ||
                (p.Status == SupportProposalStatus.Expired && p.ClosedTick != p.ExpiryTick) ||
                (p.Status != SupportProposalStatus.AcceptedAwaitingResources && p.Reserved.Length > 0))
                throw new InvalidOperationException("Invalid support lifecycle");
            if (!p.Reserved.Select(r => r.ItemId).SequenceEqual(p.Reserved.Select(r => r.ItemId).Distinct().Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("Duplicate compensation hold");
            foreach (var r in p.Reserved)
                if (r.Quantity <= 0 || r.Quantity > (p.Compensation.SingleOrDefault(m => m.ItemId == r.ItemId)?.Quantity ?? 0) ||
                    !_economy.Inventories[p.Source.Value].Reservations.TryGetValue(SupportHold(p.Id, r.ItemId), out var hold) ||
                    hold != new StockReservation(r.ItemId, r.Quantity))
                    throw new InvalidOperationException("Invalid compensation hold");
            bool signed = _politicalCommitments.TryGetValue(p.Id, out var c);
            if ((p.Status == SupportProposalStatus.Signed) != signed) throw new InvalidOperationException("Missing or duplicate political commitment");
            if (c is not null)
            {
                var election = _electionResults.SingleOrDefault(r => r.Id == c.Election);
                if (!Enum.IsDefined(c.Status) || c.Seller != p.Seller || c.Candidate != p.Candidate || c.Election != p.Election ||
                    c.Source != p.Source || c.Destination != p.Destination || c.Compensation.IsDefault || !c.Compensation.SequenceEqual(p.Compensation) ||
                    c.SignedTick != p.ClosedTick || c.SignedTick < p.AcceptedTick || c.SignedTick > p.ExpiryTick ||
                    (c.Status == PoliticalCommitmentStatus.Executed) != (election is not null) ||
                    c.ExecutedTick != election?.VotingTick ||
                    (election is not null && election.Ballots.Single(b => b.Voter == c.Seller).Candidate != c.Candidate))
                    throw new InvalidOperationException("Invalid political commitment or execution");
                var signedEvents = _events.Where(e => e.Type == "SupportSigned" && e.Support?.Proposal.Id == p.Id).ToArray();
                if (signedEvents.Length != 1 || signedEvents[0].Phase != 3 || signedEvents[0].Tick != c.SignedTick ||
                    signedEvents[0].Support!.Commitment is not { } receipt || receipt.Status != PoliticalCommitmentStatus.Active ||
                    receipt.ExecutedTick.HasValue || receipt.ProposalId != p.Id || !receipt.Compensation.SequenceEqual(p.Compensation) ||
                    receipt.Source != p.Source || receipt.Destination != p.Destination || receipt.Candidate != p.Candidate || receipt.Seller != p.Seller ||
                    receipt.SignedTick != c.SignedTick || receipt.Election != p.Election)
                    throw new InvalidOperationException("Political receipt journal mismatch");
                if (_events.Count(e => e.Type == "SupportExecuted" && e.Support?.Proposal.Id == p.Id) != (election is null ? 0 : 1))
                    throw new InvalidOperationException("Political execution journal mismatch");
            }
            var history = _events.Where(e => e.Support?.Proposal.Id == p.Id).ToArray();
            if (history.Length == 0 || history[0].Type != "SupportProposed" || history[0].Tick != p.CreatedTick ||
                !SameProposal(history[^1].Support!.Proposal, p)) throw new InvalidOperationException("Support proposal differs from journal");
        }
        if (_politicalCommitments.Keys.Any(k => !_supportProposals.ContainsKey(k)) ||
            _events.Any(e => e.Support is { } data && !_supportProposals.ContainsKey(data.Proposal.Id)))
            throw new InvalidOperationException("Orphan political state");
    }
    private static bool SameProposal(SupportProposal a, SupportProposal b) =>
        a with { Compensation = b.Compensation, Reserved = b.Reserved } == b &&
        a.Compensation.SequenceEqual(b.Compensation) && a.Reserved.SequenceEqual(b.Reserved);
}

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulation.Core;

namespace Agents.Algorithmic;

/// <summary>A model may select a trusted plan; it cannot edit its commands, prices or identity.</summary>
public record PolicyOption(string Id, string Goal, string Reason, long Utility,
    ImmutableArray<GameAction> Actions, string RelevantVersion);

public sealed partial class AlgorithmicController
{
    public ControllerSettings ModelPolicySettings => _state.Settings;
    public ImmutableArray<TrustEntry> ModelTrust => [.. _state.Trust];
    private static string Digest(object value) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, SimulationJson.Options))));

    // Pure preview: no PRNG draw, reservation or plan mutation.
    public ImmutableArray<PolicyOption> ModelOptions(ControllerObservation o)
    {
        if (o.Participant.Id != Actor) throw new ArgumentException("Observation actor mismatch");
        if (!CanOfferModelOptions(o)) return [];
        return AvailableModelCandidates(o).OrderByDescending(c => c.Score).ThenBy(c => c.Goal, StringComparer.Ordinal)
            .ThenBy(c => c.Reason, StringComparer.Ordinal)
            .DistinctBy(c => c.Goal).Select(c => Option(o, c)).Take(4)
            .Append(new("hold", "Hold", "WaitOneTick", 0, [], "hold")).ToImmutableArray();
    }
    public bool RequiresUninterruptedAlgorithm(ControllerObservation o) => HasObligations(o) ||
        UsesEconomicPlanning&&(o.Services?.OwnOrders.Any(s=>s.Active)==true||o.EconomicOpportunities?.Overflow.Length>0) ||
        o.OwnMarketBids.Any(b => o.MarketOffers.Any(m => m.Id == b.OfferId && m.Status == MarketOfferStatus.Auction));
    private bool CanOfferModelOptions(ControllerObservation o) => _state.PendingCommandId is null &&
        _state.NextStep == 0 && _state.SuspendedPlan.Length == 0 && !o.Participant.Busy &&
        !Carrier(o).Stock.Any(s => s.Reserved > 0) && !RequiresUninterruptedAlgorithm(o);
    private IEnumerable<Candidate> AvailableModelCandidates(ControllerObservation o)
    {
        var candidates = DecisionCandidates(o).Concat(NegotiationResponses(o)).AsEnumerable();
        if (_state.NextStep < _state.Plan.Length && PlanRelevant(o) && (!HasObligations(o) || ObligationGoal(_state.Goal)))
            candidates = new[] { new Candidate(_state.Goal, _state.Reason, long.MaxValue,
                _state.Plan.Skip(_state.NextStep).ToArray()) }.Concat(candidates);
        return candidates;
    }
    public void DeferUnsubmittedDecision()
    {
        if (_state.PendingCommandId is not null || _state.NextStep != 0 || _state.SuspendedPlan.Length != 0)
            throw new InvalidOperationException("An active plan cannot be deferred for a model");
        // Keep feedback/PRNG/budgets; discard only the new plan which has never been submitted.
        // At applyTick the same existing selector re-evaluates fallback from the current observation.
        _state = _state with { Plan = [], NextStep = 0, NextDecisionTick = 0 };
    }
    private static PolicyOption Option(ControllerObservation o, Candidate c)
    {
        var actions = c.Steps.Select(p => p.Action).ToImmutableArray();
        var a = actions.ToArray();
        // Only own/bilateral state and referenced public conditions affect this revision.
        // Tick, other owners' inventories and future/private market streams never participate.
        var relevant = new
        {
            o.Account, o.Participant, o.Inventories, o.Enterprises, o.Equipment, o.OwnLogistics,
            o.EmploymentGroups, o.OwnDeliveryContracts, o.OwnMarketAllocations, o.OwnMarketBids, o.OwnSupplyRequests,
            Capital = o.CapitalOffers.Where(x => a.Any(v => v.OfferId == x.Id)),
            Market = o.MarketOffers.Where(x => a.Any(v => v.MarketOfferId == x.Id)),
            Buys = o.BuyOffers.Where(x => a.Any(v => v.BuyOfferId == x.Id)),
            Plots = o.Plots.Where(x => a.Any(v => v.Plot == x.Id || v.Kind == ActionKind.Build && v.Target == x.Position)),
            Negotiations = o.Negotiations.Where(n=>a.Any(v=>v.NegotiationId==n.Id)),TradingParticipants=o.TradingParticipants,
            Support = o.Support.Proposals.Where(x => a.Any(v => v.SupportProposalId == x.Id)),
            Election = a.Any(v => v.Kind is ActionKind.ProposeSupport or ActionKind.SetVoteChoice or ActionKind.SignSupport)
                ? o.Elections.NextElectionId : null
        };
        return new("p" + Digest(new { c.Goal, c.Reason, Actions = actions })[..16], c.Goal, c.Reason,
            c.Score, actions, Digest(relevant));
    }
    public bool TrySelectModelOption(ControllerObservation o, PolicyOption offered, out ControllerDecision decision)
    {
        decision = new("Hold", "StaleRelevantState", []);
        if (!CanOfferModelOptions(o)) return false;
        if (offered.Id == "hold") { decision = new("Hold", "ModelHold", []); return true; }
        var current = AvailableModelCandidates(o).Select(c => Option(o, c))
            .FirstOrDefault(p => p.Id == offered.Id && p.RelevantVersion == offered.RelevantVersion);
        if (current is null) return false;
        RememberMarketIntent(current.Goal,current.Actions);
        if(UsesEconomicPlanning&&current.Goal=="BuildProduction"&&StartupIndustry(o) is {} planned)
            _state=_state with{EconomyMemory=_state.EconomyMemory! with{ConstructionIndustry=planned.Id}};
        _state = _state with { Goal = current.Goal, Reason = current.Reason,
            Plan = current.Actions.Select(a => new PlanStep(a)).ToArray(), NextStep = 0 };
        decision = new(current.Goal, "ModelChoice", [current.Actions[0]]);
        return true;
    }
}

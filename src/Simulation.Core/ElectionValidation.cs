using System.Globalization;

namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateElectionSettings(ScenarioDefinition s)
    {
        var e = s.Elections ?? new();
        if (e.PeriodTicks is < 1 or > 1000000 || e.BaseVote <= 0)
            throw new ArgumentException("Invalid election settings");
        var labor = s.Labor ?? new();
        Int128 maxPopulation = (Int128)s.Population + (Int128)(s.MaxTicks / labor.GrowthPeriodTicks) * labor.GrowthPerPeriod;
        if (maxPopulation + (Int128)s.Participants.Length * e.BaseVote > long.MaxValue)
            throw new ArgumentException("Election weight overflow");
    }
}

public sealed partial class SimulationEngine
{
    private void ValidateElectionInvariants()
    {
        if (!Enum.IsDefined(_electionMode)) throw new InvalidOperationException("Invalid election mode");
        var actors = _actors.Keys.Order().ToArray();
        long period = Elections.PeriodTicks;
        var victoryResult = _electionResults.SingleOrDefault(r => r.Winner.HasValue);
        if (_firstVictory is null)
        {
            if (_electionMode != ElectionMode.Competitive || _sandboxStartedAtTick.HasValue || victoryResult is not null ||
                _electionResults.Count != Tick / period || _nextElection != new ElectionId(_electionResults.Count + 1L) ||
                _nextVotingTick != checked((_electionResults.Count + 1L) * period))
                throw new InvalidOperationException("Invalid election schedule");
        }
        else if (_electionMode == ElectionMode.Competitive || _nextElection.HasValue || _nextVotingTick.HasValue ||
            _voteChoices.Count != 0 || victoryResult is null || victoryResult != _electionResults.LastOrDefault() ||
            _firstVictory != new ElectionVictory(victoryResult.Id, victoryResult.Winner!.Value, victoryResult.VotingTick) ||
            (_electionMode == ElectionMode.Sandbox) != _sandboxStartedAtTick.HasValue ||
            _sandboxStartedAtTick <= _firstVictory.Tick || _sandboxStartedAtTick > Tick)
            throw new InvalidOperationException("Invalid first victory or sandbox state");

        if (_voteChoices.Any(c => !_actors.ContainsKey(c.Key) || !_actors.ContainsKey(c.Value)))
            throw new InvalidOperationException("Invalid vote choice reference");
        // Reconstruct numeric employment from the committed journal, not from another copy of Staff.
        // This also validates old ballots after later hiring, construction and new purchases.
        var known = EmploymentGroups().ToDictionary(g => g.Id);
        var counts = _config.Enterprises.ToDictionary(e => e.Id, e => e.Staff);
        foreach (var b in _config.Buildings ?? [])
            if (_buildingTypes[b.TypeId].Kind == BuildingKind.Production) counts.Add(b.Id, b.Staff);
        long population = _config.Population;
        int cursor = 0;
        var choices = new Dictionary<long, long>();
        for (int i = 0; i < _electionResults.Count; i++)
        {
            var r = _electionResults[i];
            if (r.Id.Value != i + 1 || r.VotingTick != checked((i + 1L) * period) || r.VotingTick > Tick ||
                r.Employment.IsDefault || r.Ballots.IsDefault || r.Tally.IsDefault)
                throw new InvalidOperationException("Invalid election result identity");
            while (cursor < _events.Count && _events[cursor].Tick <= r.VotingTick)
            {
                var e = _events[cursor++];
                if ((e.Type is "EquipmentPurchased" or "LogisticsPurchased" or "BuildingConstructed") && known.ContainsKey(e.EntityId))
                    counts.Add(e.EntityId, 0);
                else if (e.Type == "EmploymentChanged") counts[e.EntityId] = checked(counts[e.EntityId] + (int)e.Quantity);
                else if (e.Type == "PopulationGrew") population = checked(population + e.Quantity);
                else if (e.Type == "VoteChoiceChanged") choices[e.ActorId!.Value.Value] = long.Parse(e.ReasonCode, CultureInfo.InvariantCulture);
            }
            var expected = counts.OrderBy(c => c.Key).Select(c => new ElectionEmployment(new(c.Key),
                new(known[c.Key].Owner), known[c.Key].Role, c.Value)).ToArray();
            if (!r.Employment.SequenceEqual(expected) || r.Employment.Any(g => g.Headcount < 0) ||
                r.Population != population || r.Unemployed < 0 || r.Population != checked(r.Unemployed + counts.Values.Sum(n => (long)n)) ||
                !r.Ballots.Select(b => b.Voter.Value).SequenceEqual(actors))
                throw new InvalidOperationException("Election employment counted more than once or differs from current voting workforce");
            foreach (var b in r.Ballots)
                if (!_actors.ContainsKey(b.Candidate.Value) || b.BaseWeight != Elections.BaseVote ||
                    b.WorkerWeight != r.Employment.Where(g => g.Owner == b.Voter).Sum(g => (long)g.Headcount) ||
                    b.Candidate.Value != choices.GetValueOrDefault(b.Voter.Value, b.Voter.Value))
                    throw new InvalidOperationException("Invalid election ballot weight or choice");
            choices.Clear();
            if (!r.Tally.SequenceEqual(TallyBallots(r.Ballots)) || r.TotalWeight != r.Ballots.Sum(b => b.Weight) ||
                r.TotalWeight != checked(Elections.BaseVote * actors.Length + r.Population - r.Unemployed) ||
                r.Winner != r.Tally.Where(t => (Int128)2 * t.Weight > r.TotalWeight).Select(t => (ParticipantId?)t.Candidate).SingleOrDefault())
                throw new InvalidOperationException("Invalid strict majority or total election weight");
            var ballotEvents = _events.Where(e => e.Type == "ElectionBallot" && e.EntityId == r.Id.Value).ToArray();
            if (ballotEvents.Length != actors.Length || ballotEvents.Where((e, index) => e.Phase != 8 || e.Tick != r.VotingTick ||
                e.ActorId != r.Ballots[index].Voter || e.Quantity != r.Ballots[index].Weight ||
                e.ReasonCode != r.Ballots[index].Candidate.Value.ToString(CultureInfo.InvariantCulture)).Any())
                throw new InvalidOperationException("Election journal ballot mismatch");
            var completed = _events.SingleOrDefault(e => e.Type == "ElectionCompleted" && e.EntityId == r.Id.Value);
            if (completed is null || completed.Tick != r.VotingTick || completed.Phase != 8 || completed.Quantity != r.TotalWeight ||
                completed.ReasonCode != (r.Winner?.Value.ToString(CultureInfo.InvariantCulture) ?? "NoMajority"))
                throw new InvalidOperationException("Election journal result mismatch");
        }
        // No hidden carry of a previous election's choice; declared choices remain private until voting.
        long lastVote = _electionResults.LastOrDefault()?.VotingTick ?? 0;
        var pendingChoices = _events.Where(e => e.Type == "VoteChoiceChanged" && e.Tick > lastVote)
            .GroupBy(e => e.ActorId!.Value.Value).ToDictionary(g => g.Key, g => long.Parse(g.Last().ReasonCode, CultureInfo.InvariantCulture));
        if (!_voteChoices.OrderBy(c => c.Key).SequenceEqual(pendingChoices.OrderBy(c => c.Key)) ||
            _events.Count(e => e.Type == "ElectionCompleted") != _electionResults.Count ||
            _events.Count(e => e.Type == "FirstVictory") != (_firstVictory is null ? 0 : 1) ||
            _events.Count(e => e.Type == "SandboxStarted") != (_sandboxStartedAtTick.HasValue ? 1 : 0))
            throw new InvalidOperationException("Election state differs from journal");
        if (_firstVictory is { } first)
        {
            var won = _events.Single(e => e.Type == "FirstVictory");
            if (won.ActorId != first.Winner || won.EntityId != first.Election.Value || won.Tick != first.Tick || won.Phase != 8)
                throw new InvalidOperationException("First victory journal mismatch");
        }
        if (_sandboxStartedAtTick is { } started)
        {
            var sandbox = _events.Single(e => e.Type == "SandboxStarted");
            if (sandbox.Tick != started || sandbox.Phase != 1 || sandbox.EntityId != _firstVictory!.Election.Value ||
                sandbox.ActorId != _firstVictory.Winner)
                throw new InvalidOperationException("Sandbox journal mismatch");
        }
    }
}

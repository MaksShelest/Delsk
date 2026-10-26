using System.Numerics;

namespace Simulation.Core;

// One aggregate per facility/role. There are no individual employee objects.
internal sealed class EmploymentGroup(long id, long owner, int headcount, int capacity, long wage,
    EmploymentRole role = EmploymentRole.Production)
{
    public long Id { get; } = id;
    public EmploymentRole Role { get; } = role;
    public long Owner { get; } = owner;
    public int Headcount { get; set; } = headcount;
    public int Capacity { get; set; } = capacity;
    public long Wage { get; set; } = wage;
    public long AttractionWage { get; set; } = wage;
    public long? PendingWage { get; set; }
    public long? PayEffectiveTick { get; set; }
    public long? PendingAttractionWage { get; set; }
    public long? AttractionEffectiveTick { get; set; }
    // Units of 1/MobilityDenominator, including at most one step of unused quota.
    public long MobilityCredit { get; set; }
    public int Target { get; set; } = headcount;
    public bool Ready { get; set; }
    public string ReadinessReason { get; set; } = "NotAssessed";
    public long AssessedAtTick { get; set; }
    public EmploymentView View() => new(new(Id), new(Owner), Role, Headcount,
        Capacity, Wage, AttractionWage, PendingWage, PayEffectiveTick, PendingAttractionWage,
        AttractionEffectiveTick, MobilityCredit, Target, Ready, ReadinessReason, AssessedAtTick);
}

internal record LaborWorkplace(EmploymentGroup Group, int MinimumStaff, string VacancyReason, Func<int, long> Cost);

internal record LaborCandidate(long Id, int Capacity, long Wage);
internal record WeightedPlace(long Id, int Capacity, BigInteger Weight);

internal static class LaborAllocation
{
    public static Dictionary<long, int> Cubic(long population, LaborCandidate[] candidates, DeterministicRandom random, int exponent = 3)
        => Weighted(population, candidates.Where(c => c.Capacity > 0 && c.Wage > 0)
            .Select(c => new WeightedPlace(c.Id, c.Capacity, BigInteger.Pow(c.Wage, exponent))).ToArray(), random);

    // Exact water filling, followed by largest remainders; no floating point and no fixed first ID.
    public static Dictionary<long, int> Weighted(long population, WeightedPlace[] places, DeterministicRandom random)
    {
        var active = places.Where(p => p.Capacity > 0 && p.Weight > 0).OrderBy(p => p.Id).ToList();
        var result = places.ToDictionary(p => p.Id, _ => 0);
        long remaining = Math.Min(population, active.Sum(p => (long)p.Capacity));
        while (remaining > 0 && active.Count > 0)
        {
            BigInteger sum = active.Aggregate(BigInteger.Zero, (n, p) => n + p.Weight);
            var saturated = active.Where(p => remaining * p.Weight >= p.Capacity * sum).ToArray();
            if (saturated.Length > 0)
            {
                foreach (var p in saturated)
                { result[p.Id] = p.Capacity; remaining -= p.Capacity; active.Remove(p); }
                continue;
            }
            foreach (var p in active) result[p.Id] = (int)(remaining * p.Weight / sum);
            int extra = checked((int)(remaining - active.Sum(p => (long)result[p.Id])));
            if (extra > 0)
            {
                var order = active.Select(p => p.Id).ToArray(); random.Shuffle(order);
                var rank = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
                foreach (var p in active.OrderByDescending(p => remaining * p.Weight % sum)
                    .ThenBy(p => rank[p.Id]).Take(extra)) ++result[p.Id];
            }
            break;
        }
        return result;
    }
}

public sealed partial class SimulationEngine
{
    private readonly DeterministicRandom _laborRandom;
    private long _population;
    private long _unemployed;
    private long _lastGrowthPeriod;
    private LaborSettings Labor => _config.Labor ?? new();

    private IEnumerable<EmploymentGroup> EmploymentGroups() => ProductionUnits().Select(e => e.Group)
        .Concat(_equipment.Values.Select(e => e.Group)).Concat(_logistics.Values.Select(e => e.Group));
    private LaborWorkplace[] LaborWorkplaces() => ProductionUnits()
        .Select(e => new LaborWorkplace(e.Group, ParallelWorkshops ? 1 : e.Recipe.MinimumStaff, VacancyReason(e), n => NextWorkCost(e, n)))
        .Concat(_equipment.Values.Select(e => new LaborWorkplace(e.Group, e.Characteristics.MinimumCrew,
            EquipmentVacancyReason(e), n => NextEquipmentCost(e, n))))
        .Concat(_logistics.Values.Select(e => new LaborWorkplace(e.Group, e.MinimumCrew,
            LogisticsVacancyReason(e), n => NextLogisticsCost(e))))
        .OrderBy(e => e.Group.Id).ToArray();

    private void SetWage(ActorState actor, CommandEnvelope c)
    {
        var g = EmploymentGroups().FirstOrDefault(g => g.Id == c.Action.Group.Value && g.Owner == actor.Id)
            ?? throw new RuleViolation("NotAvailable");
        if (c.Action.WageMinor < 0) throw new RuleViolation("InvalidWage");
        long energy = g.Role == EmploymentRole.Production ? ProductionUnit(g.Id).Recipe.EnergyPerBatchMinor : 0;
        _ = checked(g.Capacity * c.Action.WageMinor + energy);
        if (g.Role == EmploymentRole.Logistics)
            _ = checked(g.Capacity * c.Action.WageMinor * MaximumFreightTicks(_config,_logistics[g.Id].CargoCapacity,_logistics[g.Id].Level));
        long payTick = checked(Tick + 1);
        if (g.Role == EmploymentRole.Logistics && _logistics[g.Id].Trip is { } trip)
            payTick = checked(trip.EndTick + 1);
        long attractionTick = Math.Max(payTick, checked(Tick + Math.Max(1, Labor.SalaryChangeDelayTicks)));
        g.PendingWage = c.Action.WageMinor; g.PayEffectiveTick = payTick;
        g.PendingAttractionWage = c.Action.WageMinor; g.AttractionEffectiveTick = attractionTick;
        Emit("WageChangeScheduled", actor.Id, g.Id, money: c.Action.WageMinor, command: c.CommandId);
    }

    private void ApplyWages()
    {
        foreach (var g in EmploymentGroups().OrderBy(g => g.Id))
        {
            if (ParallelWorkshops && g.Role == EmploymentRole.Production && ProductionUnit(g.Id).Batch is not null)
            {
                // A promised new rate starts at the next batch boundary, including its attraction signal.
                // A stopped batch has no guessed wall-clock completion time.
                if (g.PayEffectiveTick <= Tick) g.PayEffectiveTick = checked(Tick + 1);
                if (g.AttractionEffectiveTick <= Tick) g.AttractionEffectiveTick = checked(Tick + 1);
                continue;
            }
            if(g.Role==EmploymentRole.Logistics && _logistics[g.Id].Trip is not null)
            {
                if(g.PayEffectiveTick<=Tick)g.PayEffectiveTick=checked(Tick+1);
                if(g.AttractionEffectiveTick<=Tick)g.AttractionEffectiveTick=checked(Tick+1);
                continue;
            }
            if (g.PayEffectiveTick is { } pay && pay <= Tick)
            {
                g.Wage = g.PendingWage!.Value; g.PendingWage = null; g.PayEffectiveTick = null;
                Emit("WageEffective", g.Owner, g.Id, money: g.Wage);
            }
            if (g.AttractionEffectiveTick is { } attraction && attraction <= Tick)
            {
                g.AttractionWage = g.PendingAttractionWage!.Value;
                g.PendingAttractionWage = null; g.AttractionEffectiveTick = null;
                Emit("AttractionWageEffective", g.Owner, g.Id, money: g.AttractionWage);
            }
        }
    }

    private long NextWorkCost(EnterpriseState e, int staff)
    {
        int full = e.Definition.BuildingTypeId == "" ? e.Recipe.RequiredStaff : e.Group.Capacity;
        long required = e.Batch?.WorkRequired ?? checked(e.Recipe.FullStaffTicks * (ParallelWorkshops ? 100 : full));
        long progress = e.Batch?.WorkDone ?? 0;
        long work = ParallelWorkshops ? WorkshopRules.Rate(staff) : checked((long)Math.Min(staff, full) * e.Definition.WorkRateMultiplier);
        long next = Math.Min(required, checked(progress + work));
        long total = ParallelWorkshops ? checked((long)((Int128)_config.Economy!.Workshops!.ServiceMinorPerFullTick * next / 100)) : checked((long)((Int128)e.Recipe.EnergyPerBatchMinor * next / required));
        long energy = checked(total - (e.Batch?.EnergyPaid ?? 0));
        long wage = ParallelWorkshops && e.Batch?.Terms is { } terms ? terms.WageMinor : e.Group.PendingWage ?? e.Group.Wage;
        return checked(staff * wage + energy);
    }

    private string VacancyReason(EnterpriseState e)
    {
        if (!e.Enabled || (PreparingDemolition(ProductionParent(e.Definition.Id))||PreparingDevelopment(ProductionParent(e.Definition.Id))) && e.Batch is null) return "UserPaused";
        if ((e.Group.PendingWage ?? e.Group.Wage) <= 0 || e.Group.AttractionWage <= 0) return "NoPaidOffer";
        if (e.Batch is not null) return "Ready"; // WIP already owns its inputs and output hold.
        if (e.Recipe.Ingredients.Any(i=>_economy.Inventories[e.Input].Free(i.ItemId)<i.Quantity)) return "NoInputs";
        var output = _economy.Inventories[e.Output];
        long free = checked(output.Capacity - output.Occupied(_items) - output.Incoming);
        return free < checked(e.Recipe.OutputQuantity * _items[e.Recipe.OutputItem].UnitVolume) ? "NoOutputSpace" : "Ready";
    }

    private long ExpectedPopulation(long period)
    {
        Int128 n=(Int128)_config.Population+(Int128)period*Labor.GrowthPerPeriod;
        return checked((long)(Labor.MaximumPopulation is {} max?Int128.Min(n,max):n));
    }
    private void AdvanceLabor()
    {
        if (Tick % Labor.StepTicks != 0) return;
        long period = Tick / Labor.GrowthPeriodTicks;
        long growth = checked(ExpectedPopulation(period)-_population);
        long population = checked(_population + growth);
        long pool = checked(_unemployed + growth);
        var groups = LaborWorkplaces();
        // Same owner wallet: reserve each admitted group's first interval once, in rotating order.
        var budget = _actors.Values.ToDictionary(a => a.Id, a => _economy.Accounts[a.Account].Available);
        var ready = new Dictionary<long, bool>();
        var reasons = new Dictionary<long, string>();
        var priority = groups.Length == 0 ? groups : groups.Skip((int)(Tick / Labor.StepTicks % groups.Length))
            .Concat(groups.Take((int)(Tick / Labor.StepTicks % groups.Length))).ToArray();
        foreach (var e in priority)
        {
            string reason = e.VacancyReason;
            if (reason == "Ready")
            {
                long cost = e.Cost(Math.Max(e.Group.Headcount, e.MinimumStaff));
                if (budget[e.Group.Owner] < cost) reason = "NoMoney";
                else budget[e.Group.Owner] -= cost;
            }
            ready[e.Group.Id] = reason == "Ready"; reasons[e.Group.Id] = reason;
        }
        var targets = LaborAllocation.Cubic(population, groups.Where(e => ready[e.Group.Id])
            .Select(e => new LaborCandidate(e.Group.Id, e.Group.Capacity, e.Group.AttractionWage)).ToArray(), _laborRandom, EconomyRules.WageAttractionExponent);
        var next = groups.ToDictionary(e => e.Group.Id, e => e.Group.Headcount);
        var credits = new Dictionary<long, long>();
        var requests = new List<WeightedPlace>();
        foreach (var e in groups)
        {
            var g = e.Group;
            long stepCredit = checked((long)g.Capacity * Labor.MobilityNumerator);
            long credit = Math.Min(checked(g.MobilityCredit + stepCredit), Math.Max(Labor.MobilityDenominator, stepCredit));
            int quota = checked((int)(credit / Labor.MobilityDenominator));
            int target = targets.GetValueOrDefault(g.Id);
            int departure = Math.Min(Math.Max(0, g.Headcount - target), quota);
            next[g.Id] -= departure; pool = checked(pool + departure);
            credit -= checked((long)departure * Labor.MobilityDenominator);
            credits[g.Id] = credit;
            int demand = Math.Min(Math.Max(0, target - g.Headcount), quota);
            if (ready[g.Id] && demand > 0) requests.Add(new(g.Id, demand, demand));
        }
        var hires = LaborAllocation.Weighted(pool, requests.ToArray(), _laborRandom);
        // Recompute the budget after all departures, before admitting simultaneous hires.
        budget = _actors.Values.ToDictionary(a => a.Id, a => _economy.Accounts[a.Account].Available);
        foreach (var e in priority.Where(e => ready[e.Group.Id]))
            budget[e.Group.Owner] -= e.Cost(Math.Max(next[e.Group.Id], e.MinimumStaff));
        foreach (var e in priority)
        {
            var g = e.Group;
            int requested = hires.GetValueOrDefault(g.Id);
            if (requested == 0) continue;
            int baseline = Math.Max(next[g.Id], e.MinimumStaff);
            long baselineCost = e.Cost(baseline);
            int low = 0, high = requested;
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                long extra = e.Cost(Math.Max(next[g.Id] + mid, e.MinimumStaff)) - baselineCost;
                if (extra <= budget[g.Owner]) low = mid; else high = mid - 1;
            }
            budget[g.Owner] -= e.Cost(Math.Max(next[g.Id] + low, e.MinimumStaff)) - baselineCost;
            next[g.Id] += low; pool -= low; credits[g.Id] -= checked((long)low * Labor.MobilityDenominator);
        }
        if (pool < 0 || checked(pool + next.Values.Sum(n => (long)n)) != population || budget.Values.Any(n => n < 0))
            throw new InvalidOperationException("I01: staged labor transition invalid");
        // Commit all headcounts only after planning and conservation checks.
        _population = population; _unemployed = pool; _lastGrowthPeriod = period;
        if (growth > 0) Emit("PopulationGrew", quantity: growth);
        foreach (var e in groups)
        {
            var g = e.Group; int delta = next[g.Id] - g.Headcount;
            g.Headcount = next[g.Id]; g.MobilityCredit = credits[g.Id]; g.Target = targets.GetValueOrDefault(g.Id);
            g.Ready = ready[g.Id]; g.ReadinessReason = reasons[g.Id]; g.AssessedAtTick = Tick;
            Emit("EmploymentAssessed", g.Owner, g.Id, quantity: g.Target, reason: g.ReadinessReason);
            if (delta != 0) Emit("EmploymentChanged", g.Owner, g.Id, quantity: delta);
        }
        Emit("LaborStepCommitted", quantity: _unemployed);
    }

    private void ValidateLaborInvariants()
    {
        long employed = EmploymentGroups().Sum(g => (long)g.Headcount);
        if (_population < _config.Population || Labor.MaximumPopulation is {} max && _population>max || _unemployed < 0 || checked(employed + _unemployed) != _population)
            throw new InvalidOperationException("I01: population not conserved");
        foreach (var g in EmploymentGroups())
        {
            long limit = Math.Max(Labor.MobilityDenominator, checked((long)g.Capacity * Labor.MobilityNumerator));
            if (g.Headcount < 0 || g.Headcount > g.Capacity || g.Capacity < 1 ||
                g.Target < 0 || g.Target > g.Capacity || g.Wage < 0 || g.AttractionWage < 0 ||
                g.MobilityCredit < 0 || g.MobilityCredit > limit ||
                g.PendingWage.HasValue != g.PayEffectiveTick.HasValue ||
                g.PendingAttractionWage.HasValue != g.AttractionEffectiveTick.HasValue)
                throw new InvalidOperationException("I01: invalid employment group");
        }
        if (ProductionUnits().Any(e => e.Group.Capacity != e.Definition.Capacity || e.Group.Role != EmploymentRole.Production))
            throw new InvalidOperationException("Invalid production employment group");
    }
}

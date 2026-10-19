using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record WeightedWage(Int128 WeightedMinor, long Weight);
public sealed record VacancyOffer(EmploymentGroupId Group, ParticipantId Owner, EmploymentRole Role,
    int Vacancies, long AttractionWageMinor, long CurrentWageMinor, long? PendingWageMinor);
public sealed record PublicLabor(WeightedWage EmployedRate, WeightedWage VacancyRate, ImmutableArray<VacancyOffer> Offers);
public sealed record ParticipantCapital(ParticipantId Actor, long CapitalMinor, long Employed, string PreferenceItem);
public sealed record CapitalBreakdown(long MoneyMinor, long StockMinor, long WorkInProgressMinor,
    long LandMinor, long BuildingsMinor, long EquipmentMinor, long LiabilitiesMinor)
{
    public long TotalMinor => checked(MoneyMinor + StockMinor + WorkInProgressMinor + LandMinor + BuildingsMinor + EquipmentMinor - LiabilitiesMinor);
}
public sealed record CityThreshold(long CapitalMinor, long Population, int ActiveProductions);
public sealed record CityProgression(int ConfirmationTicks = 30, CityThreshold? Settlement = null,
    CityThreshold? SmallTown = null, CityThreshold? DevelopedTown = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ImmutableArray<CityThreshold> Thresholds => [Settlement ?? new(150000, 25, 1), SmallTown ?? new(400000, 40, 3), DevelopedTown ?? new(1000000, 80, 6)];
    public void Validate()
    {
        if (ConfirmationTicks is < 1 or > 100000) throw new ArgumentException("Invalid city confirmation period");
        var old = new CityThreshold(0, 0, 0);
        foreach (var level in Thresholds)
        {
            if (level.CapitalMinor < old.CapitalMinor || level.Population < old.Population || level.ActiveProductions < old.ActiveProductions)
                throw new ArgumentException("City thresholds must be nonnegative and ordered");
            old = level;
        }
    }
}
public sealed record CityStageState(int Stage = 0, int Candidate = 0, long SinceTick = 0);
public sealed record CityEconomy(string ValuationVersion, ImmutableArray<ParticipantCapital> Participants,
    long CapitalMinor, long MunicipalMinor, long Population, long Employed, long Unemployed, int ActiveProductions,
    int Stage, long PeriodStartTick, long PeriodEndTick, long GrossOutputMinor, long CapitalDeliveredQuantity,
    long CapitalContractedQuantity, long CapitalContractDeliveredQuantity, PublicLabor Labor);

public sealed partial class SimulationEngine
{
    private CityStageState _cityStage = new();
    private CityProgression CityRules => EconomyRules.City ?? new();

    /// <summary>Same public totals for all controllers. No foreign cash, stock, escrow IDs or secret terms.</summary>
    public CityEconomy PublicEconomy()
    {
        var participants = _actors.Keys.Order().Select(id => new ParticipantCapital(new(id), CapitalOf(new(id)).TotalMinor,
            EmploymentGroups().Where(g => g.Owner == id).Sum(g => (long)g.Headcount), _config.Participants.Single(p => p.Id == id).PublicPreferenceItem)).ToImmutableArray();
        long municipal = _economy.Accounts[_treasury].Balance;
        long start = _config.CapitalDemand is { } demand ? Tick / demand.PeriodTicks * demand.PeriodTicks : 0;
        long end = _config.CapitalDemand is { } settings ? Math.Min(_config.MaxTicks, checked(start + settings.PeriodTicks)) : _config.MaxTicks;
        // The latest completed production period is distinct from current-period receipts.
        var last = _demandPeriods.LastOrDefault();
        long gross = last?.GrossOutputBaseMinor ?? _flows.Values.Sum(f => checked((f.Harvested + f.Produced) * _items[f.Id].BasePriceMinor));
        long delivered = _events.AsEnumerable().Reverse().TakeWhile(e => e.Tick > start).Where(e => e.Type == "CapitalSaleSettled").Sum(e => e.Quantity);
        var contracts = _deliveryContracts.Where(c => c.CapitalOfferId != 0 && c.CreatedTick > start && c.CreatedTick <= Tick).ToArray();
        long contractDelivered = _events.AsEnumerable().Reverse().TakeWhile(e => e.Tick > start).Where(e => e.Type == "DeliveryReceived" &&
            _deliveryContracts.Any(c => c.Id == e.EntityId && c.CapitalOfferId != 0)).Sum(e => e.Quantity);
        return new("capital-reference-v1", participants, checked(participants.Sum(p => p.CapitalMinor) + municipal), municipal,
            _population, checked(_population - _unemployed), _unemployed,
            _enterprises.Values.Count(e => e.Enabled && e.Group.Headcount > 0), _cityStage.Stage,
            start, end, gross, checked(delivered + contractDelivered), contracts.Sum(c => c.Quantity), contractDelivered, LaborSummary());
    }

    /// <summary>Only the owner may request a breakdown; public ranking exposes the total alone.</summary>
    public CapitalBreakdown CapitalOf(ParticipantId actor)
    {
        if (!_actors.TryGetValue(actor.Value, out var owner)) throw new ArgumentException("Participant not available");
        long money = _economy.Accounts[owner.Account].Balance;
        foreach (var pickup in _marketAllocations.Where(p => p.Buyer == actor))
            money = checked(money + _economy.Accounts[pickup.EscrowAccount.Value].Balance);
        foreach (var contract in _deliveryContracts)
        {
            if (contract.Buyer == actor) money = checked(money + _economy.Accounts[contract.EscrowAccount.Value].Balance);
            if (contract.Supplier == actor) money = checked(money + _economy.Accounts[contract.BondAccount.Value].Balance);
        }
        long stock = _economy.Inventories.Values.Where(i => i.Owner == actor.Value)
            .Sum(i => i.Stock.Sum(s => checked(s.Value * _items[s.Key].BasePriceMinor)));
        long wip = _enterprises.Values.Where(e => e.Definition.OwnerId == actor.Value && e.Batch is not null)
            .Sum(e => checked(e.Recipe.InputQuantity * _items[e.Recipe.InputItem].BasePriceMinor));
        long land = _plots.Values.Where(p => p.Owner == actor.Value).Sum(p => p.Definition.PriceMinor);
        long buildings = _buildings.Values.Where(b => b.Owner == actor.Value).Sum(b => b.Type.Levels.Where(l => l.Level <= b.Level)
            .Sum(l => checked(l.PriceMinor + l.Materials.Sum(m => checked(m.Quantity * _items[m.ItemId].BasePriceMinor)))));
        long equipment = _equipment.Values.Where(e => e.Owner == actor.Value)
            .Sum(e => e.Type.Levels.Where(l => l.Level <= e.Level).Sum(l => l.PriceMinor));
        foreach (var logistics in _logistics.Values.Where(l => l.Owner == actor.Value))
            equipment = checked(equipment + Logistics.PurchasePriceMinor + (logistics.Level > 1 && Logistics.Upgrade is { } upgrade
                ? checked(upgrade.PriceMinor + upgrade.Materials.Sum(m => checked(m.Quantity * _items[m.ItemId].BasePriceMinor))) : 0));
        // Current contracts are fully funded. Unrecognised promises and contingent fines are not debt/assets.
        return new(money, stock, wip, land, buildings, equipment, 0);
    }

    private PublicLabor LaborSummary()
    {
        var groups = LaborWorkplaces();
        long employed = groups.Sum(w => (long)w.Group.Headcount);
        Int128 wages = groups.Aggregate((Int128)0, (sum, w) => sum + (Int128)w.Group.Headcount * w.Group.Wage);
        var budget = _actors.Values.ToDictionary(a => a.Id, a => _economy.Accounts[a.Account].Available);
        var ready = new List<LaborWorkplace>();
        int offset = groups.Length == 0 ? 0 : (int)(Tick / Labor.StepTicks % groups.Length);
        foreach (var workplace in groups.Skip(offset).Concat(groups.Take(offset)))
        {
            if (workplace.VacancyReason != "Ready" || workplace.Group.AttractionWage <= 0) continue;
            long cost = workplace.Cost(Math.Max(workplace.Group.Headcount, workplace.MinimumStaff));
            if (cost > budget[workplace.Group.Owner]) continue;
            budget[workplace.Group.Owner] -= cost; ready.Add(workplace);
        }
        var offers = new List<VacancyOffer>();
        foreach (var workplace in ready)
        {
            var g = workplace.Group;
            int baseline = Math.Max(g.Headcount, workplace.MinimumStaff), low = baseline, high = g.Capacity;
            long cost = workplace.Cost(baseline);
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                if (workplace.Cost(mid) - cost <= budget[g.Owner]) low = mid; else high = mid - 1;
            }
            budget[g.Owner] -= workplace.Cost(low) - cost;
            if (low > g.Headcount) offers.Add(new(new(g.Id), new(g.Owner), g.Role, low - g.Headcount, g.AttractionWage, g.Wage, g.PendingWage));
        }
        return new(new(wages, employed), new(offers.Aggregate((Int128)0, (sum, o) => sum + (Int128)o.Vacancies * o.AttractionWageMinor),
            offers.Sum(o => (long)o.Vacancies)), [.. offers.OrderBy(o => o.Group.Value)]);
    }

    private void AdvanceCityStage()
    {
        long capital = checked(_actors.Keys.Sum(id => CapitalOf(new(id)).TotalMinor) + _economy.Accounts[_treasury].Balance);
        int active = _enterprises.Values.Count(e => e.Enabled && e.Group.Headcount > 0);
        int candidate = 0;
        foreach (var threshold in CityRules.Thresholds)
            if (capital >= threshold.CapitalMinor && _population >= threshold.Population && active >= threshold.ActiveProductions) ++candidate;
            else break;
        var previous = _cityStage;
        if (candidate == _cityStage.Stage)
        {
            if (candidate != _cityStage.Candidate) _cityStage = new(candidate, candidate, Tick);
        }
        else
        {
            if (candidate != _cityStage.Candidate) _cityStage = _cityStage with { Candidate = candidate, SinceTick = Tick };
            if (Tick - _cityStage.SinceTick + 1 >= CityRules.ConfirmationTicks) _cityStage = new(candidate, candidate, Tick);
        }
        if (_cityStage != previous) Emit("CityStageUpdated", entity: _cityStage.Stage, quantity: _cityStage.Candidate);
    }
    private void ValidateCityStage()
    {
        var last = _events.LastOrDefault(e => e.Type == "CityStageUpdated");
        if (last is null ? _cityStage != new CityStageState() :
            _cityStage.Stage != last.EntityId || _cityStage.Candidate != last.Quantity || _cityStage.SinceTick != last.Tick)
            throw new CheckpointException("InvalidCityStageJournal");
    }
}

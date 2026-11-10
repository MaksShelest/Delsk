using System.Text;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>
/// Короткие срезы экономики для графика. Это чтение Core, не второй расчёт.
/// Строка пишется на старте, на первом тике, далее каждые 30 тиков и в конце.
/// Author: Шелестов Максим Евгеньевич.
/// </summary>
internal sealed class EconomySamples
{
    private readonly string _path;
    private readonly GameSession _session;
    private readonly Queue<(string Type, long Tick, long? Actor, string Item, long Quantity, long Amount, string Reason)> _recent = new();
    private readonly Dictionary<long, int> _built = new();
    private readonly Dictionary<long, int> _removed = new();
    private long _last = -1;
    private long _sequence;
    private int _constructed, _demolished;
    public EconomySamples(string directory, GameSession session)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "economy-samples.jsonl");
        _session = session;
        if (File.Exists(_path)) File.Delete(_path);
        Write();
    }
    public void Maybe(bool force = false)
    {
        long tick = _session.Engine.Tick;
        if (!force && tick == _last) return;
        if (!force && tick != 1 && tick % 30 != 0) return;
        Write();
    }
    private void Write()
    {
        var state = _session.Engine.Inspect();
        var city = _session.Engine.PublicEconomy();
        var events = _session.Engine.EventsSince(_sequence).ToArray();
        if (events.Length > 0) _sequence = events[^1].Sequence;
        foreach (var item in events)
        {
            if (item.Type == "BuildingConstructed")
            {
                _constructed++;
                if (item.ActorId is { } actor) _built[actor.Value] = _built.GetValueOrDefault(actor.Value) + 1;
            }
            else if (item.Type == "BuildingDemolished")
            {
                _demolished++;
                if (item.ActorId is { } actor) _removed[actor.Value] = _removed.GetValueOrDefault(actor.Value) + 1;
            }
            _recent.Enqueue((item.Type, item.Tick, item.ActorId?.Value, item.ItemId, item.Quantity, item.AmountMinor, item.ReasonCode));
            while (_recent.Count > 8) _recent.Dequeue();
        }
        long treasury = state.Accounts.Single(account => account.Kind == "Treasury").BalanceMinor;
        long? budget = state.CapitalDemand is { Periods.Length: > 0 } demand ? demand.Periods[^1].MonetaryBudgetMinor : null;
        var line = new
        {
            Tick = state.Tick,
            TreasuryBalanceMinor = treasury,
            MunicipalMinor = city.MunicipalMinor,
            OrderBudgetMinor = budget,
            Population = city.Population,
            Employed = city.Employed,
            Unemployed = city.Unemployed,
            ActiveProductions = city.ActiveProductions,
            Harvested = state.Flows.Sum(flow => flow.Harvested),
            Produced = state.Flows.Sum(flow => flow.Produced),
            ConstructedBuildings = _constructed,
            DemolishedBuildings = _demolished,
            ExistingBuildings = state.Buildings.Length,
            Contracts = new
            {
                Total = state.DeliveryContracts.Length,
                Fulfilled = state.DeliveryContracts.Count(contract => contract.Status == "Fulfilled"),
                Expired = state.DeliveryContracts.Count(contract => contract.Status == "Expired"),
                Active = state.DeliveryContracts.Count(contract => contract.ClosedTick is null)
            },
            Participants = state.Participants.Select(person =>
            {
                var capital = city.Participants.Single(item => item.Actor == person.Id);
                var latest = _session.ControllerSamples.LastOrDefault(sample => sample.ActorId == person.Id.Value);
                return new
                {
                    Actor = person.Id.Value,
                    CashMinor = state.Accounts.Single(account => account.Id == person.AccountId).BalanceMinor,
                    CapitalMinor = capital.CapitalMinor,
                    Busy = person.Busy,
                    Buildings = state.Buildings.Count(building => building.OwnerId == person.Id),
                    ProductionBuildings = state.Buildings.Count(building => building.OwnerId == person.Id && building.Kind == BuildingKind.Production),
                    StaffedProductions = state.Enterprises.Count(enterprise => enterprise.OwnerId == person.Id && enterprise.Enabled && enterprise.Staff > 0),
                    ConstructedBuildings = _built.GetValueOrDefault(person.Id.Value),
                    DemolishedBuildings = _removed.GetValueOrDefault(person.Id.Value),
                    Goal = latest?.Goal,
                    Reason = latest?.Reason
                };
            }).ToArray(),
            RecentEvents = _recent.Select(item => new { item.Type, item.Tick, item.Actor, item.Item, item.Quantity, item.Amount, item.Reason }).ToArray()
        };
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(JsonSerializer.Serialize(line, SimulationJson.Options));
        _last = state.Tick;
    }
}

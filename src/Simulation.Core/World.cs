using System.Security.Cryptography;
using System.Text.Json;

namespace Simulation.Core;

internal sealed class GridMap
{
    private readonly int _width;
    private readonly int _height;
    private readonly HashSet<Cell> _blocked;
    public GridMap(ScenarioDefinition scenario)
    { _width = scenario.MapWidth; _height = scenario.MapHeight; _blocked = scenario.BlockedCells.ToHashSet(); }
    public bool Walkable(Cell c) => c.X >= 0 && c.Z >= 0 && c.X < _width && c.Z < _height && !_blocked.Contains(c);
    public Cell[] FindPath(Cell source, Cell destination)
    {
        if (!Walkable(source) || !Walkable(destination)) throw new RuleViolation("NoPath");
        var previous = new Dictionary<Cell, Cell>();
        var queue = new Queue<Cell>();
        var seen = new HashSet<Cell> { source };
        queue.Enqueue(source);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == destination)
            {
                var path = new List<Cell> { cell };
                while (cell != source) { cell = previous[cell]; path.Add(cell); }
                path.Reverse(); return path.ToArray();
            }
            // Canonical neighbor order is part of rules-v1.
            Cell[] neighbors = [new(cell.X + 1, cell.Z), new(cell.X, cell.Z + 1),
                new(cell.X - 1, cell.Z), new(cell.X, cell.Z - 1)];
            foreach (var next in neighbors)
                if (Walkable(next) && seen.Add(next)) { previous[next] = cell; queue.Enqueue(next); }
        }
        throw new RuleViolation("NoPath");
    }
}
internal sealed class ActorState(long id, long account, long inventory, Cell position)
{
    public long Id { get; } = id;
    public long Account { get; } = account;
    public long Inventory { get; } = inventory;
    public Cell Position { get; set; } = position;
    public PersonalJob? Job { get; set; }
}
internal sealed class PersonalJob(long id, string kind, long startTick, long completionTick,
    Cell[] path, string item, long quantity, string capacityHold, string commandId, long equipmentId = 0)
{
    public long Id { get; } = id;
    public string Kind { get; } = kind;
    public long StartTick { get; } = startTick;
    public long CompletionTick { get; } = completionTick;
    public Cell[] Path { get; } = path;
    public int PathIndex { get; set; }
    public int SegmentTicks { get; set; }
    public string Item { get; } = item;
    public long Quantity { get; } = quantity;
    public string CapacityHold { get; } = capacityHold;
    public string CommandId { get; } = commandId;
    public long EquipmentId { get; } = equipmentId;
}
internal sealed class BatchState(long id, string hold, long required)
{
    public long Id { get; } = id;
    public string Hold { get; } = hold;
    public long WorkRequired { get; } = required;
    public long WorkDone { get; set; }
    public long EnergyPaid { get; set; }
}
internal sealed class EnterpriseState(EnterpriseDefinition definition, long input, long output, RecipeDefinition recipe)
{
    public EnterpriseDefinition Definition { get; set; } = definition;
    public EmploymentGroup Group { get; } = new(definition.Id, definition.OwnerId, definition.Staff, definition.Capacity, definition.WageMinor);
    public long Input { get; } = input;
    public long Output { get; } = output;
    public RecipeDefinition Recipe { get; set; } = recipe;
    public bool Enabled { get; set; } = definition.Enabled;
    public string Reason { get; set; } = definition.Enabled ? "NoInputs" : "UserPaused";
    public BatchState? Batch { get; set; }
    public long Completed { get; set; }
    public long EnergyPaid { get; set; }
}
internal sealed class CapitalOffer(CapitalOfferDefinition definition, string budgetHold)
{
    public CapitalOfferDefinition Definition { get; } = definition;
    public string BudgetHold { get; } = budgetHold;
    public long Remaining { get; set; } = definition.Quantity;
    public long? Cutoff { get; set; }
    public Dictionary<long, SaleRequest> Requests { get; } = [];
}
internal record SaleRequest(long ActorId, long InventoryId, long Quantity, string Reservation, string CommandId);
internal sealed class ItemFlow(string id)
{
    public string Id { get; } = id;
    public long Initial { get; set; }
    public long Harvested { get; set; }
    public long RecipeInput { get; set; }
    public long Produced { get; set; }
    public long Exported { get; set; }
    public long ConstructionInput { get; set; }
}

public static partial class ScenarioValidation
{
    public static void Validate(ScenarioDefinition s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (string.IsNullOrWhiteSpace(s.Id) || s.Id.Length > 80 || s.MapWidth is < 1 or > 128 ||
            s.MapHeight is < 1 or > 128 || s.HarvestTicksPerUnit <= 0 || s.MoveTicksPerCell <= 0 ||
            s.TradeWindowTicks <= 0 || s.MaxCommandsPerActorTick is < 1 or > 64 || s.MaxTicks is < 1 or > 1000000 ||
            s.Population < 0 || s.BlockedCells is null || s.Items is null || s.Recipes is null ||
            s.Participants is null || s.Enterprises is null || s.CapitalOffers is null || s.ResourceNodes is null ||
            s.Participants.Length is < 1 or > 64)
            throw new ArgumentException("Invalid scenario settings");
        if (s.Market is { } market && (market.AuctionRoundTicks is < 1 or > 1000 ||
            market.AuctionRounds is < 1 or > 10 || market.MinimumRaiseMinor is < 1 or > 10000 ||
            market.PickupTtlTicks is < 1 or > 1000000 || market.PickupFineBasisPoints is < 0 or > 10000))
            throw new ArgumentException("Invalid market settings");
        ValidateLaborSettings(s);
        ValidateEquipmentCatalog(s);
        var map = new GridMap(s);
        bool ValidId(long id) => id is > 0 and < 1000;
        if (s.Items.Length == 0 || s.Items.Select(i => i.Id).Distinct().Count() != s.Items.Length ||
            s.Items.Any(i => string.IsNullOrWhiteSpace(i.Id) || i.Id.Length > 50 || i.UnitVolume <= 0 || i.BasePriceMinor <= 0))
            throw new ArgumentException("Invalid item catalog");
        var items = s.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        if (s.Recipes.Select(r => r.Id).Distinct().Count() != s.Recipes.Length || s.Recipes.Any(r =>
            string.IsNullOrWhiteSpace(r.Id) || !items.ContainsKey(r.InputItem) || !items.ContainsKey(r.OutputItem) ||
            r.InputQuantity <= 0 || r.OutputQuantity <= 0 || r.FullStaffTicks <= 0 ||
            r.RequiredStaff <= 0 || r.MinimumStaff < 1 || r.MinimumStaff > r.RequiredStaff || r.EnergyPerBatchMinor < 0))
            throw new ArgumentException("Invalid recipe catalog");
        if (s.Participants.Select(p => p.Id).Distinct().Count() != s.Participants.Length ||
            s.Participants.Any(p => !ValidId(p.Id) || p.CashMinor < 0 || p.InventoryCapacity <= 0 || !map.Walkable(p.Position)))
            throw new ArgumentException("Invalid participants");
        if (s.ResourceNodes.Any(n => !items.ContainsKey(n.ItemId) || !map.Walkable(n.Position)))
            throw new ArgumentException("Invalid resource nodes");
        var actors = s.Participants.Select(x => x.Id).ToHashSet();
        var recipes = s.Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);
        ValidateLandAndContent(s, map, items, recipes, actors);
        if (s.Enterprises.Select(e => e.Id).Distinct().Count() != s.Enterprises.Length || s.Enterprises.Any(e =>
            !ValidId(e.Id) || !actors.Contains(e.OwnerId) || !map.Walkable(e.Position) || !recipes.ContainsKey(e.RecipeId) ||
            e.InputCapacity <= 0 || e.OutputCapacity <= 0 || e.Staff < 0 || e.Staff > e.Capacity ||
            e.Capacity < recipes[e.RecipeId].RequiredStaff || e.WageMinor < 0 || e.Level != 1 ||
            e.WorkRateMultiplier != 1 || e.BuildingTypeId != ""))
            throw new ArgumentException("Invalid enterprises");
        if (s.CapitalOffers.Select(o => o.Id).Distinct().Count() != s.CapitalOffers.Length ||
            s.CapitalOffers.Any(o => !ValidId(o.Id) || !items.ContainsKey(o.ItemId) || o.Quantity <= 0 ||
                o.UnitPriceMinor <= 0 || !map.Walkable(o.Position)))
            throw new ArgumentException("Invalid capital offers");
        try
        {
            long staff = s.Enterprises.Aggregate(0L, (n, e) => checked(n + e.Staff));
            staff = (s.Buildings ?? []).Aggregate(staff, (n, b) => checked(n + b.Staff));
            if (staff > s.Population) throw new ArgumentException("Employment exceeds population");
            long money = s.Participants.Aggregate(0L, (n, p) => checked(n + p.CashMinor));
            money = s.CapitalOffers.Aggregate(money, (n, o) => checked(n + checked(o.Quantity * o.UnitPriceMinor)));
            _ = money;
            foreach (var r in s.Recipes)
            {
                _ = checked(r.FullStaffTicks * r.RequiredStaff);
                _ = checked(r.InputQuantity * items[r.InputItem].UnitVolume);
                _ = checked(r.OutputQuantity * items[r.OutputItem].UnitVolume);
            }
            foreach (var e in s.Enterprises) _ = checked(e.Capacity * e.WageMinor + recipes[e.RecipeId].EnergyPerBatchMinor);
        }
        catch (OverflowException ex) { throw new ArgumentException("Scenario arithmetic overflow", ex); }
    }
    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, SimulationJson.Options))).ToLowerInvariant();
}

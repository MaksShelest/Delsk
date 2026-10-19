using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record WorldEconomy(long StartingCashMinor, long PersonalInventoryCapacity, long Population,
    long PlotPriceMinor, long ShopSitePriceMinor, int HarvestTicksPerUnit, int MoveTicksPerCell, int TradeWindowTicks,
    int LandReservationTicks, int MaxActivePlotReservations, long MaxTicks, ItemDefinition[] Items,
    RecipeDefinition[] Recipes, BuildingTypeDefinition[] BuildingTypes, EquipmentTypeDefinition[] EquipmentTypes,
    LaborSettings Labor, MarketSettings Market, DeliverySettings Delivery, LogisticsSettings Logistics,
    CapitalDemandSettings CapitalDemand, ElectionSettings Elections, SupportSettings Support, EconomicRules Rules,
    ResourceTypeBalance[]? Resources = null);

public sealed record ResourceTypeBalance(string ItemId, ResourceSiteRules Private, ResourceSiteRules Public);

/// <summary>One editable balance source; geometry remains in the scenario. Resolved values are saved by Core.</summary>
public sealed record EconomyProfile(int Version, string Id, WorldEconomy World, JsonElement Npc)
{
    public static EconomyProfile Read(string json)
    {
        if (json.Length > 1024 * 1024) throw new ArgumentException("Economy profile too large");
        using var document = JsonDocument.Parse(json);
        CheckUnique(document.RootElement);
        var p = JsonSerializer.Deserialize<EconomyProfile>(json, SimulationJson.Options) ?? throw new ArgumentException("Empty economy profile");
        if (p.Version != 1 || string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 80 || p.World is null || p.Npc.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Invalid economy profile version/id/world/npc");
        if (p.World.StartingCashMinor < 0 || p.World.PersonalInventoryCapacity <= 0 || p.World.PlotPriceMinor <= 0 || p.World.ShopSitePriceMinor <= 0 ||
            p.World.Rules is null || p.World.Labor is null || p.World.Market is null || p.World.Delivery is null || p.World.Logistics is null ||
            p.World.CapitalDemand is null || p.World.Elections is null || p.World.Support is null)
            throw new ArgumentException("Economy profile: missing/invalid economic section");
        p.World.Rules.Validate(); return p;
    }
    private static void CheckUnique(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in node.EnumerateObject())
            { if (!names.Add(field.Name)) throw new ArgumentException("Duplicate economy field: " + field.Name); CheckUnique(field.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) CheckUnique(item);
    }
    public static EconomyProfile Load(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Economy profile too large");
        return Read(File.ReadAllText(path));
    }
    public ScenarioDefinition Apply(ScenarioDefinition map)
    {
        var w = World;
        var resolved = map with {
            EconomyProfileId = Id, Economy = w.Rules, Items = w.Items, Recipes = w.Recipes,
            BuildingTypes = w.BuildingTypes, EquipmentTypes = w.EquipmentTypes, Population = w.Population,
            Participants = map.Participants.Select(p => p with { CashMinor = w.StartingCashMinor, InventoryCapacity = w.PersonalInventoryCapacity }).ToArray(),
            Plots = map.Plots?.Select(p => p.IsCorridor ? p : p with { PriceMinor = p.IsShopSite || p.ShopParticipantId.HasValue ? w.ShopSitePriceMinor : w.PlotPriceMinor }).ToArray(),
            HarvestTicksPerUnit = w.HarvestTicksPerUnit, MoveTicksPerCell = w.MoveTicksPerCell, TradeWindowTicks = w.TradeWindowTicks,
            LandReservationTicks = w.LandReservationTicks, MaxActivePlotReservations = w.MaxActivePlotReservations, MaxTicks = w.MaxTicks,
            Labor = w.Labor, Market = w.Market, Delivery = w.Delivery, Logistics = w.Logistics, Elections = w.Elections,
            CapitalDemand = map.CapitalDemand is { } d ? w.CapitalDemand with { Terminal = d.Terminal } : null,
            Support = map.Support is { } s ? w.Support with { TradingPoint = s.TradingPoint } : null
        };
        if (w.Resources is { } resources)
        {
            if (resources.Select(r=>r.ItemId).Distinct().Count()!=resources.Length ||
                resources.Any(r=>r.Private.Public || !r.Public.Public || r.Private.InitialOwner.HasValue || r.Public.InitialOwner.HasValue || !w.Items.Any(i=>i.Id==r.ItemId)))
                throw new ArgumentException("Invalid resource balance");
            resolved=resolved with { ResourceNodes=map.ResourceNodes.Select(n=>n with {
                Rules=((n.Rules??new()).Public?resources.Single(r=>r.ItemId==n.ItemId).Public:resources.Single(r=>r.ItemId==n.ItemId).Private)
                    with {InitialOwner=n.Rules?.InitialOwner}
            }).ToArray() };
        }
        ScenarioValidation.Validate(resolved); return resolved;
    }
}

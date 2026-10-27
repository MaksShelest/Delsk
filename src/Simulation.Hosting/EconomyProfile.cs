using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record WorldEconomy(long StartingCashMinor, long PersonalInventoryCapacity, long Population,
    long PlotPriceMinor, long ShopSitePriceMinor, int HarvestTicksPerUnit, int MoveTicksPerCell, int TradeWindowTicks,
    int LandReservationTicks, int MaxActivePlotReservations, long MaxTicks, ItemDefinition[] Items,
    RecipeDefinition[] Recipes, BuildingTypeDefinition[] BuildingTypes, EquipmentTypeDefinition[] EquipmentTypes,
    LaborSettings Labor, MarketSettings Market, DeliverySettings Delivery, LogisticsSettings Logistics,
    CapitalDemandSettings CapitalDemand, ElectionSettings Elections, SupportSettings Support, EconomicRules Rules,
    ResourceTypeBalance[]? Resources = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long StartingTreasuryMinor=0);

public sealed record ResourceTypeBalance(string ItemId, ResourceSiteRules Private, ResourceSiteRules Public);

/// <summary>One editable balance source; geometry remains in the scenario. Resolved values are saved by Core.</summary>
public sealed record EconomyProfile(int Version, string Id, WorldEconomy World, JsonElement Npc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] VNextProjectionMetadata? VNext = null)
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
        p.World.Rules.Validate();
        if(p.Id is VNextSupportedProfile.ProfileId or VNextStorageProfile.ProfileId or VNextWorkshopProfile.ProfileId or VNextDevelopmentProfile.ProfileId or VNextProductionProfile.ProfileId or VNextFleetProfile.ProfileId && p.VNext is null)throw new ArgumentException("Supported projection requires its provenance metadata.");
        if(p.VNext is { } provenance)
        {
            if((p.Id==VNextFleetProfile.ProfileId)!=(provenance.ProjectionVersion=="fleet-projection-1") || (p.Id==VNextFleetProfile.ProfileId&&p.World.Rules.Fleet is null) || (p.Id==VNextProductionProfile.ProfileId)!=(provenance.ProjectionVersion=="production-projection-1") || (p.Id==VNextProductionProfile.ProfileId&&p.World.Rules.ProductionContent is null) || (p.Id==VNextStorageProfile.ProfileId)!=(provenance.ProjectionVersion=="storage-projection-1") || (p.Id==VNextWorkshopProfile.ProfileId)!=(provenance.ProjectionVersion=="workshops-projection-1") || (p.Id==VNextDevelopmentProfile.ProfileId)!=(provenance.ProjectionVersion=="development-projection-1") || (p.Id==VNextDevelopmentProfile.ProfileId&&p.World.Rules.Development is null) || (p.Id==VNextWorkshopProfile.ProfileId && (p.World.Rules.Workshops is null || p.World.Rules.Storage is null)) || (p.Id==VNextStorageProfile.ProfileId&&p.World.Rules.Storage is null) || provenance.ProjectionVersion is not ("supported-projection-1" or "storage-projection-1" or "workshops-projection-1" or "development-projection-1" or "production-projection-1" or "fleet-projection-1") || provenance.FixedParticipants!=4 ||
                provenance.ActiveCapabilities is not {Length:>0} || provenance.Limitations is not {Length:>0} ||
                new[]{provenance.SourceSha256,provenance.FullBalanceSha256,provenance.CatalogSha256,provenance.BaselineProfileSha256,provenance.ProjectionPayloadSha256}.Any(h=>h is null || !System.Text.RegularExpressions.Regex.IsMatch(h,"\\A[0-9a-f]{64}\\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant)) ||
                provenance.ProjectionPayloadSha256!=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p with {VNext=null})))
                throw new ArgumentException("Invalid/stale supported projection metadata. Regenerate it; edited research profiles must remove vNext metadata and choose their own id.");
        }
        return p;
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
        if(map.Layout is not null && map.ResourceNodes.Any(n=>!(w.Resources??[]).Any(r=>r.ItemId==n.ItemId)))throw new ArgumentException("Designed map needs compatible resource rules; select the A2 supported profile");
        if(VNext is {} projection && map.Participants.Length!=projection.FixedParticipants)
            throw new ArgumentException("This supported projection is fixed to four participants; variable composition belongs to B1.");
        var resolved = map with {
            EconomyProfileId = Id, Economy = w.Rules, Items = w.Items, Recipes = w.Recipes,
            BuildingTypes = w.BuildingTypes, EquipmentTypes = w.EquipmentTypes, Population = w.Population, StartingTreasuryMinor=w.StartingTreasuryMinor,
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

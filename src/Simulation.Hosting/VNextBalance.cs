using System.Collections.Immutable;
using System.Text.Json;

namespace Simulation.Hosting;

/// <summary>Full typed design data. Presence is not activation of a runtime mechanic.</summary>
public sealed record VNextBalance(string FormatVersion, string Id, string Status, string SourceSha256,
    VNextSourceMetadata SourceMetadata, VNextRules Rules,
    ImmutableArray<VNextCatalogItem> Items, ImmutableArray<VNextCatalogRecipe> Recipes,
    ImmutableArray<VNextVehicle> Vehicles, ImmutableArray<VNextCatalogIndustry> Industries,
    ImmutableArray<VNextEnterpriseTier> EnterpriseTiers, ImmutableArray<VNextWarehouse> Warehouses,
    ImmutableArray<VNextRecipeVersion> RecipeVersions, ImmutableSortedDictionary<string, VNextResource> Resources,
    ImmutableSortedDictionary<string, VNextOutfit> AdditionalWorkshopOutfits,
    ImmutableArray<VNextModernization> WorkshopRecipeModernization,
    ImmutableArray<VNextLoadingPlatform> LoadingPlatforms, ImmutableArray<VNextHandlingKit> VehicleHandlingKits,
    ImmutableArray<VNextRoadProject> RoadProjects, VNextEconomyGlobals EconomyGlobals)
{
    public const string CurrentFormat = "delsk-vnext-balance-1";
    public VNextCatalog Catalog() => new(VNextCatalog.CurrentFormat, Id, "catalog_only", SourceSha256, Items, Recipes, Industries);
    public byte[] ToUtf8() => JsonSerializer.SerializeToUtf8Bytes(this, VNextBalanceReader.JsonOptions);
}
public sealed record VNextSourceMetadata(string SchemaVersion, string Status, string Date);
public sealed record VNextVehicle(string Id, string Name, long Speed, long CapacityVolume, long PriceMinor,
    long HandlingMax, long ClassFactorBp, bool DeliveryServiceAllowed);
public sealed record VNextEnterpriseTier(long Level, long Xp, long Cells, long Workshops, long InputCapacity,
    long OutputCapacity, long PriceMinor, ImmutableSortedDictionary<string, long> Materials, long Ticks);
public sealed record VNextWarehouse(long Level, long Capacity, long PriceMinor, ImmutableSortedDictionary<string, long> Materials, long Ticks);
public sealed record VNextRecipeVersion(long Version, long UnlockLevel, long InputMultiplier, long OutputMultiplier,
    long WorkMultiplierNum, long WorkMultiplierDen);
public sealed record VNextResource(string Name, long PrivateStockPerCell, long RegenOneEveryTicks, long ManualTicks,
    long PublicStockPerCell, long PublicRegenOneEveryTicks);
public sealed record VNextOutfit(long PriceMinor, ImmutableSortedDictionary<string, long> Materials);
public sealed record VNextModernization(long Version, long PriceMinor, ImmutableSortedDictionary<string, long> Materials, long Ticks);
public sealed record VNextLoadingPlatform(long Level, long Throughput, long RequiresStaff, long ServiceMinorPerActiveTick,
    long PriceMinor, ImmutableSortedDictionary<string, long> Materials, long Ticks);
public sealed record VNextHandlingKit(string Id, long Throughput, long RequiresStaff, long PriceMinor, ImmutableSortedDictionary<string, long> Materials);
public sealed record VNextRoadProject(long Level, ImmutableSortedDictionary<string, long> Materials, long PriceMinor, long Ticks);
public sealed record VNextRules(
    long TickSeconds, long MoneyMinorPerCoin, long MinBots,
    long MaxBots, long DefaultBots, long StartCashMinor,
    long CellPriceMinor, long ReservationExtraBp, long ReservationTtl,
    long PersonalCapacity, long BareCellCapacity, ImmutableArray<long> WarehouseCapacities,
    long StaffPerWorkshop, ImmutableArray<long> StaffRateBp, long WageMinor,
    long WageButtonStepMinor, long DemolitionRecoveryBp, long DemolitionCellMinor,
    long DemolitionExtraCellLevelMinor, long OverflowGraceTicks, ImmutableArray<long> OverflowWarningsRemaining,
    long DeliveryAcceptTicks, long DeliveryDeadlineMinBuffer, long DeliveryDeadlineBufferBp,
    long DeliveryMinFeeMinor, long DeliveryFeeVolumeBlock, long DeliveryDistanceBlock,
    long DeliveryFixedMinor, long DeliveryBlockMinor, long TransportInvoicePeriod,
    long InvoiceGrace, long PenaltyPeriod, long PenaltyBp,
    long CityPrivateRoadShareBp, ImmutableArray<long> RoadSpeedCaps, long RoadIncrementLightMinor,
    long ShopUpdateTicks, long ShopMinDisplayTicks, long ElectionPeriod,
    long RoadExitHoldTicks, long RoadStopWarningTicks, long RoadStopReleaseTicks,
    long AutosaveTicks, long QuestRefreshTicks, long QuestActiveLimit,
    long BaseSpeedUnitsPerCellTick, long OffroadVehicleSpeed, long OffroadPersonSpeed);
public sealed record VNextEconomyGlobals(
    [property: System.Text.Json.Serialization.JsonPropertyName("start_treasury_minor_per_4_participants")] long StartTreasuryMinorPer4Participants, long StartPopulationPerParticipant, long MaxPopulationPerParticipant,
    long PopulationGrowthTicks, long ResourceLeaseTicks, long ImportMarkupBp,
    long ImportRefreshTicks, [property: System.Text.Json.Serialization.JsonPropertyName("import_stock_per_additive_per_4_participants")] long ImportStockPerAdditivePer4Participants, long BootstrapSteel,
    long BootstrapGlass, long BootstrapAsphalt, long BootstrapAsphaltRefreshTicks,
    long CapitalDemandPeriod, [property: System.Text.Json.Serialization.JsonPropertyName("capital_bootstrap_budget_minor_per_4_participants")] long CapitalBootstrapBudgetMinorPer4Participants, long CapitalDemandCoefficientBp,
    long ResourceCellPriceMinor, long ResourceLeasePriceMinor);

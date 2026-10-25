using System.Collections.Immutable;

namespace Simulation.Core;

public record CapitalDemandSettings(Cell Terminal, int PeriodTicks = 60, long BootstrapBaseBudgetMinor = 20000,
    int DemandNumerator = 11, int DemandDenominator = 10, int CommonWaveTicks = 600,
    int ItemWaveTicks = 240, int OrderTtlTicks = 180, int PriceScaleBasisPoints = 10000);
public record CommodityCycle(string ItemId, int PricePhase, int DemandPhase);
public record MarketPricePoint(string ItemId, long UnitPriceMinor, long PublishedAtTick);
public record DemandCommodityView(string ItemId, long Harvested, long Produced, long Consumed,
    long Exported, long StockChange, long DemandQuantity, long UnitPriceMinor,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long Lost=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long ConstructionRecovered=0);
public record DemandPeriodView(long Period, long Tick, long GrossOutputBaseMinor, long InternalConsumptionBaseMinor,
    long StockChangeBaseMinor, long SmoothedOutputBaseMinor, int SmoothingRemainder,
    long TargetBaseMinor, long OrderedBaseMinor, long MonetaryBudgetMinor, long ExternalFundingMinor,
    ImmutableArray<DemandCommodityView> Goods);
public record CapitalOrderRecord(CapitalOfferDefinition Definition, long CreatedTick, long ExpiresAtTick, long ReleasedQuantity);
// Research/checkpoint state only. Never included in a participant/controller observation.
public record CapitalDemandView(ulong RandomState, int CommonPhase, ImmutableArray<CommodityCycle> Cycles,
    ImmutableArray<FlowView> Baseline, ImmutableArray<DemandPeriodView> Periods, ImmutableArray<CapitalOrderRecord> Orders);

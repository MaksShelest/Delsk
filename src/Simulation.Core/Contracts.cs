using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simulation.Core;

public readonly record struct ParticipantId(long Value);
public readonly record struct AccountId(long Value);
public readonly record struct InventoryId(long Value);
public readonly record struct EnterpriseId(long Value);
public readonly record struct PlotId(long Value);
public readonly record struct BuildingId(long Value);
public readonly record struct EquipmentId(long Value);
public readonly record struct EmploymentGroupId(long Value);
public readonly record struct ElectionId(long Value);
public readonly record struct Cell(int X, int Z);

public enum ActionKind { MoveTo, Harvest, Transfer, SetProductionEnabled, SellAvailableToCapital, CancelCapitalSale, SetTravelHeading,
    ReservePlot, BuyPlot, CancelPlotReservation, Build, UpgradeBuilding, SetRecipe, SetWage, BuyEquipment, UpgradeEquipment, SetEquipmentMode, CreateSellOffer, SubmitBuyRequest, RaiseBid, WithdrawBid, CollectPurchase, CreateBuyOffer, AcceptDeliveryQuantity, WithdrawSupplyRequest, CancelBuyOffer, DeliverContract, BuyLogistics, SetLogisticsContract, UpgradeLogistics, SetLogisticsStorage, SetLogisticsInbound, SetLogisticsProduction, QueueLogisticsJob, SetLogisticsEnabled, SetVoteChoice, ContinueAfterVictory, ProposeSupport, AcceptSupport, RejectSupport, SignSupport, StartHarvesting, StopHarvest,
    BuyResource, LeaseResource, StopTransfer, MoveEquipment, SetExtractionRoute, SetTransportRoute,ProposeTrade,CounterTrade,AcceptTrade,RejectTrade,TakeOverflow,PrepareDemolition,CancelDemolitionPreparation,DemolishBuilding,CancelConstruction,OutfitWorkshop,AssignWorkshopWorkers,AcceptIndustryQuest,AbandonIndustryQuest,BuyExpansionLand }
public enum ResultStatus { Accepted, Rejected }
public record GameAction(ActionKind Kind, Cell Target = default, string ItemId = "", long Quantity = 0,
    InventoryId Source = default, InventoryId Destination = default, EnterpriseId Enterprise = default,
    bool Enabled = false, long OfferId = 0, PlotId Plot = default, BuildingId Building = default,
    string BuildingTypeId = "", string RecipeId = "", EmploymentGroupId Group = default, long WageMinor = 0, EquipmentId Equipment = default,
    string EquipmentTypeId = "", int EquipmentLevel = 1, EquipmentMode Mode = EquipmentMode.Personal, long MarketOfferId = 0, long UnitPriceMinor = 0, long MaximumPriceMinor = 0, int MarketRound = 0, long PurchaseId = 0, long BuyOfferId = 0, int OfferWindow = 1, long ContractId = 0, LogisticsId Logistics = default, LogisticsDirection Direction = LogisticsDirection.Output, ElectionId Election = default, ParticipantId Candidate = default, ParticipantId SupportSeller = default, long SupportProposalId = 0, System.Collections.Immutable.ImmutableArray<MaterialCost>? Compensation = null, System.Collections.Immutable.ImmutableArray<Cell>? Stops = null, bool Repeat = false,long NegotiationId=0,int NegotiationVersion=0,TradeTerms? Terms=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? DemolitionQuote=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] long WorkshopId=0);
public record CommandEnvelope(string CommandId, ParticipantId ActorId, long LocalSequence,
    long IssuedAtTick, long NotBeforeTick, long ExpiresAtTick, GameAction Action)
{
    // ImmutableArray equality is reference equality; identical decoded inputs must still deduplicate.
    public virtual bool Equals(CommandEnvelope? other) => other is not null &&
        (CommandId, ActorId, LocalSequence, IssuedAtTick, NotBeforeTick, ExpiresAtTick) ==
        (other.CommandId, other.ActorId, other.LocalSequence, other.IssuedAtTick, other.NotBeforeTick, other.ExpiresAtTick) &&
        (ReferenceEquals(Action, other.Action) || (Action is not null && other.Action is not null &&
            (Action with { Compensation = null, Stops = null, Terms=null }) == (other.Action with { Compensation = null, Stops = null, Terms=null }) &&
            (Action.Compensation ?? []).SequenceEqual(other.Action.Compensation ?? []) &&
            (Action.Stops ?? []).SequenceEqual(other.Action.Stops ?? []) && TradeTerms.Same(Action.Terms,other.Action.Terms)));
    public override int GetHashCode() => HashCode.Combine(CommandId, ActorId, LocalSequence);
}
public record CommandResult(string CommandId, ResultStatus Status, string ReasonCode, long AppliedTick);
public record LedgerRecord(long Tick, AccountId DebitAccount, AccountId CreditAccount, long AmountMinor, string Reason);
public record SimulationEvent(long Sequence, long Tick, int Phase, string Type,
    ParticipantId? ActorId = null, long EntityId = 0, string ItemId = "", long Quantity = 0,
    long AmountMinor = 0, string ReasonCode = "", string? CommandId = null, SupportEventData? Support = null,TradeNegotiation? Negotiation=null);

public record ResourceNodeDefinition(string ItemId, Cell Position, ResourceSiteRules? Rules = null);
public record ItemDefinition(string Id, long UnitVolume, long BasePriceMinor);
public record RecipeDefinition(string Id, string InputItem = "", long InputQuantity = 0, string OutputItem = "",
    long OutputQuantity = 0, long FullStaffTicks = 0, int RequiredStaff = 0, int MinimumStaff = 0, long EnergyPerBatchMinor = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] System.Collections.Immutable.ImmutableArray<MaterialCost> Inputs = default)
{
    // Legacy scenario syntax remains readable; every runtime consumer uses the same ingredient list.
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Immutable.ImmutableArray<MaterialCost> Ingredients => Inputs.IsDefault ? [new(InputItem,InputQuantity)] : Inputs;
    public bool Uses(string item) => Ingredients.Any(i=>i.ItemId==item);
}
public record ParticipantDefinition(long Id, long CashMinor, Cell Position, long InventoryCapacity, string PublicPreferenceItem = "");
public record EnterpriseDefinition(long Id, long OwnerId, Cell Position, string RecipeId,
    long InputCapacity, long OutputCapacity, int Staff, int Capacity, long WageMinor, bool Enabled = false,
    string BuildingTypeId = "", int Level = 1, int WorkRateMultiplier = 1);
public enum BuildingKind { Production, Warehouse, Shop }
public record MaterialCost(string ItemId, long Quantity);
public record PlotDefinition(long Id, Cell Position, long PriceMinor, bool IsCorridor = false,
    long? OwnerId = null, long? ShopParticipantId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool IsShopSite = false);
public record BuildingLevelDefinition(int Level, string RecipeId, long PriceMinor, MaterialCost[] Materials,
    long InputCapacity, long OutputCapacity, int StaffCapacity, long WageMinor, int WorkRateMultiplier = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] System.Collections.Immutable.ImmutableArray<string> AdditionalRecipeIds = default,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long ConstructionTicks=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int WorkshopSlots=0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> UnlockedRecipes => RecipeId.Length==0 ? [] : new[]{RecipeId}.Concat(AdditionalRecipeIds.IsDefault?[]:AdditionalRecipeIds);
}
public record BuildingTypeDefinition(string Id, BuildingKind Kind, Cell[] Footprint, BuildingLevelDefinition[] Levels, Cell[]? Walls = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] WorkshopOutfit? WorkshopOutfit=null);
public record BuildingDefinition(long Id, long OwnerId, Cell Position, string TypeId, int Level = 1,
    int Staff = 0, bool Enabled = false);
public enum EquipmentMode { Off, Personal, Automatic }
public record EquipmentLevelDefinition(int Level, long PriceMinor, long CycleQuantity, long CycleTicks,
    long StorageCapacity, int MinimumCrew, int CrewCapacity, long WageMinor);
public record EquipmentTypeDefinition(string Id, string ItemId, EquipmentLevelDefinition[] Levels);
public record CapitalOfferDefinition(long Id, string ItemId, long Quantity, long UnitPriceMinor, Cell Position);
public record LaborSettings(int StepTicks = 10, int SalaryChangeDelayTicks = 20,
    int MobilityNumerator = 1, int MobilityDenominator = 10,
    int GrowthPeriodTicks = 120, long GrowthPerPeriod = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? MaximumPopulation = null);
public enum EmploymentRole { Production, Logistics, Extraction }
public record ScenarioDefinition(string Id, ulong Seed, int MapWidth, int MapHeight, Cell[] BlockedCells,
    ItemDefinition[] Items, RecipeDefinition[] Recipes, ParticipantDefinition[] Participants,
    EnterpriseDefinition[] Enterprises, CapitalOfferDefinition[] CapitalOffers, ResourceNodeDefinition[] ResourceNodes,
    int HarvestTicksPerUnit = 2, int MoveTicksPerCell = 1, int TradeWindowTicks = 5,
    int MaxCommandsPerActorTick = 8, long Population = 2, long MaxTicks = 500,
    PlotDefinition[]? Plots = null, BuildingTypeDefinition[]? BuildingTypes = null,
    BuildingDefinition[]? Buildings = null, int LandReservationTicks = 120, int MaxActivePlotReservations = 4,
    LaborSettings? Labor = null, EquipmentTypeDefinition[]? EquipmentTypes = null, MarketSettings? Market = null, DeliverySettings? Delivery = null, LogisticsSettings? Logistics = null, CapitalDemandSettings? CapitalDemand = null, ElectionSettings? Elections = null, SupportSettings? Support = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] EconomicRules? Economy = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? EconomyProfileId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long StartingTreasuryMinor = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] NewGameProvenance? NewGame = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] MapLayout? Layout = null);

public record StockView(string ItemId, long Quantity, long Reserved);
public record InventoryView(InventoryId Id, ParticipantId OwnerId, Cell Position, long Capacity,
    long OccupiedVolume, long ReservedCapacity, StockView[] Stock,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] OverflowLot[]? Overflow=null);
public record AccountView(AccountId Id, string Kind, long BalanceMinor, long ReservedMinor)
{
    public long AvailableMinor => checked(BalanceMinor - ReservedMinor);
}
public record ParticipantView(ParticipantId Id, AccountId AccountId, InventoryId InventoryId,
    Cell Position, bool Busy, Cell? Destination, long? ArrivalTick);
public record EnterpriseView(EnterpriseId Id, ParticipantId OwnerId, InventoryId InputId,
    InventoryId OutputId, bool Enabled, int Staff, string ReasonCode, long BatchId, long WorkDone,
    long WorkRequired, long CompletedBatches, long EnergyPaidMinor, string RecipeId, int Level, int StaffCapacity,
    int WorkRateMultiplier)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Immutable.ImmutableArray<WorkshopView> Workshops {get;init;}=default;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public System.Collections.Immutable.ImmutableArray<WorkshopInstallation> Installations {get;init;}=default;
    public string BatchRecipeId {get;init;}="";
    public System.Collections.Immutable.ImmutableArray<MaterialCost> BatchInputs {get;init;}=[];
}
public record PlotView(PlotId Id, Cell Position, long PriceMinor, bool IsCorridor, ParticipantId? OwnerId,
    ParticipantId? ReservedBy, long? ReservationUntilTick, BuildingId? BuildingId, ParticipantId? ShopParticipantId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool IsShopSite = false);
public record BuildingView(BuildingId Id, ParticipantId OwnerId, string TypeId, BuildingKind Kind,
    int Level, Cell Position, Cell[] Footprint, InventoryId InputId, InventoryId? OutputId);
public record FlowView(string ItemId, long Initial, long Harvested, long RecipeInput, long Produced, long Exported,
    long ConstructionInput,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long Lost=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long ConstructionRecovered=0);
public record CapitalOfferView(long Id, string ItemId, long RemainingQuantity, long UnitPriceMinor,
    Cell Position, long? CutoffTick, long RequestedQuantity, int Window = 1,
    long OriginalQuantity = 0, long CreatedTick = 0, long? ExpiresAtTick = null, long ReleasedQuantity = 0);
public record SettlementView(long Id, long OfferId, ParticipantId Seller, string ItemId,
    long Requested, long Accepted, long PaidMinor, long Tick, string Status);
public record EmploymentView(EmploymentGroupId Id, ParticipantId OwnerId, EmploymentRole Role,
    int Headcount, int Capacity, long WageMinor, long AttractionWageMinor, long? PendingWageMinor,
    long? PayEffectiveTick, long? PendingAttractionWageMinor, long? AttractionEffectiveTick,
    long MobilityCredit, int TargetHeadcount, bool Ready, string ReadinessReason, long AssessedAtTick);
public record PublicEmploymentView(EmploymentGroupId Id, ParticipantId OwnerId, EmploymentRole Role,
    int Headcount, int Capacity, long WageMinor);
public record EquipmentView(EquipmentId Id, ParticipantId OwnerId, string TypeId, int Level,
    Cell Position, InventoryId StorageId, EmploymentGroupId GroupId, EquipmentMode Mode,
    string ReasonCode, long CycleId, long WorkDone, long WorkRequired, long CompletedCycles, long PersonalJobId)
{
    public EquipmentMotion? Motion { get; init; }
    public ExtractionRoute? Route { get; init; }
}
public record WorldView(long Tick, long NextId, ulong AllocationRandomState, string ConfigHash,
    AccountView[] Accounts, InventoryView[] Inventories, ParticipantView[] Participants,
    EnterpriseView[] Enterprises, FlowView[] Flows, CapitalOfferView[] CapitalOffers,
    SettlementView[] Settlements, long Population, long Unemployed, long InitialMoneyMinor,
    long ExternalMoneyInMinor, long ExternalMoneyOutMinor, PlotView[] Plots, BuildingView[] Buildings,
    EmploymentView[] EmploymentGroups, long PopulationGrowth, long LastGrowthPeriod, EquipmentView[] Equipment, MarketOfferView[] MarketOffers, MarketBidView[] MarketBids, MarketAllocationView[] MarketAllocations, BuyOfferView[] BuyOffers, SupplyRequest[] SupplyRequests, DeliveryContractView[] DeliveryContracts, LogisticsView[] Logistics, CapitalDemandView? CapitalDemand, ElectionStateView Elections);
public record ParticipantObservation(long Tick, ParticipantView Participant, AccountView Account,
    InventoryView[] OwnInventories, EnterpriseView[] OwnEnterprises, CapitalOfferView[] CapitalOffers,
    PlotView[] Plots, BuildingView[] OwnBuildings, BuildingTypeDefinition[] BuildingTypes,
    EmploymentView[] OwnEmploymentGroups, PublicEmploymentView[] LaborMarket, long Population, long Unemployed, EquipmentView[] OwnEquipment, EquipmentTypeDefinition[] EquipmentTypes, MarketOfferView[] MarketOffers, MarketBidView[] OwnMarketBids, MarketAllocationView[] OwnMarketAllocations, MarketSettings MarketRules, BuyOfferView[] BuyOffers, SupplyRequest[] OwnSupplyRequests, DeliveryContractView[] OwnDeliveryContracts, DeliverySettings DeliveryRules, LogisticsView[] OwnLogistics, LogisticsSettings LogisticsRules, System.Collections.Immutable.ImmutableArray<MarketPricePoint> CapitalPrices, System.Collections.Immutable.ImmutableArray<MarketPricePoint> CapitalPriceHistory, ElectionObservation Elections, SupportObservation Support, CityEconomy City)
{
    public System.Collections.Immutable.ImmutableArray<ResourceSiteView> Resources { get; init; } = [];
    public System.Collections.Immutable.ImmutableArray<TradeNegotiation> Negotiations {get;init;}=[];
    public NegotiationSettings NegotiationRules {get;init;}=new();
    public System.Collections.Immutable.ImmutableArray<ParticipantId> TradingParticipants {get;init;}=[];
}

public static class SimulationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

/// <summary>SplitMix64 v1. Overflow is intentional and independent of System.Random.</summary>
internal sealed class DeterministicRandom(ulong seed)
{
    public ulong State { get; private set; } = seed;
    public ulong Next()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            ulong z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    internal void RestoreState(ulong state) => State = state;
    public int Index(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        ulong bound = (ulong)exclusiveMax;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = Next(); } while (value < threshold);
        return (int)(value % bound);
    }
    public void Shuffle<T>(T[] array)
    {
        for (int i = array.Length - 1; i > 0; --i)
        {
            int j = Index(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}

internal sealed class RuleViolation(string reason) : Exception(reason)
{
    public string ReasonCode { get; } = reason;
}

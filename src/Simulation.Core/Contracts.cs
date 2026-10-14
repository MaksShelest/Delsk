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
public readonly record struct Cell(int X, int Z);

public enum ActionKind { MoveTo, Harvest, Transfer, SetProductionEnabled, SellAvailableToCapital, CancelCapitalSale,
    ReservePlot, BuyPlot, CancelPlotReservation, Build, UpgradeBuilding, SetRecipe, SetWage, BuyEquipment, UpgradeEquipment, SetEquipmentMode }
public enum ResultStatus { Accepted, Rejected }
public record GameAction(ActionKind Kind, Cell Target = default, string ItemId = "", long Quantity = 0,
    InventoryId Source = default, InventoryId Destination = default, EnterpriseId Enterprise = default,
    bool Enabled = false, long OfferId = 0, PlotId Plot = default, BuildingId Building = default,
    string BuildingTypeId = "", string RecipeId = "", EmploymentGroupId Group = default, long WageMinor = 0, EquipmentId Equipment = default,
    string EquipmentTypeId = "", int EquipmentLevel = 1, EquipmentMode Mode = EquipmentMode.Personal);
public record CommandEnvelope(string CommandId, ParticipantId ActorId, long LocalSequence,
    long IssuedAtTick, long NotBeforeTick, long ExpiresAtTick, GameAction Action);
public record CommandResult(string CommandId, ResultStatus Status, string ReasonCode, long AppliedTick);
public record LedgerRecord(long Tick, AccountId DebitAccount, AccountId CreditAccount, long AmountMinor, string Reason);
public record SimulationEvent(long Sequence, long Tick, int Phase, string Type,
    ParticipantId? ActorId = null, long EntityId = 0, string ItemId = "", long Quantity = 0,
    long AmountMinor = 0, string ReasonCode = "", string? CommandId = null);

public record ResourceNodeDefinition(string ItemId, Cell Position);
public record ItemDefinition(string Id, long UnitVolume, long BasePriceMinor);
public record RecipeDefinition(string Id, string InputItem, long InputQuantity, string OutputItem,
    long OutputQuantity, long FullStaffTicks, int RequiredStaff, int MinimumStaff, long EnergyPerBatchMinor);
public record ParticipantDefinition(long Id, long CashMinor, Cell Position, long InventoryCapacity);
public record EnterpriseDefinition(long Id, long OwnerId, Cell Position, string RecipeId,
    long InputCapacity, long OutputCapacity, int Staff, int Capacity, long WageMinor, bool Enabled = false,
    string BuildingTypeId = "", int Level = 1, int WorkRateMultiplier = 1);
public enum BuildingKind { Production, Warehouse, Shop }
public record MaterialCost(string ItemId, long Quantity);
public record PlotDefinition(long Id, Cell Position, long PriceMinor, bool IsCorridor = false,
    long? OwnerId = null, long? ShopParticipantId = null);
public record BuildingLevelDefinition(int Level, string RecipeId, long PriceMinor, MaterialCost[] Materials,
    long InputCapacity, long OutputCapacity, int StaffCapacity, long WageMinor, int WorkRateMultiplier = 1);
public record BuildingTypeDefinition(string Id, BuildingKind Kind, Cell[] Footprint, BuildingLevelDefinition[] Levels);
public record BuildingDefinition(long Id, long OwnerId, Cell Position, string TypeId, int Level = 1,
    int Staff = 0, bool Enabled = false);
public enum EquipmentMode { Off, Personal, Automatic }
public record EquipmentLevelDefinition(int Level, long PriceMinor, long CycleQuantity, long CycleTicks,
    long StorageCapacity, int MinimumCrew, int CrewCapacity, long WageMinor);
public record EquipmentTypeDefinition(string Id, string ItemId, EquipmentLevelDefinition[] Levels);
public record CapitalOfferDefinition(long Id, string ItemId, long Quantity, long UnitPriceMinor, Cell Position);
public record LaborSettings(int StepTicks = 10, int SalaryChangeDelayTicks = 20,
    int MobilityNumerator = 1, int MobilityDenominator = 10,
    int GrowthPeriodTicks = 120, long GrowthPerPeriod = 1);
public enum EmploymentRole { Production, Logistics, Extraction }
public record ScenarioDefinition(string Id, ulong Seed, int MapWidth, int MapHeight, Cell[] BlockedCells,
    ItemDefinition[] Items, RecipeDefinition[] Recipes, ParticipantDefinition[] Participants,
    EnterpriseDefinition[] Enterprises, CapitalOfferDefinition[] CapitalOffers, ResourceNodeDefinition[] ResourceNodes,
    int HarvestTicksPerUnit = 2, int MoveTicksPerCell = 1, int TradeWindowTicks = 5,
    int MaxCommandsPerActorTick = 8, long Population = 2, long MaxTicks = 500,
    PlotDefinition[]? Plots = null, BuildingTypeDefinition[]? BuildingTypes = null,
    BuildingDefinition[]? Buildings = null, int LandReservationTicks = 120, int MaxActivePlotReservations = 4,
    LaborSettings? Labor = null, EquipmentTypeDefinition[]? EquipmentTypes = null);

public record StockView(string ItemId, long Quantity, long Reserved);
public record InventoryView(InventoryId Id, ParticipantId OwnerId, Cell Position, long Capacity,
    long OccupiedVolume, long ReservedCapacity, StockView[] Stock);
public record AccountView(AccountId Id, string Kind, long BalanceMinor, long ReservedMinor)
{
    public long AvailableMinor => checked(BalanceMinor - ReservedMinor);
}
public record ParticipantView(ParticipantId Id, AccountId AccountId, InventoryId InventoryId,
    Cell Position, bool Busy, Cell? Destination, long? ArrivalTick);
public record EnterpriseView(EnterpriseId Id, ParticipantId OwnerId, InventoryId InputId,
    InventoryId OutputId, bool Enabled, int Staff, string ReasonCode, long BatchId, long WorkDone,
    long WorkRequired, long CompletedBatches, long EnergyPaidMinor, string RecipeId, int Level, int StaffCapacity,
    int WorkRateMultiplier);
public record PlotView(PlotId Id, Cell Position, long PriceMinor, bool IsCorridor, ParticipantId? OwnerId,
    ParticipantId? ReservedBy, long? ReservationUntilTick, BuildingId? BuildingId, ParticipantId? ShopParticipantId);
public record BuildingView(BuildingId Id, ParticipantId OwnerId, string TypeId, BuildingKind Kind,
    int Level, Cell Position, Cell[] Footprint, InventoryId InputId, InventoryId? OutputId);
public record FlowView(string ItemId, long Initial, long Harvested, long RecipeInput, long Produced, long Exported,
    long ConstructionInput);
public record CapitalOfferView(long Id, string ItemId, long RemainingQuantity, long UnitPriceMinor,
    Cell Position, long? CutoffTick, long RequestedQuantity);
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
    string ReasonCode, long CycleId, long WorkDone, long WorkRequired, long CompletedCycles, long PersonalJobId);
public record WorldView(long Tick, long NextId, ulong AllocationRandomState, string ConfigHash,
    AccountView[] Accounts, InventoryView[] Inventories, ParticipantView[] Participants,
    EnterpriseView[] Enterprises, FlowView[] Flows, CapitalOfferView[] CapitalOffers,
    SettlementView[] Settlements, long Population, long Unemployed, long InitialMoneyMinor,
    long ExternalMoneyInMinor, long ExternalMoneyOutMinor, PlotView[] Plots, BuildingView[] Buildings,
    EmploymentView[] EmploymentGroups, long PopulationGrowth, long LastGrowthPeriod, EquipmentView[] Equipment);
public record ParticipantObservation(long Tick, ParticipantView Participant, AccountView Account,
    InventoryView[] OwnInventories, EnterpriseView[] OwnEnterprises, CapitalOfferView[] CapitalOffers,
    PlotView[] Plots, BuildingView[] OwnBuildings, BuildingTypeDefinition[] BuildingTypes,
    EmploymentView[] OwnEmploymentGroups, PublicEmploymentView[] LaborMarket, long Population, long Unemployed, EquipmentView[] OwnEquipment, EquipmentTypeDefinition[] EquipmentTypes);

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

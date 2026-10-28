using System.Collections.Immutable;

namespace Simulation.Core;

// Immutable allowlist: no ScenarioDefinition, world hash, seed, journal or foreign accounts/stock.
public record ControllerInventory(InventoryId Id, Cell Position, long Capacity, long OccupiedVolume,
    long ReservedCapacity, ImmutableArray<StockView> Stock);
public record ControllerBuilding(BuildingId Id, string TypeId, BuildingKind Kind, int Level, Cell Position,
    ImmutableArray<Cell> Footprint, InventoryId InputId, InventoryId? OutputId);
public record ControllerBuildingLevel(int Level, string RecipeId, long PriceMinor,
    ImmutableArray<MaterialCost> Materials, long InputCapacity, long OutputCapacity, int StaffCapacity,
    long WageMinor, int WorkRateMultiplier, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> AdditionalRecipeIds = default,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int WorkshopSlots=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<Cell> Footprint=default,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long ConstructionTicks=0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> UnlockedRecipes => string.IsNullOrEmpty(RecipeId) ? [] : new[]{RecipeId}.Concat(AdditionalRecipeIds.IsDefault ? [] : AdditionalRecipeIds);
}
public record ControllerBuildingType(string Id, BuildingKind Kind, ImmutableArray<Cell> Footprint,
    ImmutableArray<ControllerBuildingLevel> Levels,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkshopOutfit? WorkshopOutfit=null);
public record ControllerEquipmentType(string Id, string ItemId, ImmutableArray<EquipmentLevelDefinition> Levels);
public record ControllerMap(int Width, int Height, ImmutableArray<Cell> BlockedCells,
    ImmutableArray<ResourceNodeDefinition> ResourceNodes, ImmutableArray<ItemDefinition> Items,
    ImmutableArray<RecipeDefinition> Recipes, int MoveTicksPerCell, int HarvestTicksPerUnit, int TradeWindowTicks);
public record ControllerEconomics(LaborSettings Labor,HandlingRules Handling,int WageExponent,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkshopRules? Workshops=null);
public record ControllerObservation(long Tick, ParticipantView Participant, AccountView Account,
    ImmutableArray<ControllerInventory> Inventories, ImmutableArray<EnterpriseView> Enterprises,
    ImmutableArray<CapitalOfferView> CapitalOffers, ImmutableArray<PlotView> Plots,
    ImmutableArray<ControllerBuilding> Buildings, ImmutableArray<ControllerBuildingType> BuildingTypes,
    ImmutableArray<EmploymentView> EmploymentGroups, ImmutableArray<PublicEmploymentView> LaborMarket,
    long Population, long Unemployed, ImmutableArray<EquipmentView> Equipment,
    ImmutableArray<ControllerEquipmentType> EquipmentTypes, ControllerMap Map,
    ImmutableArray<MarketOfferView> MarketOffers, ImmutableArray<MarketBidView> OwnMarketBids,
    ImmutableArray<MarketAllocationView> OwnMarketAllocations, MarketSettings MarketRules, ImmutableArray<BuyOfferView> BuyOffers, ImmutableArray<SupplyRequest> OwnSupplyRequests,
    ImmutableArray<DeliveryContractView> OwnDeliveryContracts, DeliverySettings DeliveryRules, ImmutableArray<LogisticsView> OwnLogistics, LogisticsSettings LogisticsRules,
    ImmutableArray<MarketPricePoint> CapitalPrices, ImmutableArray<MarketPricePoint> CapitalPriceHistory, ElectionObservation Elections, SupportObservation Support, CityEconomy City)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ControllerEconomics? Economics {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DevelopmentObservation? Development {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FleetObservation? Fleet {get;init;}
    public RoadObservation? Roads {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersonalAccount? PersonalAccount {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public ServiceObservation? Services {get;init;}
    public ProductionContentObservation? ProductionContent {get;init;}
    public System.Collections.Immutable.ImmutableArray<ResourceSiteView> Resources { get; init; } = [];
    public System.Collections.Immutable.ImmutableArray<TradeNegotiation> Negotiations {get;init;}=[];
    public NegotiationSettings NegotiationRules {get;init;}=new();
    public System.Collections.Immutable.ImmutableArray<ParticipantId> TradingParticipants {get;init;}=[];
}
public record ModelDecisionTrace(string DecisionId, long RequestedTick, long ApplyTick, long SessionEpoch,
    string Resolution, string ReasonCode, string RequestJson, string? ResponseJson = null,
    long? LatencyMilliseconds = null, long? PromptTokens = null, long? CompletionTokens = null, string? Model = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int RetryCount {get;init;}
}
public record ControllerDecision(string Goal, string Reason, ImmutableArray<GameAction> Actions, bool Record = true,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelDecisionTrace? ModelTrace = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<UtilityScore>? Candidates = null, int PlanStep = 0);
public record UtilityScore(string Goal,string Reason,long Score,int Steps);
public record ControllerCheckpoint(string Kind, int Version, ParticipantId Actor, string StateJson, ImmutableArray<string> PendingCommandIds);
public record ControllerSessionContext(string SessionId, long Epoch, long Tick, bool Restored);
public record BallotDecision(ParticipantId? Candidate, string Reason, ModelDecisionTrace? ModelTrace = null);

/// <summary>Replaceable policy; only the host assigns identity and submits validated commands.</summary>
public interface IController
{
    ParticipantId Actor { get; }
    ControllerDecision Decide(ControllerObservation observation, ImmutableArray<CommandResult> ownResults);
    ControllerCheckpoint Capture();
    void CommandSubmitted(string commandId);
    void BindSession(ControllerSessionContext context) { }
    void BeginDecisionBarrier(long tick) { }
    void EndDecisionBarrier(long tick) { }
    bool WaitingForModel => false;
    void BeginBallotBarrier(long barrier) { }
    void EndBallotBarrier(long barrier, long election) { }
    BallotDecision DecideBallot(ControllerObservation observation) => new(observation.Elections.Voting?.RequiredCandidate ?? observation.Elections.OwnChoice ?? Actor, "ExplicitSelfBallot");
}

public sealed partial class SimulationEngine
{
    public ControllerObservation ObserveController(ParticipantId actor)
    {
        var o = Observe(actor);
        return new(o.Tick, o.Participant, o.Account,
            o.OwnInventories.Select(i => new ControllerInventory(i.Id, i.Position, i.Capacity,
                i.OccupiedVolume, i.ReservedCapacity, [.. i.Stock])).ToImmutableArray(),
            [.. o.OwnEnterprises], [.. o.CapitalOffers], [.. o.Plots],
            o.OwnBuildings.Select(b => new ControllerBuilding(b.Id, b.TypeId, b.Kind, b.Level,
                b.Position, [.. b.Footprint], b.InputId, b.OutputId)).ToImmutableArray(),
            o.BuildingTypes.Select(t => new ControllerBuildingType(t.Id, t.Kind, [.. t.Footprint],
                t.Levels.Select(l => new ControllerBuildingLevel(l.Level, l.RecipeId, l.PriceMinor,
                    [.. l.Materials], l.InputCapacity, l.OutputCapacity, l.StaffCapacity,
                    l.WageMinor, l.WorkRateMultiplier, l.AdditionalRecipeIds,l.WorkshopSlots,l.Footprint,Development is null?0:l.ConstructionTicks)).ToImmutableArray(),t.WorkshopOutfit)).ToImmutableArray(),
            [.. o.OwnEmploymentGroups], [.. o.LaborMarket], o.Population, o.Unemployed, [.. o.OwnEquipment],
            o.EquipmentTypes.Select(t => new ControllerEquipmentType(t.Id, t.ItemId, [.. t.Levels])).ToImmutableArray(),
            new(_config.MapWidth, _config.MapHeight, BlockedCells(), [.. _config.ResourceNodes],
                [.. _config.Items], [.. _config.Recipes], _config.MoveTicksPerCell,
                _config.HarvestTicksPerUnit, _config.TradeWindowTicks), [.. o.MarketOffers], [.. o.OwnMarketBids], [.. o.OwnMarketAllocations], o.MarketRules, [.. o.BuyOffers], [.. o.OwnSupplyRequests], [.. o.OwnDeliveryContracts], o.DeliveryRules, [.. o.OwnLogistics], o.LogisticsRules, o.CapitalPrices, o.CapitalPriceHistory, o.Elections, o.Support, o.City) { Services=ServicesOf(actor),PersonalAccount=Billing is null?null:AccountOf(actor),Roads=RoadsOf(actor),Fleet=FleetOf(actor),ProductionContent=ProductionContentOf(actor),Development=DevelopmentOf(actor),Economics=new(Labor,EconomyRules.Handling??new(),EconomyRules.WageAttractionExponent,_config.Economy?.Workshops),Resources = o.Resources,Negotiations=o.Negotiations,NegotiationRules=o.NegotiationRules,TradingParticipants=o.TradingParticipants };
    }
}

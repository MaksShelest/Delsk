using System.Collections.Immutable;

namespace Simulation.Core;

// Immutable allowlist: no ScenarioDefinition, world hash, seed, journal or foreign accounts/stock.
public record ControllerInventory(InventoryId Id, Cell Position, long Capacity, long OccupiedVolume,
    long ReservedCapacity, ImmutableArray<StockView> Stock);
public record ControllerBuilding(BuildingId Id, string TypeId, BuildingKind Kind, int Level, Cell Position,
    ImmutableArray<Cell> Footprint, InventoryId InputId, InventoryId? OutputId);
public record ControllerBuildingLevel(int Level, string RecipeId, long PriceMinor,
    ImmutableArray<MaterialCost> Materials, long InputCapacity, long OutputCapacity, int StaffCapacity,
    long WageMinor, int WorkRateMultiplier);
public record ControllerBuildingType(string Id, BuildingKind Kind, ImmutableArray<Cell> Footprint,
    ImmutableArray<ControllerBuildingLevel> Levels);
public record ControllerEquipmentType(string Id, string ItemId, ImmutableArray<EquipmentLevelDefinition> Levels);
public record ControllerMap(int Width, int Height, ImmutableArray<Cell> BlockedCells,
    ImmutableArray<ResourceNodeDefinition> ResourceNodes, ImmutableArray<ItemDefinition> Items,
    ImmutableArray<RecipeDefinition> Recipes, int MoveTicksPerCell, int HarvestTicksPerUnit, int TradeWindowTicks);
public record ControllerObservation(long Tick, ParticipantView Participant, AccountView Account,
    ImmutableArray<ControllerInventory> Inventories, ImmutableArray<EnterpriseView> Enterprises,
    ImmutableArray<CapitalOfferView> CapitalOffers, ImmutableArray<PlotView> Plots,
    ImmutableArray<ControllerBuilding> Buildings, ImmutableArray<ControllerBuildingType> BuildingTypes,
    ImmutableArray<EmploymentView> EmploymentGroups, ImmutableArray<PublicEmploymentView> LaborMarket,
    long Population, long Unemployed, ImmutableArray<EquipmentView> Equipment,
    ImmutableArray<ControllerEquipmentType> EquipmentTypes, ControllerMap Map,
    ImmutableArray<MarketOfferView> MarketOffers, ImmutableArray<MarketBidView> OwnMarketBids,
    ImmutableArray<MarketAllocationView> OwnMarketAllocations, MarketSettings MarketRules, ImmutableArray<BuyOfferView> BuyOffers, ImmutableArray<SupplyRequest> OwnSupplyRequests,
    ImmutableArray<DeliveryContractView> OwnDeliveryContracts, DeliverySettings DeliveryRules);
public record ControllerDecision(string Goal, string Reason, ImmutableArray<GameAction> Actions, bool Record = true);
public record ControllerCheckpoint(string Kind, int Version, ParticipantId Actor, string StateJson, ImmutableArray<string> PendingCommandIds);

/// <summary>Replaceable policy; only the host assigns identity and submits validated commands.</summary>
public interface IController
{
    ParticipantId Actor { get; }
    ControllerDecision Decide(ControllerObservation observation, ImmutableArray<CommandResult> ownResults);
    ControllerCheckpoint Capture();
    void CommandSubmitted(string commandId);
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
                    l.WageMinor, l.WorkRateMultiplier)).ToImmutableArray())).ToImmutableArray(),
            [.. o.OwnEmploymentGroups], [.. o.LaborMarket], o.Population, o.Unemployed, [.. o.OwnEquipment],
            o.EquipmentTypes.Select(t => new ControllerEquipmentType(t.Id, t.ItemId, [.. t.Levels])).ToImmutableArray(),
            new(_config.MapWidth, _config.MapHeight, [.. _config.BlockedCells], [.. _config.ResourceNodes],
                [.. _config.Items], [.. _config.Recipes], _config.MoveTicksPerCell,
                _config.HarvestTicksPerUnit, _config.TradeWindowTicks), [.. o.MarketOffers], [.. o.OwnMarketBids], [.. o.OwnMarketAllocations], o.MarketRules, [.. o.BuyOffers], [.. o.OwnSupplyRequests], [.. o.OwnDeliveryContracts], o.DeliveryRules);
    }
}

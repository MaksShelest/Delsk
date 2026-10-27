using System.Collections.Immutable;

namespace Simulation.Core;

public enum ActionReach { Remote, AtLocation }
public sealed record ActionAccess(ActionReach Reach, ImmutableArray<Cell> Locations, string ReasonCode)
{
    public bool Allowed => ReasonCode == "Allowed";
}

/// <summary>Exhaustive command geometry. Ownership, resources and deadlines remain in their validators.</summary>
public static class ActionPolicy
{
    public static ActionReach Reach(ActionKind kind) => kind switch
    {
        ActionKind.SetRoadWork or ActionKind.TurnVehicle or ActionKind.BuyVehicle or ActionKind.BoardVehicle or ActionKind.AssignVehicle or ActionKind.InstallVehicleKit or ActionKind.BuildLoadingPlatform or ActionKind.BuyImport or ActionKind.ModernizeWorkshop or ActionKind.BuyExpansionLand or ActionKind.OutfitWorkshop or ActionKind.DemolishBuilding or ActionKind.CancelConstruction or ActionKind.TakeOverflow or ActionKind.BuyResource or ActionKind.LeaseResource or ActionKind.Harvest or ActionKind.StartHarvesting or ActionKind.Transfer or ActionKind.SellAvailableToCapital or
        ActionKind.ReservePlot or ActionKind.BuyPlot or ActionKind.Build or ActionKind.UpgradeBuilding or
        ActionKind.SetWage or ActionKind.BuyEquipment or ActionKind.UpgradeEquipment or ActionKind.CollectPurchase or
        ActionKind.DeliverContract or ActionKind.BuyLogistics or ActionKind.UpgradeLogistics or ActionKind.SignSupport => ActionReach.AtLocation,
        ActionKind.SetDeliveryService or ActionKind.RequestDeliveryService or ActionKind.AcceptDeliveryService or ActionKind.RejectDeliveryService or ActionKind.ConfirmDeliveryService or ActionKind.CancelDeliveryService or ActionKind.ViewDeliveryService or ActionKind.PayInvoice or ActionKind.ViewInvoice or ActionKind.SetAutoPay or ActionKind.AcceptRoadOrder or ActionKind.LeaveVehicle or ActionKind.PrepareEnterpriseExpansion or ActionKind.CancelExpansionPreparation or ActionKind.AcceptIndustryQuest or ActionKind.AbandonIndustryQuest or ActionKind.AssignWorkshopWorkers or ActionKind.PrepareDemolition or ActionKind.CancelDemolitionPreparation or ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade or ActionKind.RejectTrade or ActionKind.MoveEquipment or ActionKind.SetExtractionRoute or ActionKind.SetTransportRoute or ActionKind.StopTransfer or ActionKind.MoveTo or ActionKind.CancelCapitalSale or ActionKind.SetTravelHeading or ActionKind.StopHarvest or
        ActionKind.SetProductionEnabled or ActionKind.CancelPlotReservation or ActionKind.SetRecipe or ActionKind.SetEquipmentMode or
        ActionKind.CreateSellOffer or ActionKind.SubmitBuyRequest or ActionKind.RaiseBid or ActionKind.WithdrawBid or
        ActionKind.CreateBuyOffer or ActionKind.AcceptDeliveryQuantity or ActionKind.WithdrawSupplyRequest or ActionKind.CancelBuyOffer or
        ActionKind.SetLogisticsContract or ActionKind.SetLogisticsStorage or ActionKind.SetLogisticsInbound or ActionKind.SetLogisticsProduction or
        ActionKind.QueueLogisticsJob or ActionKind.SetLogisticsEnabled or ActionKind.SetVoteChoice or ActionKind.ContinueAfterVictory or
        ActionKind.ProposeSupport or ActionKind.AcceptSupport or ActionKind.RejectSupport => ActionReach.Remote,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public sealed partial class SimulationEngine
{
    private void RequireActionLocation(ActorState actor, GameAction action, string reason = "NotAtLocation")
    {
        var access = AccessFor(new(actor.Id), action);
        if (!access.Allowed) throw new RuleViolation(access.ReasonCode is "NotAvailable" or "ConstructionInProgress" ? access.ReasonCode : reason);
    }
    /// <summary>Participant-safe read query, also called immediately before execution. No hidden-ID oracle.</summary>
    public ActionAccess AccessFor(ParticipantId participant, GameAction action)
    {
        var reach = ActionPolicy.Reach(action.Kind);
        if (!_actors.TryGetValue(participant.Value, out var actor)) return new(reach, [], "NotAvailable");
        if (WaitingForBallots && action.Kind != ActionKind.SetVoteChoice) return new(reach, [], "ElectionInProgress");
        if (action.Kind == ActionKind.SetVoteChoice && Elections.PauseForBallots && RequiredCandidate(actor.Id) is { } required && required != action.Candidate)
            return new(reach, [], "CommittedVote");
        if((NewTradingAction(action.Kind) || action.Kind==ActionKind.AcceptDeliveryQuantity && action.BuyOfferId>0) && !HasTradingAccess(participant))return new(reach,[],"ShopRequired");
        Cell? constructionLocation=action.Kind==ActionKind.Build? action.Target : action.Kind==ActionKind.UpgradeBuilding && _buildings.TryGetValue(action.Building.Value,out var ownBuilding)&&ownBuilding.Owner==actor.Id?ownBuilding.Position:null;
        if(constructionLocation is {} cell&&_storageConstruction.Values.Any(c=>c.Owner==actor.Id&&c.Position==cell))return new(reach,[cell],"ConstructionInProgress");
        if (reach == ActionReach.Remote) return new(reach, [], "Allowed");
        Cell[]? locations = action.Kind switch
        {
            ActionKind.BuyVehicle=>Fleet is null?null:[Dealer.Entry],
            ActionKind.SetRoadWork or ActionKind.TurnVehicle or ActionKind.BoardVehicle or ActionKind.InstallVehicleKit=>_vehicles.TryGetValue(action.VehicleId,out var vehicle)&&vehicle.Owner.Value==actor.Id?[VehicleCargo(vehicle).Position]:null,
            ActionKind.AssignVehicle=>_enterprises.TryGetValue(action.Enterprise.Value,out var assigning)&&assigning.Definition.OwnerId==actor.Id?[assigning.Definition.Position]:null,
            ActionKind.BuildLoadingPlatform=>_buildings.TryGetValue(action.Building.Value,out var platformBuilding)&&platformBuilding.Owner==actor.Id&&!Demolished(platformBuilding.Id)?[platformBuilding.Position]:null,
            ActionKind.BuyImport=>Content is null?null:[ImportTerminal],
            ActionKind.ModernizeWorkshop=>_enterprises.TryGetValue(action.Enterprise.Value,out var modernizing)&&modernizing.Definition.OwnerId==actor.Id&&!Demolished(modernizing.Definition.Id)?[modernizing.Definition.Position]:null,
            ActionKind.BuyResource or ActionKind.LeaseResource => _resources.Keys.Any(k=>k.Position==action.Target) ? [action.Target] : null,
            ActionKind.SetWage => WageLocation(actor.Id, action.Group.Value),
            ActionKind.ReservePlot or ActionKind.BuyPlot => _plots.TryGetValue(action.Plot.Value, out var plot) ? [plot.Definition.Position] : null,
            ActionKind.Build => [action.Target],
            ActionKind.DemolishBuilding=>_buildings.TryGetValue(action.Building.Value,out var demolishing)&&demolishing.Owner==actor.Id&&!Demolished(demolishing.Id)?[demolishing.Position]:null,
            ActionKind.CancelConstruction=>_storageConstruction.TryGetValue(action.OfferId,out var construction)&&construction.Owner==actor.Id?[construction.Position]:null,
            ActionKind.UpgradeBuilding => _buildings.TryGetValue(action.Building.Value, out var building) && building.Owner == actor.Id ? [building.Position] : null,
            ActionKind.BuyExpansionLand => _buildings.TryGetValue(action.Building.Value,out var expanding) && expanding.Owner==actor.Id&&!Demolished(expanding.Id)?[expanding.Position]:null,
            ActionKind.OutfitWorkshop => _enterprises.TryGetValue(action.Enterprise.Value, out var outfitting) && outfitting.Definition.OwnerId == actor.Id && !Demolished(outfitting.Definition.Id) ? [outfitting.Definition.Position] : null,
            ActionKind.BuyLogistics => _enterprises.TryGetValue(action.Enterprise.Value, out var enterprise) && enterprise.Definition.OwnerId == actor.Id ? [enterprise.Definition.Position] : null,
            ActionKind.UpgradeLogistics => _logistics.TryGetValue(action.Logistics.Value, out var logistics) && logistics.Owner == actor.Id ? [_enterprises[logistics.Enterprise].Definition.Position] : null,
            ActionKind.BuyEquipment => [action.Target],
            ActionKind.UpgradeEquipment => _equipment.TryGetValue(action.Equipment.Value, out var machine) && machine.Owner == actor.Id ? [machine.Position] : null,
            ActionKind.Harvest or ActionKind.StartHarvesting => action.Equipment.Value != 0
                ? (_equipment.TryGetValue(action.Equipment.Value, out var equipment) && equipment.Owner == actor.Id ? [equipment.Position] : null)
                : _config.ResourceNodes.Where(n => n.ItemId == action.ItemId).Select(n => n.Position).ToArray(),
            ActionKind.Transfer => TransferLocations(actor, action),
            ActionKind.TakeOverflow=>_economy.Inventories.TryGetValue(action.Source.Value,out var overflow)&&overflow.Owner==actor.Id&&overflow.Overflow.ContainsKey(action.OfferId)?[overflow.Position]:null,
            ActionKind.SellAvailableToCapital => _offers.TryGetValue(action.OfferId, out var offer) ? [offer.Definition.Position] : null,
            ActionKind.CollectPurchase => _marketAllocations.FirstOrDefault(p => p.Id == action.PurchaseId && p.Buyer.Value == actor.Id) is { } purchase
                ? [_economy.Inventories[purchase.SourceInventory.Value].Position] : null,
            ActionKind.DeliverContract => _deliveryContracts.FirstOrDefault(d => d.Id == action.ContractId && d.Supplier.Value == actor.Id) is { } contract ? [contract.Position] : null,
            ActionKind.SignSupport => _supportProposals.Values.Any(p => p.Id == action.SupportProposalId && (p.Candidate.Value == actor.Id || p.Seller.Value == actor.Id)) && _config.Support is { } support
                ? [support.TradingPoint] : null,
            _ => null
        };
        if (locations is null) return new(reach, [], "NotAvailable");
        if (locations.Length == 0) return new(reach, [], action.Kind is ActionKind.Harvest or ActionKind.StartHarvesting ? "NoResourceNode" : "NotAtLocation");
        return new(reach, [.. locations], locations.Contains(actor.Position) ? "Allowed" : "NotAtLocation");
    }

    private Cell[]? WageLocation(long actor, long group)
    {
        var enterprise = ProductionUnits().FirstOrDefault(e => e.Group.Id == group && e.Definition.OwnerId == actor);
        if (enterprise is not null) return [enterprise.Definition.Position];
        if (_equipment.TryGetValue(group, out var machine) && machine.Owner == actor) return [machine.Position];
        if (_logistics.TryGetValue(group, out var logistics) && logistics.Owner == actor) return [_enterprises[logistics.Enterprise].Definition.Position];
        if (_roadWorks.TryGetValue(group,out var road) && road.Group.Owner==actor) return [VehicleCargo(_vehicles[group]).Position];
        if (_platforms.TryGetValue(group,out var p) && p.Group.Owner==actor) return [_buildings[p.Building].Position];
        return null;
    }
    private Cell[]? TransferLocations(ActorState actor, GameAction action)
    {
        if (!_economy.Inventories.TryGetValue(action.Source.Value, out var source) || source.Owner != actor.Id ||
            !_economy.Inventories.TryGetValue(action.Destination.Value, out var destination) || destination.Owner != actor.Id) return null;
        if (!TransferCarrier(actor,source.Id) && !TransferCarrier(actor,destination.Id)) return [];
        if (source.Id!=actor.Inventory && destination.Id!=actor.Inventory && source.Position!=destination.Position) return [];
        return [source.Id==actor.Inventory ? destination.Position : source.Position];
    }
}

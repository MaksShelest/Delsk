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
        ActionKind.Harvest or ActionKind.StartHarvesting or ActionKind.Transfer or ActionKind.SellAvailableToCapital or
        ActionKind.ReservePlot or ActionKind.BuyPlot or ActionKind.Build or ActionKind.UpgradeBuilding or
        ActionKind.SetWage or ActionKind.BuyEquipment or ActionKind.UpgradeEquipment or ActionKind.CollectPurchase or
        ActionKind.DeliverContract or ActionKind.BuyLogistics or ActionKind.UpgradeLogistics or ActionKind.SignSupport => ActionReach.AtLocation,
        ActionKind.MoveTo or ActionKind.CancelCapitalSale or ActionKind.SetTravelHeading or ActionKind.StopHarvest or
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
        if (!access.Allowed) throw new RuleViolation(access.ReasonCode == "NotAvailable" ? "NotAvailable" : reason);
    }
    /// <summary>Participant-safe read query, also called immediately before execution. No hidden-ID oracle.</summary>
    public ActionAccess AccessFor(ParticipantId participant, GameAction action)
    {
        var reach = ActionPolicy.Reach(action.Kind);
        if (!_actors.TryGetValue(participant.Value, out var actor)) return new(reach, [], "NotAvailable");
        if (WaitingForBallots && action.Kind != ActionKind.SetVoteChoice) return new(reach, [], "ElectionInProgress");
        if (action.Kind == ActionKind.SetVoteChoice && Elections.PauseForBallots && RequiredCandidate(actor.Id) is { } required && required != action.Candidate)
            return new(reach, [], "CommittedVote");
        if (reach == ActionReach.Remote) return new(reach, [], "Allowed");
        Cell[]? locations = action.Kind switch
        {
            ActionKind.SetWage => WageLocation(actor.Id, action.Group.Value),
            ActionKind.ReservePlot or ActionKind.BuyPlot => _plots.TryGetValue(action.Plot.Value, out var plot) ? [plot.Definition.Position] : null,
            ActionKind.Build => [action.Target],
            ActionKind.UpgradeBuilding => _buildings.TryGetValue(action.Building.Value, out var building) && building.Owner == actor.Id ? [building.Position] : null,
            ActionKind.BuyLogistics => _enterprises.TryGetValue(action.Enterprise.Value, out var enterprise) && enterprise.Definition.OwnerId == actor.Id ? [enterprise.Definition.Position] : null,
            ActionKind.UpgradeLogistics => _logistics.TryGetValue(action.Logistics.Value, out var logistics) && logistics.Owner == actor.Id ? [_enterprises[logistics.Enterprise].Definition.Position] : null,
            ActionKind.BuyEquipment => [action.Target],
            ActionKind.UpgradeEquipment => _equipment.TryGetValue(action.Equipment.Value, out var machine) && machine.Owner == actor.Id ? [machine.Position] : null,
            ActionKind.Harvest or ActionKind.StartHarvesting => action.Equipment.Value != 0
                ? (_equipment.TryGetValue(action.Equipment.Value, out var equipment) && equipment.Owner == actor.Id ? [equipment.Position] : null)
                : _config.ResourceNodes.Where(n => n.ItemId == action.ItemId).Select(n => n.Position).ToArray(),
            ActionKind.Transfer => TransferLocations(actor, action),
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
        if (_enterprises.TryGetValue(group, out var enterprise) && enterprise.Definition.OwnerId == actor) return [enterprise.Definition.Position];
        if (_equipment.TryGetValue(group, out var machine) && machine.Owner == actor) return [machine.Position];
        if (_logistics.TryGetValue(group, out var logistics) && logistics.Owner == actor) return [_enterprises[logistics.Enterprise].Definition.Position];
        return null;
    }
    private Cell[]? TransferLocations(ActorState actor, GameAction action)
    {
        if (!_economy.Inventories.TryGetValue(action.Source.Value, out var source) || source.Owner != actor.Id ||
            !_economy.Inventories.TryGetValue(action.Destination.Value, out var destination) || destination.Owner != actor.Id) return null;
        if (source.Id != actor.Inventory && destination.Id != actor.Inventory) return [];
        return [source.Id == actor.Inventory ? destination.Position : source.Position];
    }
}

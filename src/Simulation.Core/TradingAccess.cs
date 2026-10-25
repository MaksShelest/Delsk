using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    public bool HasTradingAccess(ParticipantId actor) => _buildings.Values.Any(b=>b.Owner==actor.Value && !Demolished(b.Id) && b.Type.Kind==BuildingKind.Shop);
    private ImmutableArray<ParticipantId> TradingParticipants() => [.._actors.Keys.Order().Where(id=>HasTradingAccess(new(id))).Select(id=>new ParticipantId(id))];
    private void RequireTradingAccess(long actor,long? other=null)
    {
        if(!HasTradingAccess(new(actor)) || other is {} id && !HasTradingAccess(new(id)))throw new RuleViolation("ShopRequired");
    }
    private bool IsTradingSource(long actor,long inventory) => _economy.Inventories.TryGetValue(inventory,out var i) && i.Owner==actor &&
        _buildings.Values.Any(b=>b.Owner==actor && (b.Input==inventory || b.Output==inventory));
    private bool IsTradingDestination(long actor,long inventory) => _economy.Inventories.TryGetValue(inventory,out var i) && i.Owner==actor && (Storage is null||!HasOverflow(inventory)) &&
        _buildings.Values.Any(b=>b.Owner==actor && b.Input==inventory);
    private static bool NewTradingAction(ActionKind kind) => kind is ActionKind.CreateSellOffer or ActionKind.SubmitBuyRequest or ActionKind.RaiseBid or
        ActionKind.CreateBuyOffer or ActionKind.AcceptSupport or ActionKind.SignSupport or ActionKind.ProposeSupport or ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade;
}

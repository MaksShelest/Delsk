using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void MarketWindow(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId seller = new(1); var shop = session.Engine.Observe(seller).OwnBuildings.Single(b => b.Kind == BuildingKind.Shop);
        var node = definition.ResourceNodes.Single(n => n.ItemId == "wood"); Move(session, seller, node.Position);
        Execute(session, seller, new(ActionKind.Harvest, ItemId: "wood", Quantity: 15));
        Wait(session, () => !session.Engine.Observe(seller).Participant.Busy); Move(session, seller, shop.Position);
        Execute(session, seller, new(ActionKind.Transfer, ItemId: "wood", Quantity: 15,
            Source: session.Engine.Observe(seller).Participant.InventoryId, Destination: shop.InputId));
        Execute(session, seller, new(ActionKind.CreateSellOffer, Building: shop.Id, ItemId: "wood", Quantity: 15, UnitPriceMinor: 500));
        long offer = session.Engine.Observe(seller).MarketOffers.Single().Id;
        var ids = new List<string>();
        foreach (var (actor, quantity) in new[] { (2L, 6L), (3L, 8L), (4L, 3L) })
            ids.Add(session.Send(new(actor), new(ActionKind.SubmitBuyRequest, MarketOfferId: offer, Quantity: quantity, MaximumPriceMinor: 1000)));
        session.Step();
        if (ids.Any(id => session.Engine.Result(id)?.Status != ResultStatus.Accepted)) throw new InvalidOperationException("Market bids failed");
        Wait(session, () => session.Engine.Inspect().MarketOffers.Single().Status == MarketOfferStatus.Auction);
        Execute(session, new(3), new(ActionKind.RaiseBid, MarketOfferId: offer, MarketRound: 1, UnitPriceMinor: 600));
        Execute(session, new(4), new(ActionKind.RaiseBid, MarketOfferId: offer, MarketRound: 1, UnitPriceMinor: 700));
        Wait(session, () => session.Engine.Inspect().MarketOffers.Single().Status == MarketOfferStatus.Closed);
        var allocations = session.Engine.Inspect().MarketAllocations;
        if (allocations.Length != 3 || allocations.Single(a => a.Buyer.Value == 2).Quantity != 4 ||
            allocations.Single(a => a.Buyer.Value == 3).Quantity != 8 || allocations.Single(a => a.Buyer.Value == 4).Quantity != 3 ||
            allocations.Sum(a => checked(a.Quantity * a.UnitPriceMinor)) != 8900)
            throw new InvalidOperationException("Market allocation differs from S2");
    }
}

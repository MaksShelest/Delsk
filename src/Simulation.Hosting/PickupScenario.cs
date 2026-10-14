using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void PickupLifecycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId seller = new(1), buyer = new(2);
        var shop = session.Engine.Observe(seller).OwnBuildings.Single();
        var home = session.Engine.Observe(buyer).OwnBuildings.Single();
        Move(session, seller, definition.ResourceNodes.Single().Position);
        Execute(session, seller, new(ActionKind.Harvest, ItemId: "wood", Quantity: 10));
        Wait(session, () => !session.Engine.Observe(seller).Participant.Busy);
        Move(session, seller, shop.Position);
        Execute(session, seller, new(ActionKind.Transfer, ItemId: "wood", Quantity: 10,
            Source: session.Engine.Observe(seller).Participant.InventoryId, Destination: shop.InputId));
        Execute(session, seller, new(ActionKind.CreateSellOffer, Building: shop.Id,
            ItemId: "wood", Quantity: 10, UnitPriceMinor: 500));
        long offer = session.Engine.Observe(seller).MarketOffers.Single().Id;
        Execute(session, buyer, new(ActionKind.SubmitBuyRequest, MarketOfferId: offer, Quantity: 10, MaximumPriceMinor: 500));
        Wait(session, () => session.Engine.Inspect().MarketAllocations.Length == 1);
        long purchase = session.Engine.Observe(buyer).OwnMarketAllocations.Single().Id;
        for (int trip = 0; trip < 2; ++trip)
        {
            Move(session, buyer, shop.Position);
            Execute(session, buyer, new(ActionKind.CollectPurchase, PurchaseId: purchase, Quantity: 3));
            Move(session, buyer, home.Position);
            Execute(session, buyer, new(ActionKind.Transfer, ItemId: "wood", Quantity: 3,
                Source: session.Engine.Observe(buyer).Participant.InventoryId, Destination: home.InputId));
        }
        Wait(session, () => session.Engine.Observe(buyer).OwnMarketAllocations.Single().Status == "Expired");
        session.Step();
        var a = session.Engine.Observe(buyer).OwnMarketAllocations.Single();
        if (a.CollectedQuantity != 6 || a.ReleasedQuantity != 4 || a.RemainingQuantity != 0 ||
            a.PaidMinor != 3000 || a.FineMinor != 100 || a.RefundedMinor != 1900 ||
            session.Engine.Observe(seller).Account.BalanceMinor != 13100 ||
            session.Engine.Observe(buyer).Account.BalanceMinor != 6900)
            throw new InvalidOperationException("Partial pickup settlement differs from S3");
    }
}

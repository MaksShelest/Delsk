using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void DeliveryLifecycle(GameSession session, ScenarioDefinition definition, bool capital = false)
    {
        ParticipantId supplier = new(1), buyer = new(2); long buyOffer = 0, capitalOffer = 0;
        Cell destination;
        if (capital)
        {
            var order = session.Engine.Observe(supplier).CapitalOffers.Single();
            capitalOffer = order.Id; destination = order.Position;
        }
        else
        {
            var shop = session.Engine.Observe(buyer).OwnBuildings.Single(); destination = shop.Position;
            Execute(session, buyer, new(ActionKind.CreateBuyOffer, Destination: shop.InputId,
                ItemId: "wood", Quantity: 10, UnitPriceMinor: 500));
            buyOffer = session.Engine.Observe(buyer).BuyOffers.Single().Id;
        }
        Execute(session, supplier, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: buyOffer,
            OfferId: capitalOffer, Quantity: 10));
        Wait(session, () => session.Engine.Inspect().DeliveryContracts.Length == 1);
        long contract = session.Engine.Observe(supplier).OwnDeliveryContracts.Single().Id;
        for (int trip = 0; trip < 2; ++trip)
        {
            Move(session, supplier, definition.ResourceNodes.Single().Position);
            Execute(session, supplier, new(ActionKind.Harvest, ItemId: "wood", Quantity: 3));
            Wait(session, () => !session.Engine.Observe(supplier).Participant.Busy);
            Move(session, supplier, destination);
            Execute(session, supplier, new(ActionKind.DeliverContract, ContractId: contract, Quantity: 3));
        }
        Wait(session, () => session.Engine.Observe(supplier).OwnDeliveryContracts.Single().Status == "Expired");
        session.Step(); var d = session.Engine.Observe(supplier).OwnDeliveryContracts.Single();
        if (d.DeliveredQuantity != 6 || d.UnfulfilledQuantity != 4 || d.PaidMinor != 3000 ||
            d.RefundedMinor != 2000 || d.FineMinor != 100 || d.BondReturnedMinor != 150 ||
            session.Engine.Observe(supplier).Account.BalanceMinor != 12900 ||
            (!capital && session.Engine.Observe(buyer).Account.BalanceMinor != 7100))
            throw new InvalidOperationException("Partial delivery settlement differs from S3");
    }
}

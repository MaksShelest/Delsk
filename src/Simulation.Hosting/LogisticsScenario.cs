using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void LogisticsCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId supplier = new(1), buyer = new(2);
        var factory = session.Engine.Observe(supplier).OwnEnterprises.Single();
        var shop = session.Engine.Observe(buyer).OwnBuildings.Single();
        Move(session, supplier, definition.ResourceNodes.Single().Position);
        Execute(session, supplier, new(ActionKind.Harvest, ItemId: "wood", Quantity: 20));
        Wait(session, () => !session.Engine.Observe(supplier).Participant.Busy);
        var position = session.Engine.Observe(supplier).OwnBuildings.Single().Position;
        Move(session, supplier, position);
        Execute(session, supplier, new(ActionKind.BuyLogistics, Enterprise: factory.Id));
        Execute(session, buyer, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "plank", Quantity: 10, UnitPriceMinor: 3000));
        long offer = session.Engine.Observe(buyer).BuyOffers.Single().Id;
        Execute(session, supplier, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: 10));
        Wait(session, () => session.Engine.Inspect().DeliveryContracts.Length == 1);
        Execute(session, supplier, new(ActionKind.Transfer, ItemId: "wood", Quantity: 20,
            Source: session.Engine.Observe(supplier).Participant.InventoryId, Destination: factory.InputId));
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnEnterprises.Single().CompletedBatches == 10);
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: false));
        var system = session.Engine.Observe(supplier).OwnLogistics.Single();
        long contract = session.Engine.Observe(supplier).OwnDeliveryContracts.Single().Id;
        Execute(session, supplier, new(ActionKind.SetLogisticsContract, Logistics: system.Id, ContractId: contract, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnLogistics.Single().CompletedTrips == 1);
        session.Step(); var d = session.Engine.Observe(supplier).OwnDeliveryContracts.Single();
        if (d.Status != "Fulfilled" || d.DeliveredQuantity != 10 || d.PaidMinor != 30000 ||
            session.Engine.Observe(supplier).Account.BalanceMinor != 108200 || session.Engine.Observe(buyer).Account.BalanceMinor != 70000 ||
            session.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor) != 300)
            throw new InvalidOperationException("Logistics economic cycle differs from S6");
    }
}

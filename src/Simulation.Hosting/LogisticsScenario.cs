using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void LogisticsCycle(GameSession session, ScenarioDefinition definition)
        => RunLogisticsCycle(session, definition, false);
    public static void LogisticsUpgradeCycle(GameSession session, ScenarioDefinition definition)
        => RunLogisticsCycle(session, definition, true);
    private static void RunLogisticsCycle(GameSession session, ScenarioDefinition definition, bool upgrade)
    {
        long wood = upgrade ? 60 : 20, product = upgrade ? 30 : 10, delivery = upgrade ? 20 : 10;
        ParticipantId supplier = new(1), buyer = new(2);
        var factory = session.Engine.Observe(supplier).OwnEnterprises.Single();
        var shop = session.Engine.Observe(buyer).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Move(session, supplier, definition.ResourceNodes.Single().Position);
        Execute(session, supplier, new(ActionKind.Harvest, ItemId: "wood", Quantity: wood));
        Wait(session, () => !session.Engine.Observe(supplier).Participant.Busy);
        var position = session.Engine.Observe(supplier).OwnBuildings.Single(b=>b.Kind==BuildingKind.Production).Position;
        Move(session, supplier, position);
        Execute(session, supplier, new(ActionKind.BuyLogistics, Enterprise: factory.Id));
        Execute(session, buyer, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "plank", Quantity: delivery, UnitPriceMinor: 3000));
        long offer = session.Engine.Observe(buyer).BuyOffers.Single().Id;
        Execute(session, supplier, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: delivery));
        Wait(session, () => session.Engine.Inspect().DeliveryContracts.Length == 1);
        Execute(session, supplier, new(ActionKind.Transfer, ItemId: "wood", Quantity: wood,
            Source: session.Engine.Observe(supplier).Participant.InventoryId, Destination: factory.InputId));
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnEnterprises.Single().CompletedBatches == product);
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: false));
        var system = session.Engine.Observe(supplier).OwnLogistics.Single();
        if (upgrade) Execute(session, supplier, new(ActionKind.UpgradeLogistics, Logistics: system.Id));
        long contract = session.Engine.Observe(supplier).OwnDeliveryContracts.Single().Id;
        Execute(session, supplier, new(ActionKind.SetLogisticsContract, Logistics: system.Id, ContractId: contract, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnLogistics.Single().CompletedTrips == 1);
        session.Step(); var d = session.Engine.Observe(supplier).OwnDeliveryContracts.Single();
        if (d.Status != "Fulfilled" || d.DeliveredQuantity != delivery || d.PaidMinor != delivery * 3000 ||
            session.Engine.Observe(supplier).Account.BalanceMinor != (upgrade ? 104900 : 108100) || session.Engine.Observe(buyer).Account.BalanceMinor != (upgrade ? 40000 : 70000) ||
            session.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor) != (upgrade ? 600 : 400))
            throw new InvalidOperationException($"Logistics economic cycle differs from S6; cash={session.Engine.Observe(new(1)).Account.BalanceMinor}; logisticsWages={session.Engine.Ledger().Where(l=>l.Reason=="LogisticsWages").Sum(l=>l.AmountMinor)}");
    }
}

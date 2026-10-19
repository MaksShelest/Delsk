using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void LogisticsQueueCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId owner = new(1), buyer = new(2);
        var factory = session.Engine.Observe(owner).OwnEnterprises.Single();
        var storage = session.Engine.Observe(owner).OwnBuildings.Single(b => b.Kind == BuildingKind.Warehouse);
        var receiver = session.Engine.Observe(buyer).OwnBuildings.Single(b=>b.Id.Value==3);
        Move(session, owner, definition.ResourceNodes.Single().Position);
        Execute(session, owner, new(ActionKind.Harvest, ItemId: "wood", Quantity: 60));
        Wait(session, () => !session.Engine.Observe(owner).Participant.Busy);
        Move(session, owner, definition.Buildings!.Single(b => b.Id == factory.Id.Value).Position);
        Execute(session, owner, new(ActionKind.BuyLogistics, Enterprise: factory.Id));
        Execute(session, owner, new(ActionKind.Transfer, ItemId: "wood", Quantity: 60,
            Source: session.Engine.Observe(owner).Participant.InventoryId, Destination: factory.InputId));
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnEnterprises.Single().CompletedBatches == 30);
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: false));
        foreach (long quantity in new long[] { 13, 7 })
        {
            Execute(session, buyer, new(ActionKind.CreateBuyOffer, Destination: receiver.InputId, ItemId: "plank", Quantity: quantity, UnitPriceMinor: 3000));
            long offer = session.Engine.Observe(buyer).BuyOffers.Max(o => o.Id);
            Execute(session, owner, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: quantity));
            Wait(session, () => session.Engine.Inspect().DeliveryContracts.Any(d => d.BuyOfferId == offer));
        }
        var system = session.Engine.Observe(owner).OwnLogistics.Single();
        Execute(session, owner, new(ActionKind.SetLogisticsStorage, Logistics: system.Id, Destination: storage.InputId, Enabled: false));
        Execute(session, owner, new(ActionKind.QueueLogisticsJob, Logistics: system.Id, Destination: storage.InputId, Enabled: true));
        foreach (var d in session.Engine.Observe(owner).OwnDeliveryContracts.OrderByDescending(d => d.DeadlineTick))
            Execute(session, owner, new(ActionKind.QueueLogisticsJob, Logistics: system.Id, ContractId: d.Id, Enabled: true));
        Execute(session, owner, new(ActionKind.SetLogisticsEnabled, Logistics: system.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips == 4);
        session.AdvanceTo(session.Engine.Tick + 5);
        var w = session.Engine.Inspect();
        if (w.DeliveryContracts.Any(d => d.Status != "Fulfilled" || d.FineMinor != 0) ||
            w.DeliveryContracts.Sum(d => d.DeliveredQuantity) != 20 ||
            w.Logistics.Single().Trip is not null ||
            session.Engine.Observe(owner).Account.BalanceMinor != 133300 ||
            session.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor) != 2200 ||
            w.Inventories.Single(i => i.Id == storage.InputId).Stock.Single().Quantity != 10 ||
            w.Accounts.Sum(a => a.ReservedMinor) != 0 || w.Inventories.Sum(i => i.ReservedCapacity) != 0)
            throw new InvalidOperationException($"Logistics queue cycle did not preserve priority, quota or balances; cash={session.Engine.Observe(new(1)).Account.BalanceMinor}; logisticsWages={session.Engine.Ledger().Where(l=>l.Reason=="LogisticsWages").Sum(l=>l.AmountMinor)}");
    }
}

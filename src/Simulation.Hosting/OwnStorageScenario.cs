using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void OwnStorageCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId owner = new(1);
        var factory = session.Engine.Observe(owner).OwnEnterprises.Single();
        var storage = session.Engine.Observe(owner).OwnBuildings.Single(b => b.Kind == BuildingKind.Warehouse);
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
        var system = session.Engine.Observe(owner).OwnLogistics.Single();
        Execute(session, owner, new(ActionKind.SetLogisticsStorage, Logistics: system.Id, Destination: storage.InputId, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips == 2);
        session.AdvanceTo(checked(session.Engine.Tick + 5));
        var state = session.Engine.Inspect(); var final = state.Logistics.Single();
        long Count(InventoryId id) => state.Inventories.Single(i => i.Id == id).Stock.SingleOrDefault(s => s.ItemId == "plank")?.Quantity ?? 0;
        if (Count(storage.InputId) != 15 || Count(factory.OutputId) != 15 || final.Trip is not null ||
            final.ReasonCode != "NoDestinationSpace" || session.Engine.Observe(owner).Account.BalanceMinor != 74700 ||
            session.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor) != 800 ||
            state.Accounts.Sum(a => a.BalanceMinor) != 200000 || state.DeliveryContracts.Length != 0 ||
            state.Accounts.Sum(a => a.ReservedMinor) != 0 || state.Inventories.Sum(i => i.ReservedCapacity) != 0)
            throw new InvalidOperationException($"Own storage cycle differs from expected physical/budget balances; cash={session.Engine.Observe(new(1)).Account.BalanceMinor}; logisticsWages={session.Engine.Ledger().Where(l=>l.Reason=="LogisticsWages").Sum(l=>l.AmountMinor)}");
    }
}

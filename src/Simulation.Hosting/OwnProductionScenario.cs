using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void OwnProductionCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId owner = new(1);
        var source = session.Engine.Observe(owner).OwnEnterprises.Single(e => e.Id.Value == 1);
        var destination = session.Engine.Observe(owner).OwnEnterprises.Single(e => e.Id.Value == 2);
        Move(session, owner, definition.ResourceNodes.Single().Position);
        Execute(session, owner, new(ActionKind.Harvest, ItemId: "wood", Quantity: 60));
        Wait(session, () => !session.Engine.Observe(owner).Participant.Busy);
        Move(session, owner, definition.Buildings!.Single(b => b.Id == 1).Position);
        Execute(session, owner, new(ActionKind.BuyLogistics, Enterprise: source.Id));
        Execute(session, owner, new(ActionKind.Transfer, ItemId: "wood", Quantity: 60,
            Source: session.Engine.Observe(owner).Participant.InventoryId, Destination: source.InputId));
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: source.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnEnterprises.Single(e => e.Id == source.Id).CompletedBatches == 30);
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: source.Id, Enabled: false));
        var system = session.Engine.Observe(owner).OwnLogistics.Single();
        Execute(session, owner, new(ActionKind.SetLogisticsProduction, Logistics: system.Id, Destination: destination.InputId, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips == 2);
        session.AdvanceTo(session.Engine.Tick + 5);
        Execute(session, owner, new(ActionKind.SetLogisticsProduction, Logistics: system.Id, Destination: destination.InputId, Enabled: false));
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: destination.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnEnterprises.Single(e => e.Id == destination.Id).CompletedBatches == 7);
        Execute(session, owner, new(ActionKind.SetProductionEnabled, Enterprise: destination.Id, Enabled: false));
        Execute(session, owner, new(ActionKind.SetLogisticsProduction, Logistics: system.Id, Destination: destination.InputId, Enabled: true));
        Wait(session, () => session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips == 4);
        session.AdvanceTo(session.Engine.Tick + 5);
        var state = session.Engine.Inspect(); var final = state.Logistics.Single();
        long Count(InventoryId id, string item) => state.Inventories.Single(i => i.Id == id).Stock.SingleOrDefault(s => s.ItemId == item)?.Quantity ?? 0;
        if (Count(destination.InputId, "plank") != 15 || Count(source.OutputId, "plank") != 1 || Count(destination.OutputId, "crate") != 7 ||
            final.Trip is not null || final.ReasonCode != "NoDestinationSpace" ||
            session.Engine.Observe(owner).Account.BalanceMinor != 73600 ||
            session.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor) != 1200 ||
            state.Accounts.Sum(a => a.BalanceMinor) != 200000 || state.DeliveryContracts.Length != 0 ||
            state.Accounts.Sum(a => a.ReservedMinor) != 0 || state.Inventories.Sum(i => i.ReservedCapacity) != 0)
            throw new InvalidOperationException("Own production cycle differs from expected physical/budget balances");
    }
}

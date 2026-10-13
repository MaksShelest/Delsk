using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Explicit P07/P08 fixture. Two initial numerical teams; every new building starts with zero workers.</summary>
    public static void Development(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId actor = new(definition.Participants[0].Id);
        var plots = definition.Plots ?? throw new ArgumentException("Development fixture requires land");
        var mill = definition.Buildings!.Single(b => b.TypeId == "sawmill");
        var bakery = definition.Buildings!.Single(b => b.TypeId == "bakery");
        var millView = session.Engine.Observe(actor).OwnEnterprises.Single(e => e.Id.Value == mill.Id);
        var bakeryView = session.Engine.Observe(actor).OwnEnterprises.Single(e => e.Id.Value == bakery.Id);
        foreach (long quantity in new long[] { 20, 20, 10 }) Produce(millView.Id, mill.Position, "wood", quantity, millView.InputId);

        var carrier = session.Engine.Observe(actor).Participant.InventoryId;
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 10, Source: millView.OutputId, Destination: carrier));
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 10, Source: carrier, Destination: millView.InputId));
        Execute(session, actor, new(ActionKind.UpgradeBuilding, Building: new(mill.Id)));

        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 10, Source: millView.OutputId, Destination: carrier));
        var warehousePlot = plots.Single(p => p.Id == 3);
        Move(session, actor, warehousePlot.Position);
        Execute(session, actor, new(ActionKind.ReservePlot, Plot: new(warehousePlot.Id)));
        Execute(session, actor, new(ActionKind.BuyPlot, Plot: new(warehousePlot.Id)));
        Execute(session, actor, new(ActionKind.Build, Target: warehousePlot.Position, BuildingTypeId: "warehouse"));
        var warehouse = session.Engine.Observe(actor).OwnBuildings.Single(b => b.TypeId == "warehouse");
        Move(session, actor, mill.Position);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 5, Source: millView.OutputId, Destination: carrier));
        Move(session, actor, warehouse.Position);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 5, Source: carrier, Destination: warehouse.InputId));

        Harvest("wood", 10);
        var shopPlot = plots.Single(p => p.ShopParticipantId == actor.Value);
        Move(session, actor, shopPlot.Position);
        Execute(session, actor, new(ActionKind.BuyPlot, Plot: new(shopPlot.Id)));
        Execute(session, actor, new(ActionKind.Build, Target: shopPlot.Position, BuildingTypeId: "shop"));
        var shop = session.Engine.Observe(actor).OwnBuildings.Single(b => b.TypeId == "shop");
        Move(session, actor, warehouse.Position);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 5, Source: warehouse.InputId, Destination: carrier));
        Move(session, actor, shop.Position);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 5, Source: carrier, Destination: shop.InputId));
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "plank", Quantity: 5, Source: shop.InputId, Destination: carrier));
        Sell("plank", 5);

        Produce(bakeryView.Id, bakery.Position, "grain", 10, bakeryView.InputId);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "bread", Quantity: 5, Source: bakeryView.OutputId, Destination: carrier));
        Sell("bread", 5);

        Harvest("wood", 10);
        var expansion = plots.Single(p => p.Id == 5);
        Move(session, actor, expansion.Position);
        Execute(session, actor, new(ActionKind.BuyPlot, Plot: new(expansion.Id)));
        Execute(session, actor, new(ActionKind.Build, Target: expansion.Position, BuildingTypeId: "bakery"));
        var empty = session.Engine.Observe(actor).OwnBuildings.Single(b => b.TypeId == "bakery" && b.Id.Value != bakery.Id);
        Execute(session, actor, new(ActionKind.SetProductionEnabled, Enterprise: new(empty.Id.Value), Enabled: true));
        if (session.Engine.Observe(actor).OwnEnterprises.Single(e => e.Id.Value == empty.Id.Value).ReasonCode != "NoStaff")
            throw new InvalidOperationException("Construction created workers unexpectedly");
        session.Engine.ValidateInvariants();

        void Harvest(string item, long quantity)
        {
            Move(session, actor, definition.ResourceNodes.Single(n => n.ItemId == item).Position);
            Execute(session, actor, new(ActionKind.Harvest, ItemId: item, Quantity: quantity));
            Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        }
        void Produce(EnterpriseId id, Cell location, string item, long quantity, InventoryId input)
        {
            Harvest(item, quantity); Move(session, actor, location);
            var inventory = session.Engine.Observe(actor).Participant.InventoryId;
            Execute(session, actor, new(ActionKind.Transfer, ItemId: item, Quantity: quantity, Source: inventory, Destination: input));
            long expected = checked(session.Engine.Observe(actor).OwnEnterprises.Single(e => e.Id == id).CompletedBatches + quantity / 2);
            Execute(session, actor, new(ActionKind.SetProductionEnabled, Enterprise: id, Enabled: true));
            Wait(session, () => session.Engine.Observe(actor).OwnEnterprises.Single(e => e.Id == id).CompletedBatches == expected);
            Execute(session, actor, new(ActionKind.SetProductionEnabled, Enterprise: id, Enabled: false));
        }
        void Sell(string item, long quantity)
        {
            var offer = definition.CapitalOffers.Single(o => o.ItemId == item);
            Move(session, actor, offer.Position);
            Execute(session, actor, new(ActionKind.SellAvailableToCapital, ItemId: item, Quantity: quantity, OfferId: offer.Id));
            Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        }
    }
}

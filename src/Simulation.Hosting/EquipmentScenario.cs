using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void EquipmentCycle(GameSession session, ScenarioDefinition definition)
    {
        var actor = new ParticipantId(1);
        var wood = definition.ResourceNodes.Single(n => n.ItemId == "wood");
        Move(session, actor, wood.Position);
        Execute(session, actor, new(ActionKind.BuyEquipment, Target: wood.Position, EquipmentTypeId: "wood-extractor"));
        var machine = session.Engine.Observe(actor).OwnEquipment.Single();
        Execute(session, actor, new(ActionKind.Harvest, Equipment: machine.Id, ItemId: "wood", Quantity: 4));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        Execute(session, actor, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Off));
        Execute(session, actor, new(ActionKind.UpgradeEquipment, Equipment: machine.Id));
        Execute(session, actor, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Personal));
        Execute(session, actor, new(ActionKind.Harvest, Equipment: machine.Id, ItemId: "wood", Quantity: 6));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        Execute(session, actor, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Automatic));
        Wait(session, () => session.Engine.Observe(actor).OwnEquipment.Single().CompletedCycles == 1);
        Execute(session, actor, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Off));
        Execute(session, actor, new(ActionKind.Transfer, ItemId: "wood", Quantity: 6, Source: machine.StorageId,
            Destination: session.Engine.Observe(actor).Participant.InventoryId));
        var sale = definition.CapitalOffers.Single(o => o.ItemId == "wood");
        Move(session, actor, sale.Position);
        Execute(session, actor, new(ActionKind.SellAvailableToCapital, OfferId: sale.Id, ItemId: "wood", Quantity: 16));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        actor = new(2);
        var grain = definition.ResourceNodes.Single(n => n.ItemId == "grain");
        Move(session, actor, grain.Position);
        Execute(session, actor, new(ActionKind.BuyEquipment, Target: grain.Position, EquipmentTypeId: "grain-extractor"));
        machine = session.Engine.Observe(actor).OwnEquipment.Single();
        Execute(session, actor, new(ActionKind.Harvest, Equipment: machine.Id, ItemId: "grain", Quantity: 2));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        sale = definition.CapitalOffers.Single(o => o.ItemId == "grain");
        Move(session, actor, sale.Position);
        Execute(session, actor, new(ActionKind.SellAvailableToCapital, OfferId: sale.Id, ItemId: "grain", Quantity: 2));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        Wait(session, () => session.Engine.Inspect().EmploymentGroups.All(g => g.Headcount == 0));
        var replay = GameSession.Replay(definition, session.CommandTape, session.Engine.Tick);
        if (session.Engine.ComputeStateHash() != replay.Engine.ComputeStateHash())
            throw new InvalidOperationException("Equipment replay mismatch");
    }
}

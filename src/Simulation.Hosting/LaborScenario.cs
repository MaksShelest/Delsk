using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Recorded manual supply plus real labor steps, not a controller or a checkpoint loader.</summary>
    public static void LaborDynamics(GameSession session, ScenarioDefinition definition)
    {
        foreach (var p in definition.Participants.OrderBy(p => p.Id))
        {
            var actor = new ParticipantId(p.Id);
            var enterprise = definition.Enterprises.Single(e => e.OwnerId == p.Id);
            var recipe = definition.Recipes.Single(r => r.Id == enterprise.RecipeId);
            Move(session, actor, definition.ResourceNodes.Single(n => n.ItemId == recipe.Ingredients.Single().ItemId).Position);
            Execute(session, actor, new(ActionKind.Harvest, ItemId: recipe.Ingredients.Single().ItemId, Quantity: 2));
            Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
            Move(session, actor, enterprise.Position);
            var o = session.Engine.Observe(actor);
            Execute(session, actor, new(ActionKind.Transfer, ItemId: recipe.Ingredients.Single().ItemId, Quantity: 2,
                Source: o.Participant.InventoryId, Destination: o.OwnEnterprises.Single().InputId));
        }
        foreach (var e in definition.Enterprises.OrderBy(e => e.Id))
            session.Send(new(e.OwnerId), new(ActionKind.SetProductionEnabled, Enterprise: new(e.Id), Enabled: true));
        session.Step();
        Wait(session, () => session.Engine.Inspect().EmploymentGroups.Sum(g => g.Headcount) == definition.Population);
        Execute(session, new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 40));
        // Complete enough steps to observe delayed attraction and limited transfers.
        long changedAt = session.Engine.Tick;
        Wait(session, () => session.Engine.Tick >= changedAt + 120);
        Execute(session, new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: false));
        Wait(session, () => session.Engine.Inspect().EmploymentGroups.Single(g => g.Id.Value == 1).Headcount == 0);
        Execute(session, new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        Wait(session, () => session.Engine.Inspect().EmploymentGroups.Single(g => g.Id.Value == 1).Headcount >= 1);
        session.Step(); // First work tick after rehiring.
        var replay = GameSession.Replay(definition, session.CommandTape, session.Engine.Tick);
        if (session.Engine.ComputeStateHash() != replay.Engine.ComputeStateHash())
            throw new InvalidOperationException("Labor replay mismatch");
    }
}

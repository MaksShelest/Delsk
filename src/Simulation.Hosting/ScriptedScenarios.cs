using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Explicit test fixture scripts, not autonomous NPC intelligence.</summary>
public static class ScriptedScenarios
{
    public static void EconomicCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId actor = new(definition.Participants[0].Id);
        var enterprise = definition.Enterprises.First(e => e.OwnerId == actor.Value);
        var recipe = definition.Recipes.Single(r => r.Id == enterprise.RecipeId);
        var node = definition.ResourceNodes.First(n => n.ItemId == recipe.InputItem);
        var offer = definition.CapitalOffers.First(o => o.ItemId == recipe.OutputItem);
        const long batches = 5;
        Move(session, actor, node.Position);
        Execute(session, actor, new(ActionKind.Harvest, ItemId: recipe.InputItem, Quantity: checked(batches * recipe.InputQuantity)));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        Move(session, actor, enterprise.Position);
        var observed = session.Engine.Observe(actor);
        var e = observed.OwnEnterprises.Single(e => e.Id.Value == enterprise.Id);
        Execute(session, actor, new(ActionKind.Transfer, ItemId: recipe.InputItem, Quantity: checked(batches * recipe.InputQuantity),
            Source: observed.Participant.InventoryId, Destination: e.InputId));
        Execute(session, actor, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(actor).OwnEnterprises.Single(x => x.Id == e.Id).CompletedBatches == batches);
        Execute(session, actor, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false));
        Execute(session, actor, new(ActionKind.Transfer, ItemId: recipe.OutputItem, Quantity: checked(batches * recipe.OutputQuantity),
            Source: e.OutputId, Destination: observed.Participant.InventoryId));
        Move(session, actor, offer.Position);
        Execute(session, actor, new(ActionKind.SellAvailableToCapital, ItemId: offer.ItemId,
            Quantity: checked(batches * recipe.OutputQuantity), OfferId: offer.Id));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        if (session.Engine.Inspect().Settlements.Sum(s => s.Accepted) != checked(batches * recipe.OutputQuantity))
            throw new InvalidOperationException("Cycle did not sell all output");
    }
    public static void ZeroCashRecovery(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId actor = new(definition.Participants[0].Id);
        var node = definition.ResourceNodes[0];
        var offer = definition.CapitalOffers.First(o => o.ItemId == node.ItemId);
        if (session.Engine.Observe(actor).Account.BalanceMinor != 0)
            throw new InvalidOperationException("Recovery fixture must start with zero cash");
        Move(session, actor, node.Position);
        Execute(session, actor, new(ActionKind.Harvest, ItemId: node.ItemId, Quantity: 2));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        Move(session, actor, offer.Position);
        Execute(session, actor, new(ActionKind.SellAvailableToCapital, ItemId: node.ItemId, Quantity: 2, OfferId: offer.Id));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
        if (session.Engine.Observe(actor).Account.BalanceMinor <= 0)
            throw new InvalidOperationException("Recovery produced no money");
    }
    private static void Move(GameSession session, ParticipantId actor, Cell target)
    {
        if (session.Engine.Observe(actor).Participant.Position == target) return;
        Execute(session, actor, new(ActionKind.MoveTo, Target: target));
        Wait(session, () => !session.Engine.Observe(actor).Participant.Busy);
    }
    private static void Execute(GameSession session, ParticipantId actor, GameAction action)
    {
        string id = session.Send(actor, action); session.Step();
        if (session.Engine.Result(id) is not { Status: ResultStatus.Accepted })
            throw new InvalidOperationException($"Script command failed: {session.Engine.Result(id)?.ReasonCode}");
    }
    private static void Wait(GameSession session, Func<bool> condition)
    {
        long start = session.Engine.Tick;
        while (!condition())
        {
            if (session.Paused) throw new InvalidOperationException("Script cannot wait in paused session");
            if (session.Engine.Tick - start >= 5000) throw new InvalidOperationException("Script wait limit reached");
            session.Step();
        }
    }
}

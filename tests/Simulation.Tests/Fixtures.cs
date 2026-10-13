using Simulation.Core;
using Simulation.Hosting;

namespace Simulation.Tests;

internal static class Fixtures
{
    public static ScenarioDefinition Cycle(long cash = 100000, int staff = 2, long outputCapacity = 40) => new(
        "test-cycle", 42, 24, 4, [], [new("wood", 1, 500), new("plank", 1, 3000)],
        [new("sawmill", "wood", 2, "plank", 1, 2, 2, 1, 100)],
        [new(1, cash, new(0, 0), 20), new(2, 0, new(0, 0), 20)],
        [new(1, 1, new(10, 0), "sawmill", 40, outputCapacity, staff, 2, 100)],
        [new(1, "wood", 20, 500, new(20, 0)), new(2, "plank", 5, 3000, new(20, 0))],
        [new("wood", new(0, 0))], Population: 2);
    public static GameSession Session(ScenarioDefinition? definition = null) => new(new SimulationEngine(definition ?? Cycle()));
    public static string Command(GameSession session, GameAction action, long actor = 1)
    { var id = session.Send(new(actor), action); session.Step(); return id; }
    public static void UntilIdle(GameSession session, long actor = 1)
    {
        int steps = 0;
        while (session.Engine.Observe(new(actor)).Participant.Busy)
        { if (++steps > 500) throw new InvalidOperationException("Fixture stuck"); session.Step(); }
    }
    public static void Supply(GameSession session, long quantity = 2)
    {
        Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: quantity)); UntilIdle(session);
        Command(session, new(ActionKind.MoveTo, Target: new(10, 0))); UntilIdle(session);
        var o = session.Engine.Observe(new(1));
        Command(session, new(ActionKind.Transfer, ItemId: "wood", Quantity: quantity,
            Source: o.Participant.InventoryId, Destination: o.OwnEnterprises[0].InputId));
    }
    public static long Count(GameSession s, InventoryId id, string item) => s.Engine.Inspect().Inventories.Single(i => i.Id == id)
        .Stock.SingleOrDefault(x => x.ItemId == item)?.Quantity ?? 0;
}

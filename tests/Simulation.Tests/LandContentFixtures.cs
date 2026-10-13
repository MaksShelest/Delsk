using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

namespace Simulation.Tests;

internal static class LandContentFixtures
{
    public static ScenarioDefinition Scenario()
    {
        using var stream = typeof(LandContentFixtures).Assembly.GetManifestResourceStream("Fixture.land-content.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    public static ScenarioDefinition OwnPlot(ScenarioDefinition s, long id, long owner = 1) => s with
    { Plots = s.Plots!.Select(p => p.Id == id ? p with { OwnerId = owner } : p).ToArray() };
    public static ScenarioDefinition Type(ScenarioDefinition s, string id, Func<BuildingTypeDefinition, BuildingTypeDefinition> change)
        => s with { BuildingTypes = s.BuildingTypes!.Select(t => t.Id == id ? change(t) : t).ToArray() };
    public static GameSession Session(ScenarioDefinition? s = null) => new(new SimulationEngine(s ?? Scenario()));
    public static void Move(GameSession session, Cell cell)
    { Fixtures.Command(session, new(ActionKind.MoveTo, Target: cell)); Fixtures.UntilIdle(session); }
    public static void Harvest(GameSession session, string item, long count)
    {
        Fixtures.Command(session, new(ActionKind.Harvest, ItemId: item, Quantity: count)); Fixtures.UntilIdle(session);
    }
    public static void OnePlank(GameSession session)
    {
        Harvest(session, "wood", 2); Move(session, new(4, 0));
        var o = session.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(session, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        while (session.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).CompletedBatches == 0) session.Step();
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false));
    }
    public static ScenarioDefinition CheapUpgrade() => Type(Scenario(), "sawmill", t => t with
    { Levels = t.Levels.Select(l => l.Level == 2 ? l with { Materials = [new("plank", 1)], PriceMinor = 200 } : l).ToArray() });
    public static string Reason(GameSession s, GameAction a, long actor = 1) => s.Engine.Result(Fixtures.Command(s, a, actor))!.ReasonCode;
}

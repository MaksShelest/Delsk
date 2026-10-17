using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PlayNoticeTests
{
    [Fact]
    public void HarvestNoticeUsesTheCompletedQuantityAndKnownReasonsStayReadable()
    {
        var done = new PublicSimulationEvent(1, 2, 3, "HarvestCompleted", new(1), 9, "wood", 1, 0, "");
        Assert.Equal("Получено: древесина ×1", PlayerText.OwnHarvest([done], 1, "ru"));
        Assert.Null(PlayerText.OwnHarvest([done], 2, "ru"));
        Assert.Equal("Недостаточно денег.", PlayerText.Reason("ru", "NoMoney"));
        Assert.Equal("CustomCode", PlayerText.Reason("ru", "CustomCode"));
        Assert.Equal(PlayerText.Keys("ru").Order(), PlayerText.Keys("en").Order());
    }

    [Fact]
    public void OneHarvestStepCreditsPersonalStockAndCapitalMarkersShareACell()
    {
        using var stream = typeof(PlayNoticeTests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;
        var logistics = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        var session = new GameSession(new SimulationEngine(logistics));
        long before = session.Engine.Observe(new(1)).OwnInventories.Single(i => i.Id == session.Engine.Observe(new(1)).Participant.InventoryId)
            .Stock.Where(s => s.ItemId == "wood").Sum(s => s.Quantity);
        Assert.Equal("Accepted", LandContentFixtures.Reason(session, PlayerDesk.Harvest("wood", 1)));
        session.Step();
        Assert.True(session.Engine.Observe(new(1)).Participant.Busy);
        Assert.Equal(before, PersonalWood(session));
        session.Step();
        Assert.False(session.Engine.Observe(new(1)).Participant.Busy);
        Assert.Equal(before + 1, PersonalWood(session));
        Assert.Equal("Получено: древесина ×1", PlayerText.OwnHarvest(session.Engine.PublicEventsSince(0), 1, "ru"));

        var cycle = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(ScenarioPath("economic-cycle.json")), SimulationJson.Options)!;
        var frame = Presentation.Read(cycle, new SimulationEngine(cycle), 1);
        var terminal = Assert.Single(frame.Markers, m => m.Kind == "capital");
        Assert.Contains("wood", terminal.Label, StringComparison.Ordinal);
        Assert.Contains("plank", terminal.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("compensation", JsonSerializer.Serialize(frame, SimulationJson.Options), StringComparison.OrdinalIgnoreCase);
    }

    private static long PersonalWood(GameSession session)
    {
        var view = session.Engine.Observe(new(1));
        return view.OwnInventories.Single(i => i.Id == view.Participant.InventoryId).Stock.Where(s => s.ItemId == "wood").Sum(s => s.Quantity);
    }

    private static string ScenarioPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "scenarios", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }
}

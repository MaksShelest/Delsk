using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PlayableEconomyTests
{
    private static readonly ParticipantId Human = new(1);

    [Fact]
    public void CashZeroPlayerCanSellThroughTheSameWindowAndSaveInFlight()
    {
        var scenario = Fixtures.Cycle(cash: 0);
        var session = Fixtures.Session(scenario);
        Fixtures.Command(session, PlayerDesk.Harvest("wood", 4));
        Fixtures.UntilIdle(session);
        Fixtures.Command(session, PlayerDesk.Move(20, 0));
        Fixtures.UntilIdle(session);
        var choices = PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en");
        var sale = Assert.Single(choices, c => c.Key == "sell:1:max");
        Assert.True(sale.Enabled);
        Assert.Equal(4, sale.Command!.Quantity);
        string id = session.Send(Human, sale.Command);
        session.Step();
        Assert.Equal(ResultStatus.Accepted, session.Engine.Result(id)!.Status);
        Assert.Equal(0, PlayerDesk.Read(session.Engine, Human).CashMinor);
        Assert.True(PlayerDesk.Read(session.Engine, Human).Participant.Busy);
        string path = Path.Combine(Path.GetTempPath(), "delsk-sale-" + Guid.NewGuid() + ".json");
        try
        {
            CheckpointStore.Save(session, path);
            var loaded = CheckpointStore.Load(path).Session;
            Fixtures.UntilIdle(session);
            Fixtures.UntilIdle(loaded);
            Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
            Assert.Equal(2000, PlayerDesk.Read(session.Engine, Human).CashMinor);
            Assert.Equal(0, Fixtures.Count(session, PlayerDesk.Read(session.Engine, Human).Participant.InventoryId, "wood"));
            session.Send(Human, sale.Command);
            session.Step();
            Assert.Equal(2000, PlayerDesk.Read(session.Engine, Human).CashMinor);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void OwnedPlotExplainsMissingMaterialsAndBuildsWithRealStock()
    {
        var basis = Economy();
        var scenario = basis with { Participants = basis.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 30000 } : p).ToArray() };
        var session = new GameSession(new SimulationEngine(scenario));
        Walk(session, new(4, 0));
        Fixtures.Command(session, PlayerDesk.BuyPlot(1));
        var missing = Assert.Single(PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en"), c => c.Key == "build:sawmill");
        Assert.False(missing.Enabled);
        Assert.Contains("150.00", missing.Label);
        Assert.Contains("wood: 0/10", missing.Details);
        Walk(session, new(0, 0));
        Fixtures.Command(session, PlayerDesk.Harvest("wood", 10));
        Fixtures.UntilIdle(session);
        Walk(session, new(4, 0));
        var enough = Assert.Single(PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en"), c => c.Key == "build:sawmill");
        Assert.True(enough.Enabled, enough.Details);
        Assert.Equal("Accepted", LandContentFixtures.Reason(session, enough.Command!));
        var view = PlayerDesk.Read(session.Engine, Human);
        Assert.Single(view.Buildings);
        Assert.Equal(0, Assert.Single(view.Employment).Headcount);
        Assert.Contains(PlayContext.At(view, scenario, "en"), c => c.Key.StartsWith("card:", StringComparison.Ordinal));
        Assert.Equal(0, Fixtures.Count(session, view.Participant.InventoryId, "wood"));
    }

    [Fact]
    public void ShopSitesAreAssignedAndDoNotGrantFreeLand()
    {
        var scenario = Economy();
        var session = new GameSession(new SimulationEngine(scenario));
        var own = Assert.Single(scenario.Plots!, p => p.ShopParticipantId == 1);
        Walk(session, own.Position);
        Assert.Contains(PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en"), c => c.Key == "buy:" + own.Id);
        Fixtures.Command(session, PlayerDesk.BuyPlot(own.Id));
        var choices = PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en");
        Assert.Contains(choices, c => c.Key == "build:shop");
        Assert.DoesNotContain(choices, c => c.Key == "build:sawmill");
        Walk(session, new(12, 0));
        Assert.Empty(PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en"));
        string hint = PlayContext.Hover(Presentation.Read(scenario, session.Engine, 1), PlayerDesk.Read(session.Engine, Human), 12, 0, "en");
        Assert.Contains("participant 2", hint);
        Assert.Contains("another participant", hint);
    }

    private static ScenarioDefinition Economy()
    {
        using var stream = typeof(PlayableEconomyTests).Assembly.GetManifestResourceStream("Fixture.baseline-economy.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static void Walk(GameSession session, Cell target)
    {
        Fixtures.Command(session, PlayerDesk.Move(target.X, target.Z));
        Fixtures.UntilIdle(session);
        Assert.Equal(target, session.Engine.Observe(Human).Participant.Position);
    }
}

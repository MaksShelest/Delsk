using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class EconomicBalanceI6C8DTests
{
    [Theory]
    [InlineData(15, "0,15", "0.15")]
    [InlineData(250, "2,50", "2.50")]
    [InlineData(1700, "17,00", "17.00")]
    public void MoneyKeepsTwoMinorDigits(long minor, string russian, string english)
    {
        Assert.Equal(russian, PlayerText.Money(minor, "ru"));
        Assert.Equal(english, PlayerText.Money(minor, "en"));
    }

    [Theory]
    [InlineData(PlayMaps.CompactValley)]
    [InlineData(PlayMaps.Second)]
    public void OrdinaryProfileFundsStarterOrdersWithoutChangingTheCatalog(string key)
    {
        var oldProfile = VNextProvincialSupportProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile());
        var candidate = VNextI6C8DBalanceProfile.Create(oldProfile);
        var profile = EconomyProfile.Read(JsonSerializer.Serialize(candidate, SimulationJson.Options));
        var map = JsonSerializer.Deserialize<ScenarioDefinition>(
            OrdinaryNewGameTests.Text(PlayMaps.NewGameResource(key).Replace("Play.", "")), SimulationJson.Options)!;
        var options = new OrdinaryGameOptions(3, Seed: 17, StartingIndustry: "dairy");
        var balance = MapDesignTests.Balance();
        var oldWorld = OrdinaryNewGame.Resolve(map, oldProfile, options, vnextBalance: balance).Scenario;
        var newWorld = OrdinaryNewGame.Resolve(map, profile, options, vnextBalance: balance).Scenario;
        Assert.Equal(VNextI6C8DBalanceProfile.ProfileId, newWorld.EconomyProfileId);
        Assert.NotEqual(oldWorld.NewGame!.EconomyProfileSha256, newWorld.NewGame!.EconomyProfileSha256);
        Assert.Equal(JsonSerializer.Serialize(oldWorld.Recipes), JsonSerializer.Serialize(newWorld.Recipes));
        Assert.Equal(oldWorld.Items, newWorld.Items);
        Assert.Equal(oldWorld.CapitalDemand, newWorld.CapitalDemand! with { Allocation = null });
        Assert.Equal(15, newWorld.Economy!.Fleet!.WageMinor);

        var oldSession = new GameSession(new(oldWorld));
        var newSession = new GameSession(new(newWorld));
        var old = oldSession.Engine.Inspect().CapitalDemand!.Periods.Single();
        var current = newSession.Engine.Inspect().CapitalDemand!.Periods.Single();
        Assert.Equal(0, old.Goods.Single(g => g.ItemId == "cheese").DemandQuantity);
        Assert.Equal(1, current.Goods.Single(g => g.ItemId == "cheese").DemandQuantity);
        foreach (var starter in StartingSpecializations.Directions)
            Assert.True(current.Goods.Single(g => g.ItemId == starter.OutputItem).DemandQuantity >= 1);
        Assert.Equal(old.Goods.Select(g => g.UnitPriceMinor), current.Goods.Select(g => g.UnitPriceMinor));
        Assert.Equal(20_000, current.TargetBaseMinor);
        Assert.InRange(current.OrderedBaseMinor, 19_801, 20_000);
        Assert.Equal(JsonSerializer.Serialize(oldSession.Engine.ProvincialProgramOf(new(1))),
            JsonSerializer.Serialize(newSession.Engine.ProvincialProgramOf(new(1))));

        var saved = CheckpointStore.Encode(oldSession);
        var restored = CheckpointStore.Decode(saved);
        Assert.Equal(VNextProvincialSupportProfile.ProfileId, restored.Engine.Configuration().EconomyProfileId);
        Assert.Null(restored.Engine.Configuration().CapitalDemand!.Allocation);
        oldSession.AdvanceTo(65);
        restored.AdvanceTo(65);
        Assert.Equal(oldSession.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
    }

    [Fact]
    public void ProductionDemandStopsAtTheBudgetCapAndKeepsTheSameOrdersAcrossLoadAndReplay()
    {
        var settings = new CapitalDemandSettings(new(20, 0), PeriodTicks: 50,
            BootstrapBaseBudgetMinor: 4000, DemandNumerator: 100, Allocation: new(1, 50, 2));
        var scenario = Fixtures.Cycle() with { CapitalOffers = [], CapitalDemand = settings };
        var session = Fixtures.Session(scenario);
        Fixtures.Supply(session, 4);
        Assert.Equal("Accepted", LandContentFixtures.Reason(session,
            new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true)));
        session.AdvanceTo(50);
        var period = session.Engine.Inspect().CapitalDemand!.Periods[1];
        Assert.Equal(6000, period.Goods.Single(g => g.ItemId == "plank").Produced * 3000);
        Assert.Equal(8000, period.TargetBaseMinor);
        Assert.InRange(period.OrderedBaseMinor, 7501, 8000);
        Assert.InRange(period.Goods.Single(g => g.ItemId == "plank").DemandQuantity, 1, 2);
        Assert.Equal(period.MonetaryBudgetMinor,
            period.Goods.Sum(g => g.DemandQuantity * g.UnitPriceMinor));

        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        loaded.AdvanceTo(125);
        session.AdvanceTo(125);
        var replay = GameSession.Replay(scenario, session.CommandTape, 125);
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Throws<ArgumentException>(() => new SimulationEngine(scenario with
        {
            CapitalDemand = settings with { BootstrapBaseBudgetMinor = 3499 }
        }));
    }
}

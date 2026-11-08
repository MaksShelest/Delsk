using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class WageScheduleI6C72Tests
{
    [Fact]
    public void NewOrdinaryProfileUsesFifteenMinorAndATwoMinorStepWithoutRepricing()
    {
        var previous = EquipmentClassesI6C22Tests.Profile();
        var profile = VNextWageScheduleProfile.Create(previous);
        Assert.Equal(VNextWageScheduleProfile.ProfileId, profile.Id);
        Assert.Equal("wage-schedule-projection-1", profile.VNext!.ProjectionVersion);
        Assert.Equal(2, profile.World.Rules.PlayerWageStepMinor);
        Assert.Equal(15, profile.World.Logistics.WageMinor);
        Assert.Equal(15, profile.World.Rules.Fleet!.WageMinor);
        Assert.Equal(15, profile.World.Rules.Roads!.WageMinor);
        Assert.Equal(10_000, profile.World.PlotPriceMinor);
        Assert.Equal(10_000, profile.World.ShopSitePriceMinor);
        Assert.All(profile.World.BuildingTypes.SelectMany(t => t.Levels), level => Assert.True(level.WageMinor is 0 or 15));
        Assert.Contains(profile.World.BuildingTypes.SelectMany(t => t.Levels), level => level.WageMinor == 15);
        Assert.All(profile.World.EquipmentTypes.SelectMany(t => t.Levels), level => Assert.Equal(15, level.WageMinor));
        Assert.Equal(15, profile.Npc.GetProperty("baseWageMinor").GetInt64());
        Assert.Equal(50, profile.Npc.GetProperty("maximumWageMinor").GetInt64());
        Assert.Equal(1, profile.Npc.GetProperty("wageRaiseMinor").GetInt64());
        Assert.Equal(JsonSerializer.Serialize(previous.World.Items, SimulationJson.Options), JsonSerializer.Serialize(profile.World.Items, SimulationJson.Options));
        Assert.Equal(JsonSerializer.Serialize(previous.World.Rules.Fleet!.Models, SimulationJson.Options), JsonSerializer.Serialize(profile.World.Rules.Fleet.Models, SimulationJson.Options));
        Assert.Equal(JsonSerializer.Serialize(previous.World.Rules.Services, SimulationJson.Options), JsonSerializer.Serialize(profile.World.Rules.Services, SimulationJson.Options));
        Assert.Equal(JsonSerializer.Serialize(previous.World.Rules.Workshops, SimulationJson.Options), JsonSerializer.Serialize(profile.World.Rules.Workshops, SimulationJson.Options));
        Assert.Equal(20, previous.World.Rules.Services!.DriverWageMinor);
        var read = EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile)));
        Assert.Equal(profile.Id, read.Id);
        Assert.Equal(15, read.World.Rules.Fleet!.WageMinor);
        Assert.Equal(2, read.World.Rules.PlayerWageStepMinor);
    }

    [Fact]
    public void EarlierProfilesAndTheValleySnapshotKeepTwentyMinorAndStepFive()
    {
        var classes = EquipmentClassesI6C22Tests.Profile();
        Assert.Equal(5, classes.World.Rules.PlayerWageStepMinor);
        Assert.Equal(20, classes.World.Logistics.WageMinor);
        Assert.Equal(20, classes.World.Rules.Fleet!.WageMinor);
        Assert.Equal(20, classes.World.Rules.Roads!.WageMinor);
        Assert.Equal(20, classes.Npc.GetProperty("baseWageMinor").GetInt64());
        Assert.All(classes.World.BuildingTypes.SelectMany(t => t.Levels).Where(l => l.WageMinor != 0), level => Assert.Equal(20, level.WageMinor));
        var mobile = MobileEquipmentI4Tests.Profile();
        Assert.Equal(5, mobile.World.Rules.PlayerWageStepMinor);
        Assert.Equal(20, mobile.World.Logistics.WageMinor);
        var snapshot = JsonNode.Parse(OrdinaryNewGameTests.Text("compact-valley-i6c21.json"))!;
        Assert.Equal(5, snapshot["economy"]!["playerWageStepMinor"]!.GetValue<int>());
        Assert.Equal(VNextMobileEquipmentProfile.ProfileId, snapshot["economyProfileId"]!.GetValue<string>());
        Assert.Equal(5, EconomyProfileTests.Profile().World.Rules.PlayerWageStepMinor);
    }

    [Theory]
    [InlineData(PlayMaps.CompactValley)]
    [InlineData(PlayMaps.Second)]
    public void CurrentOrdinaryMapsResolveTheNewScheduleAndKeepHundredCoinPlots(string key)
    {
        var scenario = I6C2MapsTests.Resolve(key).Scenario;
        Assert.Equal(VNextWageScheduleProfile.ProfileId, scenario.EconomyProfileId);
        Assert.Equal(2, scenario.Economy!.PlayerWageStepMinor);
        Assert.Equal(15, scenario.Logistics!.WageMinor);
        Assert.Equal(15, scenario.Economy.Fleet!.WageMinor);
        Assert.Equal(15, scenario.Economy.Roads!.WageMinor);
        Assert.Equal(20, scenario.Economy.Services!.DriverWageMinor);
        Assert.All(scenario.Plots!.Where(p => !p.IsCorridor), plot => Assert.Equal(10_000, plot.PriceMinor));
        Assert.All(scenario.BuildingTypes!.SelectMany(t => t.Levels), level => Assert.True(level.WageMinor is 0 or 15));
    }

    [Fact]
    public void OneWorkerAtFifteenThenSeventeenChargesExactMinorAndReplays()
    {
        var definition = Fixtures.Cycle() with
        {
            Id = "wage-schedule-i6c72", Population = 2, MaxTicks = 1000, HarvestTicksPerUnit = 1,
            Participants = [new(1, 10_000_000, new(0, 0), 1000), new(2, 10_000_000, new(0, 0), 1000)],
            Recipes = [new("sawmill", "wood", 2, "plank", 1, 1000, 2, 1, 100)],
            Enterprises = [new(1, 1, new(0, 0), "sawmill", 1000, 40, 0, 2, 15), new(2, 2, new(0, 0), "sawmill", 1000, 40, 0, 2, 10)],
            Labor = new(10, 20, 1, 10, GrowthPerPeriod: 0)
        };
        var session = new GameSession(new(definition));
        Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
        Fixtures.UntilIdle(session);
        var observed = session.Engine.Observe(new(1));
        Fixtures.Command(session, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: observed.Participant.InventoryId, Destination: observed.OwnEnterprises.Single(e => e.Id.Value == 1).InputId));
        session.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        session.Step();
        while (session.Engine.Tick < 50) session.Step();
        Assert.Equal(1, Group(session).Headcount);
        Assert.Equal(0, Wages(session));
        session.Step();
        Assert.Equal(15, Wages(session));
        Assert.Equal(15, Group(session).WageMinor);
        long before = Wages(session);
        Fixtures.Command(session, new(ActionKind.SetWage, Group: new(1), WageMinor: 17));
        Assert.Equal(15, Group(session).WageMinor);
        Assert.Equal(15, Wages(session) - before);
        session.Step();
        Assert.Equal(17, Group(session).WageMinor);
        Assert.Equal(17, Wages(session) - before - 15);
        var replay = GameSession.Replay(definition, session.CommandTape, session.Engine.Tick);
        Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.02, 2)]
    [InlineData(0.15, 15)]
    [InlineData(0.29, 29)]
    [InlineData(1.005, 101)]
    public void CoinSpinValuesRoundToExactMinor(double coins, long minor) =>
        Assert.Equal(minor, PlayerDesk.MinorFromCoins(coins));

    [Fact]
    public void EveryHundredthFromASpinBoxReturnsTheSameMinor()
    {
        for (long minor = 0; minor <= 200; minor++)
            Assert.Equal(minor, PlayerDesk.MinorFromCoins(minor / 100d));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDesk.MinorFromCoins(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDesk.MinorFromCoins(-0.01));
    }

    private static EmploymentView Group(GameSession session) => session.Engine.Inspect().EmploymentGroups.Single(g => g.Id.Value == 1);
    private static long Wages(GameSession session) => session.Engine.Inspect().Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor;
}

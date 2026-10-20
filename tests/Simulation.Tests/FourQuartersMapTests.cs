using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class FourQuartersMapTests
{
    private static ScenarioDefinition Map()
    {
        using var stream = typeof(FourQuartersMapTests).Assembly.GetManifestResourceStream("Fixture.four-corners-v1.json")!;
        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        using var economyStream = typeof(FourQuartersMapTests).Assembly.GetManifestResourceStream("Fixture.economy.json")!;
        return EconomyProfile.Read(new StreamReader(economyStream).ReadToEnd()).Apply(scenario);
    }
    private static Cell Rotate(Cell c) => new(24 - c.Z, c.X);
    private static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    [Fact]
    public void OrdinaryMapHasEqualFourStartsAndSpareUnassignedShops()
    {
        var map = Map(); ScenarioValidation.Validate(map);
        Assert.Equal(PlayMaps.Balanced, PlayMaps.KeyForScenario(map.Id));
        Assert.Equal(4, map.Participants.Length);
        Assert.Equal(8, map.Plots!.Count(p => p.IsShopSite));
        Assert.Equal(48, map.Plots!.Count(p => !p.IsShopSite && !p.IsCorridor));
        Assert.Equal(40, map.ResourceNodes.Length);
        Assert.Empty(map.BlockedCells);
        Assert.All(map.Plots!, p => { Assert.Null(p.OwnerId); Assert.Null(p.ShopParticipantId); });
        Assert.All(map.Participants, p =>
        {
            Assert.Equal(map.Participants[0].CashMinor, p.CashMinor);
            Assert.Equal(map.Participants[0].InventoryCapacity, p.InventoryCapacity);
            Assert.Equal(8, Distance(p.Position, map.CapitalDemand!.Terminal));
            Assert.Equal(3, map.Plots!.Where(t => t.IsShopSite).Min(t => Distance(p.Position, t.Position)));
            Assert.Equal(3, map.Plots!.Where(t => !t.IsShopSite && !t.IsCorridor).Min(t => Distance(p.Position, t.Position)));
        });
    }

    [Fact]
    public void QuarterTurnPreservesResourceIdentityRightsAndEveryPlotKind()
    {
        var map = Map();
        foreach (var node in map.ResourceNodes)
            Assert.Contains(map.ResourceNodes, n => n.Position == Rotate(node.Position) && n.ItemId == node.ItemId && n.Rules == node.Rules);
        foreach (var plot in map.Plots!)
            Assert.Contains(map.Plots!, p => p.Position == Rotate(plot.Position) && p.IsCorridor == plot.IsCorridor && p.IsShopSite == plot.IsShopSite && p.PriceMinor == plot.PriceMinor);
        foreach (var actor in map.Participants)
            Assert.Contains(map.Participants, p => p.Position == Rotate(actor.Position));
    }

    [Theory]
    [InlineData("wood", 5, 12)]
    [InlineData("grain", 5, 12)]
    [InlineData("milk", 8, 14)]
    [InlineData("beet", 8, 12)]
    public void PublicRecoveryIsFurtherButAllPlayersHaveTheSameResourceDistances(string item, int ownDistance, int publicDistance)
    {
        var map = Map();
        foreach (var actor in map.Participants)
        {
            Assert.Equal(ownDistance, map.ResourceNodes.Where(n => n.ItemId == item && !n.Rules!.Public).Min(n => Distance(actor.Position, n.Position)));
            Assert.Equal(publicDistance, map.ResourceNodes.Where(n => n.ItemId == item && n.Rules!.Public).Min(n => Distance(actor.Position, n.Position)));
        }
    }

    [Fact]
    public void CoreCanReachEveryInteractionAndRestoreTheMap()
    {
        var map = Map();
        foreach (var target in map.ResourceNodes.Select(n => n.Position)
            .Concat(map.Plots!.Where(p => !p.IsCorridor).Select(p => p.Position))
            .Append(map.CapitalDemand!.Terminal).Distinct())
        {
            var session = Fixtures.Session(map);
            string command = Fixtures.Command(session, new(ActionKind.MoveTo, Target: target));
            Assert.Equal("Accepted", session.Engine.Result(command)!.ReasonCode);
            Fixtures.UntilIdle(session);
            Assert.Equal(target, session.Engine.Observe(new(1)).Participant.Position);
        }
        var original = Fixtures.Session(map);
        string hash = original.Engine.ComputeStateHash();
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(original));
        Assert.Equal(hash, loaded.Engine.ComputeStateHash());
        Assert.Equal(map.Id, loaded.Engine.Configuration().Id);
    }
}

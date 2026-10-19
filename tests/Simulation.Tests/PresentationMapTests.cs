using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PresentationMapTests
{
    private static ScenarioDefinition Read(string name)
    {
        using var stream = typeof(PresentationMapTests).Assembly.GetManifestResourceStream("Fixture." + name + ".json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static ScenarioDefinition Second() => Read("presentation-map-two");

    [Fact]
    public void ExpandedMapKeepsEveryResourceWithinOriginalTravelBudget()
    {
        var first = Read("baseline-economy"); var second = Second();
        Assert.True(second.MapWidth * second.MapHeight > first.MapWidth * first.MapHeight);
        Assert.Equal(6, second.ResourceNodes.Count(n => n.ItemId == "wood"));
        Assert.Equal(6, second.ResourceNodes.Count(n => n.ItemId == "grain"));
        Assert.Equal(12, second.ResourceNodes.Select(n => n.Position).Distinct().Count());
        Assert.True(second.Plots!.Count(p => !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null) > first.Plots!.Count(p => !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null));
        Assert.True(second.Plots!.Count(p => p.IsShopSite) > second.Participants.Length);
        int budget = first.ResourceNodes.Max(n => (new GridMap(first).FindPath(n.Position, first.CapitalDemand!.Terminal).Length - 1) * first.MoveTicksPerCell);
        foreach (var node in second.ResourceNodes)
            Assert.InRange((new GridMap(second).FindPath(node.Position, second.CapitalDemand!.Terminal).Length - 1) * second.MoveTicksPerCell, 1, budget);
    }

    [Fact]
    public void EveryResourceCellAcceptsRealWalkingAndHarvesting()
    {
        var map = Second();
        foreach (var node in map.ResourceNodes)
        {
            var session = Fixtures.Session(map);
            LandContentFixtures.Move(session, node.Position);
            Assert.Equal(node.Position, session.Engine.Observe(new(1)).Participant.Position);
            LandContentFixtures.Harvest(session, node.ItemId, 1);
            Assert.Equal(1, Fixtures.Count(session, session.Engine.Observe(new(1)).Participant.InventoryId, node.ItemId));
        }
    }

    [Fact]
    public void CentralRoadIsConnectedAndCannotBeBoughtOrReserved()
    {
        var map = Second();
        var roads = map.Plots!.Where(p => p.IsCorridor).Select(p => p.Position).ToHashSet();
        for (int x = 0; x < map.MapWidth - 1; x++) Assert.Contains(new Cell(x, 5), roads);
        var reached = new HashSet<Cell>(); var queue = new Queue<Cell>(); queue.Enqueue(roads.First());
        while (queue.TryDequeue(out var at))
        {
            if (!reached.Add(at)) continue;
            foreach (var next in roads.Where(c => Math.Abs(c.X-at.X) + Math.Abs(c.Z-at.Z) == 1)) if (!reached.Contains(next)) queue.Enqueue(next);
        }
        Assert.Equal(roads.Count, reached.Count);
        foreach (var plot in map.Plots!.Where(p => p.IsCorridor))
        {
            var session = Fixtures.Session(map); LandContentFixtures.Move(session, plot.Position);
            long cash = PlayerDesk.Read(session.Engine,new(1)).CashMinor;
            Assert.Equal("ProtectedCorridor", LandContentFixtures.Reason(session, new(ActionKind.BuyPlot,Plot:new(plot.Id))));
            Assert.Equal("ProtectedCorridor", LandContentFixtures.Reason(session, new(ActionKind.ReservePlot,Plot:new(plot.Id))));
            Assert.Equal(cash, PlayerDesk.Read(session.Engine,new(1)).CashMinor);
        }
    }

    [Fact]
    public void NewMapAndNpcMemoryRestoreWithIdenticalContinuation()
    {
        var map = Second(); var session = Fixtures.Session(map);
        using var stream = typeof(PresentationMapTests).Assembly.GetManifestResourceStream("Fixture.baseline-controllers.json")!;
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(stream, SimulationJson.Options)!;
        session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(n with { HumanActorId = config.HumanActorId }, AlgorithmicController.RuntimeSeed(map.Seed,n.ActorId),map.Participants.Select(p=>p.Id))));
        session.AdvanceTo(50);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session)); loaded.RestoreControllers(AlgorithmicController.Restore);
        Assert.Equal(PlayMaps.SecondScenarioId,loaded.Engine.Configuration().Id);
        Assert.Equal(11,loaded.Engine.Configuration().MapHeight);
        for (int n=0;n<60;n++)
        { session.Step();loaded.Step();Assert.Equal(session.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(session.ComputeControllerStateHash(),loaded.ComputeControllerStateHash()); }
        Assert.Equal(session.CommandTape,loaded.CommandTape);
    }

    [Fact]
    public void ForeignExtractionProjectionContainsOnlyPublicIdentityAndPosition()
    {
        var map=Second();var session=Fixtures.Session(map);var at=map.ResourceNodes.First(n=>n.ItemId=="wood").Position;
        Fixtures.Command(session,new(ActionKind.MoveTo,Target:at),2);Fixtures.UntilIdle(session,2);
        Assert.Equal("Accepted",LandContentFixtures.Reason(session,new(ActionKind.BuyEquipment,Target:at,EquipmentTypeId:"wood-extractor"),2));
        var equipment=Assert.Single(session.Engine.Inspect().Equipment);
        var marker=Assert.Single(Presentation.Read(map,session.Engine,1).Markers,m=>m.Kind=="equipment");
        Assert.Equal(equipment.OwnerId.Value,marker.OwnerId);Assert.Equal(equipment.Position.X,marker.X);Assert.Equal(equipment.Position.Z,marker.Z);Assert.Equal("wood",marker.Label);
        string json=JsonSerializer.Serialize(marker,SimulationJson.Options);
        Assert.DoesNotContain("WorkDone",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("StorageId",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("balance",json,StringComparison.OrdinalIgnoreCase);
    }
}

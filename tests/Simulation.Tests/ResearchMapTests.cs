using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ResearchMapTests
{
    private static T Read<T>(string name)
    {
        using var stream=typeof(ResearchMapTests).Assembly.GetManifestResourceStream("Fixture."+name+".json")!;
        return JsonSerializer.Deserialize<T>(stream,SimulationJson.Options)!;
    }
    private static ScenarioDefinition Map(string name)=>Read<EconomyProfile>("economy").Apply(Read<ScenarioDefinition>(name));

    [Theory]
    [InlineData("four-corners-v1")]
    [InlineData("city-corridor-v1")]
    public void NewMapsHaveEqualAccessDistinctLandAndConnectedProtectedRoads(string name)
    {
        var map=Map(name);ScenarioValidation.Validate(map);
        Assert.Contains(PlayMaps.KeyForScenario(map.Id),PlayMaps.All);
        Assert.Equal(4,map.Participants.Length);
        Assert.Equal(48,map.Plots!.Count(p=>!p.IsCorridor&&!p.IsShopSite));
        Assert.Equal(8,map.Plots!.Count(p=>p.IsShopSite));
        Assert.Equal(map.Plots!.Length,map.Plots.Select(p=>p.Position).Distinct().Count());
        Assert.Empty(map.ResourceNodes.Select(n=>n.Position).Intersect(map.Plots.Select(p=>p.Position)));
        var grid=new GridMap(map);
        int Distance(Cell a,Cell b)=>grid.FindPath(a,b).Length-1;
        Assert.Single(map.Participants.Select(p=>Distance(p.Position,map.CapitalDemand!.Terminal)).Distinct());
        foreach(var item in new[]{"wood","grain","milk","beet"})
        {
            var nodes=map.ResourceNodes.Where(n=>n.ItemId==item&&!n.Rules!.Public).ToArray();
            Assert.Single(map.Participants.Select(p=>nodes.Min(n=>Distance(p.Position,n.Position))).Distinct());
        }
        var roads=map.Plots.Where(p=>p.IsCorridor).Select(p=>p.Position).ToHashSet();
        var reached=new HashSet<Cell>();var queue=new Queue<Cell>();queue.Enqueue(roads.First());
        while(queue.TryDequeue(out var cell))
        {
            if(!reached.Add(cell))continue;
            foreach(var next in roads.Where(c=>Math.Abs(c.X-cell.X)+Math.Abs(c.Z-cell.Z)==1&&!reached.Contains(c)))queue.Enqueue(next);
        }
        Assert.Equal(roads.Count,reached.Count);
    }

    [Theory]
    [InlineData("four-corners-v1")]
    [InlineData("city-corridor-v1")]
    public void ResourceAreasAllowRealWalkingAndHarvesting(string name)
    {
        var map=Map(name);
        foreach(var node in map.ResourceNodes.GroupBy(n=>n.ItemId).Select(g=>g.First(n=>n.Rules!.Public)))
        {
            var session=Fixtures.Session(map);
            LandContentFixtures.Move(session,node.Position);
            LandContentFixtures.Harvest(session,node.ItemId,1);
            Assert.Equal(1,Fixtures.Count(session,session.Engine.Observe(new(1)).Participant.InventoryId,node.ItemId));
        }
    }

    [Theory]
    [InlineData("four-corners-v1")]
    [InlineData("city-corridor-v1")]
    public void CheckpointKeepsMapAndIdenticalNpcContinuation(string name)
    {
        var map=Map(name);var session=Fixtures.Session(map);
        var configuration=Read<AutonomousConfiguration>("baseline-controllers");
        session.AttachControllers(configuration.Npcs.Select(n=>new AlgorithmicController(n with {HumanActorId=1},AlgorithmicController.RuntimeSeed(map.Seed,n.ActorId),map.Participants.Select(p=>p.Id)))
            .Cast<IController>().Concat([new SelfBallotController(new(1))]));
        session.AdvanceTo(35);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(session));
        loaded.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
        for(int i=0;i<40;i++)
        {
            session.Step();loaded.Step();
            Assert.Equal(session.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
            Assert.Equal(session.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());
        }
        Assert.Equal(name,loaded.Engine.Configuration().Id);
    }
}

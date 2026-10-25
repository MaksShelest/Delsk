using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MapDesignTests
{
    internal static EconomyProfile Economy()=>EconomyProfile.Read(OrdinaryNewGameTests.Text("vnext-supported.json"));
    internal static VNextBalance Balance()=>VNextBalanceReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text("vnext-balance.json"))).Balance!;
    internal static ScenarioDefinition Template(string id)=>MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text(id+".json")),Economy());
    internal static OrdinaryGameResolution Resolve(string id,int count=3,ulong seed=17)=>OrdinaryNewGame.Resolve(Template(id),Economy(),new(count,Seed:seed),vnextBalance:Balance());
    [Theory][InlineData("small_river",64,48,48,8,720,1872,327)][InlineData("large_districts",128,96,96,24,2880,7488,1163)]
    public void ExactGeometryAndAllElevenRuntimePaths(string id,int width,int height,int lots,int districts,int axes,int corridor,int dirt)
    {
        var s=Resolve(id,10).Scenario;var l=s.Layout!;Assert.Equal((width,height),(s.MapWidth,s.MapHeight));Assert.Equal(lots,l.IndustrialLots.Length);Assert.Equal(districts,l.ResourceDistricts.Length);
        Assert.Equal(axes,l.RoadCenterCells.Length);Assert.Equal(corridor,l.RoadCorridorCells.Length);Assert.Equal(dirt,l.InitialCityDirtCells.Length);Assert.Equal(15,l.MarketStalls.Length);Assert.Equal(11,l.SpawnCells.Length);
        Assert.Equal(8,s.ResourceNodes.Select(n=>n.ItemId).Distinct().Count());Assert.Equal(districts*25,s.ResourceNodes.Length);Assert.Equal(districts,s.ResourceNodes.Count(n=>n.Rules!.Public));Assert.Equal(lots*6+15,s.Plots!.Length);
        Assert.All(s.Plots,p=>Assert.Null(p.OwnerId));Assert.Equal(l.CivicObjects.SelectMany(c=>c.Body).OrderBy(c=>c.X).ThenBy(c=>c.Z),s.BlockedCells.OrderBy(c=>c.X).ThenBy(c=>c.Z));
        var nav=new ScenarioNavigation(s);var destinations=l.IndustrialLots.Select(c=>c.Entry).Concat(l.MarketStalls.Select(m=>m.Cell)).Concat(s.ResourceNodes.Where(n=>n.Rules!.Public).Select(n=>n.Position)).Concat(l.CivicObjects.Select(c=>c.Entry)).ToArray();
        foreach(var spawn in l.SpawnCells){var distances=nav.DistancesFrom(spawn);Assert.All(destinations,c=>Assert.True(distances.ContainsKey(c),$"{id}: {spawn} -> {c}"));}
        foreach(var count in new[]{3,6,10}){var resolved=Resolve(id,count);Assert.Equal(count+1,resolved.Scenario.Participants.Length);Assert.All(resolved.Scenario.Participants,a=>Assert.Contains(a.Position,l.SpawnCells));OrdinaryNewGameTests.Config().ForOrdinary(resolved.Scenario,1,resolved.Controllers).Validate(resolved.Scenario);}
    }
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void SeedChangesAssignmentNotGeometryAndNeverWorldRandom(string id)
    {
        var assignments=new HashSet<Cell>();var reference=Resolve(id,10).Scenario;string geometry=JsonSerializer.Serialize(reference.Layout,SimulationJson.Options);
        for(ulong seed=1;seed<=9;seed++){var a=Resolve(id,10,seed).Scenario;var b=Resolve(id,10,seed).Scenario;Assert.Equal(JsonSerializer.Serialize(a),JsonSerializer.Serialize(b));Assert.Equal(geometry,JsonSerializer.Serialize(a.Layout,SimulationJson.Options));assignments.Add(a.Participants[0].Position);}
        Assert.True(assignments.Count>1);Assert.Equal(15,reference.Plots!.Count(p=>p.IsShopSite));
    }
    [Theory][InlineData("small_river","small-river")][InlineData("large_districts","large-districts")]
    public void CommittedRuntimeSnapshotMatchesStrictAdapterAndNineSeedDistances(string id,string key)
    {
        var initial=Resolve(id,10).Scenario;var raw=OrdinaryNewGameTests.Map(key);
        Assert.Equal(JsonSerializer.Serialize(initial,SimulationJson.Options),JsonSerializer.Serialize(OrdinaryNewGame.Resolve(raw,Economy(),new(10),vnextBalance:Balance()).Scenario,SimulationJson.Options));
        Assert.Equal(key,PlayMaps.KeyForScenario(id));Assert.Contains(key,PlayMaps.All);
        var paths=initial.Layout!.SpawnCells.ToDictionary(c=>c,c=>new ScenarioNavigation(initial).DistancesFrom(c));
        var targets=initial.ResourceNodes.Where(n=>n.Rules!.Public).Select(n=>n.Position).Concat(initial.Layout.MarketStalls.Select(m=>m.Cell)).Append(initial.CapitalDemand!.Terminal).ToArray();
        var samples=new List<object>();
        for(ulong seed=1;seed<=9;seed++)
        {
            var s=Resolve(id,10,seed).Scenario;
            foreach(var actor in s.Participants)
            {var lengths=targets.Select(t=>paths[actor.Position][t]).Order().ToArray();samples.Add(new{seed,actor=actor.Id,spawn=actor.Position,minimum=lengths[0],median=lengths[lengths.Length/2],maximum=lengths[^1]});}
        }
        if(Environment.GetEnvironmentVariable("DELSK_MAP_EVIDENCE") is {} output)
        {Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,id+".json"),JsonSerializer.Serialize(new{map=id,initial.Layout.SourceSha256,spawns=11,destinations=targets.Length,allReachable=true,scope="Runtime GridMap including civic bodies; public sources, stalls and terminal; distances are not economic equality",samples},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));}
    }
    [Theory][InlineData("schema_version","wrong")][InlineData("id","")][InlineData("width",0)][InlineData("max_participants",3)]
    public void BadDesignHeadersRejected(string field,object value)
    {var d=JsonNode.Parse(OrdinaryNewGameTests.Text("small_river.json"))!;d[field]=JsonSerializer.SerializeToNode(value);Assert.ThrowsAny<ArgumentException>(()=>ReadResolved(d));}
    [Theory][InlineData("unknown")][InlineData("duplicate")][InlineData("null")][InlineData("cell")][InlineData("overlap")][InlineData("id")][InlineData("ref")][InlineData("public")][InlineData("entry")]
    public void BadGeometryRejectedWithoutRepair(string kind)
    {
        string json=OrdinaryNewGameTests.Text("small_river.json");var d=JsonNode.Parse(json)!;
        if(kind=="unknown")d["future_ignored"]=1;
        if(kind=="duplicate"){Assert.ThrowsAny<ArgumentException>(()=>MapDesignReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"width\": 64","\"width\": 64, \"width\": 64")),Economy()));return;}
        if(kind=="null")d["spawn_cells"]=null;
        if(kind=="cell")d["spawn_cells"]![0]=new JsonArray(999,0);
        if(kind=="overlap")d["industrial_lots"]![0]!["cells"]![0]=d["road_center_cells"]![0]!.DeepClone();
        if(kind=="id")d["industrial_lots"]![1]!["id"]=d["industrial_lots"]![0]!["id"]!.DeepClone();
        if(kind=="ref")d["resource_districts"]![0]!["profile"]="unknown";
        if(kind=="public")d["resource_districts"]![0]!["public_cells"]![0]=new JsonArray(0,0);
        if(kind=="entry")d["industrial_lots"]![0]!["entry"]=new JsonArray(0,0);
        var failure=Record.Exception(()=>ReadResolved(d));Assert.True(failure is ArgumentException or JsonException,$"Unexpected input error: {failure}");
        if(kind=="entry")
        {var civic=JsonNode.Parse(json)!;civic["civic_objects"]![0]!["building"]!.AsArray().RemoveAt(0);Assert.Throws<ArgumentException>(()=>ReadResolved(civic));}
    }
    private static ScenarioDefinition ReadResolved(JsonNode d)=>OrdinaryNewGame.Resolve(MapDesignReader.Read(Encoding.UTF8.GetBytes(d.ToJsonString()),Economy()),Economy(),new(),vnextBalance:Balance()).Scenario;
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void ExistingWarehouseWallsStayInsideAnIndustrialLotAndKeepEveryCorridorOpen(string id)
    {
        var s=Resolve(id).Scenario;var lot=s.Layout!.IndustrialLots[0];var anchor=lot.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First();var footprint=new[]{anchor,new Cell(anchor.X+1,anchor.Z)};
        // Topology fixture with a legal existing warehouse, not free ordinary ownership or new D construction.
        s=s with{Plots=s.Plots!.Select(p=>footprint.Contains(p.Position)?p with{OwnerId=1}:p).ToArray(),Buildings=[new(1,1,anchor,"warehouse")]};
        var engine=new SimulationEngine(s);var nav=new ScenarioNavigation(s);Assert.Contains(footprint[1],engine.BlockedCells());
        Assert.All(s.Layout.RoadCorridorCells,c=>Assert.True(nav.Walkable(c)));
        foreach(var spawn in s.Layout.SpawnCells){var distances=nav.DistancesFrom(spawn);Assert.All(s.Layout.IndustrialLots,l=>Assert.True(distances.ContainsKey(l.Entry)));Assert.True(distances.ContainsKey(s.CapitalDemand!.Terminal));}
    }
    [Fact]public void WrongEconomyRejectedBeforeWorld()=>Assert.Throws<ArgumentException>(()=>MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text("small_river.json")),OrdinaryNewGameTests.Economy()));
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void ProtectedCivicCorridorAndResourceCellsCannotBecomeLand(string id)
    {
        var s=Resolve(id).Scenario;
        foreach(var cell in new[]{s.Layout!.RoadCenterCells[0],s.Layout.CivicObjects[0].Entry,s.ResourceNodes[0].Position})
        {var illegal=s with{Plots=[..s.Plots!,new(999,cell,100)]};Assert.Throws<ArgumentException>(()=>ScenarioValidation.Validate(illegal));}
        var changed=s with{BlockedCells=[]};Assert.Throws<ArgumentException>(()=>ScenarioValidation.Validate(changed));
    }
}

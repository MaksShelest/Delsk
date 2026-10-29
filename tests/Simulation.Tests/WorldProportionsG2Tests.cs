using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.MapDesignTests;

namespace Simulation.Tests;

public class WorldProportionsG2Tests
{
    private static ScenarioDefinition ReadResolved(JsonNode d)=>OrdinaryNewGame.Resolve(MapDesignReader.Read(Encoding.UTF8.GetBytes(d.ToJsonString()),Economy()),Economy(),new(),vnextBalance:Balance()).Scenario;
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void ExplicitCivicGeometryHasClearPlazaLoadingAndFourDisplayCells(string id)
    {
        var definition=OrdinaryNewGame.Resolve(Template(id),NpcEconomyG1Tests.Profile(),new(10),vnextBalance:Balance()).Scenario;
        Assert.Equal("map-adapter-2",definition.Layout!.Version);
        var nav=new ScenarioNavigation(definition);var session=new GameSession(new(definition));
        foreach(var site in definition.Layout.CivicObjects)
        {
            Assert.Equal(4,site.Body.Length);Assert.DoesNotContain(site.Entry,site.Body);
            var open=site.Footprint.Except(site.Body).ToArray();
            Assert.All(open,c=>Assert.True(nav.Walkable(c)));
            Assert.All(site.Body,c=>Assert.False(nav.Walkable(c)));
            foreach(var spawn in definition.Layout.SpawnCells)
            {var distances=nav.DistancesFrom(spawn);Assert.All(open,c=>Assert.True(distances.ContainsKey(c),$"{spawn} -> {site.Kind}/{c}"));}
            foreach(var cell in open)
            {var preview=session.Engine.PreviewMovement(new(1),cell);Assert.True(preview.Allowed,preview.Reason);Assert.Equal(cell,preview.Path[^1]);}
        }
        var dealer=definition.Layout.CivicObjects.Single(c=>c.Kind=="dealership");
        Assert.Equal(4,dealer.Parking!.Length);Assert.All(dealer.Parking,c=>Assert.DoesNotContain(c,dealer.Body));
        Assert.Equal(6,session.Engine.InspectFleet()!.Stock.Length);
        Assert.All(session.Engine.InspectFleet()!.Stock.GroupBy(s=>s.Position),g=>Assert.InRange(g.Count(),1,g.Any(v=>v.ModelId=="trakt_d1")?1:2));
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(session));
        foreach(var entry in definition.Layout.CivicObjects.Select(c=>c.Entry))
        {
            StorageC1Tests.Move(session,entry);StorageC1Tests.Move(restored,entry);
            Assert.Equal(session.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        }
    }
    [Theory][InlineData("small-river")][InlineData("large-districts")]
    public void PreIterationResolvedWorldKeepsItsOwnershipCollisionsAndRoutes(string key)
    {
        using var bytes=typeof(MapDesignTests).Assembly.GetManifestResourceStream("Fixture."+key+"-adapter1.json.gz")!;
        using var gzip=new System.IO.Compression.GZipStream(bytes,System.IO.Compression.CompressionMode.Decompress);
        var old=JsonSerializer.Deserialize<ScenarioDefinition>(gzip,SimulationJson.Options)!;
        var definition=OrdinaryNewGame.Resolve(old,NpcEconomyG1Tests.Profile(),new(3),vnextBalance:Balance()).Scenario;
        var engine=new SimulationEngine(definition);var session=new GameSession(engine);
        Assert.Equal("map-adapter-1",definition.Layout!.Version);
        Assert.Equal(15,definition.Layout.CivicObjects.Single(c=>c.Kind=="dealership").Body.Length);
        var origin=engine.Observe(new(1)).Participant.Position;
        var plot=engine.Observe(new(1)).Plots.Where(p=>p.OwnerId is null&&!p.IsShopSite).OrderBy(p=>Math.Abs(p.Position.X-origin.X)+Math.Abs(p.Position.Z-origin.Z)).First();
        StorageC1Tests.Move(session,plot.Position);session.Send(new(1),new(ActionKind.BuyPlot,Plot:plot.Id));session.Step();
        Assert.Equal(new ParticipantId(1),engine.Observe(new(1)).Plots.Single(p=>p.Id==plot.Id).OwnerId);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(JsonSerializer.Serialize(definition,SimulationJson.Options),JsonSerializer.Serialize(copy.Engine.Configuration(),SimulationJson.Options));
        Assert.Equal(engine.BlockedCells().OrderBy(c=>c.X).ThenBy(c=>c.Z),copy.Engine.BlockedCells().OrderBy(c=>c.X).ThenBy(c=>c.Z));
        foreach(var site in definition.Layout.CivicObjects)
        {
            var before=engine.PreviewMovement(new(1),site.Entry);var after=copy.Engine.PreviewMovement(new(1),site.Entry);
            Assert.Equal(before.Path.ToArray(),after.Path.ToArray());
            StorageC1Tests.Move(session,site.Entry);StorageC1Tests.Move(copy,site.Entry);
            Assert.Equal(engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        }
        if(Environment.GetEnvironmentVariable("DELSK_MAP_EVIDENCE") is {} output)
        {Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,key+"-adapter1.save.json"),CheckpointStore.Encode(copy));}
        Assert.Equal(new ParticipantId(1),copy.Engine.Observe(new(1)).Plots.Single(p=>p.Id==plot.Id).OwnerId);
    }
    [Theory][InlineData("missing-body")][InlineData("missing-entry")][InlineData("blocked-entry")][InlineData("blocked-parking")]
    public void ExplicitCivicGeometryRejectsInconsistentSource(string fault)
    {
        var d=JsonNode.Parse(OrdinaryNewGameTests.Text("small_river.json"))!;var dealer=d["civic_objects"]![2]!;
        if(fault=="missing-body")dealer.AsObject().Remove("building");
        if(fault=="missing-entry")dealer.AsObject().Remove("entry");
        if(fault=="blocked-entry")dealer["entry"]=dealer["building"]![0]!.DeepClone();
        if(fault=="blocked-parking")dealer["parking"]![0]=dealer["building"]![0]!.DeepClone();
        Assert.Throws<ArgumentException>(()=>ReadResolved(d));
    }

    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void RealPurchasedVehicleCanLeaveDisplayAndReachLoadingAprons(string id)
    {
        var scenario=OrdinaryNewGame.Resolve(Template(id),NpcEconomyG1Tests.Profile(),new(),vnextBalance:Balance()).Scenario;
        // Funded action fixture; this does not claim an ordinary start can afford a car.
        scenario=scenario with{Participants=scenario.Participants.Select(p=>p.Id==1?p with{CashMinor=10000000}:p).ToArray()};
        var session=new GameSession(new(scenario));var car=FleetE1Tests.Buy(session,"sputnik20");FleetE1Tests.Board(session,car);
        if(Environment.GetEnvironmentVariable("DELSK_MAP_EVIDENCE") is {} output)
        {Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,id+"-purchased.save.json"),CheckpointStore.Encode(session));}
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));
        var dealer=scenario.Layout!.CivicObjects.Single(c=>c.Kind=="dealership");int x=dealer.Footprint.Min(c=>c.X),z=dealer.Footprint.Min(c=>c.Z);
        var targets=new[]{new Cell(x+4,z+3),new Cell(x+4,z),new Cell(x+2,z+2),dealer.Entry,scenario.CapitalDemand!.Terminal};
        foreach(var target in targets)
        {
            var preview=session.Engine.PreviewMovement(new(1),target);Assert.True(preview.Allowed,preview.Reason);
            Assert.All(preview.Path,c=>Assert.DoesNotContain(c,scenario.BlockedCells));
            StorageC1Tests.Move(session,target);StorageC1Tests.Move(copy,target);
            Assert.Equal(session.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
            Assert.Equal(target,session.Engine.Observe(new(1)).Participant.Position);
        }
    }

    [Theory][InlineData("first")][InlineData("second")][InlineData("third")][InlineData("four-quarters")][InlineData("city-corridor")]
    public void EveryPreVNextMapKeepsItsResolvedSaveAndRealTerminalRoute(string key)
    {
        var initial=OrdinaryNewGame.Resolve(OrdinaryNewGameTests.Map(key),OrdinaryNewGameTests.Economy(),new()).Scenario;
        Assert.Null(initial.Layout);var session=new GameSession(new(initial));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));var target=initial.CapitalDemand!.Terminal;
        Assert.Equal(JsonSerializer.Serialize(initial,SimulationJson.Options),JsonSerializer.Serialize(copy.Engine.Configuration(),SimulationJson.Options));
        Assert.Equal(session.Engine.PreviewMovement(new(1),target).Path.ToArray(),copy.Engine.PreviewMovement(new(1),target).Path.ToArray());
        StorageC1Tests.Move(session,target);StorageC1Tests.Move(copy,target);
        Assert.Equal(session.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(session.Engine.BlockedCells(),copy.Engine.BlockedCells());
        Assert.Equal(JsonSerializer.Serialize(session.Engine.Observe(new(1)).Plots,SimulationJson.Options),JsonSerializer.Serialize(copy.Engine.Observe(new(1)).Plots,SimulationJson.Options));
    }
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void DesignOneStillUsesItsOriginalImplicitBodyAndEntry(string id)
    {
        var source=JsonNode.Parse(OrdinaryNewGameTests.Text(id+".json"))!;source["schema_version"]="delsk-map-design-1";
        foreach(var civic in source["civic_objects"]!.AsArray())
        {civic!.AsObject().Remove("entry");if(civic["kind"]!.GetValue<string>()!="town_hall")civic.AsObject().Remove("building");}
        var scenario=MapDesignReader.Read(Encoding.UTF8.GetBytes(source.ToJsonString()),Economy());
        Assert.Equal("map-adapter-1",scenario.Layout!.Version);
        foreach(var civic in scenario.Layout.CivicObjects)
        {
            Assert.Equal(civic.Kind=="dealership"?15:4,civic.Body.Length);
            Assert.Equal(civic.Footprint.Min(c=>c.X),civic.Entry.X);
            Assert.Equal(civic.Footprint.Max(c=>c.Z)-(civic.Kind=="dealership"?1:0),civic.Entry.Z);
        }
    }

}

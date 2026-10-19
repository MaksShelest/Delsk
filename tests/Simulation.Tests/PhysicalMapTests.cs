using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class PhysicalMapTests
{
    private static ScenarioDefinition Read(string name)
    {
        using var stream=typeof(PhysicalMapTests).Assembly.GetManifestResourceStream("Fixture."+name+".json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
    }
    private static ScenarioDefinition Third()=>Read("presentation-map-three");
    [Fact]
    public void ThirdMapHasTwoEighteenCellZonesSeparatedBlocksAndEightSymmetricShops()
    {
        var d=Third();ScenarioValidation.Validate(d);Assert.Equal(PlayMaps.Third,PlayMaps.KeyForScenario(d.Id));
        var industry=d.Plots!.Where(p=>!p.IsCorridor&&!p.IsShopSite).ToArray();Assert.Equal(36,industry.Length);
        foreach(int x in new[]{8,15})foreach(int z in new[]{3,7,11})
            Assert.Equal(6,industry.Count(p=>p.Position.X>=x&&p.Position.X<x+2&&p.Position.Z>=z&&p.Position.Z<z+3));
        Assert.Equal(8,d.Plots!.Count(p=>p.IsShopSite));
        Assert.Equal(d.Plots!.Where(p=>p.IsShopSite).Select(p=>p.Position.X).Order(),d.Plots!.Where(p=>p.IsShopSite).Select(p=>24-p.Position.X).Order());
        foreach(int x in new[]{5,7,17,19})Assert.DoesNotContain(d.Plots!,p=>p.Position.X==x&&p.Position.Z is >=3 and <=13);
        foreach(int x in new[]{6,12,18})for(int z=0;z<d.MapHeight;z++)Assert.Contains(d.Plots!,p=>p.IsCorridor&&p.Position==new Cell(x,z));
    }
    [Fact]
    public void MainResourceRoutesAreSeventeenToTwentyThreeCellsAndPublicRecoveryIsFurther()
    {
        var d=Third();Cell capital=d.CapitalDemand!.Terminal;
        foreach(var node in d.ResourceNodes)
        {
            var at=d with {Participants=d.Participants.Select(p=>p.Id==1?p with {Position=node.Position}:p).ToArray()};
            var s=Fixtures.Session(at);string id=Fixtures.Command(s,new(ActionKind.MoveTo,Target:capital));Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);
            long length=(s.Engine.Observe(new(1)).Participant.ArrivalTick!.Value-s.Engine.Tick)/d.MoveTicksPerCell;
            if(node.Rules!.Public)Assert.InRange(length,29,30);else Assert.InRange(length,17,23);
            Fixtures.UntilIdle(s);Assert.Equal(capital,s.Engine.Observe(new(1)).Participant.Position);
        }
        Assert.Equal(6,d.ResourceNodes.Count(n=>n.ItemId=="wood"&&!n.Rules!.Public));Assert.Equal(6,d.ResourceNodes.Count(n=>n.ItemId=="grain"&&!n.Rules!.Public));
    }
    [Fact]
    public void EveryObjectHasPublicAccessAndSavedMapIsNotRebuiltFromAnUpdatedDefinition()
    {
        var d=Third();var s=Fixtures.Session(d);var capital=d.CapitalDemand!.Terminal;
        foreach(var plot in d.Plots!.Where(p=>!p.IsCorridor))
        {
            var at=d with {Participants=d.Participants.Select(p=>p.Id==1?p with {Position=capital}:p).ToArray()};
            var local=Fixtures.Session(at);string id=Fixtures.Command(local,new(ActionKind.MoveTo,Target:plot.Position));
            Assert.Equal("Accepted",local.Engine.Result(id)!.ReasonCode);Fixtures.UntilIdle(local);
        }
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(JsonSerializer.Serialize(d,SimulationJson.Options),JsonSerializer.Serialize(loaded.Engine.Configuration(),SimulationJson.Options));
        string before=s.Engine.ComputeStateHash();d.ResourceNodes[0]=new("wood",new(0,2));Assert.Equal(before,loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void WallsBlockFreeMovementAndBfsWhileGateDoesNotGrantStockRights()
    {
        var d=Read("physical-automation");d=d with {Participants=d.Participants.Select(p=>p with {Position=new(6,1)}).ToArray()};
        var s=Fixtures.Session(d);s.SetTravelHeading(new(1),1,0);s.CompleteTravelTick();
        Assert.Equal(new Cell(6,1),s.Engine.Observe(new(1)).Participant.Position);s.SetTravelHeading(new(1),0,0);
        string id=Fixtures.Command(s,new(ActionKind.MoveTo,Target:new(7,1)));Assert.Equal("NoPath",s.Engine.Result(id)!.ReasonCode);
        id=Fixtures.Command(s,new(ActionKind.MoveTo,Target:new(8,1)),2);Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);
        Assert.Equal(s.Engine.Tick+4,s.Engine.Observe(new(2)).Participant.ArrivalTick);Fixtures.UntilIdle(s,2);
        Fixtures.Command(s,new(ActionKind.MoveTo,Target:new(6,1)),2);Fixtures.UntilIdle(s,2);
        var warehouse=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        id=Fixtures.Command(s,new(ActionKind.Transfer,Source:warehouse.InputId,Destination:s.Engine.Observe(new(2)).Participant.InventoryId,ItemId:"wood",Quantity:1),2);
        Assert.Equal("NotAvailable",s.Engine.Result(id)!.ReasonCode);Assert.Contains(new Cell(7,1),s.Engine.ObserveController(new(2)).Map.BlockedCells);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.BlockedCells(),loaded.Engine.BlockedCells());
    }
    [Fact]
    public void NewWallCannotIntersectAnActivePathAndDoesNotPartiallyChargeOrBuild()
    {
        var d=Read("physical-automation");d=d with {Buildings=d.Buildings!.Where(b=>b.TypeId!="warehouse").ToArray(),
            Participants=d.Participants.Select(p=>p with {Position=p.Id==1?new(6,1):new(7,0)}).ToArray()};
        var s=Fixtures.Session(d);Fixtures.Command(s,new(ActionKind.MoveTo,Target:new(7,2)),2);
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        string id=Fixtures.Command(s,new(ActionKind.Build,Target:new(6,1),BuildingTypeId:"warehouse"));
        Assert.Equal("RouteWouldBeBlocked",s.Engine.Result(id)!.ReasonCode);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.DoesNotContain(new Cell(7,1),s.Engine.BlockedCells());Fixtures.UntilIdle(s,2);
        id=Fixtures.Command(s,new(ActionKind.Build,Target:new(6,1),BuildingTypeId:"warehouse"));Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);
        Assert.Contains(new Cell(7,1),s.Engine.BlockedCells());Assert.Equal(cash-1,s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void InvalidNaturalStockOrFreightProgressCannotBeRestoredEvenBeforeHashComparison()
    {
        var s=Fixtures.Session(Read("physical-automation"));var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["resources"]![0]!["stock"]=-1;
        string reason=Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode;
        Assert.NotEqual("StateHashMismatch",reason);
    }
}

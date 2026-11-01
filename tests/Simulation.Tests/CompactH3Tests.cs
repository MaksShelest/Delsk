using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class CompactH3Tests
{
    internal static OrdinaryGameResolution Resolve(int bots=3,string difficulty="Medium",ulong seed=42)=>
        OrdinaryNewGame.Resolve(MapDesignTests.Template("compact_valley"),NpcEconomyG1Tests.Profile(),
            new(bots,difficulty,seed),vnextBalance:MapDesignTests.Balance());

    [Theory][InlineData(3,"Easy")][InlineData(3,"Medium")][InlineData(3,"Hard")]
    [InlineData(6,"Easy")][InlineData(6,"Medium")][InlineData(6,"Hard")]
    [InlineData(10,"Easy")][InlineData(10,"Medium")][InlineData(10,"Hard")]
    public void FullOrdinaryRosterHasAllMechanicsAndNoPreparedAssets(int bots,string difficulty)
    {
        var r=Resolve(bots,difficulty);var d=r.Scenario;
        Assert.Equal((32,27),(d.MapWidth,d.MapHeight));Assert.Equal(bots+1,d.Participants.Length);
        Assert.Equal(24,d.Layout!.IndustrialLots.Length);Assert.Equal(11,d.Layout.MarketStalls.Length);
        Assert.Empty(d.Buildings!);Assert.Empty(d.Enterprises);Assert.All(d.Plots!,p=>Assert.Null(p.OwnerId));
        Assert.All(d.Participants,p=>{Assert.Equal(20000,p.CashMinor);Assert.Equal(20,p.InventoryCapacity);});
        Assert.Equal(8,d.ResourceNodes.Select(n=>n.ItemId).Distinct().Count());
        foreach(var item in d.ResourceNodes.GroupBy(n=>n.ItemId))
        {Assert.Contains(item,n=>n.Rules!.Public);Assert.Contains(item,n=>!n.Rules!.Public);}
        Assert.NotNull(d.Economy!.Storage);Assert.NotNull(d.Economy.Workshops);
        Assert.NotNull(d.Economy.Development);Assert.NotNull(d.Economy.ProductionContent);
        Assert.NotNull(d.Economy.Fleet);Assert.NotNull(d.Economy.Roads);
        Assert.NotNull(d.Economy.Billing);Assert.NotNull(d.Economy.Services);
        Assert.True(new SimulationEngine(d).UsesAcceleratedMovement);
        var controllers=OrdinaryNewGameTests.Config().ForOrdinary(d,1,r.Controllers);
        controllers=controllers with{Npcs=controllers.Npcs.Select(n=>AlgorithmicController.WithEconomy(n,NpcEconomyG1Tests.Profile().Npc)).ToArray()};
        controllers.Validate(d);Assert.All(controllers.Npcs,n=>{Assert.True(n.CoherentEconomy);Assert.Equal(difficulty,n.Difficulty.ToString());});
        Assert.Equal(PlayMaps.CompactValley,PlayMaps.KeyForScenario(d.Id));Assert.Contains(PlayMaps.CompactValley,PlayMaps.All);
        Assert.NotEmpty(PlayMaps.Description(PlayMaps.CompactValley,"ru"));Assert.NotEmpty(PlayMaps.Description(PlayMaps.CompactValley,"en"));
    }

    [Fact]public void EveryFullLotCanBeOccupiedWithoutCuttingAnyPublicRoute()
    {
        var d=Resolve(10).Scenario;var layout=d.Layout!;
        // Stronger than eleven factories: reserve every cell of all 24 maximum footprints.
        // Use the runtime grid, with no second pathfinding implementation in the test.
        var grid=new GridMap(d.MapWidth,d.MapHeight,d.BlockedCells.Concat(layout.IndustrialLots.SelectMany(l=>l.Cells)));
        var targets=layout.IndustrialLots.Select(l=>l.Entry).Concat(layout.MarketStalls.Select(s=>s.Cell))
            .Concat(layout.CivicObjects.SelectMany(s=>s.Footprint.Except(s.Body)))
            .Concat(d.ResourceNodes.Where(n=>n.Rules!.Public).Select(n=>n.Position)).ToArray();
        foreach(var spawn in layout.SpawnCells)
        {
            var paths=grid.DistancesFrom(spawn);
            Assert.All(targets,c=>Assert.True(paths.ContainsKey(c),$"Full build: {spawn} -> {c}"));
        }
        Assert.All(layout.RoadCorridorCells,c=>Assert.DoesNotContain(d.Plots!,p=>p.Position==c));
        Assert.All(layout.CivicObjects,s=>Assert.Equal(4,s.Body.Length));
        var dealer=layout.CivicObjects.Single(s=>s.Kind=="dealership");Assert.Equal(25,dealer.Footprint.Length);Assert.Equal(4,dealer.Parking!.Length);
    }

    [Fact]public void SnapshotIsExactlyTheStrictSourceAdapterAndSaveKeepsIt()
    {
        var fromSource=Resolve(10).Scenario;
        var fromSnapshot=OrdinaryNewGame.Resolve(OrdinaryNewGameTests.Map(PlayMaps.CompactValley),NpcEconomyG1Tests.Profile(),new(10,Seed:42),vnextBalance:MapDesignTests.Balance()).Scenario;
        Assert.Equal(JsonSerializer.Serialize(fromSource,SimulationJson.Options),JsonSerializer.Serialize(fromSnapshot,SimulationJson.Options));
        var session=new GameSession(new(fromSource));var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(JsonSerializer.Serialize(fromSource.Layout),JsonSerializer.Serialize(copy.Engine.Configuration().Layout));
        Assert.Equal(session.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
    }
}

public class HorizonH3Tests
{
    [Theory][InlineData("compact_valley")][InlineData("small_river")][InlineData("large_districts")]
    public void OrdinaryGameHasLongHorizonAndKeepsSandboxAndExactContinuation(string map)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),NpcEconomyG1Tests.Profile(),new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        Assert.Equal(1_000_000,d.MaxTicks);Assert.True(d.MaxTicks>126000);
        // Controlled political ballots, no free assets: ordinary commands produce a victory.
        var s=new GameSession(new(d));
        foreach(var p in d.Participants)s.Send(new(p.Id),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(1)));
        s.AdvanceTo(d.Elections!.PeriodTicks);
        if(s.Engine.WaitingForBallots)
            foreach(var p in d.Participants)s.Send(new(p.Id),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(1)));
        Assert.Equal(1,s.Engine.Inspect().Elections.FirstVictory!.Winner.Value);
        s.Send(new(1),new(ActionKind.ContinueAfterVictory));s.Step();
        Assert.Equal(ElectionMode.Sandbox,s.Engine.Inspect().Elections.Mode);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));long end=s.Engine.Tick+3;
        s.AdvanceTo(end);copy.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,end).Engine.ComputeStateHash());
        Assert.Equal(ElectionMode.Sandbox,copy.Engine.Inspect().Elections.Mode);
    }

    [Fact]public void OldResolvedHorizonAndMovementAreNeverRewrittenByLoad()
    {
        var s=H2StorageTests.Frozen();var before=s.Engine.Configuration();
        Assert.Equal(5000,before.MaxTicks);Assert.False(s.Engine.UsesAcceleratedMovement);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(5000,copy.Engine.Configuration().MaxTicks);Assert.False(copy.Engine.UsesAcceleratedMovement);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(5000,NpcEconomyG1Tests.Profile().World.MaxTicks);
    }
}

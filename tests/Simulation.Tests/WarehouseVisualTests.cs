using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class WarehouseVisualTests
{
    [Fact]
    public void EmptyPartialFullAndMixedHaveBoundedDistinctPhysicalPiles()
    {
        Assert.Empty(WarehouseVisuals.Layout(new(true,200,[])));
        var partial=WarehouseVisuals.Layout(new(true,200,[new("wood",20)]));
        var full=WarehouseVisuals.Layout(new(true,200,[new("wood",100),new("grain",100)]));
        Assert.True(partial.Length<full.Length);Assert.Equal(24,full.Length);
        Assert.Equal(new[]{"grain","wood"},full.Select(p=>p.ItemId).Distinct().Order().ToArray());
        Assert.All(full,p=>Assert.InRange(p.Layers,1,4));Assert.Equal(24,full.Select(p=>p.Slot).Distinct().Count());
        Assert.Empty(WarehouseVisuals.Layout(new(false,200,[new("wood",200)])));
        Assert.Equal(full,WarehouseVisuals.Layout(new(true,200,[new("grain",100),new("wood",100)])));
    }
    [Fact]
    public void StockIsOwnOnlyAndReadDoesNotMutateWorldOrLeakReservationsForFutureGoods()
    {
        var d=TradeFixtures.Scenario();var s=Fixtures.Session(d);
        LandContentFixtures.Harvest(s,"wood",4);LandContentFixtures.Move(s,new(2,0));var o=s.Engine.Observe(new(1));var store=o.OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        Fixtures.Command(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:store.InputId,ItemId:"wood",Quantity:4));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();
        string before=s.Engine.ComputeStateHash();var own=Presentation.Read(d,s.Engine,1).Markers.Single(m=>m.Kind=="warehouse" && m.EntityId==store.Id.Value);
        Assert.Equal(4*d.Items.Single(i=>i.Id=="wood").UnitVolume,Assert.Single(own.Warehouse!.Goods).Volume);
        var foreign=Presentation.Read(d,s.Engine,2).Markers.Single(m=>m.Kind=="warehouse" && m.EntityId==store.Id.Value);
        Assert.False(foreign.Warehouse!.Known);Assert.Empty(foreign.Warehouse.Goods);Assert.Equal(0,foreign.Warehouse.Capacity);
        Assert.Equal(before,s.Engine.ComputeStateHash());
    }
    [Fact]
    public void PartialPhysicalReceiptFrameRestoresAndReplaysExactly()
    {
        var d=TradeFixtures.Scenario();var s=Fixtures.Session(d);LandContentFixtures.Harvest(s,"wood",8);LandContentFixtures.Move(s,new(2,0));var o=s.Engine.Observe(new(1));
        Fixtures.Command(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId,ItemId:"wood",Quantity:8));
        Assert.NotEmpty(s.Engine.TransfersOf(new(1)));var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int i=0;i<12;i++){Assert.Equal(JsonSerializer.Serialize(Presentation.Read(d,s.Engine,1)),JsonSerializer.Serialize(Presentation.Read(d,loaded.Engine,1)));s.Step();loaded.Step();}
        var replay=GameSession.Replay(d,s.CommandTape,s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(Presentation.Read(d,s.Engine,1)),JsonSerializer.Serialize(Presentation.Read(d,replay.Engine,1)));
    }
    [Fact]
    public void VisibleLevelFootprintAndWallsComeFromTheActualCatalog()
    {
        var d=LandContentFixtures.Scenario();d=d with {Buildings=d.Buildings!.Select(b=>b.TypeId=="sawmill"?b with {Level=2}:b).ToArray()};
        var s=Fixtures.Session(d);var frame=Presentation.Read(d,s.Engine,1);var sawmill=frame.Markers.Single(m=>m.Kind=="production" && m.Label=="sawmill");
        Assert.Equal(2,sawmill.Level);Assert.Equal(d.BuildingTypes!.Single(t=>t.Id=="sawmill").Footprint,sawmill.Footprint);
        Assert.Equal(d.BuildingTypes!.Single(t=>t.Id=="sawmill").Walls??[],sawmill.Walls);
    }
}

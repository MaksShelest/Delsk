using System.Collections.Immutable;
using System.Text;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I6C24MapEditorTests
{
    private static EconomyProfile Economy()=>VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(MobileEquipmentI4Tests.Profile()));
    private static MapDraft Valley()=>MapDraft.Parse(OrdinaryNewGameTests.Text("compact_valley_i6c21.json"));
    [Fact]
    public void ViewRotationKeepsWorldCoordinates()
    {
        var draft=Valley();var json=draft.ToJson();
        for(int quarter=0;quarter<4;quarter++)
        {
            var size=MapView.DisplaySize(draft.Width,draft.Height,quarter);
            Assert.Equal((quarter&1)==1?(draft.Height,draft.Width):(draft.Width,draft.Height),size);
            for(int x=0;x<draft.Width;x++)for(int z=0;z<draft.Height;z++)
            {
                var screen=MapView.ToScreen(draft.Width,draft.Height,quarter,new(x,z));
                Assert.Equal(new Cell(x,z),MapView.ToWorld(draft.Width,draft.Height,quarter,screen.X,screen.Y));
            }
        }
        Assert.Equal(json,draft.ToJson());
    }
    [Fact]
    public void ContentRotationIsOneUndoAndKeepsTheCurrentEconomy()
    {
        var draft=Valley();var before=draft.ToJson();var roads=draft.Roads.ToArray();var entry=draft.Lots[0].Entry;int width=draft.Width,height=draft.Height;
        draft.RotateContent();
        Assert.Equal(MapDraft.FlexibleSchema,draft.SchemaVersion);
        Assert.Equal((height,width),(draft.Width,draft.Height));
        Assert.Equal(roads.Select(c=>new Cell(height-1-c.Z,c.X)).OrderBy(c=>c.X).ThenBy(c=>c.Z),draft.Roads.OrderBy(c=>c.X).ThenBy(c=>c.Z));
        Assert.Equal(new Cell(height-1-entry.Z,entry.X),draft.Lots[0].Entry);
        Assert.Equal(6,draft.Lots[0].Cells.Count);
        Assert.Empty(draft.Problems());
        var resolved=UserMaps.Start(draft,Economy(),new(3),MapDesignTests.Balance());
        Assert.Equal(VNextWageScheduleProfile.ProfileId,resolved.Scenario.EconomyProfileId);
        Assert.Equal("map-adapter-3",resolved.Scenario.Layout!.Version);
        draft.Undo();Assert.Equal(before,draft.ToJson());
        draft.Redo();Assert.Equal(MapDraft.FlexibleSchema,draft.SchemaVersion);Assert.Equal(height,draft.Width);
    }
    [Fact]
    public void CellAndRectangleLotsStayConnectedAndOldSchemaKeepsTwoByThree()
    {
        var draft=MapDraft.Create(16,12);draft.PaintRoad(new(1,1));draft.PaintRoad(new(2,1));draft.PaintRoad(new(3,1));
        var id=draft.AddLotCell(new(2,2));
        Assert.Equal(MapDraft.FlexibleSchema,draft.SchemaVersion);Assert.NotNull(id);
        draft.AddLotCell(new(3,2),id);draft.AddLotCell(new(3,3),id);
        Assert.Equal(3,draft.Lots.Single().Cells.Count);
        draft.RemoveLotCell(new(3,3));Assert.Equal(2,draft.Lots.Single().Cells.Count);
        draft.SetEntry(id!,new(2,1));
        Assert.DoesNotContain("не имеет подъезда",string.Join('\n',draft.Problems()));
        var rectangle=draft.PlaceLotRectangle(new(5,2),new(6,5));
        Assert.Equal(8,draft.Lots.Single(l=>l.Id==rectangle).Cells.Count);
        draft.Lots.Single(l=>l.Id==id).Cells.Add(new(8,8));
        Assert.Contains("разорван",string.Join('\n',draft.Problems()));
        var valley=Valley();var free=FreeCell(valley);valley.AddLotCell(free.Cell);valley.SchemaVersion="delsk-map-design-2";
        Assert.Contains("2×3",string.Join('\n',valley.Problems()));
        Assert.Throws<ArgumentException>(()=>MapDesignReader.Read(Encoding.UTF8.GetBytes(valley.ToJson()),Economy()));
    }
    [Fact]
    public void UserMapKeepsPreparedCashSeparateFromRealHarvestImportAndConstruction()
    {
        // Деньги подготовлены, чтобы не ждать естественный доход. Древесина, сталь, покупка участка и стройка — обычные команды.
        var draft=Valley();var free=FreeCell(draft);var lotId=draft.AddLotCell(free.Cell)!;draft.SetEntry(lotId,free.Road);
        draft.Id=UserMaps.SuggestId(draft.Name,[]);Assert.Equal("user_malaya_dolina",draft.Id);
        Assert.DoesNotContain(draft.Problems(),p=>p.Contains("подъезда",StringComparison.Ordinal));
        var resolved=UserMaps.Start(draft,Economy(),new(3),MapDesignTests.Balance());
        Assert.NotEqual(PlayMaps.CompactValleyScenarioId,resolved.Scenario.Id);
        Assert.Equal(4,resolved.Scenario.Economy!.Fleet!.Models.Count(m=>m.EquipmentTypeId is not null));
        var scenario=resolved.Scenario with{Participants=resolved.Scenario.Participants.Select(p=>p.Id==1?p with{CashMinor=2_000_000}:p).ToArray()};
        var session=new GameSession(new(scenario));var level=scenario.BuildingTypes!.Single(t=>t.Id=="woodworking").Levels.Single(l=>l.Level==1);
        Assert.Equal(new[]{new Cell(0,0)},MapLots.Offsets(scenario.BuildingTypes!.Single(t=>t.Id=="woodworking"),1));
        var plot=scenario.Plots!.Single(p=>p.Position==free.Cell);
        StorageC1Tests.Move(session,free.Cell);StorageC1Tests.Step(session,new(ActionKind.BuyPlot,Plot:new(plot.Id)));
        var view=PlayerDesk.Read(session.Engine,new(1));
        var choice=PlayContext.At(view,session.Engine.Configuration(),"ru",true).Single(c=>c.Key=="build:woodworking");
        Assert.DoesNotContain("занимает",choice.Details);
        long wood=level.Materials.Single(m=>m.ItemId=="wood").Quantity;long steel=level.Materials.Single(m=>m.ItemId=="steel").Quantity;
        var ground=new InventoryId(session.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==plot.Id).InventoryId);
        var node=scenario.ResourceNodes.Where(n=>n.ItemId=="wood"&&n.Rules!.Public).OrderBy(n=>Math.Abs(n.Position.X-free.Cell.X)+Math.Abs(n.Position.Z-free.Cell.Z)).First();
        long volume=scenario.Items.Single(i=>i.Id=="wood").UnitVolume;long chunk=Math.Max(1,scenario.Participants[0].InventoryCapacity/volume);
        for(long left=wood;left>0;left-=chunk)
        {
            long take=Math.Min(chunk,left);
            if(session.Engine.ResourceSites().Single(r=>r.Position==node.Position).Stock<take)session.AdvanceTo(session.Engine.Tick+take*40);
            StorageC1Tests.Move(session,node.Position);StorageC1Tests.Step(session,new(ActionKind.Harvest,ItemId:"wood",Quantity:take));StorageC1Tests.Idle(session);
            StorageC1Tests.Put(session,ground,"wood",take);
        }
        StorageC1Tests.Move(session,scenario.CapitalDemand!.Terminal);StorageC1Tests.Step(session,new(ActionKind.BuyImport,ItemId:"steel",Quantity:steel));
        var pickup=session.Engine.ProductionContentOf(new(1))!.Pickup!.Value;
        StorageC1Tests.Step(session,new(ActionKind.Transfer,Source:pickup,Destination:StorageC1Tests.Carrier(session),ItemId:"steel",Quantity:steel));StorageC1Tests.Idle(session);
        StorageC1Tests.Put(session,ground,"steel",steel);
        StorageC1Tests.Move(session,free.Cell);StorageC1Tests.Step(session,new(ActionKind.Build,BuildingTypeId:"woodworking",Target:free.Cell));
        var project=session.Engine.StorageOf(new(1)).Construction.Single();session.AdvanceTo(project.CompletesTick);
        var building=session.Engine.Observe(new(1)).OwnBuildings.Single();Assert.Equal("woodworking",building.TypeId);Assert.Equal(free.Cell,building.Position);
        Assert.Contains("LandNotOwned",session.Engine.QuoteExpansion(new(1),building.Id)!.Blockers);
        var saved=CheckpointStore.Encode(session);draft.AddLotCell(new(0,0));
        var restored=CheckpointStore.Decode(saved);
        Assert.Equal(session.Engine.Configuration().Layout!.IndustrialLots.SelectMany(l=>l.Cells).OrderBy(c=>c.X).ThenBy(c=>c.Z),restored.Engine.Configuration().Layout!.IndustrialLots.SelectMany(l=>l.Cells).OrderBy(c=>c.X).ThenBy(c=>c.Z));
        Assert.Equal(building.Id,restored.Engine.Observe(new(1)).OwnBuildings.Single().Id);
    }
    [Fact]
    public void LotTooSmallIsExplainedAndHistoricalShapeStaysRejected()
    {
        var draft=Valley();var free=FreeCell(draft);draft.AddLotCell(free.Cell);draft.SetEntry(draft.Lots[^1].Id,free.Road);draft.Id="user_small_lot";
        var resolved=UserMaps.Start(draft,Economy(),new(3),MapDesignTests.Balance()).Scenario;
        var types=resolved.BuildingTypes!.Select(t=>t.Id!="woodworking"?t:t with{Levels=t.Levels.Select(l=>l.Level==1?l with{Footprint=ImmutableArray.Create(new Cell(0,0),new Cell(1,0))}:l).ToArray()}).ToArray();
        var scenario=resolved with{BuildingTypes=types,Participants=resolved.Participants.Select(p=>p.Id==1?p with{CashMinor=2_000_000}:p).ToArray()};
        var session=new GameSession(new(scenario));var plot=scenario.Plots!.Single(p=>p.Position==free.Cell);
        StorageC1Tests.Move(session,free.Cell);StorageC1Tests.Step(session,new(ActionKind.BuyPlot,Plot:new(plot.Id)));
        var choice=PlayContext.At(PlayerDesk.Read(session.Engine,new(1)),session.Engine.Configuration(),"ru",true).Single(c=>c.Key=="build:woodworking");
        Assert.Contains("занимает 2 клетки",choice.Details);Assert.False(choice.Enabled);
        StorageC1Tests.Step(session,new(ActionKind.Build,BuildingTypeId:"woodworking",Target:free.Cell),accepted:false);
        var saved=MapDraft.Create();saved.Id="user_small_lot";
        Assert.False(UserMaps.NeedsOverwriteConfirm(saved.ToJson(),"user_small_lot"));
        saved.Id="user_other";Assert.True(UserMaps.NeedsOverwriteConfirm(saved.ToJson(),"user_small_lot"));
    }
    private static (Cell Cell,Cell Road) FreeCell(MapDraft draft)
    {
        var blocked=draft.Roads.Concat(draft.Spawns).Concat(draft.EquipmentParking).Concat(draft.Lots.SelectMany(l=>l.Cells)).Concat(draft.Districts.SelectMany(d=>d.Cells)).Concat(draft.Civics.SelectMany(c=>c.Footprint)).Concat(draft.Stalls.Select(s=>s.Cell)).ToHashSet();
        foreach(var road in draft.Roads)
            foreach(var next in new[]{new Cell(road.X,road.Z+1),new(road.X,road.Z-1),new(road.X+1,road.Z),new(road.X-1,road.Z)})
                if(next.X>=0&&next.Z>=0&&next.X<draft.Width&&next.Z<draft.Height&&!blocked.Contains(next))return(next,road);
        throw new InvalidOperationException("no free cell");
    }
}

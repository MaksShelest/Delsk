using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I6C21MapTests
{
    private static EconomyProfile Profile()=>MobileEquipmentI4Tests.Profile();
    private static MapDraft Valley()=>MapDraft.Parse(OrdinaryNewGameTests.Text("compact_valley_i6c21.json"));
    [Fact]
    public void EditorPaintsErasesResourcesLotsAndReturnsThroughUndo()
    {
        var draft=MapDraft.Create(16,12);draft.MaxParticipants=4;
        draft.PaintRoad(new(1,1));draft.PaintRoad(new(2,1));draft.EraseRoad(new(2,1));
        Assert.Equal(new[]{new Cell(1,1)},draft.Roads);draft.Undo();Assert.Equal(2,draft.Roads.Count);draft.Redo();Assert.Single(draft.Roads);
        draft.BeginResourceGroup("wood");draft.PaintResource(new(0,0),"wood");draft.PaintResource(new(1,0),"wood");draft.SetPublic(new(0,0),true);
        Assert.Equal(2,draft.Districts.Single().Cells.Count);Assert.Contains(new Cell(0,0),draft.Districts.Single().PublicCells);
        draft.PlaceLot(new(3,1));draft.PlaceLot(new(3,1));
        var notes=string.Join('\n',draft.Problems());
        Assert.Contains("Пересечение",notes);Assert.Contains("(3, 1)",notes);
        var saved=draft.ToJson();var loaded=MapDraft.Parse(saved);
        Assert.Equal(saved,loaded.ToJson());Assert.Equal(2,loaded.Lots.Count);
        loaded.Resize(4,4);Assert.DoesNotContain(loaded.Roads,c=>c.X>=4||c.Z>=4);
    }
    [Fact]
    public void ValleyIsOneCellWideAndKeepsResourcesIndustryAndTownInOrder()
    {
        var draft=Valley();var roads=draft.Roads.ToHashSet();
        Assert.InRange(roads.Count,60,80);
        Assert.DoesNotContain(roads,c=>roads.Contains(new(c.X+1,c.Z))&&roads.Contains(new(c.X,c.Z+1))&&roads.Contains(new(c.X+1,c.Z+1)));
        Assert.Equal(15,draft.Lots.Count);Assert.All(MapDraft.ResourceItems,item=>Assert.Contains(draft.Districts,d=>d.Item==item));
        Assert.All(draft.Districts.SelectMany(d=>d.Cells),c=>Assert.True(c.X<=6));
        Assert.All(draft.Civics.SelectMany(c=>c.Footprint),c=>Assert.True(c.X>=24));
        Assert.Contains(draft.Civics,c=>c.Kind=="town_hall");Assert.Contains(draft.Civics,c=>c.Kind=="terminal");Assert.Contains(draft.Civics,c=>c.Kind=="dealership");
        Assert.Empty(draft.Problems());Assert.Equal("",draft.Check(Profile()));
        var old=JsonDocument.Parse(OrdinaryNewGameTests.Text("compact_valley_i6c2.json")).RootElement.GetProperty("road_center_cells").GetArrayLength();
        Assert.True(old>roads.Count*3,$"old roads {old}, new roads {roads.Count}");
    }
    [Fact]
    public void NewGameShowsSixVehiclesAndLaterSupplyStillAddsOne()
    {
        var session=I6C2MapsTests.Session(PlayMaps.CompactValley);var fleet=session.Engine.InspectFleet()!;
        Assert.Equal(4,fleet.Stock.Length);
        Assert.Equal(new[]{"extractor_field_1","extractor_forest_1","muravey70","sputnik20"},fleet.Stock.Select(v=>v.ModelId).Order(StringComparer.Ordinal));
        Assert.Equal(2,fleet.Stock.Select(v=>v.Position).Distinct().Count(c=>session.Engine.Configuration().Layout!.CivicObjects.Single(s=>s.Kind=="dealership").EquipmentParking!.Contains(c)));
        var models=session.Engine.Configuration().Economy!.Fleet!.Models;
        Assert.Equal(2,fleet.Stock.Count(v=>models.Single(m=>m.Id==v.ModelId) is {EquipmentTypeId:null,MaterialsOnly:false}));
        session.AdvanceTo(fleet.NextSupplyTick);Assert.Equal(5,session.Engine.InspectFleet()!.Stock.Length);session.Engine.ValidateInvariants();
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void StockedExtractorsStandBesideTheDealership(string key)
    {
        var session=I6C2MapsTests.Session(key);
        var dealer=session.Engine.Configuration().Layout!.CivicObjects.Single(c=>c.Kind=="dealership");
        var foot=dealer.Footprint.ToHashSet();
        var models=session.Engine.Configuration().Economy!.Fleet!.Models;
        var machines=session.Engine.InspectFleet()!.Stock.Where(v=>models.Single(m=>m.Id==v.ModelId).EquipmentTypeId is not null).ToArray();
        Assert.Equal(2,machines.Length);
        Assert.Equal(2,machines.Select(v=>v.Position).Distinct().Count());
        Assert.All(machines,v=>
        {
            Assert.Contains(v.Position,dealer.EquipmentParking!);
            Assert.Contains(foot,cell=>Math.Abs(cell.X-v.Position.X)+Math.Abs(cell.Z-v.Position.Z)==1);
        });
    }
    [Fact]
    public void PreviousValleySaveKeepsItsRoadsAndFullExhibition()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(OrdinaryNewGameTests.Text("compact-valley-i6c2.json"),SimulationJson.Options)!;
        Assert.Equal(PlayMaps.CompactValleyI6C2ScenarioId,d.Id);Assert.DoesNotContain(MapDraft.ExhibitionToken,d.Layout!.Assignment);
        var session=new GameSession(new(d));Assert.Equal(22,session.Engine.InspectFleet()!.Stock.Length);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        Assert.Equal(d.Layout.RoadCenterCells.Length,restored.Engine.Configuration().Layout!.RoadCenterCells.Length);
        Assert.NotEqual(PlayMaps.CompactValleyScenarioId,restored.Engine.Configuration().Id);
    }
}

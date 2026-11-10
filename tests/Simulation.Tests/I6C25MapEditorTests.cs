using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I6C25MapEditorTests
{
    private static EconomyProfile Current()=>VNextResourceRightsProfile.Create(VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(MobileEquipmentI4Tests.Profile())));
    private static EconomyProfile Historical()=>MobileEquipmentI4Tests.Profile();
    /// <summary>Воспроизводимый аналог 35×20. Исходный файл пользователя в задании не был приложен.</summary>
    private static MapDraft Sample()
    {
        var draft=MapDraft.Create(35,20);
        draft.Id="user_probnik_sredney_karty";draft.Name="Пробник средней карты";draft.MaxParticipants=4;
        draft.Spawns.Clear();draft.Turns.Clear();
        for(int x=0;x<35;x++)draft.PaintRoad(new(x,10));
        foreach(var cell in new[]{new Cell(2,10),new(16,10),new(22,10),new(33,10)})draft.ToggleSpawn(cell);
        string[] items=["wood","grain","milk","beet","stone","sand","iron_ore","copper_ore"];
        for(int i=0;i<items.Length;i++){draft.BeginResourceGroup(items[i]);draft.PaintResource(new(0,i),items[i]);draft.PaintResource(new(1,i),items[i]);}
        draft.PlaceLot(new(4,7));draft.PlaceLot(new(10,7));
        draft.PlaceCivic("town_hall",new(28,1));draft.PlaceCivic("terminal",new(28,5));draft.PlaceCivic("dealership",new(28,12));
        foreach(var cell in new[]{new Cell(20,12),new(21,12),new(22,12),new(23,12)})draft.ToggleStall(cell);
        draft.ToggleParking(new(6,11));draft.ToggleParking(new(8,11));
        return draft;
    }
    [Fact]
    public void AnalogueStartsAndKeepsItsSizeWithTwoMachineBays()
    {
        var draft=Sample();var before=draft.ToJson();
        Assert.Equal((35,20),(draft.Width,draft.Height));
        Assert.Equal(2,draft.EquipmentParking.Distinct().Count());
        Assert.Empty(draft.Problems());
        Assert.Equal("",draft.Check(Current()));
        var resolved=UserMaps.Start(draft,Current(),new(3),MapDesignTests.Balance());
        Assert.Equal(35,resolved.Scenario.MapWidth);Assert.Equal(20,resolved.Scenario.MapHeight);
        Assert.Equal(2,resolved.Scenario.Layout!.CivicObjects.Single(c=>c.Kind=="dealership").EquipmentParking!.Length);
        Assert.Equal(before,draft.ToJson());
        var session=new GameSession(new(resolved.Scenario));
        for(int i=0;i<12;i++)session.Step();
        Assert.True(session.Engine.Tick>=1||session.Engine.TravelQuantum>0);
        Assert.Equal(35,resolved.Scenario.MapWidth);
    }
    [Fact]
    public void HistoricalProfileStillRequiresSixteenBaysAndDoesNotGrowTheMap()
    {
        var draft=Sample();
        Assert.Contains("16",draft.Check(Historical()));
        var scenario=Current().Apply(MapDesignReader.Read(System.Text.Encoding.UTF8.GetBytes(draft.ToJson()),Current()));
        Assert.Equal(35,scenario.MapWidth);Assert.Equal(20,scenario.MapHeight);
        var valley=MapDraft.Parse(OrdinaryNewGameTests.Text("compact_valley_i6c21.json"));
        Assert.Equal(16,valley.EquipmentParking.Distinct().Count());
        Assert.Equal("",valley.Check(Historical()));
    }
    [Fact]
    public void DuplicateIdentifierAndMissingEntryNameTheObject()
    {
        var draft=Sample();
        draft.Lots[1]=new(draft.Lots[0].Id,draft.Lots[1].Cells,draft.Lots[1].Entry);
        var local=string.Join('\n',draft.Problems());
        Assert.Contains(draft.Lots[0].Id,local);Assert.Contains("Повторяется",local);
        var error=Assert.ThrowsAny<ArgumentException>(()=>Current().Apply(MapDesignReader.Read(System.Text.Encoding.UTF8.GetBytes(draft.ToJson()),Current())));
        Assert.Contains(draft.Lots[0].Id,error.Message);
        var shown=MapDraft.Explain(error);
        Assert.Contains(draft.Lots[0].Id,shown);Assert.DoesNotContain("Проверьте участки, въезды и городские здания",shown);
        var saved=draft.ToJson();Assert.Contains("Пробник средней карты",saved);Assert.Equal(saved,MapDraft.Parse(saved).ToJson());
        var entry=Sample();var lot=entry.Lots[0];var bad=new Cell(0,19);entry.SetEntry(lot.Id,bad);
        var note=entry.Notes().Single(n=>n.Text.Contains(lot.Id)&&n.Text.Contains("подъезд"));
        Assert.Equal(bad,note.At);Assert.NotNull(note.Fix);Assert.Contains(lot.Id,entry.DescribeFix(note.Fix!));
        Assert.True(entry.ApplyFix(note.Fix!));Assert.Contains(entry.Lots[0].Entry,entry.Roads);
        entry.Undo();Assert.Equal(bad,entry.Lots[0].Entry);
    }
    [Fact]
    public void ZoomKeepsTheCursorPointAcrossRepeatedScalesAndAQuarterTurn()
    {
        var point=MapView.MapPoint(40,30,10,140.5f,90.25f);
        var shifted=MapView.ZoomOffset(140.5f,90.25f,point.X,point.Y,22);
        var again=MapView.MapPoint(shifted.X,shifted.Y,22,140.5f,90.25f);
        Assert.Equal(point.X,again.X,3);Assert.Equal(point.Y,again.Y,3);
        var further=MapView.ZoomOffset(140.5f,90.25f,again.X,again.Y,7);
        var stayed=MapView.MapPoint(further.X,further.Y,7,140.5f,90.25f);
        Assert.Equal(point.X,stayed.X,3);Assert.Equal(point.Y,stayed.Y,3);
        var screen=MapView.ToScreen(35,20,1,new(6,11));
        Assert.Equal(new Cell(6,11),MapView.ToWorld(35,20,1,screen.X,screen.Y));
    }
}

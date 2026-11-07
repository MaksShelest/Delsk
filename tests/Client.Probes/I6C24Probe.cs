using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный smoke редактора I6-C2.4. В обычный клиент не входит.</summary>
public partial class I6C24Probe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static object? Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);_checks.Add(text);GD.Print("I6C24 "+text);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"capture "+name);}
    private static T? Find<T>(Node node,string name) where T:Node
    {
        if(node.Name==name&&node is T hit)return hit;
        foreach(var child in node.GetChildren()){var found=Find<T>(child,name);if(found is not null)return found;}
        return null;
    }
    private static (Cell Road,Cell First,Cell Second) FreeLot(MapDraft draft)
    {
        var busy=draft.Roads.Concat(draft.Spawns).Concat(draft.EquipmentParking).Concat(draft.Turns).ToHashSet();
        foreach(var lot in draft.Lots)busy.UnionWith(lot.Cells);
        foreach(var district in draft.Districts)busy.UnionWith(district.Cells);
        foreach(var civic in draft.Civics){busy.UnionWith(civic.Footprint);busy.UnionWith(civic.Parking);}
        foreach(var stall in draft.Stalls)busy.Add(stall.Cell);
        foreach(var road in draft.Roads)
            foreach(var first in Around(road))
                if(Open(first))
                    foreach(var second in Around(first))
                        if(second!=road&&Open(second))return (road,first,second);
        throw new InvalidOperationException("No free lot cell");
        bool Open(Cell cell)=>cell.X>=0&&cell.Z>=0&&cell.X<draft.Width&&cell.Z<draft.Height&&!busy.Contains(cell);
        static IEnumerable<Cell> Around(Cell cell)=>[new(cell.X+1,cell.Z),new(cell.X-1,cell.Z),new(cell.X,cell.Z+1),new(cell.X,cell.Z-1)];
    }
    private static float Luma(Color color)=>color.R*.299f+color.G*.587f+color.B*.114f;
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];System.IO.Directory.CreateDirectory(_output);
            var root=args[Array.IndexOf(args,"--repository")+1];
            GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            var editor=GD.Load<PackedScene>("res://MapEditor.tscn").Instantiate<MapEditor>();AddChild(editor);await Frames(8);
            Check(editor.Draft.Id==PlayMaps.CompactValleyScenarioId,"editor opens the current valley");
            Check(Find<Button>(editor,"Tool_select") is not null&&Find<Button>(editor,"Tool_road") is not null&&Find<Button>(editor,"Tool_town_hall") is not null,"tools are grouped");
            await Shot("01-editor-tools");
            var spot=FreeLot(editor.Draft);string? lot=null;
            editor.Edit(draft=>{lot=draft.AddLotCell(spot.First);draft.AddLotCell(spot.Second,lot);draft.SetEntry(lot!,spot.Road);});
            Check(lot is not null&&editor.Draft.Lots.Single(item=>item.Id==lot).Cells.Count==2,"one-cell lot grows by one cell");
            editor.FocusCell(spot.First.X,spot.First.Z);
            Check(editor.PropertiesText.Contains(lot!)&&editor.PropertiesText.Contains("Площадь: 2"),"selected lot shows its id and area");
            await Shot("02-irregular-lot");
            string json=editor.Draft.ToJson();int width=editor.Draft.Width,height=editor.Draft.Height;
            editor.RotateView(1);Check(editor.ViewQuarter==1&&editor.Draft.ToJson()==json,"view rotation keeps the JSON");editor.RotateView(-1);
            editor.RotateContent();Check(editor.Draft.Width==height&&editor.Draft.Height==width,"content rotation swaps the map");
            Find<Button>(editor,"UndoChange")!.EmitSignal(Button.SignalName.Pressed);await Frames(1);
            Check(editor.Draft.Width==width&&editor.Draft.ToJson()==json,"undo restores the rotated map");
            editor.SetPreview(true);await Frames(4);
            var start=editor.PreviewCameraPosition;var focus=editor.PreviewFocus;
            Check(start.Y>1&&start.Z>focus.Z+1&&Mathf.Abs(start.X-focus.X)<1.5f,"preview starts south of the map, north up");
            editor.OrbitPreview(40,0);Check(Mathf.Abs(editor.PreviewCameraPosition.X-start.X)>1,"orbit moves the camera");
            await Shot("03-preview-orbit");
            editor.ResetPreviewView();Check(editor.PreviewCameraPosition.DistanceTo(start)<.5f,"reset restores the opening view");
            editor.TopPreviewView();Check(editor.PreviewCameraPosition.Y>start.Y,"top view is steeper");
            editor.OrbitPreview(0,-90);Check(editor.PreviewCameraPosition.Y>1,"camera stays above the ground");
            editor.SetPreview(false);
            Find<Button>(editor,"SaveDraft")!.EmitSignal(Button.SignalName.Pressed);await Frames(2);
            Check(editor.StatusText.Contains("Карта сохранена")&&editor.SavedPath is not null,"quick save writes the user catalog");
            var files=editor.GetNode<FileDialog>("Files");files.CurrentDir=ProjectSettings.GlobalizePath("user://maps");
            editor.ShowExportDialog();await Frames(3);
            var ink=files.GetThemeColor("font_color");
            Check(Luma(ink)<.35f,"file dialog text is dark on the light dialog");
            await Shot("04-file-dialog");files.Hide();
            string saved=editor.SavedPath!;string savedId=editor.Draft.Id;
            editor.QueueFree();await Frames(2);
            ScenarioDefinition Read(string key)=>JsonSerializer.Deserialize<ScenarioDefinition>(System.IO.File.ReadAllText(root+"/scenarios/vnext/compact-valley-i6c21.json"),SimulationJson.Options)!;
            var menu=new MapSelectionScreen("ru",false,Read);AddChild(menu);await Frames(4);
            Find<Button>(menu,"OpenMyMaps")!.EmitSignal(Button.SignalName.Pressed);await Frames(3);
            Check(Find<Control>(menu,"UserMap_"+savedId) is not null,"saved map is listed");
            Check(Find<Label>(menu,"UserMapStatus_"+savedId)!.Text=="Проверена","saved map is checked");
            await Shot("05-my-maps");
            menu.QueueFree();await Frames(1);
            MapHandoff.QueuePlay(saved,3,Agents.Algorithmic.NpcDifficulty.Medium);
            var app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);
            GameSession? session=null;
            for(int i=0;i<120&&session is null;i++){await Frames(1);session=app.GetType().GetField("_session",Flags)!.GetValue(app) as GameSession;}
            Check(session is not null&&session.Engine.Configuration().Id==savedId,"ordinary game starts on the saved map");
            var roads=session!.Engine.Configuration().Layout!.RoadCenterCells;
            app.SetProcess(false);session.SetPaused(true);
            var draft=MapDraft.Parse(System.IO.File.ReadAllText(saved));draft.Name+=" (правка)";draft.PaintRoad(new(0,0));System.IO.File.WriteAllText(saved,draft.ToJson());
            Call(app,"Load");await Frames(2);
            var loaded=Field<GameSession>(app,"_session");
            Check(loaded.Engine.Configuration().Id==savedId,"reopened game keeps the map id");
            Check(loaded.Engine.Configuration().Layout!.RoadCenterCells.SequenceEqual(roads),"later edits do not change the saved geometry");
            await System.IO.File.WriteAllTextAsync(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",map=savedId,roads=roads.Length,checks=_checks,scope="Linux software GL and synthetic input. Editor, one user map, one ordinary start and one reload. Not a physical Windows, Android or FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}

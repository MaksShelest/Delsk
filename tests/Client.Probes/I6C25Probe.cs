using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный smoke исправлений редактора I6-C2.5. В обычный клиент не входит.</summary>
public partial class I6C25Probe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);_checks.Add(text);GD.Print("I6C25 "+text);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"capture "+name);}
    private static T? Find<T>(Node node,string name) where T:Node
    {
        if(node.Name==name&&node is T hit)return hit;
        foreach(var child in node.GetChildren()){var found=Find<T>(child,name);if(found is not null)return found;}
        return null;
    }
    private static IEnumerable<T> All<T>(Node node) where T:Node
    {
        if(node is T hit)yield return hit;
        foreach(var child in node.GetChildren(true))foreach(var found in All<T>(child))yield return found;
    }
    private static float Channel(float v)=>v<=.03928f?v/12.92f:Mathf.Pow((v+.055f)/1.055f,2.4f);
    private static float Luma(Color c)=>.2126f*Channel(c.R)+.7152f*Channel(c.G)+.0722f*Channel(c.B);
    private static float Contrast(Color a,Color b){float x=Luma(a),y=Luma(b);return (Mathf.Max(x,y)+.05f)/(Mathf.Min(x,y)+.05f);}
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
    private void Wheel(MapEditor editor,Vector2 at,bool up)
    {
        editor._GuiInput(new InputEventMouseButton{ButtonIndex=up?MouseButton.WheelUp:MouseButton.WheelDown,Pressed=true,Position=at});
    }
    private async Task SampleDialog(FileDialog files,string name)
    {
        if(!files.Visible)files.PopupCentered();await Frames(2);
        foreach(var mode in new[]{FileDialog.DisplayModeEnum.Thumbnails,FileDialog.DisplayModeEnum.List})
        {
            files.DisplayMode=mode;await Frames(2);
            var list=All<ItemList>(files).OrderByDescending(item=>item.ItemCount).FirstOrDefault()??throw new InvalidOperationException("no file list in "+name+": "+string.Join(",",All<Node>(files).Select(n=>n.GetType().Name).Take(40)));
            Check(list.ItemCount>0,"dialog "+name+" "+mode+" has entries");
            list.Select(0);
            var rect=list.GetItemRect(0);var origin=list.GetGlobalTransformWithCanvas()*rect.Position;var end=list.GetGlobalTransformWithCanvas()*(rect.Position+rect.Size);
            var point=list.GetGlobalTransformWithCanvas()*rect.GetCenter();
            Input.WarpMouse(point);GetViewport().PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point});
            await Frames(2);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var image=files.GetViewport().GetTexture().GetImage();
            image.SavePng(_output+"/"+name+"-"+mode+".png");
            var values=new List<float>();
            int x0=Math.Max(0,(int)origin.X),y0=Math.Max(0,(int)origin.Y),x1=Math.Min(image.GetWidth(),(int)end.X),y1=Math.Min(image.GetHeight(),(int)end.Y);
            for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)values.Add(Luma(image.GetPixel(x,y)));
            if(values.Count==0)throw new InvalidOperationException("dialog sample missed the file list "+origin+" "+image.GetSize());
            values.Sort();
            float dark=values[0],light=values[Math.Min(values.Count-1,values.Count*9/10)];
            Check(Contrast(new(dark,dark,dark),new(light,light,light))>=4.5f&&dark<.4f,"dialog "+name+" "+mode+" text stays dark, contrast "+Contrast(new(dark,dark,dark),new(light,light,light)).ToString("0.0")+" dark "+dark.ToString("0.00"));
            var ink=list.GetThemeColor("font_hovered_color");var paper=(list.GetThemeStylebox("hovered") as StyleBoxFlat)?.BgColor??new Color("ffffff");
            Check(Contrast(ink,paper)>=4.5f,"dialog "+name+" hovered theme contrast");
        }
        await Shot(name);
        files.Hide();
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];var root=args[Array.IndexOf(args,"--repository")+1];System.IO.Directory.CreateDirectory(_output);
            GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            var editor=GD.Load<PackedScene>("res://MapEditor.tscn").Instantiate<MapEditor>();AddChild(editor);await Frames(8);
            var original=Sample();string source=original.ToJson();editor.LoadText(source);await Frames(4);
            Check(editor.Draft.Width==35&&editor.Draft.Height==20&&editor.Draft.Id=="user_probnik_sredney_karty","analogue is 35 by 20");
            Check(Find<Label>(editor,"IssueSummary")!.Text.Contains("Ошибок нет"),"a playable map says there are no errors");
            await Shot("01-map-2d");
            var cursor=new Vector2(620,360);var pan=Field<Vector2>(editor,"_pan");float zoom=Field<float>(editor,"_zoom");
            var point=MapView.MapPoint(pan.X,pan.Y,zoom,cursor.X,cursor.Y);string tool=Field<string>(editor,"_tool");
            Wheel(editor,cursor,true);await Frames(18);
            var nextPan=Field<Vector2>(editor,"_pan");float nextZoom=Field<float>(editor,"_zoom");
            var stayed=MapView.MapPoint(nextPan.X,nextPan.Y,nextZoom,cursor.X,cursor.Y);
            Check(nextZoom>zoom+1&&Mathf.Abs(stayed.X-point.X)<.02f&&Mathf.Abs(stayed.Y-point.Y)<.02f,"zoom keeps the fractional point under the cursor");
            Check(Field<string>(editor,"_tool")==tool,"zoom keeps the selected tool");
            var scroll=Find<ScrollContainer>(editor,"ToolScroll")!;int scrolled=scroll.ScrollVertical;
            Wheel(editor,new(40,240),false);await Frames(2);
            Check(Mathf.Abs(Field<float>(editor,"_zoom")-nextZoom)<.2f&&scroll.ScrollVertical>scrolled,"wheel over the tools scrolls them");
            Wheel(editor,new(400,16),true);await Frames(2);
            Check(Mathf.Abs(Field<float>(editor,"_zoom")-nextZoom)<.2f,"wheel over the menu does not zoom");
            string json=editor.Draft.ToJson();editor.RotateView(1);float turned=Field<float>(editor,"_zoom");Wheel(editor,cursor,true);await Frames(18);
            Check(editor.Draft.ToJson()==json&&Field<float>(editor,"_zoom")>turned+1,"zoom still works after a quarter turn and the map is unchanged");
            editor.RotateView(-1);Find<Button>(editor,"FitMap")!.EmitSignal(Button.SignalName.Pressed);await Frames(2);
            var lot=editor.Draft.Lots[0];editor.Edit(d=>d.SetEntry(lot.Id,new(0,19)));await Frames(2);
            Check(Find<Label>(editor,"IssueSummary")!.Text.StartsWith("Ошибок:")&&editor.IssueText.Contains(lot.Id)&&editor.IssueText.Contains("(0, 19)"),"the error names the lot and the cell");
            await Shot("02-errors");
            Find<Button>(editor,"Fix_0")!.EmitSignal(Button.SignalName.Pressed);await Frames(2);
            Check(editor.StatusText.Contains(lot.Id)&&editor.Draft.Lots[0].Entry==new Cell(0,19),"the repair previews before it changes the map");
            Find<Button>(editor,"ApplyFix")!.EmitSignal(Button.SignalName.Pressed);await Frames(3);
            Check(editor.Draft.Roads.Contains(editor.Draft.Lots[0].Entry)&&Find<Label>(editor,"IssueSummary")!.Text.Contains("Ошибок нет"),"the confirmed repair makes the map playable");
            editor.SetPreview(true);await Frames(6);
            var camera=editor.GetNode<Camera3D>("PreviewWorld/PreviewCamera");
            var corners=new[]{new Vector3(-.5f,0,-.5f),new(34.5f,0,-.5f),new(-.5f,0,19.5f),new(34.5f,0,19.5f)}.Select(camera.UnprojectPosition).ToArray();
            var view=GetViewport().GetVisibleRect().Size;
            Check(corners.All(p=>p.X>8&&p.Y>8&&p.X<view.X-8&&p.Y<view.Y-8),"the whole map stays inside the preview");
            Check(corners.Max(p=>p.Y)-corners.Min(p=>p.Y)>view.Y*.35f,"the map fills the preview");
            string preview=editor.Draft.ToJson();editor.TopPreviewView();Check(editor.Draft.ToJson()==preview&&editor.PreviewCameraPosition.Y>8,"top view does not change the map");
            editor.OrbitPreview(0,-90);Check(editor.PreviewCameraPosition.Y>1,"the camera stays above the ground");
            await Shot("03-preview");editor.SetPreview(false);
            Find<Button>(editor,"SaveDraft")!.EmitSignal(Button.SignalName.Pressed);await Frames(2);
            Check(editor.StatusText.Contains("Карта сохранена")&&editor.SavedPath is not null&&System.IO.File.ReadAllText(editor.SavedPath).Contains("Пробник средней карты"),"save writes the analogue and shows its path");
            Check(source==original.ToJson(),"the source text of the analogue was not rewritten");
            await Shot("04-save");
            var files=editor.GetNode<FileDialog>("Files");files.CurrentDir=ProjectSettings.GlobalizePath("res://");
            Find<Button>(editor,"OpenMap")!.EmitSignal(Button.SignalName.Pressed);await Frames(3);await SampleDialog(files,"05-dialog-open");
            Find<Button>(editor,"ExportMap")!.EmitSignal(Button.SignalName.Pressed);await Frames(3);await SampleDialog(files,"05-dialog-export");
            Find<Button>(editor,"BackupJson")!.EmitSignal(Button.SignalName.Pressed);await Frames(2);
            Check(files.Title.Contains("копия")||files.Title.Contains("backup")||files.Title.Contains("Резерв"),"backup export is separate from the checked export");files.Hide();
            string saved=editor.SavedPath!,savedId=editor.Draft.Id;editor.QueueFree();await Frames(2);
            ScenarioDefinition Read(string key)=>JsonSerializer.Deserialize<ScenarioDefinition>(System.IO.File.ReadAllText(root+"/scenarios/vnext/compact-valley-i6c21.json"),SimulationJson.Options)!;
            var menu=new MapSelectionScreen("ru",false,Read);AddChild(menu);await Frames(4);
            Find<Button>(menu,"OpenMyMaps")!.EmitSignal(Button.SignalName.Pressed);await Frames(3);
            Check(Find<Control>(menu,"UserMap_"+savedId) is not null&&Find<Label>(menu,"UserMapStatus_"+savedId)!.Text=="Проверена","my maps lists the checked analogue");
            await Shot("06-my-maps");menu.QueueFree();await Frames(1);
            MapHandoff.QueuePlay(saved,3,Agents.Algorithmic.NpcDifficulty.Medium);
            var app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);
            GameSession? session=null;
            for(int i=0;i<180&&session is null;i++){await Frames(1);session=app.GetType().GetField("_session",Flags)!.GetValue(app) as GameSession;}
            Check(session is not null&&session.Engine.Configuration().MapWidth==35&&session.Engine.Configuration().MapHeight==20,"ordinary game loads the 35 by 20 map");
            for(int i=0;i<180&&session!.Engine.Tick<1&&session.Engine.TravelQuantum==0;i++)await Frames(1);
            Check(session!.Engine.Tick>=1||session.Engine.TravelQuantum>0,"the new game advances past the opening");
            app.SetProcess(false);session.SetPaused(true);await Shot("07-play");
            await System.IO.File.WriteAllTextAsync(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",map=savedId,width=35,height=20,tick=session.Engine.Tick,quantum=session.Engine.TravelQuantum,checks=_checks,scope="Linux software GL and synthetic input. One analogue 35×20 map, not the user's missing original file. Not a physical Windows, Android or FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated editor and new-valley smoke. Never shipped with the ordinary client.</summary>
public partial class I6C21Probe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static object? Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);_checks.Add(text);GD.Print("I6C21 "+text);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"capture "+name);}
    private void Camera(Playfield app,Vector3 center,float size)
    {
        var stage=Field<PresentationStage>(app,"_stage");Set(stage,"_follow",false);
        var camera=Field<Camera3D>(stage,"_camera");camera.Projection=Camera3D.ProjectionType.Orthogonal;
        camera.Size=size;camera.Position=center+new Vector3(0,35,20);camera.LookAt(center,Vector3.Up);
        Field<Control>(app,"_playChrome").Visible=false;
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];System.IO.Directory.CreateDirectory(_output);
            var root=args[Array.IndexOf(args,"--repository")+1];
            GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            var editor=GD.Load<PackedScene>("res://MapEditor.tscn").Instantiate<MapEditor>();AddChild(editor);await Frames(8);
            Check(editor.Draft.Id==PlayMaps.CompactValleyScenarioId,"editor opens the current valley");
            Check(editor.Draft.Roads.Count is >=60 and <=80,"editor shows the narrow road net");
            await Shot("01-editor-valley");
            editor.SelectCell(23,12);await Frames(2);
            Check(editor.PropertiesText.Contains("Дорога")&&editor.PropertiesText.Contains("(23, 12)"),"selected road exposes its cell");
            await Shot("02-editor-road");
            int roads=editor.Draft.Roads.Count;editor.Draft.PaintRoad(new(0,0));editor.Draft.Undo();
            Check(editor.Draft.Roads.Count==roads,"undo restores the painted road");
            editor.SetPreview(true);await Frames(4);Check(editor.GetNode("PreviewWorld").GetChildCount()>2,"real 3D preview builds the valley");editor.SetPreview(false);
            editor.QueueFree();await Frames(2);
            var app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);await Frames(8);app.SetProcess(false);
            var valley=(OrdinaryGameResolution)Call(app,"ResolveNewGame",PlayMaps.CompactValley,3)!;
            Check(valley.Scenario.Id==PlayMaps.CompactValleyScenarioId,"new game uses the versioned valley");
            Check(valley.Scenario.Layout!.RoadCenterCells.Length==roads,"play uses the edited road count");
            var funded=valley.Scenario with{Participants=valley.Scenario.Participants.Select(p=>p.Id==1?p with{CashMinor=1000000}:p).ToArray()};
            var session=new GameSession(new(funded));
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(app)!.ToString()!;
            CheckpointStore.Save(session,save);Call(app,"Load");Call(app,"ContinueGame");await Frames(8);
            app.SetProcess(false);session=Field<GameSession>(app,"_session");session.SetPaused(true);Call(app,"Refresh");
            Check(session.Engine.InspectFleet()!.Stock.Length==4,"new exhibition is four vehicles");
            Camera(app,new(15.5f,0,12),36);await Shot("03-valley-3d");
            Camera(app,new(24,0,8),16);await Shot("04-city-lots");
            var entry=session.Engine.Configuration().Layout!.CivicObjects.Single(c=>c.Kind=="dealership").Entry;
            var listing=session.Engine.InspectFleet()!.Stock.First(v=>v.ModelId=="sputnik20");
            session.SetPaused(false);
            string Go(GameAction action)
            {
                string command=session.Send(new(1),action);session.Step();
                Check(session.Engine.Result(command)?.Status==ResultStatus.Accepted,action.Kind+" "+session.Engine.Result(command)?.ReasonCode);
                int bound=1200;while(session.Engine.Observe(new(1)).Participant.Busy&&bound-->0)session.Step();Check(bound>0,"bounded "+action.Kind);return command;
            }
            Go(new(ActionKind.MoveTo,Target:entry));
            Go(new(ActionKind.BuyVehicle,VehicleId:listing.Id));
            int purchase=12;
            while(session.Engine.InspectFleet()!.Purchases.All(p=>p.VehicleId!=listing.Id)&&purchase-->0)session.Step();
            Check(session.Engine.InspectFleet()!.Purchases.Count(p=>p.VehicleId==listing.Id)==1,"one purchase of the standing car");
            var bought=session.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==listing.Id);
            Go(new(ActionKind.MoveTo,Target:session.Engine.Inspect().Inventories.Single(i=>i.Id==bought.CargoInventory).Position));
            Go(new(ActionKind.BoardVehicle,VehicleId:listing.Id));
            Go(new(ActionKind.MoveTo,Target:new(14,12)));
            Check(session.Engine.Inspect().Inventories.Single(i=>i.Id==bought.CargoInventory).Position==new Cell(14,12),"purchased car reaches the central road");
            var previous=JsonSerializer.Deserialize<ScenarioDefinition>(System.IO.File.ReadAllText(root+"/scenarios/vnext/compact-valley-i6c2.json"),SimulationJson.Options)!;
            var old=new GameSession(new(previous));var copy=CheckpointStore.Decode(CheckpointStore.Encode(old));
            Check(copy.Engine.Configuration().Id==PlayMaps.CompactValleyI6C2ScenarioId,"old save keeps its scenario id");
            Check(copy.Engine.Configuration().Layout!.RoadCenterCells.Length>200,"old save keeps the previous roads");
            Check(copy.Engine.InspectFleet()!.Stock.Length==22,"old save keeps the full exhibition");
            Check(old.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"old save reloads exactly");
            GD.Print("I6C21 roads-before "+previous.Layout!.RoadCenterCells.Length+" roads-after "+roads);
            await System.IO.File.WriteAllTextAsync(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",roadsBefore=previous.Layout.RoadCenterCells.Length,roadsAfter=roads,checks=_checks,scope="Linux software GL and synthetic input. Editor, new valley, one purchase and one old save. Not a physical Windows, Android or FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}

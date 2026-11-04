using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

// Synthetic UI acceptance over paid diagnostic fixtures, never ordinary earning evidence.
public partial class I3RoadWorkProbe : Control
{
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags)!.Invoke(o,args);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags)!.SetValue(o,value);
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node child in n.GetChildren())foreach(var next in All(child))yield return next;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().Single(n=>n.Name==name);
    private void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);_checks.Add(name);}
    private async Task Frames(int count=3){for(int n=0;n<count;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Click(string name,bool touch=false)
    {
        var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();Check(button.IsVisibleInTree()&&!button.Disabled,"available "+name);
        var point=button.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Check(GetViewportRect().HasPoint(point),"button inside viewport "+name);
        if(touch)
        {
            Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=true});await Frames(1);
            Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=false});
        }
        else
        {
            Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
            Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
            Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});
        }
        await Frames();Call(_app,"Refresh");
    }
    private void Step(int count=1){for(int n=0;n<count;n++)_session.Step();Call(_app,"Refresh");}
    private async Task Shot(string name)
    {
        Call(_app,"Refresh");await Frames(5);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string name)=>args[Array.IndexOf(args,name)+1];_output=Arg("--output");System.IO.Directory.CreateDirectory(_output);
            string fixture=Arg("--fixture");
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(save)!);
            var cases=new[]{("ru",new Vector2I(800,720),false),("en",new Vector2I(1000,760),false),("en",new Vector2I(360,800),true)};
            foreach(var (language,size,touch) in cases.Where(c=>!args.Contains("--case")||c.Item1+"-"+c.Item2.X==Arg("--case")))
            {
                GetWindow().Size=size;GetWindow().ContentScaleSize=size;
                Call(_app,"CloseRoadWork");System.IO.File.Copy(fixture,save,true);Set(_app,"_language",language);Call(_app,"Load");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);
                _app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(false);
                var v=_session.Engine.InspectFleet()!.Vehicles.Single();Call(_app,"Refresh");Set(_app,"_contextIndex",0);Call(_app,"ConfirmContext");await Frames();
                Check(Named<RoadWorkCard>("RoadWorkCard").IsVisibleInTree(),"ordinary context opens compact card");
                Check(!Field<VirtualJoystick>(_app,"_stick").Visible,"joystick does not cover card controls");
                long cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;await Shot(language+"-"+size.X+"-ready");
                if(language=="en"&&!touch)
                {
                    Named<Button>("RoadCellStart").GrabFocus();int window=GetWindow().GetWindowId();
                    Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=true});await Frames(1);
                    Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=false});await Frames();
                }
                else await Click("RoadCellStart",touch);
                Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash,"click only queues command");
                Step();Check(_session.Engine.InspectRoads()!.Works.Single().Batch is null,"alignment precedes spending");await Shot(language+"-"+size.X+"-positioning");
                var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));
                for(int n=0;n<4;n++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"native alignment exact load continuation");}
                Call(_app,"Refresh");var batch=_session.Engine.InspectRoads()!.Works.Single().Batch!;Check(batch is not null,"actual Core work started");
                Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-400,"single authoritative startup payment");
                Step(3);await Shot(language+"-"+size.X+"-working");
                _app.Notification((int)NotificationApplicationFocusOut);long done=_session.Engine.InspectRoads()!.Works.Single().Batch!.Work;Step(2);Check(_session.Engine.InspectRoads()!.Works.Single().Batch!.Work==done,"focus loss pauses actual work");
                _app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session.SetPaused(false);
                await Click("RoadCellPause",touch);Step();long paused=_session.Engine.InspectRoads()!.Works.Single().Batch!.Work;Step(2);Check(_session.Engine.InspectRoads()!.Works.Single().Batch!.Work==paused,"ordinary pause retains work");
                await Click("RoadCellStart",touch);Step();int bound=40;while(_session.Engine.InspectRoads()!.Works.Single().Batch is not null&&bound-->0)Step();Check(bound>0,"road completes");
                Check(_session.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(10,1)).Level==2,"completed road surface");await Shot(language+"-"+size.X+"-complete");
                await Click("RoadCellClose",touch);Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection("fleet",v.Id);await Frames();
                await Click("FleetRoadWork_"+v.Id);Check(Named<RoadWorkCard>("RoadWorkCard").IsVisibleInTree(),"own vehicle card opens road work");
                Call(_app,"CloseRoadWork");CheckpointStore.Save(_session,_output+"/final-"+language+"-"+size.X+".save.json");
                System.IO.File.Copy(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture)!,"loadable.save.json"),save,true);Call(_app,"Load");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);
                _session=Field<GameSession>(_app,"_session");_session.SetPaused(false);Call(_app,"OpenRoadWork",v.Id);await Frames();
                Named<SpinBox>("RoadMaterialQuantity").Value=2;await Click("RoadMaterialLoad",touch);Step(3);
                var pack=_session.Engine.Observe(new(1));var cargo=pack.OwnInventories.Single(i=>i.Id==v.CargoInventory);
                Check(cargo.Stock.Single(s=>s.ItemId=="gravel").Quantity==2,"load moves actual backpack stock into cargo");
                Check(pack.OwnInventories.Single(i=>i.Id==pack.Participant.InventoryId).Stock.All(s=>s.ItemId!="gravel"||s.Quantity==0),"load does not copy goods");
                await Click("RoadPaverBoard",touch);Step();Check(Named<Button>("RoadCellStart").Disabled,"missing asphalt disables start");await Shot(language+"-"+size.X+"-missing-material");
                Call(_app,"CloseRoadWork");System.IO.File.Copy(fixture,save,true);Call(_app,"Load");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);
                _session=Field<GameSession>(_app,"_session");_session.SetPaused(false);Call(_app,"OpenRoadWork",v.Id);await Frames();
                var mode=Named<OptionButton>("RoadWorkMode");mode.Select(1);mode.EmitSignal(OptionButton.SignalName.ItemSelected,1);await Frames();
                await Click("RoadPaverBoard",touch);Step();await Click("RoadCellGo",touch);Step();int walk=10;
                while(_session.Engine.Observe(new(1)).Participant.Busy&&walk-->0)Step();Check(walk>0,"operator steps out of paver space");
                await Click("RoadCellStart",touch);Step();int align=12;while(_session.Engine.InspectRoads()!.Works.Single().Batch is null&&align-->0)Step();
                Check(align>0&&_session.Engine.InspectRoads()!.Works.Single().Group.Headcount>0,"automatic work uses real hired staff");
                await Click("RoadCellPause",touch);Step();await Click("RoadCellStart",touch);Step();await Shot(language+"-"+size.X+"-automatic");
                Check(!All(Named<RoadWorkCard>("RoadWorkCard")).OfType<Label>().Any(l=>l.Text.Contains("NoStaff",StringComparison.Ordinal)),"automatic status is localized");
                CheckpointStore.Save(_session,_output+"/automatic-"+language+"-"+size.X+".save.json");
            }
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Linux Mono software GL; native synthetic mouse/touch. Paid prepared fixtures, separate from ordinary earning and physical Windows/Android."}));GetTree().Quit();
        }
        catch(Exception error){System.IO.Directory.CreateDirectory(_output);System.IO.File.WriteAllText(_output+"/failure.txt",error.ToString());GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}

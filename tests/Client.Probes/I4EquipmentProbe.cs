using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

// Isolated ordinary UI over an actual ordinary walkthrough checkpoint. Synthetic input, not device acceptance.
public partial class I4EquipmentProbe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags)!.Invoke(o,args);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags)!.SetValue(o,value);
    private static IEnumerable<Node> All(Node node){yield return node;foreach(Node child in node.GetChildren())foreach(var next in All(child))yield return next;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().Single(n=>n.Name==name);
    private void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);_checks.Add(text);}
    private async Task Frames(int count=3){for(int n=0;n<count;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Step(int count=1){for(int n=0;n<count;n++)_session.Step();Call(_app,"Refresh");}
    private async Task Click(string name,bool touch)
    {
        var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();
        Check(button.IsVisibleInTree()&&!button.Disabled,"available "+name);
        if(name.StartsWith("I4Object_"))Check(button.Size.X>=Math.Min(90,button.Text.Length*8)&&button.Size.Y<90,"readable compact action "+name);
        var point=button.GetGlobalRect().GetCenter();Check(GetViewportRect().HasPoint(point),"inside viewport "+name);
        int window=GetWindow().GetWindowId();
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
    private async Task Shot(string name)
    {
        Call(_app,"Refresh");await Frames(5);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];_output=Arg("--output");System.IO.Directory.CreateDirectory(_output);
            var selected=Arg("--case");string language=selected.Split('-')[0];int width=int.Parse(selected.Split('-')[1]);bool touch=width==360;
            GetWindow().Size=new(width,touch?800:760);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(save)!);System.IO.File.Copy(Arg("--fixture"),save,true);
            Set(_app,"_language",language);Call(_app,"Load");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);
            _app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(false);
            var equipment=_session.Engine.Observe(new(1)).OwnEquipment.Single();long id=equipment.Id.Value;
            Check(_session.Engine.UsesMobileEquipment,"actual I4 profile loaded");
            var frame=Presentation.Read(_session.Engine.Configuration(),_session.Engine,1);
            Check(frame.Vehicles.Count(v=>v.Id==id)==1&&!frame.EquipmentTraffic.Any(e=>e.Id==id),"one authoritative visual body");
            Call(_app,"OpenObjectTablet","equipment",id);await Frames();Call(_app,"Refresh");await Frames();
            await Shot(language+"-"+width+"-arrived");
            long cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
            await Click("I4Object_place_"+id,touch);Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Deployed==false,"paused click only queues placement");await Click("TabletTime",touch);Check(!_session.Paused,"ordinary time control resumes world");Step();Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Deployed==true,"ordinary place command");
            await Click("I4Object_start_"+id,touch);Step();Step(3);
            Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==equipment.StorageId).OccupiedVolume>0,"real equipment output");
            Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash,"manual mining does not charge purchase twice");Check(!Field<VirtualJoystick>(_app,"_stick").Visible,"joystick does not overlap equipment controls");await Shot(language+"-"+width+"-mining");
            var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));
            copy.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
            for(int n=0;n<2;n++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"native work checkpoint exact");}
            Call(_app,"Refresh");await Click("I4Object_mode_Off_"+id,touch);Step();long stock=_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==equipment.StorageId).OccupiedVolume;
            await Click("I4Object_pack_"+id,touch);Step();var packed=_session.Engine.Observe(new(1)).OwnEquipment.Single();
            Check(packed.Id==equipment.Id&&packed.StorageId==equipment.StorageId&&packed.GroupId==equipment.GroupId&&packed.Deployed==false,"pick up preserves identity");
            Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==packed.StorageId).OccupiedVolume==stock,"pick up preserves cargo");await Shot(language+"-"+width+"-packed");
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,language,width,touch,scope="Actual ordinary checkpoint, native ordinary Fleet controls, synthetic mouse/touch, Linux software GL."}));
            GetTree().Quit(0);
        }
        catch(Exception ex){GD.PrintErr(ex);System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="failed",checks=_checks,error=ex.ToString()}));GetTree().Quit(1);}
    }
}

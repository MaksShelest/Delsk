using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated ordinary scene, actual input and an earned I4 checkpoint; no Developer API.</summary>
public partial class I5ContextProbe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="",_mode="";
    private readonly List<string> _checks=[];
    private readonly List<string> _inputTrace=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static IEnumerable<Node> All(Node node){yield return node;foreach(var child in node.GetChildren())foreach(var n in All(child))yield return n;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().Single(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private ContextActionsPanel Panel=>Field<ContextActionsPanel>(_app,"_contextActions");
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks.Add(reason);}
    private async Task Frames(int n=4){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Step(int n=1){_session.SetPaused(false);for(int i=0;i<n;i++)_session.Step();Call(_app,"Refresh");}
    private async Task Key(Key key,bool pressed)
    {Input.ParseInputEvent(new InputEventKey{WindowId=GetWindow().GetWindowId(),Keycode=key,PhysicalKeycode=key,Pressed=pressed});await Frames(1);}
    private async Task Click(string name,bool duringRefresh=false,bool disabled=false)
    {
        var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();
        button.ButtonDown+=()=>_inputTrace.Add(name+" down "+button.GetGlobalRect());
        button.ButtonUp+=()=>_inputTrace.Add(name+" up "+button.GetGlobalRect());
        button.Pressed+=()=>_inputTrace.Add(name+" pressed");
        Check(disabled||!button.Disabled,"available "+name);var rect=button.GetGlobalRect();
        var point=_mode=="touch"?rect.End-new Vector2(2,2):rect.GetCenter();
        Check(GetViewportRect().HasPoint(point),"hit inside viewport "+name);
        int window=GetWindow().GetWindowId();
        if(_mode=="keyboard")
        {button.GrabFocus();await Frames();await Key(Godot.Key.Enter,true);if(duringRefresh){Step();await Frames();}await Key(Godot.Key.Enter,false);}
        else if(_mode=="touch")
        {Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=true});await Frames(1);if(duringRefresh){Step();await Frames();}Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=false});}
        else
        {Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);if(duringRefresh){Step();await Frames();}Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});}
        await Frames();Call(_app,"Refresh");await Frames();
    }
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string k)=>args[Array.IndexOf(args,k)+1];_output=Arg("--output");System.IO.Directory.CreateDirectory(_output);
            string selected=Arg("--case"),language=selected.Split('-')[0];int width=int.Parse(selected.Split('-')[1]);_mode=width==360?"touch":width==800?"keyboard":"mouse";
            GetWindow().Size=new(width,width==1280?720:width==360?800:760);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(save)!);System.IO.File.Copy(Arg("--fixture"),save,true);
            Set(_app,"_language",language);Call(_app,"Load");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);
            _session=Field<GameSession>(_app,"_session");_session.SetPaused(false);Call(_app,"Refresh");await Frames(8);
            string beforeRebuild=_session.Engine.ComputeStateHash();int tapeBeforeRebuild=_session.CommandTape.Length;
            Call(_app,"RebuildChrome");Call(_app,"RebuildChrome");await Frames(8);
            var size=GetWindow().Size;GetWindow().Size=size+new Vector2I(1,0);await Frames();GetWindow().Size=size;await Frames();
            Check(Panel.IsVisibleInTree()&&_session.Engine.ComputeStateHash()==beforeRebuild&&_session.CommandTape.Length==tapeBeforeRebuild,"repeated HUD rebuild and viewport resize retain a live panel without changing the world");
            var equipment=_session.Engine.Observe(new(1)).OwnEquipment.Single();long id=equipment.Id.Value;
            Check(_session.Engine.Configuration().Participants.Length==4,"ordinary three NPC roster");
            await Click(ContextActionsPanel.ButtonName("vehicle:"+id));Step();await Frames();
            Check(_session.Engine.FleetOf(new(1))!.Vehicles.Single(v=>v.Vehicle.Id==id).Vehicle.Driver==0,"ordinary context leaves the vehicle");
            await Click(ContextActionsPanel.ButtonName("vehicle:"+id));Step();await Frames();
            Check(_session.Engine.FleetOf(new(1))!.Vehicles.Single(v=>v.Vehicle.Id==id).Vehicle.Driver==1,"same keyed control boards the same vehicle");
            Check(Panel.Selection.Choices.All(c=>!c.Key.StartsWith("section:",StringComparison.Ordinal)),"world context contains no global tablet navigation");
            Check(Panel.Selection.PrimaryKeys().Length is >=1 and <=3,"one to three semantic primary actions");
            string hash=_session.Engine.ComputeStateHash();int tape=_session.CommandTape.Length;
            await Shot("primary");
            if(Panel.Selection.Choices.Length>Panel.Selection.PrimaryKeys().Length)
            {
                await Click("ContextMore");Check(Panel.Expanded,"native more opens local overflow");
                Check(hash==_session.Engine.ComputeStateHash()&&tape==_session.CommandTape.Length,"opening changes no ledger/hash/tape");
                Check(!Field<VirtualJoystick>(_app,"_stick").Visible,"joystick hidden behind additional panel");
                var last=Panel.Selection.Choices.Last();var row=Named<Button>(ContextActionsPanel.ButtonName(last.Key));
                Panel.Selection.Select(last.Key);Step(3);await Frames();
                Check(Panel.Expanded&&Panel.Selection.SelectedKey==last.Key&&ReferenceEquals(row,Named<Button>(row.Name.ToString())),"ticks retain selection and the keyed control");
                UiNavigation.Reveal(row);await Frames();Check(GetViewportRect().HasPoint(row.GetGlobalRect().GetCenter()),"last local action is reachable");
                var outside=new Vector2(30,24);int window=GetWindow().GetWindowId();
                Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=outside,GlobalPosition=outside,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
                Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=outside,GlobalPosition=outside,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
                Check(!Panel.Expanded&&Field<object>(_app,"_surface").ToString()=="Play","outside dismissal shields Menu underneath");
            }
            await Click("ContextDismiss");Check(Panel.Dismissed&&Named<Button>("ContextReopen").Text.Contains(Panel.ActiveCount.ToString()),"one visible reopen button carries the active count");
            Step(2);await Frames();Check(Panel.Dismissed,"refresh respects manual collapse");await Click("ContextReopen");Check(!Panel.Dismissed,"native activation reopens current actions");
            if(Panel.Selection.Choices.Length>Panel.Selection.PrimaryKeys().Length)await Click("ContextMore");
            tape=_session.CommandTape.Length;long tick=_session.Engine.Tick;
            await Click(ContextActionsPanel.ButtonName("machine:place:"+id));
            Check(_session.CommandTape.Length==tape+1&&_session.Engine.Tick==tick,"one native click queues exactly one command without ticking");
            Check(Panel.Visible&&!Panel.Dismissed,"panel remains visible while waiting for Core");
            await Click(ContextActionsPanel.ButtonName("machine:place:"+id),disabled:true);
            Check(_session.CommandTape.Length==tape+1,"repeat press cannot duplicate the pending transaction");
            Step();await Frames();Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Deployed==true,"core accepted placement through the context control");
            var auto=Panel.Selection.Choices.Single(c=>c.Key=="machine:auto:"+id);Check(!Panel.Selection.PrimaryKeys().Contains(auto.Key),"automatic extraction is a genuine additional command");
            if(!Panel.Expanded)await Click("ContextMore");
            tape=_session.CommandTape.Length;await Click(ContextActionsPanel.ButtonName(auto.Key),true);Step();await Frames();
            Check(_session.CommandTape.Length==tape+1&&_session.CommandTape.Last().Command.Action==auto.Command,"refreshed additional row sends its exact command once");
            Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Mode==EquipmentMode.Automatic,"additional native command changes authoritative mode");
            Check(Panel.Selection.Choices.All(c=>c.Key!="machine:pack:"+id),"unavailable pack operation is omitted while equipment works");
            var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));copy.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
            for(int i=0;i<3;i++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"UI actions preserve exact restored future");}
            Call(_app,"Refresh");
            Check(Panel.GetGlobalRect().Position.X>=0&&Panel.GetGlobalRect().End.X<=width+1&&Panel.GetGlobalRect().End.Y<=GetViewportRect().Size.Y+1,"panel fits the real viewport: panel="+Panel.GetGlobalRect()+" viewport="+GetViewportRect()+" dock="+Field<Control>(_app,"_bottomChrome").GetGlobalRect());
            await Shot("after-command");
            var at=_session.Engine.Observe(new(1)).Participant.Position;var target=new Cell(at.X,at.Z+1);
            string move=_session.Send(new(1),PlayerDesk.Move(target.X,target.Z));
            for(int i=0;i<40&&_session.Engine.Observe(new(1)).Participant.Position==at;i++)Step();await Frames();
            Check(_session.Engine.Result(move)?.Status==ResultStatus.Accepted&&_session.Engine.Observe(new(1)).Participant.Position!=at,"ordinary physical movement changes context");Check(!Panel.Expanded,"new location closes the old object context");
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",language,width,input=_mode,checks=_checks,scope="Actual ordinary three-NPC checkpoint; real scene buttons and synthetic input. No physical-device claim."}));GetTree().Quit(0);
        }
        catch(Exception ex){try{await Shot("failure");}catch{}GD.PrintErr(ex);System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="failed",error=ex.ToString(),checks=_checks,inputTrace=_inputTrace}));GetTree().Quit(1);}
    }
}

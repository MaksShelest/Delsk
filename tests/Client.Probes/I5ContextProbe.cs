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
            Check(Panel.Selection.Choices.Length>=8,"at least eight meaningful context actions");Check(Panel.Selection.PrimaryKeys().Length is >=1 and <=3,"one to three semantic primary actions");
            string hash=_session.Engine.ComputeStateHash();int tape=_session.CommandTape.Length;
            await Shot("primary");await Click("ContextMore");Check(Panel.Expanded,"native more action opens structured panel");
            Check(hash==_session.Engine.ComputeStateHash()&&tape==_session.CommandTape.Length,"opening changes no ledger/hash/command tape");
            Check(!Field<VirtualJoystick>(_app,"_stick").Visible,"joystick hidden behind additional panel");
            var outside=new Vector2(30,24);int window=GetWindow().GetWindowId();
            if(_mode=="touch")
            {Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=outside,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=outside,Pressed=false});}
            else
            {Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=outside,GlobalPosition=outside,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=outside,GlobalPosition=outside,ButtonIndex=MouseButton.Left,Pressed=false});}
            await Frames();Check(!Panel.Expanded&&Field<object>(_app,"_surface").ToString()=="Play","outside click closes only the action panel, without clicking Menu underneath");
            Check(_session.CommandTape.Length==tape,"outside dismissal sends no command");await Click("ContextMore");
            var last=Panel.Selection.Choices.Last();var lastButton=Named<Button>(ContextActionsPanel.ButtonName(last.Key));
            Panel.Selection.Select(last.Key);Step(3);await Frames();
            Check(Panel.Expanded&&Panel.Selection.SelectedKey==last.Key,"ticks and ordinary refresh retain menu and selection");
            Check(ReferenceEquals(lastButton,Named<Button>(ContextActionsPanel.ButtonName(last.Key))),"refresh retains the actual last control");
            var scroll=All(Panel).OfType<ScrollContainer>().Single();tape=_session.CommandTape.Length;
            if(_mode=="keyboard")
            {
                Named<Button>("ContextMore").GrabFocus();
                for(int i=0;i<Panel.Selection.Choices.Length+12&&GetViewport().GuiGetFocusOwner()!=lastButton;i++){await Key(Godot.Key.Tab,true);await Key(Godot.Key.Tab,false);}
                Check(GetViewport().GuiGetFocusOwner()==lastButton,"Tab reaches the actual last action");
            }
            else if(_mode=="touch")
            {
                for(int swipe=0;swipe<3;swipe++)
                {
                    var start=scroll.GetGlobalRect().GetCenter()+new Vector2(0,65);
                    Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=start,Pressed=true});await Frames(1);
                    for(int i=1;i<=5;i++){Input.ParseInputEvent(new InputEventScreenDrag{WindowId=window,Index=0,Position=start-new Vector2(0,i*30),Relative=new(0,-30)});await Frames(1);}
                    Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=start-new Vector2(0,150),Pressed=false});await Frames();
                    if(scroll.GetGlobalRect().HasPoint(lastButton.GetGlobalRect().End-new Vector2(2,2)))break;
                }
            }
            else
            {
                var point=scroll.GetGlobalRect().GetCenter();
                for(int i=0;i<7;i++)
                {
                    Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.WheelDown,Pressed=true});
                    Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.WheelDown,Pressed=false});await Frames(1);
                }
            }
            Check(scroll.ScrollVertical>0&&Panel.Expanded&&_session.CommandTape.Length==tape,$"real list scrolling neither closes the panel nor activates a row (scroll={scroll.ScrollVertical}, expanded={Panel.Expanded}, commands={_session.CommandTape.Length-tape})");
            Check(scroll.GetGlobalRect().HasPoint(lastButton.GetGlobalRect().End-new Vector2(2,2)),"actual scrolling reaches the last row before any programmatic reveal");
            await Shot("expanded");tape=_session.CommandTape.Length;
            await Click(ContextActionsPanel.ButtonName(last.Key),true);
            Check(last.Key=="section:services","last row is the expected stable action");
            Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Player&&Field<PlayerTablet>(_app,"_playerTablet").HelpTopic=="services","last row opens exactly delivery rather than its neighbour");
            Check(_session.CommandTape.Length==tape,"navigation sends no economic command");Check(!Panel.Expanded,"incompatible tablet deliberately closes additional panel");
            await Click("TabletClose");await Click("ContextMore");
            if(_mode=="touch")
            {
                // A real drag interrupted by OS focus loss must not leave mouse input suppressed.
                var start=scroll.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=start,Pressed=true});await Frames(1);
                Input.ParseInputEvent(new InputEventScreenDrag{WindowId=window,Index=0,Position=start-new Vector2(0,30),Relative=new(0,-30)});await Frames(1);
                _app.Notification((int)NotificationApplicationFocusOut);await Frames();
                Check(!Panel.Expanded,"focus loss deliberately closes an interrupted touch gesture");
                Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=start-new Vector2(0,30),Pressed=false});await Frames();
                _app.Notification((int)NotificationApplicationFocusIn);await Frames();
                _mode="mouse";await Click("ContextMore");await Click(ContextActionsPanel.ButtonName(last.Key));
                Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Player&&Field<PlayerTablet>(_app,"_playerTablet").HelpTopic=="services","mouse activation recovers after interrupted touch and focus return");
                await Click("TabletClose");_mode="touch";await Click("ContextMore");
            }
            tape=_session.CommandTape.Length;long tick=_session.Engine.Tick;
            await Click(ContextActionsPanel.ButtonName("machine:place:"+id));
            Check(_session.CommandTape.Length==tape+1&&_session.Engine.Tick==tick,"one native click queues exactly one command without ticking");
            Check(Panel.Expanded,"panel remains open while waiting for core");
            await Click(ContextActionsPanel.ButtonName("machine:place:"+id),disabled:true);
            Check(_session.CommandTape.Length==tape+1,"repeat press cannot duplicate the pending transaction");
            Step();await Frames();Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Deployed==true,"core accepted placement through the context control");
            var auto=Panel.Selection.Choices.Single(c=>c.Key=="machine:auto:"+id);Check(!Panel.Selection.PrimaryKeys().Contains(auto.Key),"automatic extraction is a genuine additional command");
            tape=_session.CommandTape.Length;await Click(ContextActionsPanel.ButtonName(auto.Key),true);Step();await Frames();
            Check(_session.CommandTape.Length==tape+1&&_session.CommandTape.Last().Command.Action==auto.Command,"refreshed additional row sends its exact command once");
            Check(_session.Engine.Observe(new(1)).OwnEquipment.Single().Mode==EquipmentMode.Automatic,"additional native command changes authoritative mode");
            var pack=Panel.Selection.Choices.Single(c=>c.Key=="machine:pack:"+id);
            Check(!pack.Enabled&&pack.Details==PlayerText.Reason(language,"EquipmentBusy"),"busy equipment has its specific localized disabled reason");
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

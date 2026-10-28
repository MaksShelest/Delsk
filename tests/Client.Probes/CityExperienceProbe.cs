using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Stage-only native G2 input probe. No Developer API or economic mutation hook.</summary>
public partial class CityExperienceProbe : Control
{
    private Playfield _app=null!;private string _output="",_language="en";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node child in n.GetChildren())foreach(var c in All(child))yield return c;}
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    private GameSession Session=>Field<GameSession>(_app,"_session");
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().Single(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private Button TextButton(string text)=>All(GetTree().Root).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text==text);
    private async Task Reveal(Control c)
    {for(Node? p=c.GetParent();p is not null;p=p.GetParent())if(p is ScrollContainer s)s.EnsureControlVisible(c);await Frames();}
    private async Task Mouse(Vector2 position,MouseButton button=MouseButton.Left)
    {
        int id=GetTree().Root.GetWindowId();Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=button,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=button,Pressed=false});await Frames();
    }
    private async Task Click(BaseButton b)
    {
        await Reveal(b);Check(!b.Disabled&&b.IsVisibleInTree(),"Button unavailable: "+b.Name+" "+(b is Button text?text.Text:""));
        var point=b.GetGlobalRect().GetCenter();if(b.GetWindow()!=GetTree().Root)point+=b.GetWindow().Position;await Mouse(point);
    }
    private async Task KeyInput(Key key,bool down=true,uint unicode=0)
    {int id=DisplayServer.WindowGetActivePopup();if(id<0)id=GetTree().Root.GetWindowId();Input.ParseInputEvent(new InputEventKey{WindowId=id,Keycode=key,PhysicalKeycode=key,Unicode=unicode,Pressed=down});await Frames(1);}
    private async Task Press(Key key){await KeyInput(key);await KeyInput(key,false);await Frames();}
    private async Task Number(SpinBox spin,string number)
    {await Reveal(spin);var edit=spin.GetLineEdit();edit.GrabFocus();edit.SelectAll();foreach(char c in number){await KeyInput((Key)c,true,c);await KeyInput((Key)c,false);}await Press(Key.Enter);}
    private async Task Capture(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    private async Task StartMap(string map,int count)
    {
        _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(5);
        await Click(TextButton("English"));var setup=Field<MapSelectionScreen>(_app,"_mapSelection");await Number(All(setup).OfType<SpinBox>().Single(s=>s.Name=="NpcCount"),count.ToString());
        await Click(TextButton("Change map"));var choices=All(GetTree().Root).OfType<Button>().Where(b=>b.IsVisibleInTree()&&b.Text=="Select").ToArray();await Click(choices[Array.IndexOf(PlayMaps.All,map)]);
        if(_language=="ru")await Click(TextButton("Русский"));await Click(TextButton(_language=="ru"?"Начать":"Start"));await Frames(6);
        Check(Session.Engine.Configuration().Participants.Length==count+1,"Incomplete roster");Check(Session.Engine.Configuration().EconomyProfileId=="vnext-g1-npc-economy-1","G1 profile replaced by UX");
        _app.Notification((int)NotificationApplicationFocusIn);
    }
    private async Task MapCase(string map,int count,int width)
    {
        await StartMap(map,count);GetWindow().Size=new(width,760);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(5);
        await Press(Key.M);var panel=Named<CityMapPanel>("FullCityMap");var canvas=panel.Canvas;await Reveal(canvas);
        var prefs=canvas.State;var frame=Field<CityMapFrame>(panel,"_map");Check(frame.Participants.Length==count+1,"Map lost participants");
        var tablet=Named<TabletPanel>("GameTablet");var rect=tablet.GetGlobalRect();Check(rect.Position.X>=0&&rect.End.X<=width+1,"Tablet exceeds narrow viewport: "+rect);
        string hash=Session.Engine.ComputeStateHash();long tick=Session.Engine.Tick;int commands=Session.CommandTape.Length;
        var resource=frame.Objects.First(o=>o.Layer==CityLayer.Resources&&CityMap.At(frame,o.Entry,CityLayer.All,0).First().Key==o.Key);
        await Mouse(canvas.GlobalPosition+canvas.Point(resource.Entry));Check(prefs.SelectedKey==resource.Key,"Native cell hit did not select resource");
        var point=canvas.GetGlobalRect().GetCenter();await Mouse(point,MouseButton.WheelUp);Check(prefs.Zoom>1,"Wheel did not zoom");
        double before=prefs.CenterX;await Press(Key.Right);Check(prefs.CenterX!=before,"Focused keyboard map pan did not work");
        double zoom=prefs.Zoom;var selected=prefs.SelectedKey;var a=point+new Vector2(-30,0);var b=point+new Vector2(30,0);int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=a,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=1,Position=b,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventScreenDrag{WindowId=window,Index=1,Position=b+new Vector2(35,10),Relative=new(35,10)});await Frames();
        Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=1,Position=b+new Vector2(35,10),Pressed=false});
        Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=a,Pressed=false});await Frames();
        Check(prefs.Zoom>zoom,"Two-finger gesture did not zoom");Check(prefs.SelectedKey==selected,"Pinch emitted a ghost selection");Check(Session.CommandTape.Length==commands,"Map input submitted a world command");
        await Click(Named<CheckButton>("MapLayerResources"));Check(!CityMap.Visible(resource,prefs.Layers,0),"Layer filter did not hide resource");await Click(Named<CheckButton>("MapLayerResources"));
        var selector=Named<OptionButton>("MapParticipant");await Click(selector);await Press(Key.Up);await Press(Key.Enter);await Frames();Check(prefs.Participant==frame.Participants[^1],"Last participant filter did not apply");await Click(selector);await Press(Key.Down);await Press(Key.Enter);await Frames();Check(prefs.Participant==0,"All-participants filter did not restore");
        // Every roster entry remains selectable; metadata is public identity only.
        Check(selector.ItemCount==count+2,"Participant filter misses final NPC");
        await Click(Named<Button>("TabletHelp"));Check(Session.Paused,"Help did not pause");await Press(Key.Escape);Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.CityMap,"Help close lost map");
        Check(ReferenceEquals(prefs,Named<CityMapPanel>("FullCityMap").Canvas.State)&&prefs.SelectedKey==selected,"Help reset selection/viewport");
        Check(Session.Engine.Tick==tick&&Session.Engine.ComputeStateHash()==hash,"Map/help changed the economy");await Capture("map-"+_language+"-"+width);
        var saved=prefs.Capture();await Press(Key.Escape);Check(!Field<TabletState>(_app,"_tabletState").IsOpen,"Esc did not close current map");
        await Click(Named<Button>("OpenCityMap"));Check(saved==Named<CityMapPanel>("FullCityMap").Canvas.State.Capture(),"Reopening map reset viewport");
        long focusTick=Session.Engine.Tick;_app.Notification((int)NotificationApplicationFocusOut);await Frames(8);Check(Session.Engine.Tick==focusTick,"Focus loss advanced world");_app.Notification((int)NotificationApplicationFocusIn);
        await Press(Key.Escape);await Click(Named<Button>("OpenJourney"));Check(All(_app).OfType<CityJourneyPanel>().Any(p=>p.IsVisibleInTree()),"No short tutorial");
        await Click(Named<Button>("JourneyNext"));Check(Field<CityReadState>(_app,"_cityRead").TutorialPage==1,"Guide did not retain next page");await Press(Key.Escape);
        await Click(TextButton(_language=="ru"?"Меню":"Menu"));await Click(TextButton(_language=="ru"?"Оформление и персонажи":"Appearance and characters"));
        string cosmetic=Session.Engine.ComputeStateHash();await Click(Named<Button>("WorldSkinSet"));await Click(Named<Button>("PeopleSkinSet"));Check(Session.Engine.ComputeStateHash()==cosmetic,"Skin changed authoritative state");
        var appearance=All(_app).OfType<AcceptDialog>().Single(d=>d.Name=="AppearanceSettings");await Click(appearance.GetOkButton());
        await Click(TextButton(_language=="ru"?"Выбрать карту / новая партия":"Choose map / new game"));
        var setup=Field<MapSelectionScreen>(_app,"_mapSelection");await Frames();Check(setup.GetGlobalRect().End.X<=width+1,"Setup exceeds narrow window");
        await Click(TextButton(_language=="ru"?"Сменить карту":"Change map"));var catalog=All(_app).OfType<AcceptDialog>().Single(d=>d.Visible);Check(catalog.Size.X<=width,"Catalog wider than mobile window");await Click(catalog.GetOkButton());
        await Capture("setup-"+_language+"-"+width);await Click(TextButton(_language=="ru"?"Продолжить":"Continue"));
        _app.Notification((int)NotificationApplicationFocusOut);await Frames();long savedTick=Session.Engine.Tick;RemoveChild(_app);_app.QueueFree();await Frames();
        _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(5);await Click(TextButton(_language=="ru"?"Продолжить":"Continue"));
        Check(Session.Engine.Tick==savedTick&&Session.Engine.Configuration().Participants.Length==count+1,"Disk continuation changed time or roster");Check(Field<CityReadState>(_app,"_cityRead").TutorialPage==1,"Disk load lost guide memory");
        _checks.AddRange(["ordinary new game and entire roster","native full-map cell hit and wheel zoom","keyboard pan","two-finger zoom without world input or ghost tap","layer and participant filters","help/close preserve selection and time","focus pause","guide memory","skin change preserves authoritative hash","narrow setup/catalog/appearance","disk continuation"]);
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];_language=args[Array.IndexOf(args,"--language")+1];
            bool prepared=args.Contains("--fixtures");string map=args[Array.IndexOf(args,"--map")+1];int count=int.Parse(args[Array.IndexOf(args,"--count")+1]),width=int.Parse(args[Array.IndexOf(args,"--width")+1]);
            GetWindow().Size=new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;if(prepared)await FormsCase(args[Array.IndexOf(args,"--fixtures")+1]);else await MapCase(map,count,width);
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",prepared,map,npcCount=count,language=_language,width,checks=_checks,scope="Ordinary native Godot GUI with injected keyboard/mouse/touch events, software renderer; not a physical mobile device."}));RemoveChild(_app);_app.QueueFree();_app=null!;await Frames(6);GC.Collect();GC.WaitForPendingFinalizers();await Frames(3);GD.Print("DELSK_CITY_EXPERIENCE_PASS");GetTree().Quit(0);
        }
        catch(Exception failure){try{await Capture("failure");}catch{}System.IO.File.WriteAllText(_output+"/failure.json",JsonSerializer.Serialize(new{status="failed",error=failure.ToString(),checks=_checks}));GD.PushError(failure.ToString());GetTree().Quit(1);}
    }
}

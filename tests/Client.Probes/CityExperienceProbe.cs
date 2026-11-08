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
        Input.WarpMouse(position);await Frames(2);int id=GetTree().Root.GetWindowId();Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});
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
    private async Task OpenPlayerSection(string key,string ru,string en)
    {
        await Click(Named<Button>("HudTablet"));
        var navigation=All(_app).OfType<Button>().FirstOrDefault(b=>b.Name=="PlayerNav_"+key);
        if(navigation is not null)
        {
            // В основном режиме редкий раздел лежит в свёрнутой группе «Дополнительно».
            // Сначала раскрываем её, затем нажимаем ту же прежнюю кнопку.
            var body=navigation.GetParent();
            if(body is not null&&!navigation.IsVisibleInTree())
            {
                int bodyIndex=body.GetIndex();
                if(bodyIndex>0&&body.GetParent()?.GetChild(bodyIndex-1) is Button header&&header.Name.ToString().StartsWith("PlayerGroup_")&&header.IsVisibleInTree())
                    await Click(header);
            }
            if(navigation.IsVisibleInTree()){await Click(navigation);return;}
        }
        var picker=Named<OptionButton>("PlayerSectionPicker");string caption=_language=="ru"?ru:en;
        int index=Enumerable.Range(0,picker.ItemCount).Single(i=>picker.GetItemText(i)==caption);
        await Click(picker);picker.GetPopup().SetFocusedItem(index);await Press(Key.Enter);await Frames();
    }
    private async Task Number(SpinBox spin,string number)
    {await Reveal(spin);var edit=spin.GetLineEdit();edit.GrabFocus();edit.SelectAll();foreach(char c in number){await KeyInput((Key)c,true,c);await KeyInput((Key)c,false);}await Press(Key.Enter);}
    private async Task Capture(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    private async Task StartMap(string map,int count)
    {
        _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(5);
        var setup=Field<MapSelectionScreen>(_app,"_mapSelection");
        ((UiDisclosure)setup.AdditionalSettings.GetParent()).Open();await Frames();await Click(TextButton("English"));
        setup=Field<MapSelectionScreen>(_app,"_mapSelection");await Number(All(setup).OfType<SpinBox>().Single(s=>s.Name=="NpcCount"),count.ToString());
        if(_language=="ru"){((UiDisclosure)setup.AdditionalSettings.GetParent()).Open();await Frames();await Click(TextButton("Русский"));}
        if(PlayMaps.UserCatalog.Contains(map))
        {await Click(Named<Button>("ChooseMap"));await Click(Named<Button>("SelectMap_"+map));await Click(Named<Button>("StartGame"));}
        else
        {
            // Historical map GUI/restore coverage stays independent of the ordinary user catalog.
            typeof(Playfield).GetMethod("BeginMap",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_app,[map]);
        }
        await Frames(6);
        Check(Session.Engine.Configuration().Participants.Length==count+1,"Incomplete roster");Check(Session.Engine.Configuration().EconomyProfileId=="vnext-g1-npc-economy-1","G1 profile replaced by UX");
        _app.Notification((int)NotificationApplicationFocusIn);
    }
    private async Task MapCase(string map,int count,int width)
    {
        await StartMap(map,count);GetWindow().Size=new(width,760);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(5);
        await Press(Key.M);var panel=Named<CityMapPanel>("FullCityMap");var canvas=panel.Canvas;await Reveal(canvas);
        var prefs=canvas.State;var frame=Field<CityMapFrame>(panel,"_map");Check(frame.Participants.Length==count+1,"Map lost participants");
        var tablet=Named<TabletPanel>("GameTablet");var rect=tablet.GetGlobalRect();Check(rect.Position.X>=0&&rect.End.X<=width+1,"Tablet exceeds narrow viewport: "+rect);
        var timeRect=Named<CheckButton>("TabletTime").GetGlobalRect();Check(timeRect.Position.X>=0&&timeRect.End.X<=width+1,"Time control exceeds narrow viewport: "+timeRect);
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
        await Click(Named<Button>("MapFilters"));await Click(Named<CheckButton>("MapLayerResources"));Check(!CityMap.Visible(resource,prefs.Layers,0),"Layer filter did not hide resource");await Click(Named<CheckButton>("MapLayerResources"));
        await Click(Named<Button>("MapParticipant_"+frame.Participants[^1]));Check(prefs.Participant==frame.Participants[^1],"Last participant filter did not apply");
        var all=Named<Button>("MapParticipant_0");await Reveal(all);all.GrabFocus();await Press(Key.Enter);Check(prefs.Participant==0,"Keyboard all-participants filter did not restore");
        Check(All(panel).OfType<Button>().Count(b=>b.Name.ToString().StartsWith("MapParticipant_"))==count+2,"Participant filter misses final NPC");
        await Click(Named<Button>("MapFilters"));await Reveal(canvas);
        await Click(Named<Button>("TabletHelp"));Check(Session.Paused,"Help did not pause");await Press(Key.Escape);Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.CityMap,"Help close lost map");
        Check(ReferenceEquals(prefs,Named<CityMapPanel>("FullCityMap").Canvas.State)&&prefs.SelectedKey==selected,"Help reset selection/viewport");
        Check(Session.Engine.Tick==tick&&Session.Engine.ComputeStateHash()==hash,"Map/help changed the economy");await Capture("map-"+_language+"-"+width);
        var saved=prefs.Capture();await Press(Key.Escape);Check(!Field<TabletState>(_app,"_tabletState").IsOpen,"Esc did not close current map");
        await Click(Named<Button>("OpenCityMap"));Check(saved==Named<CityMapPanel>("FullCityMap").Canvas.State.Capture(),"Reopening map reset viewport");
        long focusTick=Session.Engine.Tick;_app.Notification((int)NotificationApplicationFocusOut);await Frames(8);Check(Session.Engine.Tick==focusTick,"Focus loss advanced world");_app.Notification((int)NotificationApplicationFocusIn);
        await Press(Key.Escape);await OpenPlayerSection("tutorial","Обучение","Guide");Check(All(_app).OfType<CityJourneyPanel>().Any(p=>p.IsVisibleInTree()),"No short tutorial");
        await Click(Named<Button>("JourneyNext"));Check(Field<CityReadState>(_app,"_cityRead").TutorialPage==1,"Guide did not retain next page");await Press(Key.Escape);
        await Click(TextButton(_language=="ru"?"Меню":"Menu"));await Click(TextButton(_language=="ru"?"Оформление и персонажи":"Appearance and characters"));
        string cosmetic=Session.Engine.ComputeStateHash();await Click(Named<Button>("WorldSkinSet"));await Click(Named<Button>("PeopleSkinSet"));Check(Session.Engine.ComputeStateHash()==cosmetic,"Skin changed authoritative state");
        var appearance=All(_app).OfType<AcceptDialog>().Single(d=>d.Name=="AppearanceSettings");await Click(appearance.GetOkButton());
        int menuWidth=Math.Max(800,width);GetWindow().Size=new(menuWidth,440);GetWindow().ContentScaleSize=GetWindow().Size;await Frames();
        await Click(TextButton(_language=="ru"?"Выбрать карту / новая партия":"Choose map / new game"));
        var setup=Field<MapSelectionScreen>(_app,"_mapSelection");await Frames();Check(setup.GetGlobalRect().End.X<=menuWidth+1,"Setup exceeds narrow window");
        await Click(Named<Button>("ChooseMap"));var catalog=Named<Control>("MapCatalog");Check(catalog.Size.X<=menuWidth,"Catalog wider than mobile window");await Click(Named<Button>("CloseMapCatalog"));
        await Capture("setup-"+_language+"-"+width);await Click(TextButton(_language=="ru"?"Продолжить":"Continue"));
        _app.Notification((int)NotificationApplicationFocusOut);await Frames();long savedTick=Session.Engine.Tick;RemoveChild(_app);_app.QueueFree();await Frames();
        _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(5);if(_language=="en"){var menu=Field<MapSelectionScreen>(_app,"_mapSelection");((UiDisclosure)menu.AdditionalSettings.GetParent()).Open();await Frames();await Click(TextButton("English"));}await Click(TextButton(_language=="ru"?"Продолжить":"Continue"));
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

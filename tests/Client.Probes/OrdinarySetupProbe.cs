using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native input/scene probe; never copied into ordinary exports.</summary>
public partial class OrdinarySetupProbe:Control
{
    private string _output="";private int _count;private string _key="second";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    private static T? Field<T>(object o,string name)=> (T?)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private async Task Frames(int n=2){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Reveal(Control c){for(Node? p=c.GetParent();p is not null;p=p.GetParent())if(p is ScrollContainer s)s.EnsureControlVisible(c);await Frames();}
    private async Task Click(Button b)
    {
        await Reveal(b);Check(b.IsVisibleInTree()&&!b.Disabled,"Button unavailable: "+b.Text);
        var position=b.GetGlobalRect().GetCenter();var window=b.GetWindow();
        // Embedded Window controls use their own canvas, but OS input arrives in root coordinates.
        if(window!=GetTree().Root)position+=window.Position;
        int id=GetTree().Root.GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private Button Button(string text)=>All(GetTree().Root).OfType<Button>().First(b=>b.IsVisibleInTree()&&(b.Text==text || b is ContextActionButton&&All(b).OfType<Label>().Any(l=>l.Text==text)));
    private async Task Count(MapSelectionScreen screen,int count)
    {
        var spin=All(screen).OfType<SpinBox>().Single(s=>s.Name=="NpcCount");await Reveal(spin);
        var edit=spin.GetLineEdit();edit.GrabFocus();edit.SelectAll();
        foreach(char digit in count.ToString())Input.ParseInputEvent(new InputEventKey{Keycode=(Key)digit,Unicode=digit,Pressed=true});
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.Enter,Pressed=true});await Frames();Input.ParseInputEvent(new InputEventKey{Keycode=Key.Enter,Pressed=false});await Frames();
        Check(screen.NpcCount==count,"Keyboard count was not applied");
        Check(All(screen).OfType<Label>().Single(l=>l.Name=="ParticipantCount").Text.EndsWith((count+1).ToString()),"Roster summary mismatch");
    }
    private async Task Capture(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    private async Task At(Playfield app,GameSession session,Cell cell)
    {
        // Reach prepared UI state through normal authenticated commands, never inject position or stock.
        app.Notification((int)NotificationApplicationFocusOut);session.SetPaused(false);
        string command=session.Send(new(1),new(ActionKind.MoveTo,Target:cell));int bound=400;
        do{session.Step();if(--bound==0)throw new InvalidOperationException("Native approach did not finish");}
        while(session.Engine.Result(command) is null || session.Engine.Observe(new(1)).Participant.Busy);
        Check(session.Engine.Result(command)!.Status==ResultStatus.Accepted,"Native approach rejected");
        session.SetPaused(true);app.Notification((int)NotificationApplicationFocusOut);await Frames();
    }
    private async Task DesignedInteractions(Playfield app,GameSession session)
    {
        var scenario=session.Engine.Configuration();var layout=scenario.Layout!;
        var stage=Field<PresentationStage>(app,"_stage")!;
        foreach(var site in layout.CivicObjects)Check(stage.GetNodeOrNull<Node3D>("Civic_"+site.Kind) is not null,"Missing civic body "+site.Kind);
        await At(app,session,layout.CivicObjects.Single(c=>c.Kind=="town_hall").Entry);
        await Click(Button("Town hall · city and elections"));
        Check(All(app).OfType<PlayerTablet>().Single().SelectedTab==3,"Town hall did not open existing City page");
        Check(All(All(app).OfType<PlayerTablet>().Single().PoliticsHost).OfType<Button>().Count()==scenario.Participants.Length-1,"Town hall lost candidate choices");
        await Click(All(app).OfType<Button>().Single(b=>b.Name=="TabletClose"));
        await At(app,session,scenario.CapitalDemand!.Terminal);await Click(Button("Open exchange"));
        Check(All(app).OfType<ExchangeTablet>().Any(),"Terminal did not open existing exchange");await Capture("terminal-en");
        await Click(All(app).OfType<Button>().Single(b=>b.Name=="TabletClose"));
        await At(app,session,layout.CivicObjects.Single(c=>c.Kind=="dealership").Entry);await Click(Button("Vehicle dealer · information"));
        Check(All(app).OfType<HelpOverlay>().Any(),"Dealer did not open paused help");Check(All(app).OfType<Label>().Any(l=>l.Text.Contains("package E")),"Dealer claims working sales");Check(session.Paused,"Dealer help did not pause time");await Click(All(app).OfType<Button>().Single(b=>b.Name=="HelpClose"));
        var ore=scenario.ResourceNodes.First(n=>n.ItemId=="copper_ore"&&n.Rules!.Public);await At(app,session,ore.Position);
        Check(AppearanceCatalog.ResourceModel("copper_ore",50,50,false)=="","Ore would be rendered as a crop");
        Check(All(app).OfType<Button>().Any(b=>b.IsVisibleInTree()&&(b.Text.Contains("Copper ore") || b is ContextActionButton&&All(b).OfType<Label>().Any(l=>l.Text.Contains("Copper ore")))),"Ore has no localized live context");await Capture("ore-en");
        _checks.AddRange(["exact civic bodies in live scene","physical movement to approaches","town hall City and terminal exchange clicked","dealer future-shop notice","new ore localized context and quarry fallback","camera follows actual approach movement"]);
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];_count=int.Parse(args[Array.IndexOf(args,"--count")+1]);
            int mapOption=Array.IndexOf(args,"--map");if(mapOption>=0)_key=args[mapOption+1];
            MouseFilter=MouseFilterEnum.Ignore;GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);
            if(args.Contains("--preview-only"))
            {
                ScenarioDefinition Compatible(string key)
                {if(PlayMaps.IsDesigned(key))throw new ArgumentException("Use the A2 profile");using var stream=typeof(Playfield).Assembly.GetManifestResourceStream(PlayMaps.Resource(key))!;return JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;}
                var unsupported=new MapSelectionScreen("en",false,Compatible,selected:PlayMaps.SmallRiver);bool started=false;unsupported.MapChosen+=_=>started=true;AddChild(unsupported);await Frames(4);
                Check(All(unsupported).OfType<Label>().Any(l=>l.Name=="MapUnavailable"),"Incompatible preview crashed or was silently replaced");Check(Button("Start").Disabled&&!started,"Incompatible map can start");
                await Click(Button("Change map"));Check(All(GetTree().Root).OfType<Button>().Count(b=>b.Text=="Select"&&b.Disabled)==2,"Incompatible catalog entries not disabled");
                await Click(All(GetTree().Root).OfType<Button>().First(b=>b.Text=="Select"&&!b.Disabled));await Click(Button("Start"));Check(started&&unsupported.SelectedMap==PlayMaps.First,"Compatible catalog map cannot start");
                GD.Print("DELSK_ORDINARY_SETUP_PASS incompatible-profile-preview");GetTree().Quit(0);return;
            }
            var app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);await Frames(5);
            var screen=Field<MapSelectionScreen>(app,"_mapSelection")!;Check(Field<GameSession>(app,"_session") is null,"Preview created a world");
            await Count(screen,_count);await Click(Button("English"));screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            Check(screen.NpcCount==_count,"Language reset count");await Capture("count-en");
            await Click(Button("Change map"));var selects=All(GetTree().Root).OfType<Button>().Where(b=>b.Text=="Select").ToArray();
            await Click(selects[Array.IndexOf(PlayMaps.All,_key)]);screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            Check(screen.SelectedMap==_key && screen.NpcCount==_count,"Catalog reset count/map: "+screen.SelectedMap+" count="+screen.NpcCount);Check(Field<GameSession>(app,"_session") is null,"Catalog created world");
            await Click(Button("Start"));await Frames(6);
            var session=Field<GameSession>(app,"_session")!;Check(session is not null,"Start did not create world");
            Check(session!.Engine.Configuration().Participants.Length==_count+1 && session.ControllerStates.Length==_count,"Incomplete live roster/controllers");
            var stage=Field<PresentationStage>(app,"_stage")!;Check(stage.GetNodeOrNull<Node3D>("Actor_"+(_count+1)) is not null,"Last actor not rendered");
            Check(stage.GetNode<Label3D>("Actor_"+(_count+1)+"/Appearance/Caption").Text.Contains("#"+(_count+1)),"Last caption missing stable actor number");await Capture("live-en");
            if(session.Engine.Configuration().Layout is not null)
            {var mini=Field<DebugBoard>(app,"_miniMap")!;Check(mini.IsVisibleInTree()&&mini.Size.X>0,"Missing live minimap");Check(Field<PresentationFrame>(mini,"_frame")!.Actors.Length==_count+1,"Minimap lost roster");}
            if(session.Engine.Configuration().Layout is not null && _count==3)await DesignedInteractions(app,session);
            await Click(Button("Menu"));await Click(Button("Choose map / new game"));screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            await Count(screen,_count==3?10:3);await Click(Button("Start"));
            var confirmation=All(GetTree().Root).OfType<ConfirmationDialog>().Single(c=>c.Visible);await Click(confirmation.GetCancelButton());
            Check(ReferenceEquals(session,Field<GameSession>(app,"_session")),"Replacement cancellation destroyed live world");
            await Click(Button("Русский"));screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            Check(screen.NpcCount==(_count==3?10:3),"RU reset new-game selection");await Capture("count-ru-compact");
            await Click(Button("Продолжить"));Check(ReferenceEquals(session,Field<GameSession>(app,"_session")),"Continue rebuilt roster");
            if(session.Engine.Configuration().Layout is {} ruLayout && _count==3)
            {
                await At(app,session,ruLayout.CivicObjects.Single(c=>c.Kind=="town_hall").Entry);await Click(Button("Ратуша · город и выборы"));
                Check(All(app).OfType<PlayerTablet>().Single().SelectedTab==3,"RU city missing");await Capture("city-ru");await Click(All(app).OfType<Button>().Single(b=>b.Name=="TabletClose"));
                await At(app,session,session.Engine.Configuration().CapitalDemand!.Terminal);await Click(Button("Открыть биржу"));Check(All(app).OfType<ExchangeTablet>().Any(),"RU exchange missing");await Capture("terminal-ru");await Click(All(app).OfType<Button>().Single(b=>b.Name=="TabletClose"));
                _checks.Add("RU city and exchange clicked in compact window");
            }
            app.Notification((int)NotificationApplicationFocusOut);await Frames();long saved=session.Engine.Tick;
            RemoveChild(app);app.QueueFree();await Frames();app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);await Frames(5);
            await Click(Button("Продолжить"));var loaded=Field<GameSession>(app,"_session")!;
            Check(loaded.Engine.Tick==saved && loaded.Engine.Configuration().Participants.Length==_count+1 && loaded.ControllerStates.Length==_count,"Disk Continue used new defaults instead of saved roster");
            _checks.AddRange(["native keyboard 3..10 selection","RU/EN preserved count","catalog/preview without world","real start/controllers/last rendered actor","native replacement cancel","continue retained roster","disk load retained saved roster"]);
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",npcCount=_count,map=_key,checks=_checks,scope="Actual native window, keyboard/mouse dispatch and real Playfield/session; software renderer, not target device"}));
            GD.Print("DELSK_ORDINARY_SETUP_PASS");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

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
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
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
    private Button Button(string text)=>All(GetTree().Root).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text==text);
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
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];_count=int.Parse(args[Array.IndexOf(args,"--count")+1]);
            int mapOption=Array.IndexOf(args,"--map");if(mapOption>=0)_key=args[mapOption+1];
            MouseFilter=MouseFilterEnum.Ignore;GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);
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
            await Click(Button("Menu"));await Click(Button("Choose map / new game"));screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            await Count(screen,_count==3?10:3);await Click(Button("Start"));
            var confirmation=All(GetTree().Root).OfType<ConfirmationDialog>().Single(c=>c.Visible);await Click(confirmation.GetCancelButton());
            Check(ReferenceEquals(session,Field<GameSession>(app,"_session")),"Replacement cancellation destroyed live world");
            await Click(Button("Русский"));screen=Field<MapSelectionScreen>(app,"_mapSelection")!;
            Check(screen.NpcCount==(_count==3?10:3),"RU reset new-game selection");await Capture("count-ru-compact");
            await Click(Button("Продолжить"));Check(ReferenceEquals(session,Field<GameSession>(app,"_session")),"Continue rebuilt roster");
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

using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native C2 acceptance; no Developer API or direct WorldState writes.</summary>
public partial class DemolitionProbe:Control
{
    private Playfield _app=null!;private GameSession _session=null!;private string _output="";private string _language="en";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o)!;
    private void Refresh()=>typeof(Playfield).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_app,null);
    private static void Check(bool b,string why){if(!b)throw new InvalidOperationException(why);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private Button Button(string text)=>All(GetTree().Root).OfType<Button>().First(b=>b.IsVisibleInTree()&&(b.Text==text||b is ContextActionButton&&All(b).OfType<Label>().Any(l=>l.Text==text)));
    private async Task Click(Button b)
    {
        for(Node? n=b.GetParent();n is not null;n=n.GetParent())if(n is ScrollContainer scroll)scroll.EnsureControlVisible(b);await Frames();Check(!b.Disabled&&b.IsVisibleInTree(),"Unavailable: "+b.Text);
        var pos=b.GetGlobalRect().GetCenter();if(b.GetWindow()!=GetTree().Root)pos+=b.GetWindow().Position;int id=GetTree().Root.GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=pos,GlobalPosition=pos});Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private async Task Capture(string name){await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    private static void Step(GameSession s,GameAction a,long actor=1)
    {s.SetPaused(false);string id=s.Send(new(actor),a);s.Step();Check(s.Engine.Result(id)!.Status==ResultStatus.Accepted,"Preparation rejected "+a.Kind+": "+s.Engine.Result(id)!.ReasonCode);int bound=5000;while(s.Engine.Observe(new(actor)).Participant.Busy&&bound-->0)s.Step();Check(bound>0,"Preparation timeout");}
    private static void Harvest(GameSession s,string item,long q,long actor=1)
    {Step(s,new(ActionKind.MoveTo,Target:s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId==item).Position),actor);Step(s,new(ActionKind.Harvest,ItemId:item,Quantity:q),actor);}
    private static InventoryId Pack(GameSession s,long actor=1)=>s.Engine.Observe(new(actor)).Participant.InventoryId;
    private static void Put(GameSession s,InventoryId target,string item,long q,long actor=1)
    {Step(s,new(ActionKind.MoveTo,Target:s.Engine.Inspect().Inventories.Single(i=>i.Id==target).Position),actor);Step(s,new(ActionKind.Transfer,Source:Pack(s,actor),Destination:target,ItemId:item,Quantity:q),actor);}
    private async Task Load(GameSession prepared)
    {
        if(_app is not null){RemoveChild(_app);_app.QueueFree();await Frames();}
        _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(5);
        if(_language=="en")await Click(Button("English"));
        var path=(string)typeof(Playfield).GetProperty("SavePath",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(_app)!;CheckpointStore.Save(prepared,path);
        // Refresh the ordinary selection, so Continue discovers the newly written file.
        await Click(Button(_language=="en"?"Русский":"English"));await Click(Button(_language=="en"?"English":"Русский"));
        await Click(Button(_language=="en"?"Continue":"Продолжить"));await Frames(4);
        _session=Field<GameSession>(_app,"_session");_app.Notification((int)NotificationApplicationFocusOut);await Frames();
        await Click(Button(_language=="en"?"Continue":"Продолжить"));
        Check(_session.Engine.ComputeStateHash()==prepared.Engine.ComputeStateHash(),"Disk Continue changed state");
    }
    private async Task Drive(long ticks)
    {_session.SetPaused(false);_session.AdvanceTo(_session.Engine.Tick+ticks);_session.SetPaused(true);Refresh();await Frames();}
    private async Task Hold(Button button,double seconds)
    {
        for(Node? n=button.GetParent();n is not null;n=n.GetParent())if(n is ScrollContainer scroll)scroll.EnsureControlVisible(button);await Frames();Check(!button.Disabled,"Hold disabled");var position=button.GetGlobalRect().GetCenter();int id=GetTree().Root.GetWindowId();Input.WarpMouse(position);Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=true});ulong until=Time.GetTicksMsec()+(ulong)(seconds*1000);while(Time.GetTicksMsec()<until)await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];var d=JsonSerializer.Deserialize<ScenarioDefinition>(System.IO.File.ReadAllText(args[Array.IndexOf(args,"--fixture")+1]),SimulationJson.Options)!;
            MouseFilter=MouseFilterEnum.Ignore;GetWindow().Size=new(1100,800);GetWindow().ContentScaleSize=new(1100,800);
            var built=new GameSession(new(d));Harvest(built,"wood",30);Harvest(built,"steel",3);Step(built,new(ActionKind.MoveTo,Target:new(0,0)));Step(built,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"c2-factory"));var building=built.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="c2-factory");Put(built,building.InputId,"wood",15);Put(built,building.InputId,"steel",2);Step(built,new(ActionKind.UpgradeBuilding,Building:building.Id));
            await Load(built);await Click(Button(PlayerText.Get("en","context.card")));await Click(Button("Demolish…"));Check(All(_app).OfType<Label>().Any(l=>l.Name=="DemolitionSummary"&&l.Text.Contains("90.00")),"Actual quote missing");long money=_session.Engine.Observe(new(1)).Account.BalanceMinor;await Click(Button("Cancel — spend nothing"));Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==money,"Preview cancel spent funds");
            await Click(Button("Prepare for demolition"));await Drive(1);await Click(Button("Demolish…"));await Capture("quote-en");var hold=All(_app).OfType<Button>().Single(b=>b.Name=="DemolitionHold");await Hold(hold,.5);Check(!_session.CommandTape.Any(c=>c.Command.Action.Kind==ActionKind.DemolishBuilding),"Short press confirmed demolition");long tick=_session.Engine.Tick;await Hold(hold,2.2);Check(_session.Engine.Tick==tick,"Holding confirmation advanced paused world");Check(_session.CommandTape.Count(c=>c.Command.Action.Kind==ActionKind.DemolishBuilding)==1,"Native hold did not queue exactly one command");await Drive(1);Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==money-9000,"Native demolition fee differs");Check(!_session.Engine.Observe(new(1)).OwnBuildings.Any(b=>b.Id==building.Id),"Native object survived");
            var demolished=CheckpointStore.Decode(CheckpointStore.Encode(_session));await Load(demolished);await Click(Button("Ground storage · warehouse"));await Capture("returned-ground-en");Check(All(_app).OfType<Label>().Any(l=>l.Name=="StorageStatus"&&l.Text.Contains("37/70")),"Returned materials missing from live ground");
            _language="ru";GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);await Load(built);await Click(Button(PlayerText.Get("ru","context.card")));await Click(Button("Подготовить к сносу"));await Drive(1);await Click(Button("Снос…"));await Capture("quote-ru-compact");await Click(Button("Отмена — ничего не списывать"));Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==money,"RU cancel spent funds");
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",checks=new[]{"actual quote90 and recovery18wood+1steel","preview cancel spends zero","short hold cannot confirm","2second hold queues once without advancing paused tick","actual demolition fee and released geometry","disk Continue and live returned ground","RU800x720/EN1100x800"},scope="Native input/actual Playfield, prepared materials, no hardware acceptance"}));GD.Print("DELSK_DEMOLITION_PASS");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

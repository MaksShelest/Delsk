using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native C2 acceptance; no Developer API or direct WorldState writes.</summary>
public partial class WorkshopProbe:Control
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
            var built=new GameSession(new(d));var e=built.Engine.Observe(new(1)).OwnEnterprises.Single();
            Harvest(built,"plank",6);Put(built,e.InputId,"plank",6);Harvest(built,"steel",1);Put(built,e.InputId,"steel",1);
            await Load(built);await Click(Button(PlayerText.Get("en","context.card")));long outfitCash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
            await Click(All(_app).OfType<Button>().Single(b=>b.Name=="Workshop_outfit"));await Drive(1);
            Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==outfitCash-15000&&_session.Engine.Observe(new(1)).OwnEnterprises.Single().Installations.Length==1,"Native outfit did not pay/start exactly once");
            var installing=CheckpointStore.Decode(CheckpointStore.Encode(_session));await Load(installing);await Drive(60);Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Length==2&&_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Sum(w=>w.Staff)==5,"Montage restore created workers or lost a place");
            built=CheckpointStore.Decode(CheckpointStore.Encode(_session));
            Harvest(built,"wood",8);Put(built,e.InputId,"wood",8);Harvest(built,"grain",8);Put(built,e.InputId,"grain",8);
            long second=built.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Single(w=>w.Id!=e.Id.Value).Id;
            Step(built,new(ActionKind.SetRecipe,Enterprise:e.Id,WorkshopId:second,RecipeId:"d1-bread"));Step(built,new(ActionKind.AssignWorkshopWorkers,Enterprise:e.Id,WorkshopId:second,Group:new(8),Quantity:2));
            await Load(built);await Click(Button(PlayerText.Get("en","context.card")));
            Button Named(string name)=>All(_app).OfType<Button>().Single(b=>b.Name==name);
            Check(All(_app).OfType<Label>().Any(l=>l.Name=="WorkshopSummary"&&l.Text.Contains("2/2")),"Two real equipped places missing");
            long tick=_session.Engine.Tick;await Click(Named("Workshop_work_8"));Check(_session.Engine.Tick==tick,"Paused control advanced time");await Drive(1);
            await Click(Named("Workshop_work_"+second));await Drive(1);var lines=_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops;Check(lines.All(w=>w.BatchId!=0),"Both batches did not start");await Capture("parallel-en");
            await Click(Named("Workshop_work_"+second));await Drive(1);long stopped=_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Single(w=>w.Id==second).WorkDone;
            await Drive(3);lines=_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops;Check(lines.Single(w=>w.Id==second).WorkDone==stopped&&lines.Single(w=>w.Id==8).WorkDone>stopped,"Stop paused the wrong/shared batch");
            await Click(Named("Workshop_staff_8_"+second));await Drive(1);Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Single(w=>w.Id==second).Staff==3,"Native staff transfer failed");
            Check(Named("Workshop_recipe_"+second+"_d1-plank").Disabled,"Recipe switch allowed during paused WIP");
            await Click(Named("Workshop_work_"+second));await Drive(1);var saved=CheckpointStore.Decode(CheckpointStore.Encode(_session));await Load(saved);await Click(Button(PlayerText.Get("en","context.card")));Check(_session.Engine.ComputeStateHash()==saved.Engine.ComputeStateHash(),"WIP Continue differs");
            _language="ru";GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);await Load(saved);await Click(Button(PlayerText.Get("ru","context.card")));Check(All(_app).OfType<Label>().Any(l=>l.Name=="WorkshopStatus_"+second&&l.Text.Contains("3/5")),"RU live staff missing");await Capture("parallel-ru-compact");
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",checks=new[]{"native paid outfit and disk Continue during montage","two real equipped workshop cards","native start two parallel batches","paused controls do not advance world","stop one preserves WIP while other advances","native conservative staff transfer","recipe switch disabled during WIP","disk Continue restores parallel WIP","RU800x720/EN1100x800"},scope="Actual Playfield and ordinary input/validators; prepared paid-place fixture, no hardware acceptance"}));GD.Print("DELSK_WORKSHOPS_PASS");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

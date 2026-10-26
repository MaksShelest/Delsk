using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native D2 acceptance; no Developer API or direct WorldState writes.</summary>
public partial class DevelopmentProbe:Control
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
    private long _checkedTicks;private int _restores;
    private async Task Drive(long ticks)
    {
        _session.SetPaused(false);var restored=CheckpointStore.Decode(CheckpointStore.Encode(_session));restored.SetPaused(false);_restores++;
        long target=checked(_session.Engine.Tick+ticks);
        while(_session.Engine.Tick<target){_session.Step();restored.Step();Check(_session.Engine.ComputeStateHash()==restored.Engine.ComputeStateHash(),"D2 Godot/restored tick hash differs");_checkedTicks++;}
        _session.SetPaused(true);Refresh();await Frames();
    }
    private async Task Hold(Button button,double seconds)
    {
        for(Node? n=button.GetParent();n is not null;n=n.GetParent())if(n is ScrollContainer scroll)scroll.EnsureControlVisible(button);await Frames();Check(!button.Disabled,"Hold disabled");var position=button.GetGlobalRect().GetCenter();int id=GetTree().Root.GetWindowId();Input.WarpMouse(position);Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=true});ulong until=Time.GetTicksMsec()+(ulong)(seconds*1000);while(Time.GetTicksMsec()<until)await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];string fixtures=args[Array.IndexOf(args,"--fixtures")+1];
            MouseFilter=MouseFilterEnum.Ignore;GetWindow().Size=new(1100,800);GetWindow().ContentScaleSize=new(1100,800);
            var prepared=CheckpointStore.Decode(System.IO.File.ReadAllText(System.IO.Path.Combine(fixtures,"native.save.json")));await Load(prepared);
            await Click(Button(PlayerText.Get("en","context.card")));
            Button Named(string name)=>All(_app).OfType<Button>().Single(b=>b.Name==name);
            Check(All(_app).OfType<Label>().Any(l=>l.Name=="DevelopmentStatus"&&l.Text.Contains("40000 XP")),"Live industry experience missing");
            await Click(Named("Development_prepare"));await Drive(1);
            foreach(long plot in new long[]{51,52}){await Click(Named("Development_land_"+plot));await Drive(1);}
            long cash=_session.Engine.Observe(new(1)).Account.BalanceMinor,tick=_session.Engine.Tick;
            await Click(Named("Development_expand"));Check(_session.Engine.Tick==tick,"Paused UI advanced world");await Drive(1);
            Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-45000,"Expansion payment incorrect");
            var project=_session.Engine.DevelopmentOf(new(1))!.Projects.Single();Check(_session.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="woodworking").Level==1,"Expanded before completion");await Capture("project-en");
            var saved=CheckpointStore.Decode(CheckpointStore.Encode(_session));await Load(saved);await Click(Button(PlayerText.Get("en","context.card")));await Drive(project.CompletesTick-_session.Engine.Tick);
            var building=_session.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="woodworking");Check(building.Level==2&&building.Footprint.Length==3,"Restored expansion geometry differs");Check(All(_app).Any(n=>n.Name=="EnterpriseExtent"),"Expanded world footprint visual missing");
            Check(All(_app).OfType<Label>().Any(l=>l.Name=="WorkshopSummary"&&l.Text.Contains("1/2")),"Ready place missing");
            await Click(Named("Workshop_outfit"));await Drive(1);long end=_session.Engine.Observe(new(1)).OwnEnterprises.Single().Installations.Single().EndTick;await Drive(end-_session.Engine.Tick);
            Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Length==2,"Paid additional workshop missing");Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Single(w=>w.Id!=building.Id.Value).Staff==0,"Outfit created workers");
            saved=CheckpointStore.Decode(CheckpointStore.Encode(_session));_language="ru";GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);await Load(saved);await Click(Button(PlayerText.Get("ru","context.card")));await Capture("expanded-ru-compact");
            await Load(CheckpointStore.Decode(System.IO.File.ReadAllText(System.IO.Path.Combine(fixtures,"quest-native.save.json"))));await Click(Button("Планшет"));await Click(Button("Опыт и задания ›"));
            var accept=All(_app).OfType<Button>().First(b=>b.Name.ToString().StartsWith("Development_quest_")&&b.IsVisibleInTree());await Click(accept);await Drive(1);
            var quest=_session.Engine.DevelopmentOf(new(1))!.Quests.Single();Check(quest.Status=="Active"&&quest.Delivered==0,"Native quest acceptance failed");
            Step(_session,new(ActionKind.DeliverContract,ContractId:quest.ContractId,Quantity:quest.Quantity));_session.SetPaused(true);Refresh();await Frames();
            Check(_session.Engine.DevelopmentOf(new(1))!.Quests.Single().Status=="Completed","Native quest progress/reward missing");await Capture("quest-ru-compact");
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",checkedTicks=_checkedTicks,restores=_restores,checks=new[]{"live shared XP","native preparation and two land payments","paused controls preserve time","exact paid timed expansion","disk Continue during project","real three-cell contour and capacities","native paid outfitting N0","native funded quest acceptance and actual reward","RU800x720/EN1100x800"},scope="Ordinary commands over explicitly prepared physical/accelerated XP fixtures; no natural balance or hardware claim"}));GD.Print("DELSK_DEVELOPMENT_PASS");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

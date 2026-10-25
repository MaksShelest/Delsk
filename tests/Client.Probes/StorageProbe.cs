using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native C1 acceptance; no Developer API or direct WorldState writes.</summary>
public partial class StorageProbe:Control
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
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];var d=JsonSerializer.Deserialize<ScenarioDefinition>(System.IO.File.ReadAllText(args[Array.IndexOf(args,"--fixture")+1]),SimulationJson.Options)!;
            MouseFilter=MouseFilterEnum.Ignore;GetWindow().Size=new(1100,800);GetWindow().ContentScaleSize=new(1100,800);
            var ground=new GameSession(new(d));var pile=new InventoryId(ground.Engine.StorageOf(new(1)).Ground.Single().InventoryId);Harvest(ground,"plank",10);Put(ground,pile,"plank",10);Harvest(ground,"grain",5);Step(ground,new(ActionKind.MoveTo,Target:new(0,0)));
            await Load(ground);await Click(Button("Ground storage · warehouse"));Check(_session.Paused,"Tablet did not pause");
            var status=All(_app).OfType<Label>().Single(l=>l.Name=="StorageStatus");Check(status.Text.Contains("10/70"),"Ground stock/capacity not real");
            await Click(Button("Store: grain"));Check(Field<string>(_app,"_trackedId") is not null,"Native button did not queue command");long tick=_session.Engine.Tick;await Frames(10);Check(_session.Engine.Tick==tick,"Paused tablet aged world");await Drive(2);Check(All(_app).OfType<Label>().Single(l=>l.Name=="StorageStatus").Text.Contains("11/70"),"Queued stock was not transferred");await Capture("ground-en");
            await Click(Button("Build warehouse"));await Drive(1);Check(_session.Engine.StorageOf(new(1)).Construction.Length==1,"Native construction button failed");await Capture("construction-en");
            var work=CheckpointStore.Decode(CheckpointStore.Encode(_session));await Load(work);var job=_session.Engine.StorageOf(new(1)).Construction.Single();await Drive(job.CompletesTick-_session.Engine.Tick);
            Check(_session.Engine.Observe(new(1)).OwnBuildings.Any(b=>b.Kind==BuildingKind.Warehouse),"Loaded construction did not complete");_checks.AddRange(["native ground deposit and exact capacity","paused command queue and paused tick","native build/cost/time","disk Continue resumes construction"]);
            var receiving=d with{Participants=d.Participants.Select(p=>p with{Position=d.Participants.Single(other=>other.Id!=p.Id).Position}).ToArray(),Plots=d.Plots!.Select(p=>p with{OwnerId=p.OwnerId is {} owner?3-owner:null,ShopParticipantId=p.ShopParticipantId is {} shop?3-shop:null}).ToArray(),Buildings=d.Buildings!.Select(b=>b with{OwnerId=3-b.OwnerId}).ToArray()};
            var full=new GameSession(new(receiving));var receiver=full.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;Harvest(full,"grain",200);Put(full,receiver,"grain",200);Harvest(full,"plank",10,2);
            Step(full,new(ActionKind.CreateBuyOffer,Destination:receiver,ItemId:"plank",Quantity:10,UnitPriceMinor:10));Step(full,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:full.Engine.Observe(new(1)).BuyOffers.Single().Id,Quantity:10),2);full.AdvanceTo(full.Engine.Tick+6);var contract=full.Engine.Inspect().DeliveryContracts.Single();Step(full,new(ActionKind.MoveTo,Target:new(0,2)),2);Step(full,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10),2);
            await Load(full);Check(All(_app).OfType<Label>().Any(l=>l.Name=="StorageWarning"&&l.IsVisibleInTree()&&l.Text.Contains("First loss")),"Own overflow HUD warning missing");await Capture("overflow-warning-en");
            await Click(Button("Open storage card"));Check(All(_app).OfType<Label>().Any(l=>l.Text.Contains("210/200")),"Overflow total missing");await Capture("overflow-en");await Click(Button("Rescue overflow: "+PlayerText.Item("en","plank")));await Drive(2);Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==Pack(_session)).Stock.Any(s=>s.ItemId=="plank"&&s.Quantity==1),"Rescue did not move a physical unit");_checks.Add("native real warehouse overflow/rescue/validator and own HUD warning");
            _language="ru";GetWindow().Size=new(800,720);GetWindow().ContentScaleSize=new(800,720);await Load(ground);await Click(Button("Хранение на земле · склад"));await Capture("ground-ru-compact");Check(All(_app).OfType<Label>().Any(l=>l.Text.Contains("10/70")),"RU compact capacity missing");await Click(Button("Выложить: зерно"));await Drive(2);Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==pile).Stock.Any(s=>s.ItemId=="grain"&&s.Quantity==1),"RU deposit failed");_checks.Add("RU/EN native controls at 1100x800 and 800x720");
            var expected=System.IO.File.ReadAllLines(args[Array.IndexOf(args,"--metrics")+1]).Skip(1).Select(line=>line.Split(',')).ToDictionary(row=>long.Parse(row[0]),row=>row[7]);int hashes=0;
            var shared=new GameSession(new(d));shared.TickCompleted+=state=>{Check(expected[state.Engine.Tick]==state.Engine.ComputeStateHash(),"C1 Godot/runner hash differs at "+state.Engine.Tick);++hashes;};ScriptedScenarios.StorageC1(shared,d);Check(hashes==expected.Count,"C1 hash coverage incomplete");_checks.Add(hashes+" C1 tick hashes equal real runner");
            System.IO.File.WriteAllText(_output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Prepared sources, actual native input and Playfield; no target-device/D progression claim"}));GD.Print("DELSK_STORAGE_PASS");GetTree().Quit(0);
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

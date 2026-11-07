using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native map and purchase acceptance. Never shipped with the ordinary client.</summary>
public partial class I6C2Probe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static object? Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static IEnumerable<Node> All(Node root){yield return root;foreach(var c in root.GetChildren())foreach(var n in All(c))yield return n;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().First(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private ContextActionsPanel Panel=>Field<ContextActionsPanel>(_app,"_contextActions");
    private PlayerView View=>PlayerDesk.Read(_session.Engine,new(1));
    private void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);_checks.Add(text);GD.Print("I6C2 "+text);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"capture "+name);}
    private void Refresh()=>Call(_app,"Refresh");
    private async Task Click(string name)
    {
        var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();Check(!button.Disabled,"enabled "+name);
        var p=button.GetGlobalRect().GetCenter();int w=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=w,Position=p,GlobalPosition=p});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=w,Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=w,Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private void Tick(int n=1)
    {_session.SetPaused(false);for(int i=0;i<n;i++)_session.Step();_session.SetPaused(true);Refresh();}
    private void Command(GameAction action)
    {
        _session.SetPaused(false);string id=_session.Send(new(1),action);_session.Step();
        Check(_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"Core "+action.Kind+" "+_session.Engine.Result(id)?.ReasonCode);
        int limit=250;while(View.Participant.Busy&&limit-->0)_session.Step();
        Check(limit>0,"bounded real movement/work");_session.SetPaused(true);Refresh();
    }
    private void Move(Cell at){if(View.Participant.Position!=at)Command(new(ActionKind.MoveTo,Target:at));}
    private string SavePath=>typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;
    private async Task Load(GameSession session)
    {
        CheckpointStore.Save(session,SavePath);Call(_app,"Load");Call(_app,"ContinueGame");await Frames(8);
        _app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);
        _session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();
    }
    private void Camera(Vector3 center,float size,bool chrome=false)
    {
        var stage=Field<PresentationStage>(_app,"_stage");Set(stage,"_follow",false);
        var camera=Field<Camera3D>(stage,"_camera");camera.Projection=Camera3D.ProjectionType.Orthogonal;
        camera.Size=size;camera.Position=center+new Vector3(0,35,20);camera.LookAt(center,Vector3.Up);
        Field<Control>(_app,"_playChrome").Visible=chrome;
    }
    private async Task Maps()
    {
        var menu=Field<MapSelectionScreen>(_app,"_mapSelection");
        Check(menu.SelectedMap==PlayMaps.CompactValley,"default Small valley");await Click("ChooseMap");
        Check(All(_app).Count(n=>n.Name.ToString().StartsWith("MapCard_"))==2,"exactly two current map cards");
        Check(All(_app).OfType<Label>().Any(l=>l.Text=="Лесной край"),"localized Woodland Region name");
        await Shot("05-map-catalog");await Click("SelectMap_second");
        Check(!Named<Button>("StartGame").Disabled,"Woodland ordinary start enabled");
        Check(Named<SpinBox>("NpcCount").MaxValue==3&&menu.NpcCount==3,"Woodland roster capped at three NPCs");
        await Click("StartGame");await Frames(8);_app.SetProcess(false);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();
        Check(_session.Engine.Configuration().Id==PlayMaps.WoodlandI6C4ScenarioId,"ordinary start uses versioned Woodland world");
        Check(_session.ControllerStates.Length==3,"ordinary start attaches three independent NPCs");
        Check(_session.Engine.Configuration().MapHeight==15,"no appended lower equipment corridor");
        Camera(new(8.5f,0,7),17.5f);await Shot("03-woodland-overview");
        Camera(new(4.5f,0,3),9);await Shot("04-woodland-town-hall");
        Field<Control>(_app,"_playChrome").Show();
        Move(_session.Engine.Configuration().ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public).Position);
        Command(new(ActionKind.Harvest,ItemId:"wood",Quantity:2));Check(View.Inventories.Single(i=>i.Id==View.Participant.InventoryId).Stock.Any(i=>i.ItemId=="wood"&&i.Quantity>=2),"ordinary public harvesting");
        var lot=_session.Engine.Configuration().Plots!.First(p=>!p.IsShopSite);Move(lot.Position);Command(new(ActionKind.BuyPlot,Plot:new(lot.Id)));
        Check(View.Plots.Single(p=>p.Id.Value==lot.Id).OwnerId==new ParticipantId(1),"ordinary legal plot ownership");
        var valley=(OrdinaryGameResolution)Call(_app,"ResolveNewGame",PlayMaps.CompactValley,3)!;
        await Load(new(new(valley.Scenario)));Check(_session.Engine.Configuration().Id==PlayMaps.CompactValleyScenarioId,"new Small valley scenario identity");
        Camera(new(15.5f,0,12),27);await Shot("01-valley-overview");
        Camera(new(14,0,20),15);await Shot("02-valley-center-industry");
        Check(_session.Engine.Configuration().Layout!.IndustrialLots.Length==15,"five by three full industrial lots");
    }
    private async Task Purchase()
    {
        // Money is prepared only for the purchase UI. Model prices and all Core rules are original.
        var r=(OrdinaryGameResolution)Call(_app,"ResolveNewGame",PlayMaps.Second,3)!;
        var d=r.Scenario with{Participants=r.Scenario.Participants.Select(p=>p.Id==1?p with{CashMinor=1000000}:p).ToArray()};
        await Load(new(new(d)));var listing=View.Fleet!.Stock.First(v=>v.ModelId=="sputnik20");
        Move(listing.Position);Panel.Reopen();Refresh();await Frames();
        var offer=Panel.Selection.Choices.First(c=>c.Command?.Kind==ActionKind.BuyVehicle&&c.Command.VehicleId==listing.Id);
        var model=View.Fleet.Models.Single(m=>m.Id==listing.ModelId);var button=Named<Button>(ContextActionsPanel.ButtonName(offer.Key));
        Check(button.Text.Contains(model.Name)&&button.Text.Contains(PlayerText.Money(model.PriceMinor)),"RU button shows concrete model and actual price");
        Check(View.Participant.Position!=View.Fleet.Dealer,"local offer works away from the dealership entrance");
        Camera(new(6.5f,0,3),7,true);await Shot("06-concrete-car-purchase");
        int tape=_session.CommandTape.Length;await Click(ContextActionsPanel.ButtonName(offer.Key));
        Call(_app,"ConfirmContextChoice",offer);Check(_session.CommandTape.Length==tape+1,"pending suppresses repeated vehicle purchase");
        Tick(6);await Frames();Check(_session.Engine.InspectFleet()!.Purchases.Count(p=>p.VehicleId==listing.Id)==1,"one purchase receipt for the selected instance");
        Check(_session.Engine.Ledger().Count(l=>l.Reason=="VehiclePurchase")==1,"one actual vehicle charge");
        Check(!Panel.Selection.Choices.Any(c=>c.Command?.Kind==ActionKind.BuyVehicle&&c.Command.VehicleId==listing.Id),"sold listing disappears from local context");
        Command(new(ActionKind.BoardVehicle,VehicleId:listing.Id));Command(new(ActionKind.MoveTo,Target:new(12,5)));
        Check(View.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==listing.Id).Position==new Cell(12,5),"purchased car physically leaves its parking place");
        Command(new(ActionKind.LeaveVehicle));Move(View.Fleet.Dealer);
        Set(_app,"_language","en");Call(_app,"RebuildChrome");Refresh();await Frames();
        var english=Panel.Selection.Choices.First(c=>c.Command?.Kind==ActionKind.BuyVehicle);
        var englishButton=Named<Button>(ContextActionsPanel.ButtonName(english.Key));
        Check(englishButton.Text.Contains("Buy for")&&englishButton.Text.Contains(PlayerText.Money(View.Fleet.Models.Single(m=>View.Fleet.Stock.Single(s=>s.Id==english.Command!.VehicleId).ModelId==m.Id).PriceMinor)),"EN button retains name and catalog price");
        Check(_session.Engine.AccessFor(new(1),english.Command!).Allowed,"entrance purchase access retained");
        GetWindow().Size=new(960,440);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(8);
        var bounds=Panel.GetGlobalRect();Check(bounds.Position.X>=0&&bounds.End.X<=960&&bounds.End.Y<=440,"compact purchase dock stays inside viewport");
        var hash=_session.Engine.ComputeStateHash();var restored=CheckpointStore.Decode(CheckpointStore.Encode(_session));
        Check(hash==restored.Engine.ComputeStateHash(),"new world preserves bought car and physical route on load");_session.Engine.ValidateInvariants();
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];System.IO.Directory.CreateDirectory(_output);
            GetWindow().Size=new(1440,1024);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(10);_app.SetProcess(false);
            Set(_app,"_language","ru");Call(_app,"ShowMaps");await Frames();await Maps();await Purchase();
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Real Godot ordinary map/menu/harvest/land/purchase; only purchase money prepared; Linux software GL, synthetic mouse, no device claim"},new JsonSerializerOptions{WriteIndented=true}));
            _app.QueueFree();await Frames();GetTree().Quit();
        }
        catch(Exception e){try{await Shot("failure");}catch{}System.IO.File.WriteAllText(_output+"/failure.txt",e.ToString());GD.PrintErr(e);GetTree().Quit(1);}
    }
}

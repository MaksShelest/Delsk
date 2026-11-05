using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;

/// <summary>Bounded I6-A revision acceptance. One prepared Small valley, three ordinary NPCs; never shipped.</summary>
public partial class I6ATabletProbe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="",_case="all";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static void Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static IEnumerable<Node> All(Node n){yield return n;foreach(var child in n.GetChildren())foreach(var item in All(child))yield return item;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().First(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private PlayerView View=>PlayerDesk.Read(_session.Engine,new(1));
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks.Add(reason);GD.Print("I6A "+reason);}
    private async Task Frames(int n=2){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Refresh()=>Call(_app,"Refresh");
    private void Drive(int ticks=1){_session.SetPaused(false);for(int i=0;i<ticks;i++)_session.Step();_session.SetPaused(true);Refresh();}
    private async Task FinishTransfer()
    {
        Drive();int bound=100;while((View.Participant.Busy||View.Transfers.Length>0)&&bound-->0)Drive();Check(bound>0,"bounded real transfer completion");await Frames();
        var result=_session.Engine.Result(Field<string>(_app,"_trackedId"));Check(result?.Status==ResultStatus.Accepted,"Core accepted transfer: "+result?.ReasonCode);
    }
    private void Prepare(GameAction action)
    {
        _session.SetPaused(false);string id=_session.Send(new(1),action);_session.Step();
        Check(_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"ordinary preparation "+action.Kind+" "+_session.Engine.Result(id)?.ReasonCode);
        int limit=1500;while(_session.Engine.Observe(new(1)).Participant.Busy&&limit-->0)_session.Step();Check(limit>0,"bounded preparation "+action.Kind);
    }
    private async Task Key(Key key)
    {
        int window=GetWindow().GetWindowId();Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=key,PhysicalKeycode=key,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=key,PhysicalKeycode=key,Pressed=false});await Frames();
    }
    [DllImport("libX11.so.6")]private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")]private static extern byte XKeysymToKeycode(nint display,nuint keysym);
    [DllImport("libX11.so.6")]private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")]private static extern int XCloseDisplay(nint display);
    [DllImport("libXtst.so.6")]private static extern int XTestFakeKeyEvent(nint display,uint keycode,int pressed,nuint delay);
    [DllImport("libX11.so.6")]private static extern int XSetInputFocus(nint display,nuint window,int revert,nuint time);
    [DllImport("libXtst.so.6")]private static extern int XTestFakeMotionEvent(nint display,int screen,int x,int y,nuint delay);
    [DllImport("libXtst.so.6")]private static extern int XTestFakeButtonEvent(nint display,uint button,int pressed,nuint delay);
    private async Task PopupClick(Control control)
    {
        nint display=XOpenDisplay(0);if(display==0)throw new InvalidOperationException("Owned X11 display unavailable");
        try
        {
            var point=control.GetGlobalRect().GetCenter()+GetWindow().Position;
            XSetInputFocus(display,(nuint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle,GetWindow().GetWindowId()),2,0);
            XTestFakeMotionEvent(display,-1,(int)point.X,(int)point.Y,0);XFlush(display);await Frames(3);
            XTestFakeButtonEvent(display,1,1,0);XFlush(display);await Frames(2);XTestFakeButtonEvent(display,1,0,0);XFlush(display);await Frames(3);
        }
        finally{XCloseDisplay(display);}
    }
    private async Task PopupKey(uint keysym)
    {
        nint display=XOpenDisplay(0);if(display==0)throw new InvalidOperationException("Owned X11 display unavailable");
        try{byte code=XKeysymToKeycode(display,keysym);XTestFakeKeyEvent(display,code,1,0);XFlush(display);await Frames();XTestFakeKeyEvent(display,code,0,0);XFlush(display);await Frames();}
        finally{XCloseDisplay(display);}
    }
    private async Task RawClick(Control control,bool touch=false,bool refreshDown=false)
    {
        var point=control.GetGlobalRect().GetCenter();Check(GetViewportRect().HasPoint(point),"visible native hit "+control.Name);int window=GetWindow().GetWindowId();
        if(touch)Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=true});
        else {Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});}
        await Frames(1);if(refreshDown){Refresh();await Frames(1);}
        if(touch)Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=false});
        else Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});
        await Frames();Refresh();await Frames();
    }
    private async Task Click(string name,bool touch=false,bool refreshDown=false)
    {var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();Check(!button.Disabled,"available "+name);await RawClick(button,touch,refreshDown);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"captured "+name);}
    private void PlayerBounds()
    {
        var panel=Named<TabletPanel>("GameTablet");var close=Named<Button>("TabletClose");var pause=Named<CheckButton>("TabletTime");
        Check(GetViewportRect().Encloses(close.GetGlobalRect())&&GetViewportRect().Encloses(pause.GetGlobalRect()),"close and pause inside viewport");
        Check(Named<ScrollContainer>("TabletScroll").VerticalScrollMode==ScrollContainer.ScrollMode.Disabled,"no outer player scrolling");
        Check(panel.GetGlobalRect().End.X<=GetWindow().Size.X,"player frame fits safe width");
    }
    private async Task OpenPlayer(string language,Vector2I size)
    {
        Call(_app,"CloseTablet");GetWindow().Size=size;GetWindow().ContentScaleSize=size;Set(_app,"_language",language);Call(_app,"RebuildChrome");await Frames();Call(_app,"OpenPlayerTablet");await Frames();
        Field<PlayerTablet>(_app,"_playerTablet").OpenSection("overview");await Frames();PlayerBounds();
    }
    private async Task Navigation()
    {
        await OpenPlayer("ru",new(1100,720));var player=Field<PlayerTablet>(_app,"_playerTablet");
        var nav=Named<ScrollContainer>("PlayerNavigation");var buttons=All(nav).OfType<Button>().Where(b=>b.Name.ToString().StartsWith("PlayerNav_")).ToArray();
        Check(buttons.All(b=>Math.Abs(b.Size.X-buttons[0].Size.X)<1&&Math.Abs(b.Size.Y-40)<1),"all navigation buttons have equal width and height");
        Check(nav.Size.X is >=200 and <=230,"navigation column is 200–230 logical pixels");
        await Shot("revision-01-player-expanded-ru-1100");string hash=_session.Engine.ComputeStateHash();long tick=_session.Engine.Tick;
        await Click("PlayerGroup_holdings",refreshDown:true);await Click("PlayerGroup_economy");Check(player.CurrentSection=="overview","collapsing current group retains page");
        for(int i=0;i<3;i++)Refresh();await Frames();Check(!All(player).OfType<Button>().Single(b=>b.Name=="PlayerNav_overview").IsVisibleInTree(),"collapsed group survives ordinary Refresh");
        await Shot("revision-02-player-collapsed-ru-1100");
        player.OpenSection("account");await Frames();Check(Named<Button>("PlayerNav_account").IsVisibleInTree(),"deep link expands its own group");
        Check(!All(player).OfType<Button>().Single(b=>b.Name=="PlayerNav_overview").IsVisibleInTree(),"deep link leaves other collapsed groups alone");
        var city=Named<Button>("PlayerGroup_city");UiNavigation.Reveal(city);await Frames();city.GrabFocus();await Key(Godot.Key.Space);Check(!city.ButtonPressed,"native keyboard collapses group");await Key(Godot.Key.Space);Check(city.ButtonPressed,"native keyboard expands group");
        await Click("PlayerGroup_information",touch:true);Check(!Named<Button>("PlayerGroup_information").ButtonPressed,"native touch collapses group");await Click("PlayerGroup_information",touch:true);Check(Named<Button>("PlayerGroup_information").ButtonPressed,"native touch expands group");
        player.OpenSection("overview");await Frames();await Click("PlayerNav_fleet");Check(player.CurrentSection=="fleet","real fleet page");
        Check(hash==_session.Engine.ComputeStateHash()&&tick==_session.Engine.Tick,"navigation does not advance or change paused world");
        Call(_app,"CloseTablet");Call(_app,"OpenEnterpriseTablet",8L);await Frames();var direct=Field<HouseholdObjectCard>(_app,"_enterpriseTablet");
        Check(All(direct).OfType<WorkshopPanel>().Any()&&All(direct).OfType<DevelopmentPanel>().Any(),"map enterprise uses original workshop and development panels");
        Check(!All(direct).Any(n=>n.GetType().Name=="CompactProductionTablet"),"rejected compact enterprise removed");
        var theme=Named<TabletPanel>("GameTablet").Theme;
        await Click("TabletClose");Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection("holdings");await Frames();await Click("HoldingOpen_8");
        Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Object,"owned production opens same ordinary object path");
        Check(Named<TabletPanel>("GameTablet").Theme==theme&&Field<HouseholdObjectCard>(_app,"_enterpriseTablet").Theme is null,"both opening paths retain old production theme");

    }
    private async Task Transfers()
    {
        Call(_app,"CloseTablet");var e=View.Enterprises.Single();var b=View.Buildings.Single();var config=_session.Engine.Configuration();
        // Only bounded fixture preparation: ordinary harvested wood and six real batches.
        var wood=config.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        Prepare(PlayerDesk.Move(wood.X,wood.Z));Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:4));Prepare(PlayerDesk.Move(b.Position.X,b.Position.Z));Prepare(PlayerDesk.Transfer(View.Participant.InventoryId.Value,e.InputId.Value,"wood",4));
        int guard=500;_session.SetPaused(false);while(Stock(e.OutputId,"plank")<24&&guard-->0)_session.Step();Check(guard>0,"six actual prepared batches complete within 500 ticks");Prepare(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:e.Workshops[0].Id,Enabled:false));_session.SetPaused(true);Refresh();
        Call(_app,"OpenEnterpriseTablet",8L);await Frames();await Shot("revision-03-production-restored-ru-1100");await Click("HouseholdGroup_Transfer");
        long output=Stock(e.OutputId,"plank");await Click("HouseholdTransfer_take_"+e.OutputId.Value+"_plank_one");await FinishTransfer();Check(Stock(e.OutputId,"plank")==output-1,"one-unit transfer preserved");
        Named<SpinBox>("HouseholdTransferQuantity").Value=3;Refresh();await Frames();await Click("HouseholdTransfer_take_"+e.OutputId.Value+"_plank_selected");await FinishTransfer();Check(Stock(View.Participant.InventoryId,"plank")==4,"selected quantity transfer preserved");
        SelectItem("EnterprisePutItem","plank");await Click("EnterprisePutAll",refreshDown:true);await FinishTransfer();Check(Stock(View.Participant.InventoryId,"plank")==0,"production Store all uses selected item");
        Named<SpinBox>("HouseholdTransferQuantity").Value=2;Refresh();await Frames();await Click("HouseholdTransfer_take_"+e.OutputId.Value+"_plank_selected");await FinishTransfer();
        Call(_app,"CloseTablet");var grain=config.ResourceNodes.First(n=>n.ItemId=="grain"&&n.Rules!.Public).Position;Prepare(PlayerDesk.Move(grain.X,grain.Z));Prepare(new(ActionKind.Harvest,ItemId:"grain",Quantity:11));Prepare(PlayerDesk.Move(b.Position.X,b.Position.Z));_session.SetPaused(true);Refresh();Call(_app,"OpenEnterpriseTablet",8L);await Frames();
        Check(Stock(e.OutputId,"plank")==18&&View.Inventories.Single(i=>i.Id==View.Participant.InventoryId).OccupiedVolume==13,"real boundary fixture has 18 planks and 7 free backpack volume");
        Check(Named<Button>("EnterpriseTakeAll").Text.Contains("(7)"),"Take all previews actual capacity-limited 7");
        int tape=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);long tick=_session.Engine.Tick;await Click("EnterpriseTakeAll",refreshDown:true);await RawClick(Named<Button>("EnterpriseTakeAll"));Check(_session.Engine.Tick==tick,"all transfer remains queued while paused");await FinishTransfer();
        Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==tape+1,"double press sends exactly one transfer");Check(Stock(e.OutputId,"plank")==11&&View.Inventories.Single(i=>i.Id==View.Participant.InventoryId).OccupiedVolume==20,"Core transferred 7 of 18, never exceeded backpack capacity");
        Check(Named<Button>("EnterpriseTakeAll").Disabled&&Named<Button>("EnterpriseTakeAll").TooltipText.Length>0,"full backpack disables shortcut with reason");
        var source=View.Inventories.Single(i=>i.Id==e.OutputId) with{Stock=[new("wood",18,3)]};var destination=View.Inventories.Single(i=>i.Id==View.Participant.InventoryId) with{OccupiedVolume=13,ReservedCapacity=1};
        Check(ManualTransferChoice.MaximumQuantity(source,destination,"wood",2)==3,"shared preview respects reserved stock, reserved room and unit volume");
        Check(!ManualTransferChoice.Read(config,View,View.Participant.InventoryId,e.InputId,"grain",a=>_session.Engine.AccessFor(new(1),a),false).Enabled,"unsupported production ingredient is disabled before Core");
        Call(_app,"CloseTablet");var cell=config.Layout!.IndustrialLots.First().Cells.First(c=>c!=b.Position);var plot=View.Plots.Single(p=>p.Position==cell);
        Prepare(PlayerDesk.Move(cell.X,cell.Z));Prepare(new(ActionKind.BuyPlot,Plot:plot.Id));_session.SetPaused(true);Refresh();Call(_app,"OpenObjectTablet","ground",plot.Id.Value);await Frames();
        SelectItem("StoragePutItem","grain");await Click("StoragePutAll");await FinishTransfer();Check(Stock(View.Participant.InventoryId,"grain")==0,"Storage all transfers selected grain to real owned ground through Core");
        Call(_app,"CloseTablet");var vehicle=View.Fleet!.Vehicles.Single();Prepare(PlayerDesk.Move(vehicle.Position.X,vehicle.Position.Z));_session.SetPaused(true);Refresh();Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection("fleet");await Frames();
        var ids=Field<long[]>(All(_app).OfType<FleetPanel>().Single(),"_inventoryIds");Choose("FleetTransferSource",Array.IndexOf(ids,View.Participant.InventoryId.Value));Choose("FleetTransferDestination",Array.IndexOf(ids,vehicle.Vehicle.CargoInventory.Value));SelectItem("FleetTransferItem","plank");await Frames();
        Check(Named<Button>("FleetTransferAll").Text.Contains("(9)"),"Fleet all previews current chosen source and item");await Click("FleetTransferAll");await FinishTransfer();Check(Stock(vehicle.Vehicle.CargoInventory,"plank")==9,"Fleet all loaded real vehicle cargo");
        // The factory inventory is distant; a UI preview may not authorize its transfer.
        var denied=ManualTransferChoice.Read(config,View,e.OutputId,View.Participant.InventoryId,"plank",a=>_session.Engine.AccessFor(new(1),a),false);
        Check(!denied.Enabled&&denied.Maximum>0&&denied.Reason!="NoFreeStock","distant transfer remains denied by existing access rules with actual available goods");_session.Engine.ValidateInvariants();Call(_app,"CloseTablet");
    }
    private long Stock(InventoryId inventory,string item)=>View.Inventories.Single(i=>i.Id==inventory).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity);
    private void Choose(string name,int index){Check(index>=0,"choice exists "+name);var picker=Named<OptionButton>(name);picker.Select(index);picker.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
    private void SelectItem(string name,string item)
    {
        var picker=Named<OptionButton>(name);string title=PlayerText.Item(Field<string>(_app,"_language"),item);int index=Enumerable.Range(0,picker.ItemCount).First(i=>picker.GetItemText(i)==title);Choose(name,index);
    }
    private async Task Landscape()
    {
        foreach(var spec in new[]{(Language:"ru",Size:new Vector2I(960,440)),(Language:"en",Size:new Vector2I(800,360))})
        {
            if(_case=="landscape-800"&&spec.Size.X!=800)continue;
            await OpenPlayer(spec.Language,spec.Size);Check(Named<TabContainer>("PlayerSections").Size.Y>=180,"landscape keeps at least 180px for working area "+spec.Size);
            var picker=Named<OptionButton>("PlayerSectionPicker");Check(picker.Size.Y<=44&&!All(_app).OfType<ScrollContainer>().Single(s=>s.Name=="PlayerNavigation").Visible,"landscape header picker replaces sidebar");
            int selectedEvent=-1;picker.ItemSelected+=index=>selectedEvent=(int)index;
            UiNavigation.Reveal(picker);await Frames();await PopupClick(picker);var menu=picker.GetPopup();if(menu.Visible){menu.GrabFocus();await PopupKey(0xff50);await PopupKey(0xff54);await PopupKey(0xff0d);}await Frames();
            string[] targets=["overview","holdings","fleet","services","roads","account","quests","negotiations","purchases","participants","city","map","notices","tutorial"];
            Check(selectedEvent>0&&selectedEvent<11&&picker.Selected==selectedEvent&&Field<PlayerTablet>(_app,"_playerTablet").CurrentSection==targets[selectedEvent],"native landscape item-selected event opens corresponding actual page: "+picker.GetItemText(picker.Selected));
            Field<PlayerTablet>(_app,"_playerTablet").OpenSection("overview");await Frames();if(spec.Size.X==960)await Shot("revision-04-player-landscape-ru-960");
            long tick=_session.Engine.Tick;await Click("TabletTime",touch:true);Check(!Field<TabletState>(_app,"_tabletState").PauseTime&&_session.Engine.Tick==tick,"native landscape pause toggle works without hidden ticks");await Click("TabletTime",touch:true);Check(Field<TabletState>(_app,"_tabletState").PauseTime,"native landscape pause restored");await Click("TabletClose",touch:true);Check(!Field<TabletState>(_app,"_tabletState").IsOpen,"native landscape close");
        }
    }
    private async Task Phase(string name,Func<Task> run)
    {int start=_checks.Count;await run();System.IO.File.WriteAllText(_output+"/phase-"+name+".json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks.Skip(start).ToArray()},new JsonSerializerOptions{WriteIndented=true}));}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];string root=Arg("--repository");_output=Arg("--output");_case=Arg("--case");System.IO.Directory.CreateDirectory(_output);MouseFilter=MouseFilterEnum.Ignore;
            if(args.Contains("--fixture"))_session=CheckpointStore.Decode(System.IO.File.ReadAllText(Arg("--fixture")));
            else
            {
                var profile=EconomyProfile.Load(root+"/config/vnext/economy.i4-mobile-equipment.json");var balance=VNextBalanceReader.Read(System.IO.File.ReadAllBytes(root+"/config/vnext/balance.b1.json")).Balance!;
                var resolution=OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario(root+"/scenarios/vnext/compact-valley.json"),profile,new(3,Seed:42),vnextBalance:balance);var d=resolution.Scenario;var anchor=d.Layout!.IndustrialLots.First().Cells[0];var dealer=d.Layout.CivicObjects.Single(c=>c.Kind=="dealership").Entry;
                d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{CashMinor=5_000_000,Position=dealer}:p)],Plots=[..d.Plots!.Select(p=>p.Position==anchor?p with{OwnerId=1}:p)],Buildings=[new(8,1,anchor,"woodworking",Staff:5)]};_session=new(new(d));
                var cfg=JsonSerializer.Deserialize<AutonomousConfiguration>(System.IO.File.ReadAllText(root+"/scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(d,1,resolution.Controllers);
                _session.AttachControllers(cfg.Npcs.Select(n=>new AlgorithmicController(AlgorithmicController.WithEconomy(n,profile.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))));
                var listing=_session.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId=="vector200");Prepare(new(ActionKind.BuyVehicle,VehicleId:listing.Id));for(int i=0;i<6;i++)_session.Step();
                var wood=d.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;Prepare(PlayerDesk.Move(wood.X,wood.Z));Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:9));Prepare(PlayerDesk.Move(anchor.X,anchor.Z));var e=View.Enterprises.Single();Prepare(PlayerDesk.Transfer(View.Participant.InventoryId.Value,e.InputId.Value,"wood",9));Prepare(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true));for(int i=0;i<4;i++)_session.Step();_session.SetPaused(true);
            }
            CheckpointStore.Save(_session,_output+"/prepared.save.json");GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");Set(_app,"_language","ru");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;CheckpointStore.Save(_session,save);Call(_app,"Load");Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();
            var banner=new Label{Text="I6-A Revision · подготовленный мир / prepared world · 3 NPC",Position=new(24,35),MouseFilter=MouseFilterEnum.Ignore};banner.AddThemeFontSizeOverride("font_size",12);AddChild(banner);
            Check(_session.Engine.Configuration().Id=="compact_valley"&&_session.Engine.Configuration().Participants.Length==4,"Small valley only; three ordinary NPCs");
            if(_case is "all" or "navigation")await Phase("navigation",Navigation);if(_case is "all" or "transfers" or "remaining")await Phase("transfers",Transfers);if(_case is "all" or "landscape" or "landscape-800" or "remaining")await Phase("landscape",Landscape);
            _session.Engine.ValidateInvariants();Check(CheckpointStore.Decode(CheckpointStore.Encode(_session)).Engine.ComputeStateHash()==_session.Engine.ComputeStateHash(),"ordinary checkpoint round trip unchanged");
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",selection=_case,checks=_checks,scope="I6-A revision; one prepared Small valley / three ordinary NPCs; native mouse, keyboard, synthetic touch; RU desktop and RU/EN landscape. Linux software GL is not device acceptance."},new JsonSerializerOptions{WriteIndented=true}));GD.Print("DELSK_I6A_REVISION_PASS");GetTree().Quit();
        }
        catch(Exception ex){if(_case=="landscape")await Shot("diagnostic-landscape-failure");if(_session is not null)CheckpointStore.Save(_session,_output+"/failed.save.json");System.IO.File.WriteAllText(_output+"/failure.txt",ex.ToString());GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}

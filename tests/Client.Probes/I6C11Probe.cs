using Godot;
using Simulation.Core;
using Simulation.Hosting;
using Agents.Algorithmic;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated native UI acceptance. Prepared ownership/cash are test data, never ordinary progression.</summary>
public partial class I6C11Probe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="",_root="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static object? Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static IEnumerable<Node> All(Node root){yield return root;foreach(var child in root.GetChildren())foreach(var node in All(child))yield return node;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().First(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private PlayerView View=>PlayerDesk.Read(_session.Engine,new(1));
    private ContextActionsPanel Panel=>Field<ContextActionsPanel>(_app,"_contextActions");
    private void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);_checks.Add(message);GD.Print("I6C11 "+message);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task ResizeWindow(int x,int y){GetWindow().Size=new(x,y);GetWindow().ContentScaleSize=new(x,y);await Frames(10);}
    private async Task Shot(string name){await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"captured "+name);}
    private async Task Raw(Button button,string mode="mouse",bool refresh=false)
    {
        int window=GetWindow().GetWindowId();var point=button.GetGlobalRect().GetCenter();Check(GetViewportRect().HasPoint(point),"native hit inside viewport "+button.Name);
        if(mode=="keyboard")
        {button.GrabFocus();Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=true});await Frames(1);if(refresh)Call(_app,"Refresh");Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=false});}
        else if(mode=="touch")
        {Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=true});await Frames(1);if(refresh)Call(_app,"Refresh");Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=false});}
        else
        {Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);if(refresh)Call(_app,"Refresh");Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});}
        await Frames();
    }
    private async Task Click(string name,string mode="mouse",bool refresh=false)
    {var b=Named<Button>(name);UiNavigation.Reveal(b);await Frames();Check(!b.Disabled,"available "+name);await Raw(b,mode,refresh);}
    private void Refresh()=>Call(_app,"Refresh");
    private void Tick(int n=1){_session.SetPaused(false);for(int i=0;i<n;i++)_session.Step();_session.SetPaused(true);Refresh();}
    private void Command(GameAction action,bool idle=true)
    {_session.SetPaused(false);string id=_session.Send(new(1),action);_session.Step();Check(_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"Core accepts "+action.Kind+" "+_session.Engine.Result(id)?.ReasonCode);int limit=1500;while(idle&&_session.Engine.Observe(new(1)).Participant.Busy&&limit-->0)_session.Step();_session.SetPaused(true);Refresh();Check(limit>0,"bounded local preparation");}
    private void Move(Cell cell){if(View.Participant.Position!=cell)Command(PlayerDesk.Move(cell.X,cell.Z));}
    private void Bounds(Control control,string label)
    {var r=control.GetGlobalRect();Check(r.Position.X>=-1&&r.Position.Y>=-1&&r.End.X<=GetViewportRect().End.X+1&&r.End.Y<=GetViewportRect().End.Y+1,label+" fits "+GetWindow().Size+" "+r);}
    private string SavePath=>typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;
    private async Task Load(GameSession session)
    {CheckpointStore.Save(session,SavePath);Call(_app,"Load");Call(_app,"ContinueGame");await Frames(10);_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();}
    private async Task Menu()
    {
        var menu=Field<MapSelectionScreen>(_app,"_mapSelection");Check(menu.SelectedMap==PlayMaps.CompactValley,"Small valley is the default");
        foreach(var size in new[]{new Vector2I(1100,720),new Vector2I(960,440),new Vector2I(800,360)})
        {await ResizeWindow(size.X,size.Y);Bounds(Named<Control>("SelectedMapPreview"),"large selected preview");Bounds(Named<Button>("StartGame"),"Start");Check(Named<Control>("MainMapPreview").Size.X>=260,"preview remains wide in landscape");}
        await ResizeWindow(1100,720);await Shot("04-main-menu");await Click("ChooseMap");
        Check(All(_app).OfType<PanelContainer>().Count(n=>n.Name.ToString().StartsWith("MapCard_"))==2,"catalog has exactly compact-valley and second");
        Bounds(Named<Control>("MapCard_compact-valley"),"first horizontal card");Bounds(Named<Control>("MapCard_second"),"second horizontal card");
        await Shot("05-map-catalog");
        int window=GetWindow().GetWindowId();Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=true});await Frames();Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=false});await Frames();
        Check(!All(_app).Any(n=>n.Name=="MapCatalog"),"Escape closes only the catalog");await Click("ChooseMap");await Click("SelectMap_second");Check(menu.SelectedMap==PlayMaps.Second&&Named<Button>("StartGame").Disabled,"second preview is selectable but incompatible ordinary Start is disabled");
        var preview=Named<Control>("MainMapPreview");Check(preview.IsVisibleInTree(),"future map keeps its actual geographic preview");
        var count=Named<SpinBox>("NpcCount");count.Value=4;await Frames();Check(menu.SelectedMap==PlayMaps.Second&&Named<Button>("StartGame").Disabled,"NPC update preserves the future map and disabled status");
        var difficulty=Named<OptionButton>("NpcDifficulty");difficulty.Select(2);difficulty.EmitSignal(OptionButton.SignalName.ItemSelected,2L);Check(menu.SelectedMap==PlayMaps.Second,"difficulty update preserves map selection");
        await Click("ChooseMap");await ResizeWindow(960,440);Bounds(Named<Control>("MapCard_second"),"compact catalog second card");await Click("SelectMap_compact-valley");Check(!Named<Button>("StartGame").Disabled,"Small valley can start after selection");
        count.Value=3;await Frames();
        // Continue is independent from the ordinary new-game catalog.
        var legacy=(OrdinaryGameResolution)Call(_app,"ResolveNewGame",PlayMaps.First,3)!;
        await Load(new(new(legacy.Scenario)));Check(Field<string>(_app,"_mapKey")==PlayMaps.First,"historical first save still restores by its identifier");
        await ResizeWindow(1100,720);
    }
    private async Task Context()
    {
        var resolution=(OrdinaryGameResolution)Call(_app,"ResolveNewGame",PlayMaps.CompactValley,3)!;var d=resolution.Scenario;var lot=d.Layout!.IndustrialLots[0];
        d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=1000000}:p).ToArray(),Buildings=[new(800,1,lot.Cells[0],"warehouse")],Plots=d.Plots!.Select(p=>lot.Cells.Contains(p.Position)?p with{OwnerId=1}:p).ToArray()};
        var session=new GameSession(new(d));var profile=EconomyProfile.Load(_root+"/config/vnext/economy.i4-mobile-equipment.json");
        var cfg=JsonSerializer.Deserialize<AutonomousConfiguration>(System.IO.File.ReadAllText(_root+"/scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(d,1,resolution.Controllers);
        session.AttachControllers(cfg.Npcs.Select(n=>new AlgorithmicController(AlgorithmicController.WithEconomy(n,profile.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))));
        await Load(session);Check(d.Participants.Length==4,"one prepared Small valley with three ordinary NPCs");
        var empty=d.Layout.SpawnCells.First(c=>!d.ResourceNodes.Any(r=>r.Position==c)&&!d.Plots.Any(p=>p.Position==c));Move(empty);await Frames();Check(Panel.ActiveCount==0,"empty cell has no global actions");
        var forest=d.ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public).Position;Move(forest);await Frames();Check(Panel.Selection.Choices.Any(c=>c.Command?.Kind==ActionKind.StartHarvesting),"public resource exposes harvest");
        int tape=_session.CommandTape.Length;var harvest=Panel.Selection.Choices.Single(c=>c.Command?.Kind==ActionKind.StartHarvesting);await Click(ContextActionsPanel.ButtonName(harvest.Key),"keyboard",true);await Raw(Named<Button>(ContextActionsPanel.ButtonName(harvest.Key)));Check(_session.CommandTape.Length==tape+1,"keyboard confirm and repeated press queue exactly one harvest");
        Tick();await Frames();Check(Panel.Selection.Choices.Any(c=>c.Command?.Kind==ActionKind.StopHarvest),"active extraction exposes Stop");Tick(4);Command(PlayerDesk.StopHarvest());
        Move(lot.Cells[0]);var storage=View.Buildings.Single(b=>b.Id.Value==800).InputId;Command(PlayerDesk.Transfer(View.Participant.InventoryId.Value,storage.Value,"wood",1));await Frames();
        Check(Panel.Selection.Choices.Count(c=>c.Key.StartsWith("local-transfer:"))==2,"owned warehouse has compact Store and Take with actual goods");Check(Panel.Selection.Choices.All(c=>!c.Key.StartsWith("section:")),"world dock has no global sections");Bounds(Panel,"local dock");Check(Panel.Size.Y<=100,"three desktop local actions fit on one compact row");
        var primary=Panel.Selection.PrimaryKeys().Select(k=>Named<Button>(ContextActionsPanel.ButtonName(k))).ToArray();Check(primary.Select(b=>b.Size.Y).Distinct().Count()==1,"primary buttons share one height");
        var scroll=All(Panel).OfType<ScrollContainer>().Single();Check(!scroll.Visible||scroll.GetVScrollBar().MaxValue<=scroll.GetVScrollBar().Page,"ordinary local actions need no scrollbar");
        await Shot("01-local-actions");int count=Panel.ActiveCount;tape=_session.CommandTape.Length;await Click("ContextDismiss","touch");Check(Panel.Dismissed&&Named<Button>("ContextReopen").Text=="⚡ "+count,"touch collapses to one active-count button");Bounds(Panel,"collapsed dock");Check(Panel.GlobalPosition.X>GetViewportRect().Size.X*.7f,"collapsed button is at bottom right");await Shot("02-collapsed-actions");
        int humanBefore=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);Move(forest);await Frames();Check(Panel.Dismissed&&Panel.ActiveCount!=count,"physical move updates count and respects manual collapse");Check(All(Panel).OfType<Button>().Count(b=>b.IsVisibleInTree())==1,"collapsed dock exposes only one button");await Click("ContextReopen","keyboard");Check(!Panel.Dismissed&&_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==humanBefore+1,"explicit reopen executes no hidden command");
        tape=_session.CommandTape.Length;Call(_app,"ConfirmContextChoice",harvest with{Command=harvest.Command! with{ItemId="grain"}});Check(_session.CommandTape.Length==tape,"changed payload cannot submit stale selection");
        Move(lot.Cells[0]);await Frames();var transfer=Panel.Selection.Choices.First(c=>c.Key.StartsWith("local-transfer:"));await Click(ContextActionsPanel.ButtonName(transfer.Key));Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Player&&Named<OptionButton>("BackpackTransferSource").ItemCount>1,"local goods action opens the existing transfer form");Call(_app,"CloseTablet");
        Move(View.Fleet!.Dealer);await Frames();var offer=Panel.Selection.Choices.First(c=>c.Command?.Kind==ActionKind.BuyVehicle);var model=View.Fleet.Models.Single(m=>View.Fleet.Stock.Single(s=>s.Id==offer.Command!.VehicleId).ModelId==m.Id);Check(offer.Label.Contains(PlayerText.Money(model.PriceMinor)),"vehicle offer uses the actual model price");
        await Shot("03-vehicle-price");tape=_session.CommandTape.Length;await Click(ContextActionsPanel.ButtonName(offer.Key),"mouse",true);await Raw(Named<Button>(ContextActionsPanel.ButtonName(offer.Key)));Check(_session.CommandTape.Length==tape+1,"vehicle purchase queues one transaction despite refresh and repeat");Tick(6);await Frames();var own=View.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==offer.Command!.VehicleId);Check(_session.Engine.InspectFleet()!.Purchases.Count(p=>p.VehicleId==own.Vehicle.Id)==1,"one actual vehicle purchase receipt");Move(own.Position);await Frames();Check(Panel.Selection.Choices.Any(c=>c.Command?.Kind==ActionKind.BoardVehicle),"own vehicle exposes Board");
        foreach(var size in new[]{new Vector2I(960,440),new Vector2I(800,360)}){await ResizeWindow(size.X,size.Y);Bounds(Panel,"landscape context");await Click("ContextDismiss");Bounds(Panel,"landscape collapsed context");await Click("ContextReopen");}
        Set(_app,"_language","en");Call(_app,"RebuildChrome");Refresh();await Frames();Check(Panel.Selection.Choices.All(c=>!c.Key.StartsWith("section:")),"English shares local-only choices");
        string hash=_session.Engine.ComputeStateHash();_session.SetPaused(true);_session.Step();Check(hash==_session.Engine.ComputeStateHash(),"pause preserves the authoritative world");
        _session.Engine.ValidateInvariants();var restored=CheckpointStore.Decode(CheckpointStore.Encode(_session));Check(hash==restored.Engine.ComputeStateHash(),"UI work preserves checkpoint restore");
    }
    private async Task EightActionLayout()
    {
        // Synthetic layout data only: no game command or economic authority is simulated here.
        var panel=new ContextActionsPanel{Position=new(28,80),Size=new(744,0)};AddChild(panel);
        foreach(var size in new[]{new Vector2I(1100,720),new Vector2I(960,440),new Vector2I(800,360)})
        {
            await ResizeWindow(size.X,size.Y);panel.Size=new(744,0);
            panel.ShowData("layout",Enumerable.Range(1,8).Select(n=>new ContextChoice("layout:"+n,"Положить · склад "+n,null)).ToArray(),"ru",false,"",60);panel.SetExpanded(true);await Frames(10);
            var scroll=All(panel).OfType<ScrollContainer>().Single();var bar=scroll.GetVScrollBar();
            Check(bar.MaxValue<=bar.Page+1,"eight wrapping actions fit without scrolling at "+size+"; scroll="+bar.MaxValue+"/"+bar.Page+" flow="+scroll.GetChild<Control>(0).Size);
            Bounds(panel,"eight-action fixture");
        }
        panel.QueueFree();await Frames();
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter=MouseFilterEnum.Ignore;var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];_output=Arg("--output");_root=Arg("--repository");System.IO.Directory.CreateDirectory(_output);
            if(Arg("--case")=="layout")
            {await EightActionLayout();System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Synthetic eight-button layout only; no economic or command claim"}));GetTree().Quit();return;}
            await ResizeWindow(1100,720);_app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames(10);_app.SetProcess(false);Set(_app,"_language","ru");Call(_app,"ShowMaps");await Frames();await Menu();await Context();
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Prepared warehouse/cash, actual Small valley and three ordinary NPCs; Linux software GL and synthetic mouse/keyboard/touch, not hardware acceptance"},new JsonSerializerOptions{WriteIndented=true}));
            _app.QueueFree();await Frames();GetTree().Quit();
        }
        catch(Exception error){try{await Shot("failure");}catch{}System.IO.File.WriteAllText(_output+"/failure.txt",error.ToString());GD.PrintErr(error);GetTree().Quit(1);}
    }
}

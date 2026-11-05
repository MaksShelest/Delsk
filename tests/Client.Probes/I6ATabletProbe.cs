using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated I6-A native acceptance on one explicitly prepared Small valley, three ordinary NPCs.</summary>
public partial class I6ATabletProbe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="";
    private readonly List<string> _checks=[];
    private readonly List<string> _results=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static void Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static IEnumerable<Node> All(Node n){yield return n;foreach(var child in n.GetChildren())foreach(var descendant in All(child))yield return descendant;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().First(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks.Add(reason);if(reason.StartsWith("section "))GD.Print(reason);}
    private async Task Frames(int count=2){for(int i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Refresh()=>Call(_app,"Refresh");
    private void Drive(int ticks=1){_session.SetPaused(false);for(int i=0;i<ticks;i++)_session.Step();_session.SetPaused(true);Refresh();}
    private void Prepare(GameAction action)
    {
        _session.SetPaused(false);string command=_session.Send(new(1),action);_session.Step();
        Check(_session.Engine.Result(command)?.Status==ResultStatus.Accepted,"preparation "+action.Kind+": "+_session.Engine.Result(command)?.ReasonCode);
        int limit=1500;while(_session.Engine.Observe(new(1)).Participant.Busy&&limit-->0)_session.Step();Check(limit>0,"bounded preparation "+action.Kind);
    }
    private async Task Click(string name,bool refreshDuringPress=false)
    {
        var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();Check(!button.Disabled,"available "+name);
        var point=button.GetGlobalRect().GetCenter();Check(GetViewportRect().HasPoint(point),"inside viewport "+name);
        int window=GetWindow().GetWindowId();Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        if(refreshDuringPress){Refresh();await Frames(1);Check(ReferenceEquals(button,Named<Button>(name)),"same control during refresh "+name);}
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();Refresh();await Frames();
    }
    private async Task Shot(string name){await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"captured "+name);}
    private void Bounds()
    {
        var panel=Named<TabletPanel>("GameTablet");Check(panel.GetGlobalRect().End.X<=GetWindow().Size.X,"tablet fits width");
        var close=Named<Button>("TabletClose");Check(GetViewportRect().Encloses(close.GetGlobalRect()),"close fully visible");
        foreach(var control in All(panel).OfType<Control>().Where(c=>c.IsVisibleInTree()&&c is BaseButton))
            Check(control.GlobalPosition.X>=panel.GlobalPosition.X-1&&control.GetGlobalRect().End.X<=panel.GetGlobalRect().End.X+1,"horizontal bounds "+control.Name);
        Check(Named<ScrollContainer>("TabletScroll").VerticalScrollMode==ScrollContainer.ScrollMode.Disabled,"no outer tablet scrolling");
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];string root=Arg("--repository");_output=Arg("--output");System.IO.Directory.CreateDirectory(_output);MouseFilter=MouseFilterEnum.Ignore;
            if(args.Contains("--fixture"))_session=CheckpointStore.Decode(System.IO.File.ReadAllText(Arg("--fixture")));
            else
            {
            var profile=EconomyProfile.Load(root+"/config/vnext/economy.i4-mobile-equipment.json");var balance=VNextBalanceReader.Read(System.IO.File.ReadAllBytes(root+"/config/vnext/balance.b1.json")).Balance!;
            var resolution=OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario(root+"/scenarios/vnext/compact-valley.json"),profile,new(3,Seed:42),vnextBalance:balance);
            var d=resolution.Scenario;var anchor=d.Layout!.IndustrialLots.First().Cells[0];var dealer=d.Layout.CivicObjects.Single(c=>c.Kind=="dealership").Entry;
            // Explicit fixture funding, a staffed building and starting position; all goods/vehicle/work actions below are ordinary commands.
            d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{CashMinor=5_000_000,Position=dealer}:p)],Plots=[..d.Plots!.Select(p=>p.Position==anchor?p with{OwnerId=1}:p)],Buildings=[new(8,1,anchor,"woodworking",Staff:5)]};
            _session=new(new(d));
            var cfg=JsonSerializer.Deserialize<AutonomousConfiguration>(System.IO.File.ReadAllText(root+"/scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(d,1,resolution.Controllers);
            _session.AttachControllers(cfg.Npcs.Select(n=>new AlgorithmicController(AlgorithmicController.WithEconomy(n,profile.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))));
            var listing=_session.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId=="vector200");Prepare(new(ActionKind.BuyVehicle,VehicleId:listing.Id));for(int i=0;i<6;i++)_session.Step();
            var wood=d.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;Prepare(PlayerDesk.Move(wood.X,wood.Z));Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:9));
            Prepare(PlayerDesk.Move(anchor.X,anchor.Z));var seen=_session.Engine.Observe(new(1));var b=seen.OwnBuildings.Single();Prepare(PlayerDesk.Transfer(seen.Participant.InventoryId.Value,b.InputId.Value,"wood",9));
            Prepare(new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));for(int i=0;i<4;i++)_session.Step();_session.SetPaused(true);_session.Engine.ValidateInvariants();
            CheckpointStore.Save(_session,_output+"/prepared.save.json");
            }
            GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");Set(_app,"_language","ru");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;CheckpointStore.Save(_session,save);Call(_app,"Load");Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();
            var banner=new Label{Text="I6-A · подготовленный мир / prepared world · Малая долина · 3 NPC",Position=new(24,38),MouseFilter=MouseFilterEnum.Ignore};banner.AddThemeFontSizeOverride("font_size",13);AddChild(banner);
            Check(_session.Engine.Configuration().Id=="compact_valley"&&_session.Engine.Configuration().Participants.Length==4,"Small valley only, human and three NPCs");
            if(!args.Contains("--mobile-only"))
            {
            Call(_app,"OpenPlayerTablet");await Frames();Bounds();await Shot("01-player-overview-ru-1100");
            long tick=_session.Engine.Tick;string hash=_session.Engine.ComputeStateHash();await Click("PlayerNav_fleet",true);Check(Field<PlayerTablet>(_app,"_playerTablet").CurrentSection=="fleet","real fleet section selected");Check(tick==_session.Engine.Tick&&hash==_session.Engine.ComputeStateHash(),"navigation preserves paused world");await Click("PlayerNav_holdings");await Shot("02-player-objects-ru-1100");
            foreach(string section in new[]{"holdings","account","quests","negotiations","purchases","participants","city","services","roads","overview"}){await Click("PlayerNav_"+section);Check(Field<PlayerTablet>(_app,"_playerTablet").CurrentSection==section,"section "+section);}
            await Click("OverviewOpen_8");Check(All(_app).OfType<CompactProductionTablet>().Any(c=>c.IsVisibleInTree()),"owned object opens live production prototype");await Click("TabletClose");
            Call(_app,"OpenEnterpriseTablet",8L);await Frames();Bounds();await Shot("03-production-ru-1100");
            var e=_session.Engine.Observe(new(1)).OwnEnterprises.Single();var workshop=e.Workshops[0];long line=workshop.Id;
            var workName="ProductionWork_"+line;var wageName="ProductionWagePlus_"+line;
            var work=Named<Button>(workName);Check(work.GetGlobalRect().End.Y<Named<Button>("TabletTime").GlobalPosition.Y,"production command visible on first desktop screen");
            Check(Named<Button>(wageName).GetGlobalRect().End.Y<Named<Button>("TabletTime").GlobalPosition.Y,"wage command visible on first desktop screen");
            tick=_session.Engine.Tick;int tape=_session.CommandTape.Length;await Click(workName,true);Check(_session.Engine.Tick==tick,"paused production command does not tick");Check(Named<Button>(workName).Disabled,"production pending disabled");Drive();await Frames();
            Check(_session.CommandTape.Length==tape+1,"one production click submits exactly once");Check(!_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops[0].Enabled,"production stopped through Core");
            await Click(wageName);Drive();await Frames();Check(_session.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id==workshop.GroupId).PendingWageMinor is not null,"wage reaches authoritative pending state");
            await Click(workName);Drive(3);await Frames();Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops[0].Enabled,"production restarted and refreshed");
            foreach(string section in new[]{"logistics","recipes","development","production"}){await Click("ProductionNav_"+section);Bounds();}
            await Click("TabletClose");GetWindow().Size=new(1280,720);GetWindow().ContentScaleSize=GetWindow().Size;Set(_app,"_language","en");Call(_app,"RebuildChrome");await Frames();Call(_app,"OpenPlayerTablet");await Frames();Bounds();Check(Named<Button>("PlayerNav_participants").Text=="Participants","English navigation");Call(_app,"CloseTablet");Call(_app,"OpenEnterpriseTablet",8L);await Frames();Bounds();Check(Named<Button>(workName).Text=="Stop production","English production commands");_results.Add("en 1280x720 player and production");
            }
            Call(_app,"CloseTablet");GetWindow().Size=new(360,800);GetWindow().ContentScaleSize=GetWindow().Size;Set(_app,"_language","ru");Call(_app,"RebuildChrome");await Frames();Call(_app,"OpenPlayerTablet");await Frames();Bounds();Check(Named<OptionButton>("PlayerSectionPicker").IsVisibleInTree(),"mobile section picker");Check(!All(_app).OfType<ScrollContainer>().Single(c=>c.Name=="PlayerNavigation").Visible,"mobile sidebar hidden");Call(_app,"CloseTablet");Call(_app,"OpenEnterpriseTablet",8L);await Frames();Bounds();Check(Named<OptionButton>("ProductionSectionPicker").Size.Y<=44,"compact mobile picker height");Check(Named<Button>("TabletClose").Size.Y==44,"compact mobile close touch target");await Shot("04-production-ru-360");
            var picker=Named<OptionButton>("ProductionSectionPicker");picker.Select(1);picker.EmitSignal(OptionButton.SignalName.ItemSelected,1L);await Frames();Bounds();picker.Select(0);picker.EmitSignal(OptionButton.SignalName.ItemSelected,0L);await Frames();
            CheckpointStore.Decode(CheckpointStore.Encode(_session)).Engine.ValidateInvariants();_session.Engine.ValidateInvariants();await Click("TabletClose");Check(!Field<TabletState>(_app,"_tabletState").IsOpen,"native close works");
            if(!args.Contains("--mobile-only"))_results.Add("ru 1100x720 all sections, commands, refresh, pause");_results.Add("ru 360x800 both prototypes and native pickers");
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,results=_results,mode=args.Contains("--mobile-only")?"mobile-only":"all",scope="Linux native Godot 4.7.2 / .NET 10; software GL and synthetic input; explicitly funded prepared Small valley, three live ordinary NPC controllers; no device acceptance"},new JsonSerializerOptions{WriteIndented=true}));
            GD.Print("DELSK_I6A_PASS");GetTree().Quit();
        }
        catch(Exception ex){System.IO.File.WriteAllText(_output+"/failure.txt",ex.ToString());GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}

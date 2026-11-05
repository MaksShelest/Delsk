using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>One bounded native Revision 2 smoke. Prepared Small valley / three ordinary NPCs; never shipped.</summary>
public partial class I6ATabletProbe : Control
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private string _output="",_case="all";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    private static void Call(object o,string key,params object[] args)=>o.GetType().GetMethod(key,Flags)!.Invoke(o,args);
    private static IEnumerable<Node> All(Node root){yield return root;foreach(var child in root.GetChildren())foreach(var item in All(child))yield return item;}
    private T Named<T>(string name)where T:Node=>All(_app).OfType<T>().First(n=>n.Name==name&&(n is not Control c||c.IsVisibleInTree()));
    private PlayerView View=>PlayerDesk.Read(_session.Engine,new(1));
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks.Add(reason);GD.Print("I6A_R2 "+reason);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Refresh()=>Call(_app,"Refresh");
    private void Drive(int ticks=1){_session.SetPaused(false);for(int i=0;i<ticks;i++)_session.Step();_session.SetPaused(true);Refresh();}
    private void Prepare(GameAction action)
    {
        _session.SetPaused(false);string id=_session.Send(new(1),action);_session.Step();Check(_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"ordinary preparation "+action.Kind);
        int limit=1500;while(View.Participant.Busy&&limit-->0)_session.Step();Check(limit>0,"bounded preparation");
    }
    private async Task RawClick(Button button,bool duringRefresh=false)
    {
        var point=button.GetGlobalRect().GetCenter();Check(GetViewportRect().HasPoint(point),"native hit inside viewport "+button.Name);int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        if(duringRefresh){Refresh();await Frames(1);Check(GodotObject.IsInstanceValid(button)&&button.IsInsideTree(),"refresh retains pressed control");}
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();Refresh();await Frames();
    }
    private async Task Click(string name,bool refresh=false)
    {var button=Named<Button>(name);UiNavigation.Reveal(button);await Frames();Check(!button.Disabled,"available "+name);await RawClick(button,refresh);}
    private async Task Shot(string name)
    {await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"captured "+name);}
    private async Task FinishCommand()
    {
        string id=Field<string>(_app,"_trackedId");Drive();int bound=100;while((View.Participant.Busy||View.Transfers.Length>0)&&bound-->0)Drive();
        Check(bound>0&&_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"ordinary UI command accepted by Core");await Frames();
    }
    private async Task OpenSawmill()
    {Call(_app,"CloseTablet");Call(_app,"OpenEnterpriseTablet",8L);await Frames();Check(Named<WoodworkingTabletPresenter>("WoodworkingWorkspace").IsVisibleInTree(),"real woodworking workspace opens");}
    private async Task Navigation()
    {
        Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection("overview");await Frames();
        var nav=Named<ScrollContainer>("PlayerNavigation");var buttons=All(nav).OfType<Button>().Where(b=>b.Name.ToString().StartsWith("PlayerNav_")).ToArray();
        Check(buttons.All(b=>Math.Abs(b.Size.X-buttons[0].Size.X)<1),"navigation buttons keep equal width");
        float left=buttons[0].GlobalPosition.X-nav.GlobalPosition.X,right=nav.GetVScrollBar().GlobalPosition.X-buttons[0].GetGlobalRect().End.X;
        Check(left is >=12 and <=14&&right is >=12 and <=14,$"navigation padding left={left} right={right}");
        await Shot("revision2-01-player-navigation-ru-1100");await Click("PlayerGroup_economy",true);Refresh();await Frames();
        Check(!All(_app).OfType<Button>().Single(b=>b.Name=="PlayerNav_account").IsVisibleInTree()&&Field<PlayerTablet>(_app,"_playerTablet").CurrentSection=="overview","group and active page survive Refresh");
        Field<PlayerTablet>(_app,"_playerTablet").OpenSection("account");await Frames();Check(Named<Button>("PlayerNav_account").IsVisibleInTree(),"deep link still opens its group");
        Field<PlayerTablet>(_app,"_playerTablet").OpenSection("holdings");await Frames();await Click("HoldingOpen_8");Check(Named<WoodworkingTabletPresenter>("WoodworkingWorkspace").IsVisibleInTree(),"owned-object path opens same sawmill");
    }
    private async Task Production()
    {
        await OpenSawmill();var e=View.Enterprises.Single();var unit=e.Workshops[0];var recipe=_session.Engine.Configuration().Recipes.Single(r=>r.Id==unit.RecipeId);
        Check(recipe.Ingredients.Single().ItemId=="wood"&&recipe.OutputItem=="plank","actual wood to planks recipe");
        var artwork=Named<TextureRect>("WoodworkingIllustration");Check(artwork.Texture.GetWidth()==360&&artwork.Size.X>=200&&artwork.Size.Y>=150,"large real sawmill illustration");
        var formula=All(Named<HFlowContainer>("WoodworkingRecipe")).OfType<Label>().Select(l=>l.Text).ToArray();Check(formula.Any(t=>t.Contains("×"+recipe.Ingredients[0].Quantity))&&formula.Any(t=>t.Contains("×"+recipe.OutputQuantity)),"recipe icons use authoritative quantities");
        Check(Named<ProgressBar>("WoodworkingProgress").Value==unit.WorkDone,"actual batch progress");await Shot("revision2-02-sawmill-working-ru-1100");
        long tick=_session.Engine.Tick;await Click("WoodworkingWork",true);Check(_session.Engine.Tick==tick&&Named<Button>("WoodworkingWork").Disabled,"stop remains queued and disables pending button");await FinishCommand();Check(!View.Enterprises.Single().Workshops[0].Enabled,"real production stopped");
        // Remove only the free input with an ordinary transfer. The unfinished batch remains intact.
        var input=View.Inventories.Single(i=>i.Id==e.InputId);long free=input.Stock.Where(s=>s.ItemId=="wood").Sum(s=>s.Quantity-s.Reserved);
        Prepare(PlayerDesk.Transfer(input.Id.Value,View.Participant.InventoryId.Value,"wood",free));_session.SetPaused(true);Refresh();await Frames();
        await Click("WoodworkingWork");await FinishCommand();int bound=200;while(View.Enterprises.Single().Workshops[0].ReasonCode!="NoInputs"&&bound-->0)Drive();Check(bound>0,"real production reaches NoInputs after finishing retained batch");await Frames();
        Check(Named<Label>("WoodworkingStatus").Text.Contains("Не хватает")&&Named<Label>("WoodworkingGuidance").Text.Contains("Положить всё"),"human explanation names the available next action");
        await Shot("revision2-03-sawmill-materials-needed-ru-1100");
        int humanBefore=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);await Click("EnterprisePutAll",true);await RawClick(Named<Button>("EnterprisePutAll"));await FinishCommand();
        Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==humanBefore+1,"load shortcut sends one command despite repeat click");
        Check(View.Inventories.Single(i=>i.Id==e.InputId).Stock.Any(s=>s.ItemId=="wood"&&s.Quantity>0),"real materials loaded into factory");
        await Click("EnterpriseTakeAll");await FinishCommand();Check(View.Inventories.Single(i=>i.Id==View.Participant.InventoryId).Stock.Any(s=>s.ItemId=="plank"&&s.Quantity>0),"real completed planks collected");
        await Click("WoodworkingStaff");Check(All(_app).OfType<WorkshopPanel>().Any(p=>p.IsVisibleInTree()),"existing worker and recipe commands available");
        await Click("Workshop_wage-plus_"+unit.Id);await FinishCommand();var group=View.Employment.Single(g=>g.Id==unit.GroupId);Check(group.PendingWageMinor is not null||group.WageMinor>20,"wage UI uses existing authoritative pending rules");
    }
    private async Task Remote()
    {
        var forest=_session.Engine.Configuration().ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        Prepare(PlayerDesk.Move(forest.X,forest.Z));_session.SetPaused(true);Refresh();await Frames();Call(_app,"OpenPlayerTablet");await Frames();
        Field<PlayerTablet>(_app,"_playerTablet").OpenHolding("building",8);await Frames();
        var workspace=Named<WoodworkingTabletPresenter>("WoodworkingWorkspace");
        Check(workspace.Size.Y>250,"remote sawmill receives the available page height");
        Check(Named<ScrollContainer>("PlayerPage_object").VerticalScrollMode==ScrollContainer.ScrollMode.Disabled,"remote sawmill has no outer duplicate scroll");
        UiNavigation.Reveal(Named<Button>("WoodworkingWork"));await Frames();
        Check(GetViewportRect().Encloses(Named<Button>("WoodworkingWork").GetGlobalRect()),"remote primary action remains reachable");
        Check(!Named<Button>("EnterprisePutAll").IsVisibleInTree()||Named<Button>("EnterprisePutAll").Disabled,"remote material shortcut respects existing access and stock");
    }
    private async Task Secondary()
    {
        await OpenSawmill();await Click("WoodworkingNav_delivery");await Click("HouseholdRoutes");Check(All(_app).OfType<RouteEditor>().Any(r=>r.IsVisibleInTree()),"original logistics and routes available");
        await Click("WoodworkingNav_development");Check(Named<DevelopmentPanel>("EnterpriseDevelopment").IsVisibleInTree(),"existing expansion controls available");
        await Click("WoodworkingNav_object");await Click("ObjectManagementToggle");
        var prepare=Named<Button>("PrepareDemolition");var preview=Named<Button>("DemolitionPreview");Check(prepare.Size.X is >150 and <400&&preview.Size.X is >60 and <220&&prepare.Size.Y<=48&&preview.Size.Y<=48,"demolition actions have natural compact widths and heights");
        await Shot("revision2-04-sawmill-object-actions-ru-1100");await Click("DemolitionPreview");Check(Named<Button>("DemolitionHold").Disabled,"unsafe demolition remains blocked before preparation");await Click("DemolitionCancel");
        // One short landscape check of the same real factory, with no separate portrait layout.
        Call(_app,"CloseTablet");GetWindow().Size=new(960,440);GetWindow().ContentScaleSize=GetWindow().Size;Set(_app,"_language","en");Call(_app,"RebuildChrome");await Frames();await OpenSawmill();
        Check(GetViewportRect().Encloses(Named<Button>("TabletClose").GetGlobalRect())&&GetViewportRect().Encloses(Named<CheckButton>("TabletTime").GetGlobalRect()),"landscape close and pause visible");
        Check(Named<ScrollContainer>("TabletScroll").VerticalScrollMode==ScrollContainer.ScrollMode.Disabled,"no outer production scroll");
        UiNavigation.Reveal(Named<Button>("WoodworkingWork"));await Frames();Check(GetViewportRect().HasPoint(Named<Button>("WoodworkingWork").GetGlobalRect().GetCenter()),"main landscape action accessible by one content scroll");
        Check(Named<TextureRect>("WoodworkingIllustration").Size.X>=170,"landscape retains readable illustration");
        await Demolition();
    }
    private async Task Demolition()
    {
        Call(_app,"CloseTablet");GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;Set(_app,"_language","ru");Call(_app,"RebuildChrome");await Frames();await OpenSawmill();await Click("WoodworkingNav_object");await Click("ObjectManagementToggle");
        await Click("PrepareDemolition");await FinishCommand();int bound=200;while(_session.Engine.QuoteDemolition(new(1),8,false)!.Blockers.Length>0&&bound-->0)Drive();Check(bound>0,"real demolition preparation drains current work");await Frames();await Click("DemolitionPreview");Check(!Named<Button>("DemolitionHold").Disabled,"fresh safe quote enables hold");
        int tape=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);await Hold(.15);Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==tape,"short demolition click sends no command");
        Check(Named<Button>("DemolitionHold").Size.Y<=48,"confirmation also keeps compact height");await Hold(2.2);Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==tape+1,"continuous two-second hold queues exactly one demolition");await FinishCommand();Check(!View.Buildings.Any(b=>b.Id.Value==8),"Core confirms quoted demolition only after safe hold");
    }
    private async Task Hold(double seconds)
    {
        var button=Named<Button>("DemolitionHold");UiNavigation.Reveal(button);await Frames();var point=button.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});
        // Software rendering can take longer than Godot's clamped frame delta. Keep the real
        // input pressed until the existing UI timer reaches its threshold, within a wall bound.
        ulong end=Time.GetTicksMsec()+(ulong)((seconds>=2?15:seconds)*1000);int before=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);var panel=All(_app).OfType<DemolitionPanel>().Single(p=>p.IsVisibleInTree());
        while(Time.GetTicksMsec()<end)
        {
            await Frames(1);if(seconds>=2)
            {
                if(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)>before)break;
                Check(Field<bool>(panel,"_holding"),"safe hold stays active; seconds="+Field<double>(panel,"_held")+" rect="+button.GetGlobalRect()+" point="+point);
            }
        }
        if(seconds>=2)GD.Print("hold ended: caption="+button.Text+" simulated="+Field<double>(panel,"_held"));
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();Refresh();await Frames();
    }
    private void CreateFixture(string root)
    {
        var profile=EconomyProfile.Load(root+"/config/vnext/economy.i4-mobile-equipment.json");var balance=VNextBalanceReader.Read(System.IO.File.ReadAllBytes(root+"/config/vnext/balance.b1.json")).Balance!;
        var resolution=OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario(root+"/scenarios/vnext/compact-valley.json"),profile,new(3,Seed:42),vnextBalance:balance);var d=resolution.Scenario;var anchor=d.Layout!.IndustrialLots.First().Cells[0];var dealer=d.Layout.CivicObjects.Single(c=>c.Kind=="dealership").Entry;
        d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{CashMinor=5_000_000,Position=dealer}:p)],Plots=[..d.Plots!.Select(p=>p.Position==anchor?p with{OwnerId=1}:p)],Buildings=[new(8,1,anchor,"woodworking",Staff:5)]};_session=new(new(d));
        var cfg=JsonSerializer.Deserialize<AutonomousConfiguration>(System.IO.File.ReadAllText(root+"/scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(d,1,resolution.Controllers);
        _session.AttachControllers(cfg.Npcs.Select(n=>new AlgorithmicController(AlgorithmicController.WithEconomy(n,profile.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))));
        var listing=_session.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId=="vector200");Prepare(new(ActionKind.BuyVehicle,VehicleId:listing.Id));for(int i=0;i<6;i++)_session.Step();
        var wood=d.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;Prepare(PlayerDesk.Move(wood.X,wood.Z));Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:9));Prepare(PlayerDesk.Move(anchor.X,anchor.Z));Prepare(PlayerDesk.Transfer(View.Participant.InventoryId.Value,View.Enterprises.Single().InputId.Value,"wood",9));Prepare(new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));for(int i=0;i<4;i++)_session.Step();_session.SetPaused(true);
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];_output=Arg("--output");_case=Arg("--case");System.IO.Directory.CreateDirectory(_output);MouseFilter=MouseFilterEnum.Ignore;
            if(args.Contains("--fixture"))_session=CheckpointStore.Decode(System.IO.File.ReadAllText(Arg("--fixture")));else CreateFixture(Arg("--repository"));
            CheckpointStore.Save(_session,_output+"/prepared.save.json");GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");Set(_app,"_language","ru");
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;CheckpointStore.Save(_session,save);Call(_app,"Load");Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();
            AddChild(new Label{Text="I6-A R2 · подготовленный мир · Малая долина · 3 NPC",Position=new(24,35),MouseFilter=MouseFilterEnum.Ignore});
            Check(_session.Engine.Configuration().Id=="compact_valley"&&_session.Engine.Configuration().Participants.Length==4,"one Small valley with three ordinary NPCs");
            if(_case is "all" or "layout")await Navigation();if(_case is "all" or "production" or "remaining")await Production();if(_case=="remote")await Remote();if(_case is "all" or "secondary" or "remaining")await Secondary();if(_case=="demolition")await Demolition();
            _session.Engine.ValidateInvariants();Check(CheckpointStore.Decode(CheckpointStore.Encode(_session)).Engine.ComputeStateHash()==_session.Engine.ComputeStateHash(),"ordinary checkpoint round trip preserved");
            System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",selection=_case,checks=_checks,scope="Revision 2 native sawmill / Small valley / three ordinary NPCs. Linux software GL and synthetic input; not physical-device acceptance."},new JsonSerializerOptions{WriteIndented=true}));GD.Print("DELSK_I6A_R2_PASS");GetTree().Quit();
        }
        catch(Exception ex){System.IO.File.WriteAllText(_output+"/failure.txt",ex.ToString());CheckpointStore.Save(_session,_output+"/failed.save.json");GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}

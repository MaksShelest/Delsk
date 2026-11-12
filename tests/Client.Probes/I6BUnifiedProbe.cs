using Godot;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>One isolated prepared Small valley / three ordinary NPCs. Never part of the game package.</summary>
public partial class I6BUnifiedProbe : Control
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
    private void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);_checks.Add(reason);GD.Print("I6B "+reason);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Refresh()=>Call(_app,"Refresh");
    private void Drive(int ticks=1){_session.SetPaused(false);for(int i=0;i<ticks;i++)StepOrdinary(_session);_session.SetPaused(true);Refresh();}
    private void StepOrdinary(GameSession session)
    {
        if(session.Engine.WaitingForBallots&&PlayerDesk.Read(session.Engine,new(1)).Elections.Voting is {} vote&&vote.Awaiting.Contains(new ParticipantId(1)))
        {session.Send(new(1),PlayerDesk.Vote(vote.Election.Value,(vote.RequiredCandidate??new ParticipantId(1)).Value));session.PumpVoting();Check(!session.Engine.WaitingForBallots,"ordinary preparation ballot resolves the real frozen election");}
        session.Step();
    }
    private void Prepare(GameAction action)
    {
        _session.SetPaused(false);string id=_session.Send(new(1),action);StepOrdinary(_session);Check(_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"ordinary preparation "+action.Kind+" "+_session.Engine.Result(id)?.ReasonCode);
        int limit=1500;while(View.Participant.Busy&&limit-->0)StepOrdinary(_session);Check(limit>0,"bounded preparation");
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
        string id=Field<string>(_app,"_trackedId");var kind=_session.CommandTape.Last(c=>c.Command.CommandId==id).Command.Action.Kind;Drive();int bound=100;while(((View.Participant.Busy&&kind!=ActionKind.StartHarvesting)||View.Transfers.Length>0)&&bound-->0)Drive();
        Check(bound>0&&_session.Engine.Result(id)?.Status==ResultStatus.Accepted,"ordinary UI command accepted by Core");await Frames();
    }
    private long _extractor,_truck;private readonly Dictionary<string,long> _industries=[];
    private void Move(Cell cell){if(View.Participant.Position!=cell)Prepare(PlayerDesk.Move(cell.X,cell.Z));}
    private void Ticks(int count){_session.SetPaused(false);for(int i=0;i<count;i++)StepOrdinary(_session);_session.SetPaused(true);}
    private long Stock(InventoryId id,string item)=>View.Inventories.Single(i=>i.Id==id).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
    private void Transfer(InventoryId source,InventoryId dest,string item,long quantity)=>Prepare(PlayerDesk.Transfer(source.Value,dest.Value,item,quantity));
    private async Task Open(string section){Call(_app,"CloseTablet");Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection(section);Refresh();await Frames();}
    private async Task Factory(long id){Call(_app,"CloseTablet");var building=View.Buildings.Single(b=>b.Id.Value==id);if(View.Participant.Position==building.Position)Call(_app,"OpenEnterpriseTablet",id);else{Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenHolding("building",id);}await Frames();Refresh();await Frames();}
    private void Pick(string name,long id){var option=Named<OptionButton>(name);int index=Enumerable.Range(0,option.ItemCount).Single(i=>option.GetItemMetadata(i).AsInt64()==id);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
    private void PrepareFixture(string root,string source)
    {
        var original=CheckpointStore.Decode(System.IO.File.ReadAllText(source));var d=original.Engine.Configuration();
        Check(d.Id=="compact_valley"&&d.Participants.Length==4,"reuse I6-A compact-valley configuration and exactly three NPCs");
        string[] types=["woodworking","metallurgy","materials","food","dairy","machinery","warehouse"];
        var lots=d.Layout!.IndustrialLots.Take(types.Length).ToArray();var builds=types.Select((t,i)=>new BuildingDefinition(8+i,1,lots[i].Cells[0],t,Level:t=="woodworking"?2:1,Staff:i<6?5:0)).ToArray();
        d=d with{Buildings=builds,Plots=[..d.Plots!.Select(p=>lots.Any(l=>l.Cells.Contains(p.Position))?p with{OwnerId=1}:p)]};_session=new(new(d));
        foreach(var b in builds.Where(b=>b.TypeId!="warehouse"))_industries.Add(b.TypeId,b.Id);
        var profile=EconomyProfile.Load(root+"/config/vnext/economy.i4-mobile-equipment.json");var cfg=JsonSerializer.Deserialize<AutonomousConfiguration>(System.IO.File.ReadAllText(root+"/scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(d,1,OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario(root+"/scenarios/vnext/compact-valley.json"),profile,new(3,Seed:42),vnextBalance:VNextBalanceReader.Read(System.IO.File.ReadAllBytes(root+"/config/vnext/balance.b1.json")).Balance!).Controllers);
        _session.AttachControllers(cfg.Npcs.Select(n=>new AlgorithmicController(AlgorithmicController.WithEconomy(n,profile.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))));
        // Initial preparation changes only owned buildings/plots. Everything below is an ordinary Core command.
        Move(View.Fleet!.Dealer);var car=View.Fleet.Stock.First(s=>s.ModelId=="vector200");_truck=car.Id;Prepare(new(ActionKind.BuyVehicle,VehicleId:car.Id));Ticks(6);
        var machine=View.Fleet!.Stock.First(s=>s.ModelId=="extractor_extract_wood_1");_extractor=machine.Id;Prepare(new(ActionKind.BuyVehicle,VehicleId:machine.Id));Ticks(6);
        var site=View.Resources.Where(r=>r.ItemId=="wood"&&!r.Public&&r.Owner is null).OrderBy(r=>Math.Abs(r.Position.X-machine.Position.X)+Math.Abs(r.Position.Z-machine.Position.Z)).First();
        Move(site.Position);Prepare(new(ActionKind.BuyResource,Target:site.Position));Move(machine.Position);Prepare(new(ActionKind.BoardVehicle,VehicleId:_extractor));Move(site.Position);
        Check(View.Equipment!.Single(e=>e.Id.Value==_extractor).Position==site.Position,"ordinary I4 body physically drove to its legally purchased site");
        // Leave the packed machine on its real site; actual place/start are exercised through the UI later.
        Prepare(new(ActionKind.LeaveVehicle,VehicleId:_extractor));
        var saw=View.Enterprises.Single(e=>e.Id.Value==8);var forest=d.ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public).Position;
        Move(forest);Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:8));Move(View.Buildings.Single(b=>b.Id==new BuildingId(8)).Position);Transfer(View.Participant.InventoryId,saw.InputId,"wood",8);Prepare(new(ActionKind.SetProductionEnabled,Enterprise:saw.Id,Enabled:true));_session.SetPaused(true);CompletePreparation(source,builds);
    }
    private void CompletePreparation(string source,BuildingDefinition[]? builds=null)
    {
        var saw=View.Enterprises.Single(e=>e.Id.Value==8);Prepare(new(ActionKind.SetProductionEnabled,Enterprise:saw.Id,Enabled:true));int limit=240;while(Stock(saw.OutputId,"plank")<6&&limit-->0)Ticks(1);Check(limit>0,"real sawmill made workshop materials with actual current staff");Prepare(new(ActionKind.SetProductionEnabled,Enterprise:saw.Id,Enabled:false));Move(View.Buildings.Single(b=>b.Id.Value==8).Position);Transfer(saw.OutputId,View.Participant.InventoryId,"plank",6);
        Move(View.ProductionContent!.Terminal);Prepare(new(ActionKind.BuyImport,ItemId:"steel",Quantity:1));Transfer(View.ProductionContent!.Pickup!.Value,View.Participant.InventoryId,"steel",1);Move(View.Buildings.Single(b=>b.Id.Value==8).Position);Transfer(View.Participant.InventoryId,saw.InputId,"plank",6);Transfer(View.Participant.InventoryId,saw.InputId,"steel",1);_session.SetPaused(true);_session.Engine.ValidateInvariants();
        System.IO.File.WriteAllText(_output+"/preparation.json",JsonSerializer.Serialize(new{source,kind="I6-A configuration with explicitly prepared owned buildings/plots; funded human inherited; ordinary prices/rules/controllers unchanged",buildings=builds??_session.Engine.Configuration().Buildings,tick=_session.Engine.Tick,commandCount=_session.CommandTape.Length},new JsonSerializerOptions{WriteIndented=true}));
    }
    private async Task Navigation()
    {
        string hash=_session.Engine.ComputeStateHash();int commands=_session.CommandTape.Length;await Open("overview");
        var nav=Named<ScrollContainer>("PlayerNavigation");var buttons=All(nav).OfType<Button>().Where(b=>b.Name.ToString().StartsWith("PlayerNav_")).ToArray();Check(buttons.All(b=>Math.Abs(b.Size.X-buttons[0].Size.X)<1),"approved equal-width player navigation retained");
        float left=buttons[0].GlobalPosition.X-nav.GlobalPosition.X,right=nav.GetVScrollBar().GlobalPosition.X-buttons[0].GetGlobalRect().End.X;Check(left is >=12 and <=14&&right is >=12 and <=14,"approved 13px navigation inset retained");await Shot("i6b-01-player-ru-1100");
        foreach(string page in new[]{"participants","city","inventory","account","services","roads","negotiations","quests","holdings"}){Field<PlayerTablet>(_app,"_playerTablet").OpenSection(page);Refresh();await Frames(1);}
        Call(_app,"CloseTablet");Check(_session.Engine.ComputeStateHash()==hash&&_session.CommandTape.Length==commands,"screen navigation alone changes neither economy nor command tape");
    }
    private async Task Production()
    {
        var saw=View.Enterprises.Single(e=>e.Id.Value==8);Move(View.Buildings.Single(b=>b.Id==new BuildingId(8)).Position);Refresh();await Factory(8);await Click("WoodworkingNav_workers");await Click("Workshop_outfit");await FinishCommand();Ticks(60);Refresh();await Frames();
        Check(View.Enterprises.Single(e=>e.Id.Value==8).Workshops.Length==2,"ordinary outfit completed: two real workshops");
        for(int n=0;n<4;n++){await Click("Workshop_wage-plus_8");await FinishCommand();}Check(View.Employment.Single(e=>e.Id.Value==8).PendingWageMinor is not null||View.Employment.Single(e=>e.Id.Value==8).WageMinor>20,"wage retains authoritative announcement semantics");
        var forest=_session.Engine.Configuration().ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public).Position;Call(_app,"CloseTablet");Move(forest);Prepare(new(ActionKind.Harvest,ItemId:"wood",Quantity:4));Move(View.Buildings.Single(b=>b.Id==new BuildingId(8)).Position);Refresh();await Factory(8);
        int before=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);await Click("EnterprisePutAll",true);await RawClick(Named<Button>("EnterprisePutAll"));await FinishCommand();Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==before+1,"production load repeat sends one ordinary command");
        await Click("WoodworkingWork");await FinishCommand();Check(View.Enterprises.Single(e=>e.Id.Value==8).Workshops[0].Enabled,"production start changes authoritative enabled state");int hiring=120;while(View.Enterprises.Single(e=>e.Id.Value==8).Workshops[0].WorkDone==0&&hiring-->0)Ticks(1);Check(hiring>0,"started workshop performs real work after gradual hiring");Refresh();await Frames();await Shot("i6b-02-sawmill-ru-1100");
        Ticks(20);Refresh();await Frames();await Click("EnterpriseTakeAll");await FinishCommand();Check(Stock(View.Participant.InventoryId,"plank")>0,"quick take collects real completed goods");
        // All six industries use their actual recipe and resource projections; no fabricated output.
        foreach(var pair in _industries){await Factory(pair.Value);Check(Named<WoodworkingTabletPresenter>("WoodworkingWorkspace").IsVisibleInTree(),"shared ordinary production workspace: "+pair.Key);var enterprise=View.Enterprises.Single(e=>e.Id.Value==pair.Value);var recipe=_session.Engine.Configuration().Recipes.Single(r=>r.Id==enterprise.Workshops[0].RecipeId);Check(All(Named<UiRecipe>("WoodworkingRecipe")).OfType<Label>().Any(l=>l.Text.Contains(PlayerText.Item("ru",recipe.OutputItem))),"authoritative formula: "+pair.Key);}
        var machinery=View.Enterprises.Single(e=>e.Id.Value==_industries["machinery"]);Check(_session.Engine.Configuration().Recipes.Single(r=>r.Id==machinery.Workshops[0].RecipeId).Ingredients.Length>=2,"machinery really requires multiple ingredients");await Shot("i6b-03-multi-input-ru-1100");
    }
    private async Task Equipment()
    {
        Call(_app,"CloseTablet");var machine=View.Equipment!.Single(e=>e.Id.Value==_extractor);Move(machine.Position);Refresh();Call(_app,"OpenObjectTablet","equipment",_extractor);await Frames();
        Check(Named<ExtractionTabletPresenter>("ExtractionWorkspace").IsVisibleInTree(),"dedicated extraction workspace");await Click("I4Object_place_"+_extractor);await FinishCommand();Check(View.Equipment!.Single(e=>e.Id.Value==_extractor).Deployed==true,"UI places the existing I4 body");
        await Click("I4Object_start_"+_extractor);await FinishCommand();Ticks(4);Refresh();await Frames();Check(Stock(machine.StorageId,"wood")>0,"real I4 output after UI start");await Shot("i6b-04-extraction-ru-1100");
        await Click("I4Object_mode_Off_"+_extractor);await FinishCommand();await Click("ExtractionTakeAll");await FinishCommand();await Click("I4Object_pack_"+_extractor);await FinishCommand();Check(View.Equipment!.Single(e=>e.Id.Value==_extractor).Id==machine.Id,"pack preserves existing equipment identity");
    }
    private async Task Fleet()
    {
        Call(_app,"CloseTablet");Move(View.Fleet!.Dealer);Refresh();await Open("dealer");await Shot("i6b-05-dealer-ru-1100");
        var listing=View.Fleet!.Stock.First(v=>v.ModelId=="sputnik20");await Click("FleetBuy_"+listing.Id);await FinishCommand();Ticks(6);Refresh();Check(View.Fleet!.Vehicles.Any(v=>v.Vehicle.Id==listing.Id),"dealer UI buys an actual listed vehicle");
        var paver=View.Fleet!.Stock.First(v=>v.ModelId=="trakt_d1");await Click("FleetBuy_"+paver.Id);await FinishCommand();Ticks(6);Refresh();
        Call(_app,"CloseTablet");var truck=View.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_truck);Move(truck.Position);Refresh();await Open("fleet");await Click("FleetBoard_"+_truck);await FinishCommand();Check(View.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_truck).Vehicle.Driver==1,"fleet board action retains physical driver");Call(_app,"CloseTablet");Move(View.Buildings.Single(b=>b.Id.Value==8).Position);Refresh();await Open("fleet");await Click("FleetBoard_"+_truck);await FinishCommand();
        Call(_app,"CloseTablet");Move(View.Buildings.Single(b=>b.Id.Value==8).Position);Prepare(new(ActionKind.AssignVehicle,VehicleId:_truck,Enterprise:new(8)));Refresh();
    }
    private async Task Stock()
    {
        Call(_app,"CloseTablet");var storage=View.Buildings.Single(b=>b.Kind==BuildingKind.Warehouse);Move(storage.Position);Refresh();Call(_app,"OpenObjectTablet","building",storage.Id.Value);await Frames();
        Check(Named<StoragePanel>("StoragePanel").IsVisibleInTree(),"warehouse opens dedicated storage workspace");await Click("StorageNav_transfer");Pick("StorageManualSource",View.Participant.InventoryId.Value);Pick("StorageManualDestination",storage.InputId.Value);await Frames();
        var item=Named<OptionButton>("StorageManualItem");Check(item.ItemCount>0,"actual backpack goods populate transfer selector");string goods=item.GetItemMetadata(item.Selected).AsString();long before=Stock(storage.InputId,goods);await Click("StorageManualOne",true);await FinishCommand();Check(Stock(storage.InputId,goods)==before+1,"transfer one respects actual stock");
        Named<SpinBox>("StorageManualQuantity").Value=1;await Click("StorageManual");await FinishCommand();Check(Stock(storage.InputId,goods)==before+2,"transfer selected quantity");
        if(Stock(View.Participant.InventoryId,goods)>0){await Click("StorageManualAll");await FinishCommand();Check(Stock(View.Participant.InventoryId,goods)==0,"store all transfers only available goods");}
        await Click("StorageNav_stock");await Shot("i6b-06-warehouse-ru-1100");await Click("StorageTakeAll");await FinishCommand();Check(Stock(View.Participant.InventoryId,goods)>0,"warehouse take all returns physical goods");
    }
    private async Task Trade()
    {
        Call(_app,"CloseTablet");Move(View.ProductionContent!.Terminal);Refresh();Call(_app,"OpenCapitalTablet");await Frames();
        var offer=View.CapitalOffers.First(o=>o.RemainingQuantity>0&&Stock(View.Participant.InventoryId,o.ItemId)>0&&o.CreatedTick<View.Tick);await Click("CapitalGoods_"+offer.ItemId);await Shot("i6b-07-exchange-ru-1100");await Click((Math.Min(Stock(View.Participant.InventoryId,offer.ItemId),offer.RemainingQuantity)>1?"CapitalSellOne_":"CapitalSellAll_")+offer.Id);await FinishCommand();Check(_session.CommandTape.Last(c=>c.AuthenticatedActor.Value==1).Command.Action.Kind==ActionKind.SellAvailableToCapital,"capital sale uses original ordinary command");
        if(All(_app).OfType<UiDisclosure>().FirstOrDefault(c=>c.Name=="ExchangeMore") is {} extra){extra.Open();await Frames();}
        await Click("ExchangePage5");long money=View.CashMinor;await Click("ImportBuy_steel");await FinishCommand();Check(View.CashMinor<money,"import UI uses published price");await Click("ImportTakeAll_steel");await FinishCommand();Check(Stock(View.Participant.InventoryId,"steel")>0,"paid import pickup remains physical transfer");
        await Click("ExchangePage1");await Frames();Check(All(_app).OfType<ParticipantMarketTablet>().Any(p=>p.IsVisibleInTree()),"participant market remains reachable");
    }
    private async Task Logistics()
    {
        await Factory(8);await Click("WoodworkingNav_delivery");Check(Named<OptionButton>("RouteSource").IsVisibleInTree()&&Named<OptionButton>("RouteItem").IsVisibleInTree()&&Named<OptionButton>("RouteDestination").IsVisibleInTree(),"source goods recipient flow is directly visible");await Shot("i6b-08-logistics-ru-1100");await Click("RouteApply");await FinishCommand();Check(View.Logistics.Single(l=>l.EnterpriseId.Value==8).Queue.Any(j=>j.Enabled&&j.Source.Value!=0),"route UI saves real enabled transport queue");await Click("RouteStop");await FinishCommand();
        await Open("services");Check(Named<SpinBox>("ServiceQuantity").IsVisibleInTree(),"delivery form retains draft while inspecting orders");Named<SpinBox>("ServiceQuantity").Value=7;Refresh();await Frames();Check(Named<SpinBox>("ServiceQuantity").Value==7,"delivery draft survives ordinary refresh");
    }
    private async Task Roads()
    {
        await Open("roads");var accept=All(_app).OfType<Button>().FirstOrDefault(b=>b.IsVisibleInTree()&&!b.Disabled&&(b.Text=="Принять задание"));if(accept is not null){UiNavigation.Reveal(accept);await Frames();await RawClick(accept);await FinishCommand();Check(View.Roads!.Orders.Any(o=>o.Assignee==1),"ordinary city road order accepted through UI");}
        Check(All(_app).OfType<LineEdit>().Any(n=>n.Name=="RoadRoute"),"advanced I3 route input retained behind disclosure");
    }
    private async Task City()
    {
        await Open("account");bool previous=View.PersonalAccount!.AutoPay;await Click("AccountAutoPay");await FinishCommand();Check(View.PersonalAccount!.AutoPay!=previous,"finance UI preserves ordinary autopay action");
        await Open("city");await Shot("i6b-09-city-ru-1100");await Click("CityNav_labor");Check(All(_app).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains("Простой не оплачивается")),"labour explanation uses existing hiring rules");
        Call(_app,"CloseTablet");string hash=_session.Engine.ComputeStateHash();int count=_session.CommandTape.Length;Call(_app,"OpenCityMap");await Frames();Call(_app,"CloseTablet");Call(_app,"OpenHelp","basics");await Frames();Call(_app,"CloseHelp");Check(hash==_session.Engine.ComputeStateHash()&&count==_session.CommandTape.Length,"map/help opening preserves economy and tape");
    }
    private async Task Landscape()
    {
        Call(_app,"CloseTablet");Set(_app,"_language","en");Call(_app,"RebuildChrome");GetWindow().Size=new(960,440);GetWindow().ContentScaleSize=GetWindow().Size;await Frames();await Factory(_industries["machinery"]);await Shot("i6b-10-landscape-en-960");
        foreach(var dimensions in new[]{new Vector2I(960,440),new Vector2I(800,360)})
        {
            Call(_app,"CloseTablet");GetWindow().Size=dimensions;GetWindow().ContentScaleSize=dimensions;await Frames();await Factory(8);UiNavigation.Reveal(Named<Button>("WoodworkingWork"));await Frames();Check(GetViewportRect().Encloses(Named<Button>("WoodworkingWork").GetGlobalRect()),"landscape production primary inside viewport "+dimensions);
            Check(GetViewportRect().Encloses(Named<Button>("TabletClose").GetGlobalRect()),"landscape close inside viewport "+dimensions);await Click("TabletClose");await Open("inventory");Check(Named<OptionButton>("PlayerSectionPicker").IsVisibleInTree(),"landscape uses approved compact section picker "+dimensions);Call(_app,"CloseTablet");
        }
        Call(_app,"OpenHelp","dealership");await Frames();Check(GetViewportRect().Encloses(Named<Button>("HelpClose").GetGlobalRect()),"compact help has reachable close");await Click("HelpClose");
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
    private async Task Demolition()
    {
        Call(_app,"CloseTablet");GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;Set(_app,"_language","ru");Call(_app,"RebuildChrome");var b=View.Buildings.Single(b=>b.Id.Value==8);Move(b.Position);Refresh();await Factory(8);await Click("WoodworkingNav_object");await Click("ObjectManagementToggle");await Click("PrepareDemolition");await FinishCommand();
        int bound=200;while(_session.Engine.QuoteDemolition(new(1),8,false)!.Blockers.Length>0&&bound-->0)Drive();Check(bound>0,"safe demolition preparation drains existing work");await Frames();await Click("DemolitionPreview");Check(!Named<Button>("DemolitionHold").Disabled,"fresh safe demolition quote enables hold");int tape=_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1);await Hold(.15);Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==tape,"short demolition press sends no command");await Hold(2.2);Check(_session.CommandTape.Count(c=>c.AuthenticatedActor.Value==1)==tape+1,"unchanged two-second safe hold sends exactly one command");await FinishCommand();Check(!View.Buildings.Any(b=>b.Id.Value==8),"Core executes only safely quoted demolition");
    }
    private ContextActionsPanel HudPanel=>Field<ContextActionsPanel>(_app,"_contextActions");
    private async Task HudKey(Key code)
    {
        int window=GetWindow().GetWindowId();Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=code,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventKey{WindowId=window,Keycode=code,Pressed=false});await Frames();
    }
    private void HudBounds()
    {
        var rect=HudPanel.GetGlobalRect();var size=GetViewportRect().Size;var safe=UiSafeArea.Read(HudPanel);
        Check(rect.Position.X>=safe.Left&&rect.End.X<=size.X-safe.Right+1&&rect.End.Y<=size.Y-safe.Bottom-10&&rect.Position.Y>=Field<float>(_app,"_roadCardTop"),"dock stays within usable viewport "+size+" "+rect);
        Check(Math.Abs(rect.GetCenter().X-(safe.Left+size.X-safe.Right)/2)<2,"dock is horizontally centred");
        var primary=All(HudPanel).OfType<HFlowContainer>().Single();var rows=primary.GetChildren().OfType<Control>().ToArray();Check(rows.Length is >=1 and <=3&&rows.All(r=>Math.Abs(r.GlobalPosition.Y-rows[0].GlobalPosition.Y)<2),"semantic primary actions share one horizontal row");
        var bar=Named<HBoxContainer>("HudRow");Check(bar.GetGlobalRect().End.X<=size.X+1&&bar.Size.Y<=44,"top navigation remains one compact row");
        var stick=Field<VirtualJoystick>(_app,"_stick");if(stick is not null&&stick.IsVisibleInTree())Check(!stick.GetGlobalRect().Intersects(rect),"dock does not intersect joystick");
    }
    private async Task Hud()
    {
        Check(_session.Engine.Configuration().Id=="compact_valley"&&_session.Engine.Configuration().Participants.Length==4,"one existing compact-valley world with three ordinary NPCs");
        Check(!All(_app).Any(n=>n.Name=="PlayMiniMap")&&!All(Field<Control>(_app,"_playChrome")).OfType<DebugBoard>().Any(),"ordinary HUD creates no minimap or DebugBoard");
        var bar=Named<HBoxContainer>("HudRow");Check(bar.GetChildren().OfType<Button>().Select(b=>b.Name.ToString()).SequenceEqual(new[]{"HudMenu","HudTablet","OpenCityMap","AccountNotification","HudHelp"}),"exact five-button HUD order");
        HudBounds();await Shot("01-desktop-collapsed");
        var panel=HudPanel;string hash=_session.Engine.ComputeStateHash();int tape=_session.CommandTape.Length;var primary=All(panel).OfType<HFlowContainer>().Single().GetChildren().OfType<Control>().First();var primaryPosition=primary.GlobalPosition;
        await Click("ContextMore",true);Check(panel.Expanded,"native more expands upward across refresh");HudBounds();Check(Math.Abs(primary.GlobalPosition.Y-primaryPosition.Y)<2,"expansion preserves bottom action row position");await Shot("02-desktop-expanded");
        var scroll=All(panel).OfType<ScrollContainer>().Single();var last=panel.Selection.Choices.Last();var row=Named<Button>(ContextActionsPanel.ButtonName(last.Key));panel.Selection.Select(last.Key);Drive(3);await Frames();Check(panel.Expanded&&panel.Selection.SelectedKey==last.Key&&ReferenceEquals(row,Named<Button>(row.Name.ToString())),"ticks retain selection and actual keyed control");
        UiNavigation.Reveal(row);await Frames();Check(scroll.ScrollVertical>0&&scroll.GetGlobalRect().HasPoint(row.GetGlobalRect().GetCenter()),"bounded grouped list can reach its last action");
        await Click("ContextMore");Check(!panel.Expanded&&panel.Visible,"more collapses without dismissal");await Click("ContextMore");await HudKey(Key.Escape);Check(!panel.Expanded&&panel.Visible,"Escape collapses upward list");
        await Click("ContextMore");var outside=new Vector2(25,24);int window=GetWindow().GetWindowId();Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,ButtonIndex=MouseButton.Left,Pressed=true,Position=outside,GlobalPosition=outside});await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,ButtonIndex=MouseButton.Left,Pressed=false,Position=outside,GlobalPosition=outside});await Frames();Check(!panel.Expanded&&Field<object>(_app,"_surface").ToString()=="Play","outside click collapses and shields Menu underneath");
        await Click("ContextDismiss");Check(panel.Dismissed&&!panel.Visible&&!Field<Control>(_app,"_contextShield").Visible,"close fully hides dock and shield");Drive(3);await Frames();Check(panel.Dismissed&&!panel.Visible,"ordinary ticks and refresh do not reopen dismissed context");
        tape=_session.CommandTape.Length;await HudKey(Key.Enter);Check(panel.Visible&&!panel.Dismissed&&_session.CommandTape.Length==tape,"explicit Enter restores dock without executing a hidden command");
        GetWindow().Size=new(960,440);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(10);HudBounds();await Shot("03-landscape-dock");
        // Native touch toggles both ways, without modifying world commands.
        var more=Named<Button>("ContextMore");var point=more.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventScreenTouch{WindowId=window,Index=0,Position=point,Pressed=false});await Frames();Check(panel.Expanded,"native touch opens more");HudBounds();await HudKey(Key.Escape);
        GetWindow().Size=new(800,360);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(10);HudBounds();await Click("ContextMore");HudBounds();await Click("ContextMore");
        Set(_app,"_language","en");Call(_app,"RebuildChrome");Refresh();await Frames(10);HudBounds();Check(Named<Button>("HudMenu").Text=="Menu","English HUD remains available");
        Set(_app,"_language","ru");Call(_app,"RebuildChrome");Refresh();GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;await Frames(10);
        // Preserve the I5 pending and authority path with one real equipment transaction.
        panel=HudPanel;await Click("ContextMore");long id=View.Equipment!.Single().Id.Value;var place=panel.Selection.Choices.Single(c=>c.Key=="machine:place:"+id);row=Named<Button>(ContextActionsPanel.ButtonName(place.Key));UiNavigation.Reveal(row);await Frames();tape=_session.CommandTape.Length;await RawClick(row,true);Check(_session.CommandTape.Length==tape+1&&row.Disabled,"real command queues once and pending disables duplicate");await RawClick(row);Check(_session.CommandTape.Length==tape+1,"disabled repeat sends no command");Drive();await Frames();Check(View.Equipment!.Single().Deployed==true,"ordinary Core accepts context placement");
        var auto=panel.Selection.Choices.Single(c=>c.Key=="machine:auto:"+id);Check(!panel.Selection.PrimaryKeys().Contains(auto.Key),"automatic mode is an additional action");row=Named<Button>(ContextActionsPanel.ButtonName(auto.Key));UiNavigation.Reveal(row);await Frames();tape=_session.CommandTape.Length;await RawClick(row,true);Check(_session.CommandTape.Length==tape+1&&_session.CommandTape.Last().Command.Action==auto.Command,"real additional-row click sends its exact payload once across refresh");Drive();await Frames();Check(View.Equipment!.Single().Mode==EquipmentMode.Automatic,"ordinary Core accepts additional automatic-mode command");
        int before=_session.CommandTape.Length;Call(_app,"ConfirmContextChoice",place);Check(_session.CommandTape.Length==before,"stale payload cannot repeat an obsolete command");
        await HudKey(Key.Escape);await Click("ContextDismiss");var at=View.Participant.Position;Prepare(PlayerDesk.Move(at.X,at.Z+1));_session.SetPaused(true);Refresh();await Frames(10);Check(!HudPanel.Dismissed&&HudPanel.Visible,"new physical location reopens meaningful context");HudBounds();
        await Click("HudTablet");await Frames();var tablet=Named<TabletPanel>("GameTablet");var footer=Named<HBoxContainer>("TabletFooter");var clock=Named<Label>("TabletClock");var toggle=Named<CheckButton>("TabletTime");Check(footer.Size.Y<=44&&toggle.Text.Length==0&&clock.Text.StartsWith("◷ ")&&clock.Text.Count(c=>c=='·')==1,"single compact footer status and 44px touch switch");Check(toggle.GetGlobalRect().End.X<=tablet.GetGlobalRect().End.X,"footer switch stays inside tablet");await Shot("04-tablet-footer");
        var player=Field<PlayerTablet>(_app,"_playerTablet");player.OpenSection("services");Refresh();await Frames();Check(player.HelpTopic=="services","Delivery remains in player tablet");
        player.OpenSection("notices");await Frames();Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Notices,"Notices remain accessible through tablet");Call(_app,"CloseTablet");Call(_app,"OpenPlayerTablet");await Frames();Field<PlayerTablet>(_app,"_playerTablet").OpenSection("tutorial");await Frames();Check(Field<TabletState>(_app,"_tabletState").Kind==TabletKind.Tutorial,"Guide remains accessible through tablet");Call(_app,"CloseTablet");Call(_app,"OpenPlayerTablet");await Frames();toggle=Named<CheckButton>("TabletTime");bool running=Field<TabletState>(_app,"_tabletState").PauseTime;await RawClick(toggle);Check(Field<TabletState>(_app,"_tabletState").PauseTime!=running,"small footer switch retains existing pause delegate");await Click("TabletClose");await HudKey(Key.M);Check(Field<Control>(_app,"_cityMapPanel").IsVisibleInTree(),"full city map remains accessible with M");Call(_app,"DismissMaps");
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];_output=Arg("--output");_case=Arg("--case");System.IO.Directory.CreateDirectory(_output);MouseFilter=MouseFilterEnum.Ignore;
            if(_case=="all")PrepareFixture(Arg("--repository"),Arg("--fixture"));else if(_case=="hud"){_session=CheckpointStore.Decode(System.IO.File.ReadAllText(Arg("--fixture")));}else{_session=CheckpointStore.Decode(System.IO.File.ReadAllText(Arg("--fixture")));foreach(var b in _session.Engine.Configuration().Buildings!.Where(b=>b.TypeId!="warehouse"))_industries[b.TypeId]=b.Id;_truck=View.Fleet!.Vehicles.First(v=>v.Model.Id=="vector200").Vehicle.Id;_extractor=View.Equipment!.First().Id.Value; }
            _session.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));if(_case=="prepare")CompletePreparation(Arg("--fixture"));CheckpointStore.Save(_session,_output+"/prepared.save.json");GetWindow().Size=new(1100,720);GetWindow().ContentScaleSize=GetWindow().Size;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Call(_app,"DismissMaps");Set(_app,"_language","ru");string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;CheckpointStore.Save(_session,save);Call(_app,"Load");Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_session=Field<GameSession>(_app,"_session");_session.SetPaused(true);Refresh();await Frames();
            if(_case!="hud"){var label=new Label{Text="I6-B · подготовленные здания · Малая долина · 3 NPC",MouseFilter=MouseFilterEnum.Ignore,Position=new(12,GetViewportRect().Size.Y-24)};label.AddThemeFontSizeOverride("font_size",12);AddChild(label);Resized+=()=>label.Position=new(12,GetViewportRect().Size.Y-24);}
            var phases=new (string Key,Func<Task> Run)[]{("hud",Hud),("navigation",Navigation),("production",Production),("equipment",Equipment),("fleet",Fleet),("stock",Stock),("trade",Trade),("logistics",Logistics),("roads",Roads),("city",City),("landscape",Landscape),("demolition",Demolition)};
            var selected=_case=="regression"?phases.Where(p=>p.Key is "navigation" or "production" or "equipment" or "trade" or "landscape").ToArray():_case is "all" or "prepare"?phases.Where(p=>p.Key!="hud").ToArray():_case.StartsWith("from-")?phases.SkipWhile(p=>p.Key!=_case[5..]).ToArray():phases.Where(p=>p.Key==_case).ToArray();Check(selected.Length>0,"explicit native phase selection");
            foreach(var phase in selected){CheckpointStore.Save(_session,_output+"/"+phase.Key+".input.save.json");GD.Print("I6B_PHASE "+phase.Key);await phase.Run();}

            _session.Engine.ValidateInvariants();var restored=CheckpointStore.Decode(CheckpointStore.Encode(_session));Check(restored.Engine.ComputeStateHash()==_session.Engine.ComputeStateHash(),"ordinary checkpoint round trip");restored.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));restored.SetPaused(false);for(int n=0;n<3;n++){StepOrdinary(restored);Ticks(1);Check(restored.Engine.ComputeStateHash()==_session.Engine.ComputeStateHash(),"checkpoint future with restored ordinary controllers "+n);}
            CheckpointStore.Save(_session,_output+"/final.save.json");System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",selection=_case,checks=_checks,scope="Prepared buildings on I6-A Small valley / 3 ordinary NPCs; Linux software GL/synthetic input, not physical device acceptance"},new JsonSerializerOptions{WriteIndented=true}));GD.Print("DELSK_I6B_PASS");_app.QueueFree();_app=null!;await Frames(4);GC.Collect();GC.WaitForPendingFinalizers();await Frames(2);GD.Print("I6B remaining orphan nodes: "+Node.GetOrphanNodeIds().Count);Node.PrintOrphanNodes();GetTree().Quit();
        }
        catch(Exception ex){if(_case=="hud")try{await Shot("failure");}catch{}System.IO.File.WriteAllText(_output+"/failure.txt",ex.ToString());if(_session is not null)CheckpointStore.Save(_session,_output+"/failed.save.json");GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own fleet, a separate illustrated dealer and the shared manual cargo form.</summary>
public partial class FleetPanel : VBoxContainer
{
    private sealed class Listing
    {
        public required Control Shell;public Label Terms=GameUi.Text(),Price=GameUi.Text("",22),Why=GameUi.Hint("");public Button Buy=null!;
    }
    private sealed class VehicleCard
    {
        public required Control Shell;public Label Status=GameUi.Text(),Capacity=GameUi.Hint(""),Crew=GameUi.Hint(""),Guidance=GameUi.Hint("");
        public UiResourceList Stock=new();public ProgressBar Fill=new(){ShowPercentage=false,CustomMinimumSize=new(0,10)},Work=new(){ShowPercentage=true,CustomMinimumSize=new(0,10)};
        public VBoxContainer Controls=new(),Advanced=new();
    }
    public event Action<long>? RoadWorkRequested;
    private readonly string _language;private readonly ScenarioDefinition _scenario;
    private readonly Action<GameAction> _submit;private readonly Func<GameAction,ActionAccess> _access;
    private readonly UiSections _sections=new("FleetNav_");private readonly VBoxContainer _stock=new(),_owned=new(),_platforms=new();
    private readonly GridContainer _catalog=new(){Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly OptionButton _category=new(){Name="FleetCategory",FitToLongestItem=false,ClipText=true};
    private readonly Label _summary=GameUi.Hint(""),_emptyDealer=GameUi.Hint(""),_ownSummary=GameUi.Hint(""),_refresh=GameUi.Hint("");
    private readonly VBoxContainer _orders=new();
    private string _orderKey="";
    private readonly InventoryTransferPanel _transfer;
    private readonly Dictionary<long,Listing> _listings=[];private readonly Dictionary<long,VehicleCard> _vehicles=[];
    private readonly Dictionary<Container,HFlowContainer> _flows=[];private readonly Dictionary<Container,Container> _advanced=[];
    private readonly List<(Button Button,GameAction Action)> _actions=[];
    private PlayerView? _view;private bool _pending;private long _focus;private string _controlsKey="uninitialized";
    public FleetPanel(string language,Action<GameAction> submit,Func<GameAction,ActionAccess> access,ScenarioDefinition scenario)
    {
        Name="FleetPanel";_language=language;_submit=submit;_access=access;_scenario=scenario;AddChild(_sections);
        var own=_sections.Add("owned",L("Мой транспорт","My vehicles"));own.AddChild(_ownSummary);own.AddChild(_owned);
        var dealer=_sections.Add("dealer",L("Автосалон","Dealer"));dealer.AddChild(GameUi.Hint(L("Выберите модель по назначению и вместимости. Для покупки подойдите ко входу в автосалон.","Choose a model for its purpose and capacity. Visit the dealer entrance to buy.")));
        foreach(var name in new[]{L("Все модели","All models"),L("Легковые","Passenger"),L("Грузовые","Cargo"),L("Добывающие","Extraction"),L("Дорожные","Road works")})_category.AddItem(name);
        dealer.AddChild(_category);dealer.AddChild(_stock);_stock.AddChild(_catalog);_emptyDealer.Text=L("Машин в продаже пока нет. Включите время и дождитесь новой поставки.","No vehicles in stock. Run time and wait for the next delivery.");dealer.AddChild(_emptyDealer);dealer.AddChild(_orders);dealer.AddChild(_refresh);
        dealer.AddChild(new UiDisclosure(L("Условия покупки","Purchase details"),_summary));_category.ItemSelected+=_=>FilterCatalog();
        var cargo=_sections.Add("cargo",L("Груз","Cargo"));cargo.AddChild(GameUi.Intro(L("Погрузка и разгрузка","Load & unload"),L("Выберите источник, товар и получателя. Кнопки учитывают свободное место и резервы; перенос выполняется рядом с грузом.","Choose source, goods and recipient. Amounts allow for capacity and holds; transfers require you to be beside the cargo."),"truck",64));
        _transfer=new(scenario,language,"FleetTransfer",submit,access);cargo.AddChild(GameUi.Card(_transfer));
        var platforms=_sections.Add("platforms",L("Погрузка и оснащение","Platforms & equipment"));platforms.AddChild(GameUi.Hint(L("Площадки ускоряют работу с грузом у здания. Условия оплаты и материалы показаны возле действия.","Platforms improve cargo handling at buildings. Each action shows its price and materials.")));platforms.AddChild(_platforms);
        Resized+=()=>_catalog.Columns=Size.X>=700?2:1;
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private EquipmentTypeDefinition EquipmentType(VehicleModel model)=>_view!.Fleet!.EquipmentTypes.Single(t=>t.Id==model.EquipmentTypeId);
    private string ResourceItem(VehicleModel model)=>EquipmentType(model).ItemId;
    private string ClassTitle(EquipmentTypeDefinition type)=>type.Id switch{"extract_field"=>L("Полевой комбайн","Field harvester"),"extract_forest"=>L("Лесозаготовительная машина","Forestry machine"),"extract_dairy"=>L("Молочная машина","Dairy machine"),"extract_mine"=>L("Горная машина","Mining machine"),_=>type.Id};
    private string ModelName(VehicleModel model)
    {
        if(model.EquipmentTypeId is null)return model.Name;
        var type=EquipmentType(model);
        return type.Id is "extract_field" or "extract_forest" or "extract_dairy" or "extract_mine"
            ? ClassTitle(type)+L(" — уровень "," — level ")+model.EquipmentLevel
            : L("Добыча: ","Extract: ")+PlayerText.Item(_language,type.ItemId)+" · "+(model.EquipmentLevel==1?L("малая","small"):L("большая","large"));
    }
    public void ShowCatalog(bool extractionOnly=false){_sections.Select("dealer");if(extractionOnly)_category.Select(3);FilterCatalog();}
    public void FocusVehicle(long id){_focus=id;_sections.Select("owned");if(_vehicles.TryGetValue(id,out var row))UiNavigation.Reveal(row.Shell);}
    private HFlowContainer Flow(Container host)
    {if(host is HFlowContainer existing)return existing;if(!_flows.TryGetValue(host,out var flow)){flow=TabletDesign.Flow();host.AddChild(flow);_flows.Add(host,flow);}return flow;}
    private void ActionButton(Container host,string name,string text,GameAction action)
    {
        if(_advanced.TryGetValue(host,out var secondary)&&(name.StartsWith("FleetTurn_")||name.StartsWith("FleetKit_")||name.StartsWith("FleetAssign_")))host=secondary;
        var button=GameUi.Button(name,text,()=>{});GameUi.Command(button,()=>{if(_pending||!_access(action).Allowed)return;_pending=true;_submit(action);});Flow(host).AddChild(button);_actions.Add((button,action));
    }
    private static void Clear(Container host){foreach(Node n in host.GetChildren()){host.RemoveChild(n);n.QueueFree();}}
    private void FilterCatalog()
    {
        if(_view?.Fleet is not {} fleet)return;
        foreach(var entry in _listings)
        {
            var listing=fleet.Stock.FirstOrDefault(s=>s.Id==entry.Key);if(listing is null)continue;var model=fleet.Models.Single(m=>m.Id==listing.ModelId);
            entry.Value.Shell.Visible=_category.Selected switch{1=>model.Id=="sputnik20",2=>model.EquipmentTypeId is null&&!model.MaterialsOnly&&model.Id!="sputnik20",3=>model.EquipmentTypeId is not null,4=>model.MaterialsOnly,_=>true};
        }
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;Visible=view.Fleet is not null;if(view.Fleet is not {} fleet)return;
        _ownSummary.Text=L("Ваших машин: ","Your vehicles: ")+fleet.Vehicles.Length+L(". Откройте груз для переноса ресурсов или выберите машину для поездки и работы.",". Open Cargo to move resources, or choose a vehicle to travel and work.");
        _summary.Text=L("Вход автосалона: ","Dealer entrance: ")+$"({fleet.Dealer.X},{fleet.Dealer.Z})"+L(". Покупка проходит через обычное окно заявок. Машина остаётся на стоянке; сядьте и вывезите её. Во время паузы заявки и перемещения ожидают включения времени.",". Purchases use the ordinary request window. The vehicle stays parked; board it and drive away. Requests and movement wait while time is paused.");
        _emptyDealer.Visible=fleet.Stock.IsEmpty;
        foreach(long id in _listings.Keys.Where(id=>!fleet.Stock.Any(v=>v.Id==id)).ToArray()){var row=_listings[id];row.Shell.GetParent().RemoveChild(row.Shell);row.Shell.QueueFree();_listings.Remove(id);}
        foreach(var listing in fleet.Stock)
        {
            var model=fleet.Models.Single(m=>m.Id==listing.ModelId);long id=listing.Id;
            if(!_listings.TryGetValue(id,out var row))
            {
                var body=new VBoxContainer();var shell=GameUi.Card(body);row=new(){Shell=shell};body.AddChild(TabletDesign.Heading(ModelName(model),18));
                var description=new HBoxContainer();description.AddChild(GameUiArt.Vehicle(model,new(112,82)));var words=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};words.AddChild(row.Price);words.AddChild(row.Terms);description.AddChild(words);body.AddChild(description);
                row.Buy=GameUi.Button("FleetBuy_"+id,L("Купить","Buy"),()=>{},true);var captured=row;GameUi.Command(row.Buy,()=>{if(_pending||_view?.Fleet?.Stock.Any(s=>s.Id==id)!=true)return;_pending=true;_submit(new(ActionKind.BuyVehicle,VehicleId:id));});body.AddChild(row.Buy);body.AddChild(row.Why);_catalog.AddChild(shell);_listings.Add(id,captured);
            }
            row.Price.Text=PlayerText.Money(model.PriceMinor)+L(" монет"," coins");
            var typeText=model.EquipmentTypeId is null?"":string.Join(", ",EquipmentType(model).CompatibleItems().Select(item=>PlayerText.Item(_language,item)));
            row.Terms.Text=L("Скорость до ","Top speed ")+model.Speed+L(" км/ч · объём "," km/h · capacity ")+model.CapacityVolume+"\n"+(model.MaterialsOnly?L("Дорожные работы","Road construction"):model.Id=="sputnik20"?L("Поездки по городу","City travel"):model.EquipmentTypeId is null?L("Перевозка грузов","Cargo transport"):L("Подходит для ","Works with ")+typeText);
            if(model.EquipmentTypeId is {} typeId){var data=fleet.EquipmentTypes.Single(t=>t.Id==typeId).Levels.Single(l=>l.Level==model.EquipmentLevel);row.Terms.Text+="\n"+data.CycleQuantity+L(" за цикл · команда до "," per cycle · crew up to ")+data.CrewCapacity;}
            var access=_access(new(ActionKind.BuyVehicle,VehicleId:id));row.Buy.Disabled=pending||view.Participant.Busy||fleet.OwnRequests.Length>0||view.AvailableCashMinor<model.PriceMinor||!access.Allowed;
            row.Why.Text=pending?L("Заявка отправляется. Включите время.","Request queued. Run time."):fleet.OwnRequests.Length>0?L("Дождитесь результата текущей заявки.","Wait for your current purchase request."):view.AvailableCashMinor<model.PriceMinor?GameUi.Reason(_language,"NoMoney"):!access.Allowed?GameUi.Reason(_language,access.ReasonCode):view.Participant.Busy?GameUi.Reason(_language,"ParticipantBusy"):"";row.Why.Visible=row.Why.Text.Length>0;row.Buy.TooltipText=row.Why.Text;
        }
        ShowCatalogWaits(view,fleet,pending);
        FilterCatalog();
        string key=string.Join(',',(view.Equipment??[]).Select(e=>$"{e.Id.Value}:{e.Level}:{e.ResourceItem}:{e.Deployed}:{e.Mode}:{e.PersonalJobId!=0}:{e.CycleId}"))+"|"+string.Join(',',fleet.Vehicles.Select(v=>$"{v.Vehicle.Id}:{v.Vehicle.Driver}:{v.Vehicle.HandlingLevel}:{v.Vehicle.Installation?.Level}:{v.Logistics.Value}"))+"|"+string.Join(',',view.Enterprises.Select(e=>e.Id.Value))+"|"+string.Join(',',view.Buildings.Select(b=>b.Id.Value))+"|"+string.Join(',',fleet.Platforms.Select(p=>$"{p.Id}:{p.Level}:{p.Installation?.Level}:{p.Group.Headcount}:{p.Group.WageMinor}:{p.Group.PendingWageMinor}"));
        if(key!=_controlsKey)
        {
            var scroll=UiNavigation.KeepScroll(this);var focus=UiNavigation.KeepFocus(this);_controlsKey=key;Clear(_owned);Clear(_platforms);_vehicles.Clear();_flows.Clear();_advanced.Clear();_actions.Clear();
            if(fleet.Vehicles.IsEmpty){_owned.AddChild(GameUi.Intro(L("Своих машин пока нет","No vehicles owned yet"),L("Откройте автосалон, чтобы сравнить модели и выбрать подходящую.","Open the dealer to compare models and choose one."),"truck",80));_owned.AddChild(GameUi.Button("FleetOpenDealer",L("Открыть автосалон","Open dealer"),()=>ShowCatalog(),true));}
            foreach(var item in fleet.Vehicles)
            {
                var vehicle=item.Vehicle;var body=new VBoxContainer();var card=new VehicleCard{Shell=GameUi.Card(body)};body.AddChild(TabletDesign.Heading(ModelName(item.Model),20));
                var hero=new HBoxContainer();hero.AddChild(GameUiArt.Vehicle(item.Model,new(140,100)));var state=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};state.AddChild(card.Status);state.AddChild(card.Capacity);state.AddChild(card.Fill);hero.AddChild(state);body.AddChild(hero);
                body.AddChild(card.Guidance);body.AddChild(card.Work);body.AddChild(card.Crew);body.AddChild(card.Stock);body.AddChild(card.Controls);body.AddChild(new UiDisclosure(L("Настройки и оснащение","Settings & equipment"),card.Advanced));_advanced.Add(card.Controls,card.Advanced);_owned.AddChild(card.Shell);_vehicles.Add(vehicle.Id,card);
                Flow(card.Controls).AddChild(GameUi.Button("FleetFind_"+vehicle.Id,L("К машине","Go to vehicle"),()=>{if(_pending)return;var current=_view!.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==vehicle.Id).Position;_pending=true;_submit(new(ActionKind.MoveTo,Target:current));}));
                ActionButton(card.Controls,"FleetBoard_"+vehicle.Id,vehicle.Driver==0?L("Сесть","Board"):L("Выйти","Leave"),new(vehicle.Driver==0?ActionKind.BoardVehicle:ActionKind.LeaveVehicle,VehicleId:vehicle.Id));
                Flow(card.Controls).AddChild(GameUi.Button("FleetCargo_"+vehicle.Id,L("Груз","Cargo"),()=>{_sections.Select("cargo");_transfer.FocusInventory(vehicle.CargoInventory.Value);}));
                if(item.Model.MaterialsOnly&&view.Roads is not null)Flow(card.Controls).AddChild(GameUi.Button("FleetRoadWork_"+vehicle.Id,L("Строить дорогу","Build road"),()=>RoadWorkRequested?.Invoke(vehicle.Id)));
                if(view.Roads is not null)ActionButton(card.Controls,"FleetTurn_"+vehicle.Id,L("Развернуться","Turn around"),new(ActionKind.TurnVehicle,VehicleId:vehicle.Id));
                foreach(var kit in fleet.Kits.Where(k=>k.Level>vehicle.HandlingLevel&&(k.Level==2?vehicle.ModelId=="vector200":vehicle.ModelId is "muravey70" or "eger70" or "trakt_d1")))
                    ActionButton(card.Controls,"FleetKit_"+vehicle.Id+"_"+kit.Level,L("Погрузочный комплект ","Handling kit ")+kit.Throughput+" · "+PlayerText.Money(kit.PriceMinor)+" + "+string.Join(", ",kit.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity)),new(ActionKind.InstallVehicleKit,VehicleId:vehicle.Id,EquipmentLevel:kit.Level,Source:vehicle.CargoInventory));
                if(item.Model.EquipmentTypeId is not null)
                {
                    var equipment=view.Equipment!.Single(e=>e.Id.Value==vehicle.Id);var eid=equipment.Id;
                    ActionButton(card.Controls,"I4Place_"+vehicle.Id,L("Разместить","Place"),new(ActionKind.PlaceEquipment,Equipment:eid));
                    var machineType=fleet.EquipmentTypes.Single(t=>t.Id==item.Model.EquipmentTypeId);var machineLevel=machineType.Levels.Single(l=>l.Level==equipment.Level);string harvestItem=equipment.ResourceItem??machineType.ItemId;
                    ActionButton(card.Controls,"I4Start_"+vehicle.Id,L("Начать добычу","Start harvest"),new(ActionKind.StartHarvesting,Equipment:eid,ItemId:harvestItem,Quantity:machineLevel.CycleQuantity));
                    if(_scenario.Economy?.Fleet?.EquipmentDealerVersion==2&&machineType.Levels.FirstOrDefault(l=>l.Level==equipment.Level+1) is {} next)
                        ActionButton(card.Controls,"I6Upgrade_"+vehicle.Id,L("Улучшить до уровня ","Upgrade to level ")+next.Level+" — "+PlayerText.Money(next.PriceMinor),new(ActionKind.UpgradeEquipment,Equipment:eid));
                    ActionButton(card.Controls,"I4Auto_"+vehicle.Id,L("Автоматически","Automatic"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Automatic));
                    ActionButton(card.Controls,"I4Stop_"+vehicle.Id,L("Остановить","Stop"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Off));
                    ActionButton(card.Controls,"I4Pack_"+vehicle.Id,L("Забрать технику","Pack equipment"),new(ActionKind.PackEquipment,Equipment:eid));
                    if(equipment.Mode!=EquipmentMode.Personal)ActionButton(card.Controls,"I4Personal_"+vehicle.Id,L("Личный режим","Personal mode"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Personal));
                }
                if(item.Model.EquipmentTypeId is null&&!item.Model.MaterialsOnly)foreach(var enterprise in view.Enterprises)
                {var b=view.Buildings.Single(b=>b.Id.Value==enterprise.Id.Value);ActionButton(card.Controls,"FleetAssign_"+vehicle.Id+"_"+enterprise.Id.Value,L("Назначить: ","Assign: ")+GameUi.Industry(_language,b.TypeId,b.Level),new(ActionKind.AssignVehicle,VehicleId:vehicle.Id,Enterprise:enterprise.Id));}
            }
            foreach(var building in view.Buildings)
            {
                var platform=fleet.Platforms.SingleOrDefault(p=>p.Building==building.Id.Value);var box=new VBoxContainer();box.AddChild(TabletDesign.Heading(PlayerText.Building(_language,building.TypeId),19));box.AddChild(GameUi.Text(L("Площадка уровня ","Platform level ")+(platform?.Level??0)));
                if(platform?.Installation is {} install)box.AddChild(GameUi.Hint(L("Монтаж: осталось ","Installation: remaining ")+GameDuration.Format(install.CompletionTick-view.Tick,_language)));
                if((platform?.Level??0)<3){int level=(platform?.Level??0)+1;var upgrade=fleet.PlatformUpgrades.Single(u=>u.Level==level);ActionButton(box,"FleetPlatform_"+building.Id.Value,L("Улучшить до ","Upgrade to ")+level+" · "+PlayerText.Money(upgrade.PriceMinor)+" + "+string.Join(", ",upgrade.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity)),new(ActionKind.BuildLoadingPlatform,Building:building.Id,EquipmentLevel:level));}
                if(platform is not null){box.AddChild(GameUi.Hint(L("Работников ","Crew ")+platform.Group.Headcount+" / 1 · "+PlayerText.Money(platform.Group.WageMinor)));ActionButton(box,"FleetWage_"+platform.Id,L("Ставка +","Wage +"),new(ActionKind.SetWage,Group:platform.Group.Id,WageMinor:platform.Group.WageMinor+(view.Economy??new()).PlayerWageStepMinor));}
                _platforms.AddChild(GameUi.Card(box));
            }
            Callable.From(()=>{scroll();focus();}).CallDeferred();
        }
        foreach(var item in fleet.Vehicles)
        {
            var inventory=view.Inventories.Single(i=>i.Id==item.Vehicle.CargoInventory);var row=_vehicles[item.Vehicle.Id];var vehicle=item.Vehicle;
            row.Status.Text=State(item.State)+$" · ({item.Position.X},{item.Position.Z})"+(vehicle.Installation is {} install?"\n"+L("Монтаж: осталось ","Installation: remaining ")+GameDuration.Format(install.CompletionTick-view.Tick,_language):"");
            row.Capacity.Text=L("Кузов ","Cargo ")+inventory.OccupiedVolume+" / "+inventory.Capacity+(inventory.ReservedCapacity>0?L(" · резерв места "," · space held ")+inventory.ReservedCapacity:"");row.Fill.MaxValue=Math.Max(1,inventory.Capacity);row.Fill.Value=inventory.OccupiedVolume+inventory.ReservedCapacity;row.Stock.ShowStock(inventory,_language);
            row.Work.Visible=row.Crew.Visible=row.Guidance.Visible=item.Model.EquipmentTypeId is not null;
            if(item.Model.EquipmentTypeId is not null)
            {
                var equipment=view.Equipment!.Single(e=>e.Id.Value==vehicle.Id);var group=view.Employment.Single(g=>g.Id==equipment.GroupId);
                row.Status.Text=(equipment.Deployed==true?GameUi.Reason(_language,equipment.ReasonCode):L("Готова к перевозке","Packed for transport"))+$" · ({equipment.Position.X},{equipment.Position.Z})";
                row.Guidance.Text=equipment.Deployed!=true?L("Доставьте машину на свой участок с подходящим ресурсом и нажмите «Разместить».","Drive to your own matching resource site, then choose Place."):equipment.ReasonCode=="NoStaff"?L("Найм идёт постепенно. Проверьте ставку и наличие ресурса.","Hiring takes time. Check wages and resource availability."):L("Личный режим использует персонажа; автоматический — наёмную команду.","Personal mode uses your character; automatic mode uses a hired crew.");
                row.Work.MaxValue=Math.Max(1,equipment.WorkRequired);row.Work.Value=equipment.WorkDone;row.Crew.Text=L("Команда ","Crew ")+group.Headcount+" / "+group.Capacity+L(" · ставка "," · wage ")+PlayerText.Money(group.WageMinor);
            }
        }
        foreach(var item in _actions){var access=_access(item.Action);bool stop=item.Action.Kind==ActionKind.SetEquipmentMode&&item.Action.Mode==EquipmentMode.Off;item.Button.Disabled=pending||!access.Allowed||view.Participant.Busy&&!stop;item.Button.TooltipText=!access.Allowed?GameUi.Reason(_language,access.ReasonCode):view.Participant.Busy&&!stop?GameUi.Reason(_language,"ParticipantBusy"):"";}
        _transfer.ShowData(view,pending);if(_focus>0&&_vehicles.TryGetValue(_focus,out var selected)){_focus=0;Callable.From(()=>UiNavigation.Reveal(selected.Shell)).CallDeferred();}
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
    private void ShowCatalogWaits(PlayerView view,FleetObservation fleet,bool pending)
    {
        bool classes=_scenario.Economy?.Fleet?.EquipmentDealerVersion==2;
        _refresh.Visible=classes&&fleet.NextSupplyTick>0;
        _refresh.Text=classes?L("Следующее обновление ассортимента: тик ","Next assortment refresh: tick ")+fleet.NextSupplyTick+". "+L("На стоянке два места для легковых и грузовых, одно для укладчика и два для добывающей техники.","The lot has two car bays, one paver bay and two extractor bays."):"";
        if(!classes){_orders.Visible=false;return;}
        var models=fleet.Models.IsDefault?[]:fleet.Models;
        var stock=fleet.Stock.IsDefault?[]:fleet.Stock;
        var waits=fleet.CatalogWaits.IsDefault?[]:fleet.CatalogWaits;
        var missing=models.Where(m=>m.EquipmentLevel<=1&&!stock.Any(s=>s.ModelId==m.Id)).OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray();
        string key=fleet.NextSupplyTick+"|"+string.Join(',',missing.Select(m=>m.Id))+"|"+string.Join(',',waits.Select(w=>w.ModelId))+"|"+pending+"|"+view.AvailableCashMinor+"|"+view.Participant.Busy;
        if(key==_orderKey)return;
        _orderKey=key;Clear(_orders);_orders.Visible=missing.Length>0;
        foreach(var model in missing)
        {
            var body=new VBoxContainer();body.AddChild(TabletDesign.Heading(ModelName(model),18));
            string kind=model.EquipmentTypeId is not null?L("Сейчас эта добывающая машина не выставлена.","This extractor is not on the lot."):model.MaterialsOnly?L("Укладчик сейчас не выставлен.","The paver is not on the lot."):L("Свободного места на стоянке для этой модели нет.","There is no free parking bay for this model.");
            body.AddChild(GameUi.Hint(kind+" "+L("Заявка дождётся свободного места и следующей поставки. Купленная машина не исчезает.","A request waits for a free bay and the next delivery. A purchased vehicle stays where it is.")));
            bool waiting=waits.Any(w=>w.ModelId==model.Id);
            var action=new GameAction(ActionKind.BuyVehicle,VehicleId:0,EquipmentTypeId:model.Id);
            var button=GameUi.Button("FleetOrder_"+model.Id,waiting?L("Заявка принята","Request accepted"):L("Заказать","Request"),()=>{});
            GameUi.Command(button,()=>{if(_pending||!_access(action).Allowed)return;_pending=true;_submit(action);});
            var access=_access(action);button.Disabled=pending||waiting||view.Participant.Busy||view.AvailableCashMinor<model.PriceMinor||!access.Allowed;
            body.AddChild(button);if(!access.Allowed&&!waiting)body.AddChild(GameUi.Hint(GameUi.Reason(_language,access.ReasonCode)));
            _orders.AddChild(GameUi.Card(body));
        }
    }
    private string State(string state)=>state switch{"ServiceReserved"=>L("Закреплена за доставкой","Reserved for delivery"),"Parked"=>L("Свободна","Parked"),"Personal"=>L("Личное управление","Personal control"),"Installing"=>L("Монтаж","Installing"),_=>GameUi.Reason(_language,state)};
}

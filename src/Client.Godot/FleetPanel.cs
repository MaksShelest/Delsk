using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own fleet and public dealer listings. All mutations use the ordinary command inbox.</summary>
public partial class FleetPanel : VBoxContainer
{
    public event Action<long>? RoadWorkRequested;
    private readonly string _language;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly VBoxContainer _stock=new(),_owned=new(),_platforms=new();
    private readonly Label _summary=TextLabel();
    private readonly OptionButton _source=new(){Name="FleetTransferSource"},_destination=new(){Name="FleetTransferDestination"},_item=new(){Name="FleetTransferItem"};
    private readonly SpinBox _quantity=new(){Name="FleetTransferQuantity",MinValue=1,MaxValue=100000,Step=1,Value=1,UpdateOnTextChanged=true};
    private readonly Button _transfer=new(){Name="FleetTransfer"};
    private readonly Dictionary<long,(Label Label,Button Buy)> _listings=[];
    private readonly Dictionary<long,(Label Label,VBoxContainer Controls)> _vehicles=[];
    private readonly List<(Button Button,GameAction Action)> _actions=[];
    private PlayerView? _view;
    private long[] _inventoryIds=[];
    private string _controlsKey="",_transferKey="";
    public FleetPanel(string language,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name="FleetPanel";_language=language;_submit=submit;_access=access;
        AddChild(TabletDesign.Heading(L("Автопарк и автосалон","Fleet and dealer")));AddChild(_summary);AddChild(_stock);AddChild(_owned);
        AddChild(new HSeparator());AddChild(TextLabel(L("Передача груза · источник → получатель. Машина и груз должны быть рядом с вами. Комплекты монтируются из материалов в кузове.","Cargo transfer · source → destination. Vehicle and cargo must be beside you. Kits use materials in the vehicle cargo.")));
        var flow=TabletDesign.Flow();foreach(var c in new Control[]{_source,_destination,_item,_quantity,_transfer})flow.AddChild(c);AddChild(flow);
        _transfer.Text=L("Перенести","Transfer");_transfer.Pressed+=()=>{if(_view is null||_source.Selected<0||_destination.Selected<0||_item.Selected<0)return;_submit(new(ActionKind.Transfer,Source:new(_inventoryIds[_source.Selected]),Destination:new(_inventoryIds[_destination.Selected]),ItemId:_item.GetItemMetadata(_item.Selected).AsString(),Quantity:(long)_quantity.Value));};
        AddChild(_platforms);
    }
    private string ResourceItem(VehicleModel model)=>_view!.Fleet!.EquipmentTypes.Single(t=>t.Id==model.EquipmentTypeId).ItemId;
    private string ModelName(VehicleModel model)=>model.EquipmentTypeId is null?model.Name:PlayerText.Item(_language,ResourceItem(model))+" · "+(model.EquipmentLevel==1?L("малая","small"):L("большая","large"));
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    public void FocusVehicle(long id)
    {if(_vehicles.TryGetValue(id,out var row)){row.Label.AddThemeColorOverride("font_color",new Color("956019"));UiNavigation.Reveal(row.Label);}}
    private static Label TextLabel(string text="")=>new(){Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private void ActionButton(Container host,string name,string text,GameAction action)
    {var button=new Button{Name=name,Text=text,FocusMode=FocusModeEnum.None,AutowrapMode=TextServer.AutowrapMode.WordSmart};button.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;button.Pressed+=()=>_submit(action);host.AddChild(button);_actions.Add((button,action));}
    private static void Clear(Container host){foreach(Node n in host.GetChildren()){host.RemoveChild(n);n.QueueFree();}}
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;Visible=view.Fleet is not null;if(view.Fleet is not {} f)return;
        _summary.Text=L("Покупка у входа в автосалон: ","Purchase at the dealer entrance: ")+$"({f.Dealer.X},{f.Dealer.Z})"+L(". Окно конкуренции 5 тиков. Поставки каждые 500, замена непроданного после 2500. Купленная машина остаётся на стоянке до выезда. Базовая ручная погрузка 10 объёма/тик; кузов её не увеличивает. Время включается кнопкой планшета.",". Competitive window: 5 ticks. Supply every 500, unsold replacement after 2500. A purchased vehicle stays parked until driven away. Base manual loading: 10 volume/tick; a larger body adds no loading power. Use the tablet time button.");
        foreach(long id in _listings.Keys.Where(id=>!f.Stock.Any(v=>v.Id==id)).ToArray()){var row=_listings[id].Label.GetParent().GetParent();row.GetParent().RemoveChild(row);row.QueueFree();_listings.Remove(id);}
        foreach(var listing in f.Stock)
        {
            var model=f.Models.Single(m=>m.Id==listing.ModelId);long id=listing.Id;
            if(!_listings.TryGetValue(id,out var row)){var box=new VBoxContainer();row=(TextLabel(),new Button{Name="FleetBuy_"+id,FocusMode=FocusModeEnum.None,Text=L("Заявка на покупку","Request purchase")});row.Buy.Pressed+=()=>_submit(new(ActionKind.BuyVehicle,VehicleId:id));row.Buy.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;box.AddChild(row.Label);box.AddChild(row.Buy);_stock.AddChild(TabletDesign.Wrap(box));_listings.Add(id,row);}
            row.Label.Text=model.Name+" #"+id+" · "+PlayerText.Money(model.PriceMinor)+L(" · объём "," · volume ")+model.CapacityVolume+L(" · скорость до "," · top speed ")+model.Speed+L(" км/ч · возраст "," km/h · stock age ")+(view.Tick-listing.DeliveredTick)+L(" тиков · "," ticks · ")+(model.MaterialsOnly?L("дорожные материалы","road materials"):model.Id=="sputnik20"?L("легковой","passenger car"):L("грузовой","cargo"));
            if(model.EquipmentTypeId is {} typeId)
            {
                var characteristics=f.EquipmentTypes.Single(t=>t.Id==typeId).Levels.Single(l=>l.Level==model.EquipmentLevel);
                row.Label.Text=PlayerText.Item(_language,ResourceItem(model))+" · "+(model.EquipmentLevel==1?L("малая","small"):L("большая","large"))+" #"+id+" · "+PlayerText.Money(model.PriceMinor)+L(" · выпуск "," · output ")+characteristics.CycleQuantity+L(" за "," per ")+characteristics.CycleTicks+L(" тика · запас "," ticks · storage ")+model.CapacityVolume+L(" · штат "," · crew ")+characteristics.CrewCapacity+L(" · в наличии ("," · available (")+listing.Position.X+","+listing.Position.Z+")";
            }
            row.Buy.TooltipText=view.AvailableCashMinor<model.PriceMinor?PlayerText.Reason(_language,"NoMoney"):PlayerText.Reason(_language,_access(new(ActionKind.BuyVehicle,VehicleId:id)).ReasonCode);
            row.Buy.Disabled=pending||view.Participant.Busy||f.OwnRequests.Length>0||view.AvailableCashMinor<model.PriceMinor||!_access(new(ActionKind.BuyVehicle,VehicleId:id)).Allowed;
        }
        string key=string.Join(',',(view.Equipment??[]).Select(e=>$"{e.Id.Value}:{e.Deployed}:{e.Mode}:{e.PersonalJobId!=0}"))+"|"+string.Join(',',f.Vehicles.Select(v=>$"{v.Vehicle.Id}:{v.Vehicle.Driver}:{v.Vehicle.HandlingLevel}:{v.Vehicle.Installation?.Level}:{v.Logistics.Value}"))+"|"+string.Join(',',view.Enterprises.Select(e=>e.Id.Value))+"|"+string.Join(',',view.Buildings.Select(b=>b.Id.Value))+"|"+string.Join(',',f.Platforms.Select(p=>$"{p.Id}:{p.Level}:{p.Installation?.Level}:{p.Group.Headcount}:{p.Group.WageMinor}:{p.Group.PendingWageMinor}"));
        if(key!=_controlsKey)
        {
            _controlsKey=key;Clear(_owned);Clear(_platforms);_vehicles.Clear();_actions.Clear();
            foreach(var item in f.Vehicles)
            {
                var v=item.Vehicle;var box=new VBoxContainer();var label=TextLabel();box.AddChild(label);var buttons=new VBoxContainer();box.AddChild(buttons);_owned.AddChild(TabletDesign.Wrap(box));_vehicles.Add(v.Id,(label,buttons));
                // Resolve current position at click time, so the button never follows an old captured coordinate.
                var walk=new Button{Name="FleetFind_"+v.Id,Text=L("К машине","Go to vehicle"),SizeFlagsHorizontal=SizeFlags.ShrinkBegin,FocusMode=FocusModeEnum.None};walk.Pressed+=()=>{var current=_view!.Fleet!.Vehicles.Single(x=>x.Vehicle.Id==v.Id).Position;_submit(new(ActionKind.MoveTo,Target:current));};buttons.AddChild(walk);
                ActionButton(buttons,"FleetBoard_"+v.Id,v.Driver==0?L("Сесть","Board"):L("Выйти","Leave"),new(v.Driver==0?ActionKind.BoardVehicle:ActionKind.LeaveVehicle,VehicleId:v.Id));
                if(item.Model.MaterialsOnly&&view.Roads is not null)
                {var road=new Button{Name="FleetRoadWork_"+v.Id,Text=L("Строить / улучшить дорогу","Build / upgrade road")};road.Pressed+=()=>RoadWorkRequested?.Invoke(v.Id);buttons.AddChild(road);}
                if(view.Roads is not null)ActionButton(buttons,"FleetTurn_"+v.Id,L("Развернуться в свободном месте","Turn in a free place"),new(ActionKind.TurnVehicle,VehicleId:v.Id));
                foreach(var kit in f.Kits.Where(k=>k.Level>v.HandlingLevel&&(k.Level==2?v.ModelId=="vector200":v.ModelId is "muravey70" or "eger70" or "trakt_d1")))ActionButton(buttons,"FleetKit_"+v.Id+"_"+kit.Level,L("Комплект ","Handling kit ")+kit.Throughput+" · "+PlayerText.Money(kit.PriceMinor)+" + "+string.Join(", ",kit.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity))+L(" · 60 тиков"," · 60 ticks"),new(ActionKind.InstallVehicleKit,VehicleId:v.Id,EquipmentLevel:kit.Level,Source:v.CargoInventory));
                if(item.Model.EquipmentTypeId is not null)
                {
                    var equipment=view.Equipment!.Single(e=>e.Id.Value==v.Id);var eid=equipment.Id;
                    var flow=TabletDesign.Flow();buttons.AddChild(flow);
                    ActionButton(flow,"I4Place_"+v.Id,L("Разместить технику","Place equipment"),new(ActionKind.PlaceEquipment,Equipment:eid));
                    ActionButton(flow,"I4Start_"+v.Id,L("Начать добычу","Start mining"),new(ActionKind.StartHarvesting,Equipment:eid,ItemId:ResourceItem(item.Model),Quantity:f.EquipmentTypes.Single(t=>t.Id==item.Model.EquipmentTypeId).Levels.Single(l=>l.Level==item.Model.EquipmentLevel).CycleQuantity));
                    ActionButton(flow,"I4Auto_"+v.Id,L("Авто / наём","Auto / hire"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Automatic));
                    ActionButton(flow,"I4Stop_"+v.Id,L("Остановить","Stop"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Off));
                    ActionButton(flow,"I4Pack_"+v.Id,L("Забрать технику","Pick up equipment"),new(ActionKind.PackEquipment,Equipment:eid));
                    if(equipment.Mode!=EquipmentMode.Personal)ActionButton(flow,"I4Personal_"+v.Id,L("Лично","Personal"),new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Personal));
                }
                if(item.Model.EquipmentTypeId is null&&!item.Model.MaterialsOnly)foreach(var enterprise in view.Enterprises)ActionButton(buttons,"FleetAssign_"+v.Id+"_"+enterprise.Id.Value,L("Назначить предприятию #","Assign to enterprise #")+enterprise.Id.Value,new(ActionKind.AssignVehicle,VehicleId:v.Id,Enterprise:enterprise.Id));
            }
            foreach(var b in view.Buildings)
            {
                var p=f.Platforms.SingleOrDefault(p=>p.Building==b.Id.Value);var box=new VBoxContainer();box.AddChild(TextLabel(PlayerText.Building(_language,b.TypeId)+" #"+b.Id.Value+L(" · погрузочная площадка "," · loading platform ")+(p?.Level??0)));
                if(p?.Installation is {} install)box.AddChild(TextLabel(L("Монтаж до тика ","Installing until tick ")+install.CompletionTick));
                if((p?.Level??0)<3){int level=(p?.Level??0)+1;var upgrade=f.PlatformUpgrades.Single(u=>u.Level==level);ActionButton(box,"FleetPlatform_"+b.Id.Value,L("Площадка ","Platform ")+level+" · "+PlayerText.Money(upgrade.PriceMinor)+" + "+string.Join(", ",upgrade.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity)),new(ActionKind.BuildLoadingPlatform,Building:b.Id,EquipmentLevel:level));}
                if(p is not null){box.AddChild(TextLabel(L("Работников: ","Staff: ")+p.Group.Headcount+"/1 · "+PlayerText.Money(p.Group.WageMinor)+L("/рабочий тик","/working tick")));ActionButton(box,"FleetWage_"+p.Id,L("Ставка +","Wage +"),new(ActionKind.SetWage,Group:p.Group.Id,WageMinor:p.Group.WageMinor+(view.Economy??new()).PlayerWageStepMinor));}
                _platforms.AddChild(TabletDesign.Wrap(box));
            }
        }
        foreach(var item in f.Vehicles)
        {
            var inv=view.Inventories.Single(i=>i.Id==item.Vehicle.CargoInventory);var v=item.Vehicle;
            _vehicles[v.Id].Label.Text=ModelName(item.Model)+" #"+v.Id+" · "+L("Объём ","Volume ")+$"{inv.OccupiedVolume}+{inv.ReservedCapacity}/{inv.Capacity}"+" · "+$"({item.Position.X},{item.Position.Z})"+" · "+State(item.State)+(v.Installation is {} job?L(" до тика "," until tick ")+job.CompletionTick:"")+"\n"+string.Join(" · ",inv.Stock.Select(s=>PlayerText.Item(_language,s.ItemId)+" ×"+s.Quantity));
        }
        foreach(var item in f.Vehicles.Where(v=>v.Model.EquipmentTypeId is not null))
        {
            var e=view.Equipment!.Single(e=>e.Id.Value==item.Vehicle.Id);var group=view.Employment.Single(g=>g.Id==e.GroupId);
            _vehicles[e.Id.Value].Label.Text+="\n"+(e.Deployed==true?L("Размещена · рабочий участок ","Deployed · work site ")+$"({e.Position.X},{e.Position.Z})":L("Транспортное положение","Packed for transport"))+" · "+PlayerText.Reason(_language,e.ReasonCode)+L(" · прогресс "," · progress ")+e.WorkDone+"/"+e.WorkRequired+L(" · штат "," · crew ")+group.Headcount+"/"+group.Capacity;
        }
        foreach(var a in _actions)
        {
            var access=_access(a.Action);bool stop=a.Action.Kind==ActionKind.SetEquipmentMode&&a.Action.Mode==EquipmentMode.Off;
            a.Button.Disabled=pending||!access.Allowed||view.Participant.Busy&&!stop;
            a.Button.TooltipText=!access.Allowed?PlayerText.Reason(_language,access.ReasonCode):view.Participant.Busy&&!stop?PlayerText.Reason(_language,"ParticipantBusy"):"";
        }
        var nearby=view.Inventories.Where(i=>i.Position==view.Participant.Position).OrderBy(i=>i.Id.Value).ToArray();
        string tk=string.Join(',',nearby.Select(i=>i.Id.Value));if(tk!=_transferKey){_transferKey=tk;_source.Clear();_destination.Clear();_inventoryIds=nearby.Select(i=>i.Id.Value).ToArray();foreach(var i in nearby){string label=i.Id==view.Participant.InventoryId?L("Рюкзак","Backpack"):f.Vehicles.FirstOrDefault(v=>v.Vehicle.CargoInventory==i.Id) is {} vehicle?ModelName(vehicle.Model):L("Хранилище","Storage");_source.AddItem(label+" #"+i.Id.Value);_destination.AddItem(label+" #"+i.Id.Value);}if(nearby.Length>1)_destination.Select(1);}
        // Newly acquired items join the list without resetting the current choice.
        foreach(var id in view.Inventories.SelectMany(i=>i.Stock).Select(s=>s.ItemId).Distinct().Order(StringComparer.Ordinal))if(!Enumerable.Range(0,_item.ItemCount).Any(n=>_item.GetItemMetadata(n).AsString()==id)){_item.AddItem(PlayerText.Item(_language,id));_item.SetItemMetadata(_item.ItemCount-1,id);}
        _transfer.Disabled=pending||view.Participant.Busy||nearby.Length<2||_item.ItemCount==0;
        if(f.OwnRequests.Length>0)_summary.Text+="\n"+L("Заявка ждёт распределения до тика ","Purchase request closes at tick ")+f.OwnRequests[0].CutoffTick;
    }
    private string State(string state)=>state switch{"ServiceReserved"=>L("Закреплена за доставкой","Reserved for delivery"),"Parked"=>L("Свободна","Parked"),"Personal"=>L("Личное управление","Personal control"),"Installing"=>L("Монтаж","Installing"),_=>TabletText.Reason(_language,state)};
}

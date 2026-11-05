using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Source → goods → recipient → executor. Existing Core routes retain their physical semantics.</summary>
public partial class RouteEditor : VBoxContainer
{
    public event Action? FleetRequested;
    private readonly ScenarioDefinition _scenario;private readonly string _language,_kind;private readonly long _object;private readonly Action<GameAction> _submit;
    private readonly RouteMiniMap _map;private readonly List<Cell> _stops=[];private readonly CheckBox _repeat=null!;
    private readonly Label _status=GameUi.Text(),_overview=GameUi.Text();private readonly OptionButton _source=null!,_destination=null!,_item=null!;
    private readonly Button _apply=new(),_stop=new(),_fleet;
    private PlayerView? _view;private bool _pending,_extractionInitialized,_transportInitialized;private string _inventoryKey="";
    public RouteEditor(ScenarioDefinition scenario,string language,string kind,long id,Action<GameAction> submit)
    {
        Name="RouteEditor";_scenario=scenario;_language=language;_kind=kind;_object=id;_submit=submit;_status.Name="RouteStatus";_overview.Name="RouteOverview";
        AddChild(TabletDesign.Heading(L("Маршрут доставки","Delivery route"),21));AddChild(_status);AddChild(GameUi.Card(_overview));
        _map=new(scenario){CustomMinimumSize=new(0,210),SizeFlagsHorizontal=SizeFlags.ExpandFill};
        _fleet=GameUi.Button("RouteFleet",L("Выбрать машину в автопарке","Choose a vehicle in Fleet"),()=>FleetRequested?.Invoke());AddChild(_fleet);
        if(kind=="equipment")
        {
            _repeat=new(){Text=L("Повторять","Repeat")};_map.Chosen+=cell=>{if(_map.Allowed.Contains(cell)&&_stops.Count<32){_stops.Add(cell);RefreshStops();}};
            var row=TabletDesign.Flow();AddChild(row);row.AddChild(GameUi.Button("RouteUndo",L("Убрать последнюю","Remove last"),()=>{if(_stops.Count>0)_stops.RemoveAt(_stops.Count-1);RefreshStops();}));row.AddChild(GameUi.Button("RouteClear",L("Очистить","Clear"),()=>{_stops.Clear();RefreshStops();}));row.AddChild(_repeat);
            row.AddChild(GameUi.Button("RouteMoveFirst",L("К первой остановке","Go to first stop"),()=>{if(_stops.Count>0)Send(new(ActionKind.MoveEquipment,Equipment:new(_object),Target:_stops[0]));}));
            _apply.Text=L("Сохранить и включить","Save & enable");_apply.Pressed+=()=>Send(new(ActionKind.SetExtractionRoute,Equipment:new(_object),Stops:[.._stops],Repeat:_repeat.ButtonPressed,Enabled:true));
            _stop.Text=L("Остановить маршрут","Stop route");_stop.Pressed+=()=>Send(new(ActionKind.SetExtractionRoute,Equipment:new(_object),Stops:[.._stops],Repeat:_repeat.ButtonPressed,Enabled:false));AddChild(_map);
        }
        else
        {
            _source=new(){Name="RouteSource"};_destination=new(){Name="RouteDestination"};_item=new(){Name="RouteItem"};
            var fields=new TabletColumns{Breakpoint=700};AddChild(fields);
            foreach(var pair in new[]{(L("Источник","Source"),_source),(L("Товар","Goods"),_item),(L("Получатель","Recipient"),_destination)})
            {var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};box.AddChild(GameUi.Hint(pair.Item1));pair.Item2.SizeFlagsHorizontal=SizeFlags.ExpandFill;box.AddChild(pair.Item2);fields.AddChild(box);pair.Item2.ItemSelected+=_=>Preview();}
            foreach(var item in scenario.Items){_item.AddItem(PlayerText.Item(language,item.Id));_item.SetItemMetadata(_item.ItemCount-1,item.Id);}
            _apply.Text=L("Сохранить и включить доставку","Save & enable delivery");_apply.Pressed+=()=>SendTransport(true);_stop.Text=L("Остановить маршрут","Stop route");_stop.Pressed+=()=>SendTransport(false);
            AddChild(new UiDisclosure(L("Карта и подробности","Map & details"),_map));
        }
        var actions=TabletDesign.Flow();_apply.Name="RouteApply";_stop.Name="RouteStop";actions.AddChild(_apply);actions.AddChild(_stop);AddChild(actions);GameUi.Primary(_apply);
        AddChild(GameUi.Hint(L("Начатый рейс завершится после выключения маршрута. Груз и обязательства сохраняются.","An active trip finishes after disabling the route. Cargo and commitments are retained.")));
        Resized+=()=>_map.CustomMinimumSize=new(0,GetViewportRect().Size.Y<540?140:210);
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private void Send(GameAction action){if(_pending)return;_pending=true;_apply.Disabled=_stop.Disabled=true;_submit(action);}
    private static long Id(OptionButton option)=>option.Selected>=0&&option.ItemCount>0?option.GetItemMetadata(option.Selected).AsInt64():0;
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;_map.Blocked=view.Blocked.ToHashSet();
        if(_kind=="equipment")
        {
            var machine=view.Equipment!.Single(e=>e.Id.Value==_object);string item=_scenario.EquipmentTypes!.Single(t=>t.Id==machine.TypeId).ItemId;
            if(!_extractionInitialized&&machine.Route is {} route){_stops.Clear();_stops.AddRange(route.Stops);_repeat.SetPressedNoSignal(route.Repeat);_extractionInitialized=true;}
            _map.Allowed=view.Resources.Where(r=>r.Owner==view.Participant.Id&&r.ItemId==item).Select(r=>r.Position).ToHashSet();_map.Current=machine.Position;
            _status.Text=GameUi.Reason(_language,machine.ReasonCode);_overview.Text=L("Выберите свои участки на карте по порядку. Остановок: ","Select your owned sites on the map in order. Stops: ")+_stops.Count;
            _apply.Disabled=pending||_stops.Count==0;_stop.Disabled=pending;_fleet.Visible=false;RefreshStops();
        }
        else
        {
            var system=view.Logistics.SingleOrDefault(l=>l.EnterpriseId.Value==_object);
            _status.Text=system is null?L("Назначьте предприятию машину в автопарке, затем выберите точки доставки.","Assign a vehicle to this enterprise in Fleet, then choose delivery endpoints."):GameUi.Reason(_language,system.ReasonCode);
            _fleet.Visible=system is null;
            var sources=view.Inventories.Where(i=>view.Buildings.Any(b=>b.InputId==i.Id||b.OutputId==i.Id)||(view.Equipment??[]).Any(e=>e.StorageId==i.Id)).ToArray();
            var destinations=view.Inventories.Where(i=>view.Buildings.Any(b=>b.InputId==i.Id)).ToArray();string key=string.Join(',',sources.Select(i=>$"{i.Id.Value}:{i.Position}"))+"|"+string.Join(',',destinations.Select(i=>i.Id.Value));
            if(key!=_inventoryKey&&!_source.GetPopup().Visible&&!_destination.GetPopup().Visible){_inventoryKey=key;Fill(_source,sources);Fill(_destination,destinations);}
            var configured=system?.Queue.Where(j=>j.Source.Value!=0).OrderByDescending(j=>j.Enabled).ThenByDescending(j=>j.LastServed).FirstOrDefault();
            if(!_transportInitialized)
            {
                if(configured is not null){Select(_source,configured.Source.Value);Select(_destination,configured.Destination.Value);SelectItem(configured.ItemId);_transportInitialized=true;}
                else if(view.Enterprises.FirstOrDefault(e=>e.Id.Value==_object) is {} enterprise)
                {Select(_source,enterprise.OutputId.Value);var warehouse=view.Buildings.FirstOrDefault(b=>b.Kind==BuildingKind.Warehouse);if(warehouse is not null)Select(_destination,warehouse.InputId.Value);SelectItem(_scenario.Recipes.Single(r=>r.Id==enterprise.RecipeId).OutputItem);_transportInitialized=true;}
            }
            _map.Allowed=sources.Concat(destinations).Select(i=>i.Position).ToHashSet();_map.Current=view.Participant.Position;
            _apply.Disabled=pending||system is null||sources.Length==0||destinations.Length==0||Id(_source)==Id(_destination);_stop.Disabled=pending||system is null||!system.Queue.Any(j=>j.Source.Value!=0&&j.Enabled);Preview();_map.QueueRedraw();
        }
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
    private void Preview()
    {
        if(_kind=="equipment"||_view is null)return;
        var system=_view.Logistics.FirstOrDefault(l=>l.EnterpriseId.Value==_object);var car=_view.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Id==system?.VehicleId);
        string executor=car?.Model.Name??L("Команда логистики","Logistics crew"),item=_item.Selected<0?"":_item.GetItemMetadata(_item.Selected).AsString();
        _overview.Text=GameUi.InventoryName(_view,Id(_source),_language)+" → "+PlayerText.Item(_language,item)+" → "+GameUi.InventoryName(_view,Id(_destination),_language)+"\n"+L("Исполнитель: ","Executor: ")+executor+" · "+(system?.Trip is {} trip?trip.Quantity+L(" ед. в текущем рейсе"," units in the current trip"):L("Количество определяется запасом и местом в кузове.","Quantity follows available stock and cargo capacity."));
    }
    private void SelectItem(string id){for(int i=0;i<_item.ItemCount;i++)if(_item.GetItemMetadata(i).AsString()==id)_item.Select(i);}
    private void SendTransport(bool enabled)
    {
        if(_pending||_view?.Logistics.FirstOrDefault(l=>l.EnterpriseId.Value==_object) is not {} system)return;
        if(!enabled){var job=system.Queue.FirstOrDefault(j=>j.Source.Value!=0&&j.Enabled);if(job is not null)Send(new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:job.Source,Destination:job.Destination,ItemId:job.ItemId,Enabled:false));return;}
        if(_apply.Disabled||_item.Selected<0)return;Send(new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:new(Id(_source)),Destination:new(Id(_destination)),ItemId:_item.GetItemMetadata(_item.Selected).AsString(),Enabled:true));
    }
    private void Fill(OptionButton option,InventoryView[] inventories)
    {long selected=Id(option);option.Clear();foreach(var inventory in inventories){option.AddItem(GameUi.InventoryName(_view!,inventory.Id.Value,_language));option.SetItemMetadata(option.ItemCount-1,inventory.Id.Value);if(inventory.Id.Value==selected)option.Select(option.ItemCount-1);}}
    private static void Select(OptionButton option,long id){for(int i=0;i<option.ItemCount;i++)if(option.GetItemMetadata(i).AsInt64()==id)option.Select(i);}
    private void RefreshStops(){_map.Stops=[.._stops];_map.QueueRedraw();}
}

public partial class RouteMiniMap(ScenarioDefinition scenario) : Control
{
    public event System.Action<Cell>? Chosen;
    public HashSet<Cell> Allowed=[];
    public HashSet<Cell> Blocked=[];
    public Cell[] Stops=[];
    public Cell[] Path=[];
    public Cell Current;
    private float CellSize=>Mathf.Min(Size.X/scenario.MapWidth,Size.Y/scenario.MapHeight);
    private Vector2 Origin=>(Size-new Vector2(scenario.MapWidth,scenario.MapHeight)*CellSize)/2;
    public override void _GuiInput(InputEvent input)
    {
        if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} mouse)
        {var p=(mouse.Position-Origin)/CellSize;Chosen?.Invoke(new((int)Mathf.Floor(p.X),(int)Mathf.Floor(p.Y)));AcceptEvent();}
    }
    public override void _Draw()
    {
        for(int z=0;z<scenario.MapHeight;z++)for(int x=0;x<scenario.MapWidth;x++)
        {
            var cell=new Cell(x,z);var area=new Rect2(Origin+new Vector2(x,z)*CellSize+Vector2.One,Vector2.One*(CellSize-2));
            DrawRect(area,Blocked.Contains(cell)?new Color("384750"):Allowed.Contains(cell)?new Color("83b67f"):new Color("71816c"));
            if(Stops.Contains(cell))DrawRect(area,new Color("ead275"),false,2);
            if(Current==cell)DrawCircle(area.GetCenter(),Mathf.Max(2,CellSize/5),Colors.White);
        }
        for(int i=1;i<Path.Length;i++)DrawLine(Origin+(new Vector2(Path[i-1].X,Path[i-1].Z)+Vector2.One/2)*CellSize,Origin+(new Vector2(Path[i].X,Path[i].Z)+Vector2.One/2)*CellSize,new Color("ead275"),2);
        for(int i=0;i<Stops.Length;i++)
            DrawString(ThemeDB.FallbackFont,Origin+new Vector2(Stops[i].X+.15f,Stops[i].Z+.8f)*CellSize,(i+1).ToString(),fontSize:Mathf.Max(9,(int)CellSize-2),modulate:Colors.Black);
    }
}

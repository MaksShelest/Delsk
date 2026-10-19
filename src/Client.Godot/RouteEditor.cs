using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Edits ordinary commands; the core owns route validity, pathfinding and execution.</summary>
public partial class RouteEditor : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language,_kind;
    private readonly long _object;
    private readonly System.Action<GameAction> _submit;
    private readonly RouteMiniMap _map;
    private readonly List<Cell> _stops=[];
    private readonly CheckBox _repeat=null!;
    private readonly Label _status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly OptionButton _source=null!,_destination=null!,_item=null!;
    private readonly Button _apply=new(),_stop=new();
    private PlayerView? _view;
    private string _inventoryKey="";
    private bool _extractionInitialized,_transportInitialized;
    public RouteEditor(ScenarioDefinition scenario,string language,string kind,long id,System.Action<GameAction> submit)
    {
        _scenario=scenario;_language=language;_kind=kind;_object=id;_submit=submit;
        AddChild(_status);_map=new(scenario){CustomMinimumSize=new(520,260)};AddChild(_map);
        if(kind=="equipment")
        {
            _repeat=new();
            _map.Chosen+=cell=>{if(_map.Allowed.Contains(cell)&&_stops.Count<32){_stops.Add(cell);RefreshStops();}};
            var row=new HBoxContainer();AddChild(row);
            var undo=new Button{Text=L("Убрать последнюю","Remove last")};undo.Pressed+=()=>{if(_stops.Count>0)_stops.RemoveAt(_stops.Count-1);RefreshStops();};row.AddChild(undo);
            var clear=new Button{Text=L("Очистить","Clear")};clear.Pressed+=()=>{_stops.Clear();RefreshStops();};row.AddChild(clear);
            _repeat.Text=L("Повторять","Repeat");row.AddChild(_repeat);
            var move=new Button{Text=L("Переехать к первой остановке","Move to first stop")};
            move.Pressed+=()=>{if(_stops.Count>0)_submit(new(ActionKind.MoveEquipment,Equipment:new(_object),Target:_stops[0]));};row.AddChild(move);
            _apply.Text=L("Сохранить и включить маршрут","Save and enable route");_apply.Pressed+=()=>_submit(new(ActionKind.SetExtractionRoute,Equipment:new(_object),Stops:[.._stops],Repeat:_repeat.ButtonPressed,Enabled:true));
            _stop.Text=L("Выключить маршрут после текущего перехода","Disable route after current leg");_stop.Pressed+=()=>_submit(new(ActionKind.SetExtractionRoute,Equipment:new(_object),Stops:[.._stops],Repeat:_repeat.ButtonPressed,Enabled:false));
        }
        else
        {
            _source=new();_destination=new();_item=new();
            foreach(var pair in new[]{(L("Источник","Source"),_source),(L("Получатель","Destination"),_destination),(L("Товар","Item"),_item)})
            {var row=new HBoxContainer();row.AddChild(new Label{Text=pair.Item1,CustomMinimumSize=new(130,0)});pair.Item2.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(pair.Item2);AddChild(row);}
            foreach(var item in scenario.Items){_item.AddItem(PlayerText.Item(language,item.Id));_item.SetItemMetadata(_item.ItemCount-1,item.Id);}
            _apply.Text=L("Сохранить и включить доставку","Save and enable transport");_apply.Pressed+=()=>SendTransport(true);
            _stop.Text=L("Выключить маршрут; начатый рейс завершится","Disable route; finish current trip");_stop.Pressed+=()=>SendTransport(false);
        }
        AddChild(_apply);AddChild(_stop);
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_map.Blocked=view.Blocked.ToHashSet();
        if(_kind=="equipment")
        {
            var machine=view.Equipment!.Single(e=>e.Id.Value==_object);string item=_scenario.EquipmentTypes!.Single(t=>t.Id==machine.TypeId).ItemId;
            if(!_extractionInitialized && machine.Route is {} route)
            {_stops.Clear();_stops.AddRange(route.Stops);_repeat.SetPressedNoSignal(route.Repeat);_extractionInitialized=true;}
            _map.Allowed=view.Resources.Where(r=>r.Owner==view.Participant.Id && r.ItemId==item).Select(r=>r.Position).ToHashSet();
            _map.Current=machine.Position;
            _status.Text=TabletText.Reason(_language,machine.ReasonCode)+L("\nВыберите свои клетки на карте по порядку (до 32). Рейс и груз сохраняются при изменении маршрута.","\nSelect owned stops in order (up to 32). Editing preserves the active leg and cargo.")+"\n"+
                L("Действующий маршрут: ","Active route: ")+string.Join(" → ",machine.Route?.Stops.Select(c=>$"({c.X},{c.Z})")??[]);
            _apply.Disabled=pending||_stops.Count==0;_stop.Disabled=pending;RefreshStops();
        }
        else
        {
            var system=view.Logistics.SingleOrDefault(l=>l.EnterpriseId.Value==_object);
            _status.Text=system is null?L("Сначала купите доставку предприятия.","Buy the enterprise's logistics first."):
                TabletText.Reason(_language,system.ReasonCode)+L("\nИсточник → получатель; количество определяется запасом и вместимостью.","\nSource → destination; stock and capacity determine quantity.");
            var sources=view.Inventories.Where(i=>view.Buildings.Any(b=>b.InputId==i.Id||b.OutputId==i.Id)||view.Equipment!.Any(e=>e.StorageId==i.Id)).ToArray();
            var destinations=view.Inventories.Where(i=>view.Buildings.Any(b=>b.InputId==i.Id)).ToArray();
            string key=string.Join(',',sources.Select(i=>$"{i.Id.Value}:{i.Position}"))+"|"+string.Join(',',destinations.Select(i=>i.Id.Value));
            if(key!=_inventoryKey){_inventoryKey=key;Fill(_source,sources);Fill(_destination,destinations);}
            var configured=system?.Queue.Where(j=>j.Source.Value!=0).OrderByDescending(j=>j.Enabled).ThenByDescending(j=>j.LastServed).FirstOrDefault();
            if(!_transportInitialized && configured is not null)
            {
                Select(_source,configured.Source.Value);Select(_destination,configured.Destination.Value);
                for(int i=0;i<_item.ItemCount;i++)if(_item.GetItemMetadata(i).AsString()==configured.ItemId)_item.Select(i);
                _transportInitialized=true;
            }
            _map.Allowed=sources.Concat(destinations).Select(i=>i.Position).ToHashSet();_map.Current=view.Participant.Position;
            bool unavailable=pending||system is null||sources.Length==0||destinations.Length==0;
            _apply.Disabled=unavailable||_source.GetItemMetadata(_source.Selected).AsInt64()==_destination.GetItemMetadata(_destination.Selected).AsInt64();
            _stop.Disabled=pending||system is null||!system.Queue.Any(j=>j.Source.Value!=0&&j.Enabled);
            _map.QueueRedraw();
        }
    }
    private void SendTransport(bool enabled)
    {
        var system=_view!.Logistics.Single(l=>l.EnterpriseId.Value==_object);
        if(!enabled)
        {
            var job=system.Queue.FirstOrDefault(j=>j.Source.Value!=0&&j.Enabled);
            if(job is not null)_submit(new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:job.Source,Destination:job.Destination,ItemId:job.ItemId,Enabled:false));
            return;
        }
        _submit(new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:new(_source.GetItemMetadata(_source.Selected).AsInt64()),
            Destination:new(_destination.GetItemMetadata(_destination.Selected).AsInt64()),ItemId:_item.GetItemMetadata(_item.Selected).AsString(),Enabled:enabled));
    }
    private void Fill(OptionButton option,InventoryView[] inventories)
    {
        long selected=option.ItemCount>0?option.GetItemMetadata(option.Selected).AsInt64():0;option.Clear();
        foreach(var inventory in inventories)
        {
            var building=_view!.Buildings.SingleOrDefault(b=>b.InputId==inventory.Id||b.OutputId==inventory.Id);
            var machine=_view.Equipment!.SingleOrDefault(e=>e.StorageId==inventory.Id);
            string name=building is not null?PlayerText.Building(_language,building.TypeId)+" #"+building.Id.Value+(building.OutputId==inventory.Id?L(" · выход"," · output"):L(" · запас"," · stock")):
                machine is not null?L("Техника #","Equipment #")+machine.Id.Value:L("Запас #","Stock #")+inventory.Id.Value;
            option.AddItem(name+$" ({inventory.Position.X},{inventory.Position.Z})");int i=option.ItemCount-1;option.SetItemMetadata(i,inventory.Id.Value);if(inventory.Id.Value==selected)option.Select(i);
        }
    }
    private static void Select(OptionButton option,long id)
    {for(int i=0;i<option.ItemCount;i++)if(option.GetItemMetadata(i).AsInt64()==id)option.Select(i);}
    private void RefreshStops(){_map.Stops=[.._stops];_map.QueueRedraw();}
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

public partial class RouteMiniMap(ScenarioDefinition scenario) : Control
{
    public event System.Action<Cell>? Chosen;
    public HashSet<Cell> Allowed=[];
    public HashSet<Cell> Blocked=[];
    public Cell[] Stops=[];
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
        for(int i=0;i<Stops.Length;i++)
            DrawString(ThemeDB.FallbackFont,Origin+new Vector2(Stops[i].X+.15f,Stops[i].Z+.8f)*CellSize,(i+1).ToString(),fontSize:Mathf.Max(9,(int)CellSize-2),modulate:Colors.Black);
    }
}

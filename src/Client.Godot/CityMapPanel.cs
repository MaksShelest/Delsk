using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class CityMapPanel : VBoxContainer
{
    public event Action<string,long>? OpenObject;
    public event Action<string,long>? OpenSection;
    private readonly string _language;
    private readonly Action<GameAction> _submit;
    private readonly Func<Cell,MovementGuide> _route;
    private readonly CityMapPreferences _state;
    public CityMapCanvas Canvas {get;}
    private readonly OptionButton _participant=new(){Name="MapParticipant"},_objects=new(){Name="MapObjects"};
    private readonly Label _legend=Wrap(),_details=Wrap(),_routeInfo=Wrap();
    private readonly HFlowContainer _owners=new();
    private readonly Button _open=new(){Name="MapOpenObject"},_walk=new(){Name="MapNavigate"};
    private CityMapFrame? _map;private PlayerView? _view;private MovementGuide? _guide;private string[] _keys=[];private long[] _participants=[];
    public CityMapPanel(ScenarioDefinition scenario,string language,CityMapPreferences state,Action<GameAction> submit,Func<Cell,MovementGuide> route)
    {
        Name="FullCityMap";_language=language;_state=state;_submit=submit;_route=route;
        var toolbar=TabletDesign.Flow();AddChild(toolbar);
        toolbar.AddChild(Button("MapZoomIn","+",()=>Canvas!.Zoom(1.25)));toolbar.AddChild(Button("MapZoomOut","−",()=>Canvas!.Zoom(.8)));
        toolbar.AddChild(Button("MapFit",L("Весь город","Fit city"),()=>Canvas!.Fit()));
        toolbar.AddChild(Button("MapMe",L("Ко мне","Find me"),()=>{if(_view is null)return;_state.Center(_view.Participant.Position.X+.5,_view.Participant.Position.Z+.5);Canvas!.Zoom(2); }));
        toolbar.AddChild(_participant);_participant.ItemSelected+=i=>{_state.Participant=_participants[i];Canvas!.QueueRedraw();UpdateDetails();};
        var layers=TabletDesign.Flow();AddChild(layers);
        foreach(var layer in new[]{CityLayer.Resources,CityLayer.Industry,CityLayer.Property,CityLayer.Roads,CityLayer.Services,CityLayer.Deliveries,CityLayer.People})
        {var toggle=new CheckButton{Name="MapLayer"+layer,Text=LayerTitle(layer),ButtonPressed=(_state.Layers&layer)!=0,CustomMinimumSize=new(0,44)};toggle.Toggled+=on=>{_state.Layers=on?_state.Layers|layer:_state.Layers&~layer;Canvas!.QueueRedraw();UpdateDetails();};layers.AddChild(toggle);}
        Canvas=new(state);Canvas.CellChosen+=Choose;AddChild(Canvas);
        AddChild(new Label{Text=L("Колесо / + −: масштаб · перетаскивание / стрелки / WASD: сдвиг · Home: весь город · касание: выбор, два пальца: масштаб.","Wheel / + −: zoom · drag / arrows / WASD: pan · Home: fit · tap: select, two fingers: zoom."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        _objects.ItemSelected+=i=>{_state.SelectedKey=_keys[i];UpdateDetails(false);};AddChild(_objects);AddChild(_details);
        var actions=TabletDesign.Flow();AddChild(actions);_open.Pressed+=OpenSelected;actions.AddChild(_open);
        _walk.Text=L("Маршрут и движение","Route and move");_walk.Pressed+=()=>{if(_guide is {Allowed:true} g)_submit(new(ActionKind.MoveTo,Target:g.Target));};actions.AddChild(_walk);AddChild(_routeInfo);
        var legend=Button("MapLegend",L("Легенда и собственники ▾","Legend and owners ▾"),()=>{_legend.Visible=!_legend.Visible;_owners.Visible=_legend.Visible;});AddChild(legend);_legend.Visible=true;AddChild(_legend);AddChild(_owners);
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private static Label Wrap()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private static Button Button(string name,string text,Action action){var b=new Button{Name=name,Text=text,CustomMinimumSize=new(44,44)};b.Pressed+=action;return b;}
    private string LayerTitle(CityLayer layer)=>layer switch{CityLayer.Resources=>L("Ресурсы","Resources"),CityLayer.Industry=>L("Объекты","Industry"),CityLayer.Property=>L("Собственность","Ownership"),CityLayer.Roads=>L("Дороги","Roads"),CityLayer.Services=>L("Услуги","Services"),CityLayer.Deliveries=>L("Поставки","Deliveries"),_=>L("Транспорт и люди","Vehicles and people")};
    public void ShowData(CityMapFrame map,PlayerView view,bool pending)
    {
        _map=map;_view=view;
        var ids=new[]{0L}.Concat(map.Participants).ToArray();
        if(!_participants.SequenceEqual(ids))
        {
            _participants=ids;_participant.Clear();_participant.AddItem(L("Все участники","All participants"));foreach(long id in map.Participants)_participant.AddItem(OwnerCaption(id));_participant.Select(Math.Max(0,Array.IndexOf(ids,_state.Participant)));
            foreach(Node child in _owners.GetChildren()){_owners.RemoveChild(child);child.QueueFree();}
            foreach(long id in map.Participants){var row=new HBoxContainer();row.AddChild(new ColorRect{Color=MapSymbols.ActorColor(id,id==map.Viewer),CustomMinimumSize=new(16,16),SizeFlagsVertical=SizeFlags.ShrinkCenter});row.AddChild(new Label{Text=OwnerCaption(id)});_owners.AddChild(row);}
        }
        Canvas.ShowData(map);Canvas.CustomMinimumSize=new(220,Math.Clamp(GetViewportRect().Size.Y*.43f,230,570));
        UpdateDetails();_walk.Disabled|=pending;_open.Disabled|=pending;
        _legend.Text=L("R ресурс · I уровень объекта · V машина · D услуги · → поставка. Обводка — участок, точка — участник. Цвет собственника совпадает с его цветом в мире.","R resource · I object level · V vehicle · D service · → delivery. Outline: plot; dot: participant. Owner colours match the world.")+"\n"+
            L("Грунт 20 · дорога I 40 · магистраль II 60. У дороги несколько вкладов; городская основа сохраняется. Тёмные клетки — преграды. Показаны только публичные и свои данные; ожидаемый груз не является физическим запасом.","Dirt 20 · road I 40 · highway II 60. Roads retain multiple contributions and the city foundation. Dark cells are obstacles. Only public and own data is shown; expected cargo is not physical stock.")+"\n"+string.Join(" · ",map.Participants.Select(OwnerCaption));
    }
    private string OwnerCaption(long id)=>id==0?L("Город / общее","City / public"):L("Участник #","Participant #")+id+" · "+WorldSkins.ActorName(id,_language);
    private void Choose(Cell cell)
    {
        if(_map is null)return;var choices=CityMap.At(_map,cell,_state.Layers,_state.Participant);SetChoices(choices);
        _state.SelectedKey=choices.FirstOrDefault()?.Key??$"cell:{cell.X}:{cell.Z}";
        if(choices.Length==0){_details.Text=L("Пустая клетка","Empty cell")+$" ({cell.X},{cell.Z})";_open.Disabled=true;Preview(cell);Canvas.QueueRedraw();return;}
        UpdateDetails(false);
    }
    private void SetChoices(CityMapObject[] choices)
    {
        var keys=choices.Select(c=>c.Key).ToArray();if(_keys.SequenceEqual(keys))return;_keys=keys;_objects.Clear();foreach(var c in choices)_objects.AddItem(Title(c));_objects.Visible=choices.Length>1;
    }
    private CityMapObject? Selected=>_map is null?null:CityMap.Selected(_map,_state.SelectedKey);
    private string Title(CityMapObject o)=>o.Kind switch
    {"cell"=>L("Пустая клетка","Empty cell"),"road"=>RoadTitle(o.Level),"bypass"=>L("Временный объезд","Temporary bypass"),"actor"=>OwnerCaption(o.Id),"vehicle" or "service"=>Model(o.Label)+" #"+o.Id,
        "resource" or "resource-depleted"=>PlayerText.Item(_language,o.Label),"production" or "warehouse" or "shop" or "project"=>PlayerText.Building(_language,o.Label)+" #"+o.Id,
        "pickup"=>L("Оплачено · забрать #","Paid · collect #")+o.Id+" · "+PlayerText.Item(_language,o.Label),"delivery" or "incoming" or "logistics"=>L("Поставка #","Delivery #")+o.Id+" · "+PlayerText.Item(_language,o.Label),"equipment"=>L("Добывающая техника #","Extraction equipment #")+o.Id,
        "civic"=>o.Label switch{"cityhall" or "town_hall"=>L("Ратуша","Town hall"),"capital_terminal"=>L("Терминал столицы","Capital terminal"),"bank"=>L("Банк","Bank"),"dealership"=>L("Автосалон","Dealership"),_=>L("Городской объект","City facility")},"capital"=>L("Биржа и столица","Exchange and capital"),_=>L("Участок #","Plot #")+o.Id};
    private string RoadTitle(int level)=>level switch{1=>L("Грунтовка","Dirt road"),2=>L("Дорога I","Road I"),3=>L("Магистраль II","Highway II"),_=>L("Нет покрытия","No surface")};
    private string Model(string id)=>_view?.Fleet?.Models.FirstOrDefault(m=>m.Id==id)?.Name??id;
    private void UpdateDetails(bool refreshChoices=true)
    {
        var selected=Selected;if(selected is null){_details.Text=L("Выберите объект или клетку. Масштаб и фильтры сохраняются при обновлении.","Select an object or cell. Live updates preserve zoom and filters.");_open.Disabled=_walk.Disabled=true;return;}
        if(selected.Layer!=0&&!CityMap.Visible(selected,_state.Layers,_state.Participant)){_details.Text=L("Выбранный объект скрыт фильтром. Выбор сохранён.","The selected object is hidden by a filter. Selection is retained.");_open.Disabled=_walk.Disabled=true;_guide=null;_objects.Visible=false;if(_map is not null)Canvas.ShowData(_map);return;}
        if(refreshChoices&&_map is not null)SetChoices(CityMap.At(_map,selected.Entry,_state.Layers,_state.Participant));
        int index=Array.IndexOf(_keys,selected.Key);if(index>=0)_objects.Select(index);
        _details.Text=Title(selected)+$" · ({selected.Entry.X},{selected.Entry.Z}) · "+(selected.Layer==CityLayer.Roads?L("Вклады по ступеням","Contributions by stage"):OwnerCaption(selected.Owner))+(selected.Level>0&&selected.Layer==CityLayer.Industry?L(" · уровень "," · level ")+selected.Level:"");
        if(selected.Holder>0)_details.Text+="\n"+L(selected.Layer==CityLayer.Resources?"Арендатор: ":"Бронь: ",selected.Layer==CityLayer.Resources?"Tenant: ":"Reserved by: ")+OwnerCaption(selected.Holder)+(selected.Deadline>0?L(" · до t"," · through t")+selected.Deadline:"");
        if(selected.Layer==CityLayer.Roads)
            _details.Text+="\n"+L("Вклады по ступеням и будущие права на доход:","Contributions by stage and future revenue rights:")+"\n"+string.Join("\n",(selected.Contributions??[]).OrderBy(c=>c.Level).Select(c=>RoadTitle(c.Level)+" · "+OwnerCaption(c.Builder.Value)+" · t"+c.Tick+L(" · денежный вклад "," · cash contribution ")+PlayerText.Money(c.PriceMinor)+(c.Builder.Value==0?L(" · городу 100%"," · city 100%"):L(" · исполнителю 90%, городу 10%"," · builder 90%, city 10%"))));
        if(selected.Layer==CityLayer.Deliveries)_details.Text+="\n"+PlayerText.Item(_language,selected.Label)+" ×"+selected.Quantity+L(" · срок t"," · due t")+selected.Deadline+L(" · осталось "," · remaining ")+GameDuration.Format(Math.Max(0,selected.Deadline-(_view?.Tick??0)),_language);
        if(selected.Kind=="service")_details.Text+="\n"+L("Вместимость по объёму: ","Volume capacity: ")+selected.Quantity+" · "+(selected.Status=="Available"?L("Свободна","Available"):L("Занята","Busy"));
        if(selected.Layer==CityLayer.Resources)_details.Text+="\n"+(selected.Status=="Depleted"?L("Истощён · дождитесь восстановления","Depleted · wait for regeneration"):L("Смотрите доступ и добычу на месте","Check access and harvest at the site"));
        if(selected.Owner!=_map!.Viewer&&selected.Owner>0)_details.Text+="\n"+L("Чужие запасы, счета и закрытые договоры скрыты.","Foreign inventories, accounts and private contracts are hidden.");
        _open.Text=L("Открыть карточку","Open card");_open.Disabled=!(selected.Owner==_map.Viewer&&selected.Kind is "production" or "warehouse" or "shop" or "equipment" or "vehicle"||selected.Kind=="service"||selected.Layer==CityLayer.Deliveries);
        Preview(selected.Entry,selected.Route);Canvas.QueueRedraw();
    }
    private void Preview(Cell target,Cell[]? agreed=null)
    {
        _guide=_route(target);_walk.Disabled=!_guide.Allowed;
        _routeInfo.Text=_guide.Allowed?L("Предварительный путь: ","Route preview: ")+Math.Max(0,_guide.Path.Length-1)+L(" клеток. Препятствия проверяются повторно при движении. Пустая земля не является пунктом услуги доставки."," cells. Obstacles are checked again during movement. Empty land is not a delivery-service endpoint."):PlayerText.Reason(_language,_guide.Reason);
        if(_map is not null)Canvas.ShowData(_map,agreed is {Length:>1}?agreed:[.._guide.Path]);
    }
    private void OpenSelected()
    {
        if(Selected is not {} o||_map is null)return;
        if(o.Owner==_map.Viewer&&o.Kind is "production" or "warehouse" or "shop")OpenObject?.Invoke("building",o.Id);
        else if(o.Owner==_map.Viewer&&o.Kind=="equipment")OpenObject?.Invoke("equipment",o.Id);
        else if(o.Kind=="vehicle")OpenSection?.Invoke("fleet",o.Id);
        else if(o.Kind=="service")OpenSection?.Invoke("service-vehicle",o.Id);
        else if(o.Kind=="delivery")OpenSection?.Invoke("services",o.Id);
        else if(o.Kind=="pickup")OpenSection?.Invoke("purchases",o.Id);
        else OpenSection?.Invoke("holdings",0);
    }
}

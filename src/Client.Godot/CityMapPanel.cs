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
    private readonly OptionButton _objects=new(){Name="MapObjects"};
    private readonly HFlowContainer _participant=new(){Name="MapParticipants"};
    private readonly Label _participantCaption=Wrap(),_legend=Wrap(),_details=Wrap(),_routeInfo=Wrap();
    private readonly HFlowContainer _owners=new();
    private readonly HBoxContainer _modes=new(){Name="MapFocusModes"},_body=new(){SizeFlagsVertical=SizeFlags.ExpandFill};
    private readonly HFlowContainer _toolbar;
    private HFlowContainer _layerRow=null!;
    private readonly VBoxContainer _side=new(){Name="MapDetails"},_filters=new(){Visible=false};
    private readonly Button _open=new(){Name="MapOpenObject"},_walk=new(){Name="MapNavigate"},_showRoute=new(){Name="MapShowRoute"},_detailsToggle=new(){Name="MapDetailsToggle"};
    private readonly Dictionary<CityLayer,CheckButton> _layers=new();
    private readonly Dictionary<CityMapFocus,Button> _focus=new();
    private CityMapFrame? _map;private PlayerView? _view;private MovementGuide? _guide;private string[] _keys=[];private long[] _participants=[];
    private bool _detailsOpen;
    public CityMapPanel(ScenarioDefinition scenario,string language,CityMapPreferences state,Action<GameAction> submit,Func<Cell,MovementGuide> route)
    {
        Name="FullCityMap";_language=language;_state=state;_submit=submit;_route=route;SizeFlagsVertical=SizeFlags.ExpandFill;
        Canvas=new(state);Canvas.CellChosen+=Choose;Canvas.SizeFlagsVertical=SizeFlags.ExpandFill;
        _modes.AddThemeConstantOverride("separation",4);AddChild(_modes);
        foreach(var focus in new[]{CityMapFocus.All,CityMapFocus.Production,CityMapFocus.Transport,CityMapFocus.Orders,CityMapFocus.Property})
        {
            var choice=new Button{Name="MapFocus"+focus,Text=FocusTitle(focus),ToggleMode=true,CustomMinimumSize=new(40,36)};
            choice.SetPressedNoSignal(focus==_state.Focus);choice.Toggled+=on=>{if(!on||_state.Focus==focus)return;_state.ApplyFocus(focus);SyncLayers();Canvas.QueueRedraw();UpdateDetails();};
            _focus[focus]=choice;_modes.AddChild(choice);
        }
        _toolbar=TabletDesign.Flow();AddChild(_toolbar);
        _toolbar.AddChild(Button("MapZoomIn","+",()=>Canvas.Zoom(1.25)));_toolbar.AddChild(Button("MapZoomOut","−",()=>Canvas.Zoom(.8)));
        _toolbar.AddChild(Button("MapFit",L("Весь город","Fit city"),()=>Canvas.Fit()));
        _toolbar.AddChild(Button("MapMe",L("Ко мне","Find me"),()=>{if(_view is null)return;_state.Center(_view.Participant.Position.X+.5,_view.Participant.Position.Z+.5);Canvas.Zoom(2);}));
        _toolbar.AddChild(Button("MapFilters",L("Фильтры","Filters"),()=>_filters.Visible=!_filters.Visible));
        _detailsToggle.Text=L("Сведения","Details");_detailsToggle.CustomMinimumSize=new(40,36);_detailsToggle.Pressed+=()=>{_detailsOpen=!_detailsOpen;ApplyLayout();};_toolbar.AddChild(_detailsToggle);
        _filters.AddChild(_participantCaption);_filters.AddChild(_participant);
        var layers=TabletDesign.Flow();_filters.AddChild(layers);_layerRow=layers;
        foreach(var layer in new[]{CityLayer.Resources,CityLayer.Industry,CityLayer.Property,CityLayer.Roads,CityLayer.Services,CityLayer.Deliveries,CityLayer.People})
        {
            var toggle=new CheckButton{Name="MapLayer"+layer,Text=LayerTitle(layer),ButtonPressed=(_state.Layers&layer)!=0,CustomMinimumSize=new(0,36)};
            toggle.Toggled+=on=>{_state.Focus=CityMapFocus.All;_state.Layers=on?_state.Layers|layer:_state.Layers&~layer;SyncFocus();Canvas.QueueRedraw();UpdateDetails();};
            _layers[layer]=toggle;layers.AddChild(toggle);
        }
        AddChild(_filters);
        _body.AddChild(Canvas);
        _side.CustomMinimumSize=new(280,0);_side.SizeFlagsVertical=SizeFlags.ExpandFill;_side.AddThemeConstantOverride("separation",4);
        _details.AddThemeColorOverride("font_color",new Color("24312d"));_routeInfo.AddThemeColorOverride("font_color",new Color("24312d"));
        var detailsScroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,CustomMinimumSize=new(0,56)};
        _details.SizeFlagsHorizontal=SizeFlags.ExpandFill;detailsScroll.AddChild(_details);
        _side.AddChild(detailsScroll);_side.AddChild(_objects);_side.AddChild(_routeInfo);
        var actions=TabletDesign.Flow();_side.AddChild(actions);
        _open.CustomMinimumSize=new(40,36);_open.Pressed+=OpenSelected;actions.AddChild(_open);
        _showRoute.Text=L("Показать маршрут","Show route");_showRoute.CustomMinimumSize=new(40,36);_showRoute.Pressed+=()=>{if(Selected is {} o)Preview(o.Entry,o.Route,true);};actions.AddChild(_showRoute);
        _walk.Text=L("Перейти к объекту","Go to object");_walk.CustomMinimumSize=new(40,36);_walk.Pressed+=()=>{if(_guide is {Allowed:true} g)_submit(new(ActionKind.MoveTo,Target:g.Target));};actions.AddChild(_walk);
        _body.AddChild(_side);AddChild(_body);
        _objects.ItemSelected+=i=>{_state.SelectedKey=_keys[i];UpdateDetails(false);};
        var legend=Button("MapLegend",L("Легенда","Legend"),()=>{_legend.Visible=!_legend.Visible;_owners.Visible=_legend.Visible;});AddChild(legend);_legend.Visible=false;_owners.Visible=false;AddChild(_legend);AddChild(_owners);
        Resized+=ApplyLayout;
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private static Label Wrap()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private static Button Button(string name,string text,Action action){var b=new Button{Name=name,Text=text,CustomMinimumSize=new(36,36)};b.Pressed+=action;return b;}
    private string LayerTitle(CityLayer layer)=>layer switch{CityLayer.Resources=>L("Ресурсы","Resources"),CityLayer.Industry=>L("Объекты","Industry"),CityLayer.Property=>L("Собственность","Ownership"),CityLayer.Roads=>L("Дороги","Roads"),CityLayer.Services=>L("Услуги","Services"),CityLayer.Deliveries=>L("Поставки","Deliveries"),_=>L("Транспорт","Transport")};
    private string FocusTitle(CityMapFocus focus)=>focus switch{CityMapFocus.Production=>L("Производство","Production"),CityMapFocus.Transport=>L("Транспорт","Transport"),CityMapFocus.Orders=>L("Заказы","Orders"),CityMapFocus.Property=>L("Собственность","Property"),_=>L("Все объекты","All objects")};
    private bool Narrow(){if(!IsInsideTree())return false;var size=GetViewportRect().Size;return size.Y<520||size.X<1000;}
    private float PageWidth()
    {
        for(Node? node=this;node is not null;node=node.GetParent())
            if(node is ScrollContainer scroll&&scroll.Size.X>1)return scroll.Size.X;
        return IsInsideTree()?GetViewportRect().Size.X:Size.X;
    }
    private void ApplyLayout()
    {
        bool narrow=Narrow();_detailsToggle.Visible=narrow;_side.Visible=!narrow||_detailsOpen;
        _side.CustomMinimumSize=new(narrow?Mathf.Min(320,GetViewportRect().Size.X*.42f):280,0);
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Mathf.Min(Size.X,PageWidth()));
        // Ряд шире планшета сдвигает карту вбок, и подобранная клетка оказывается за окном.
        float page=PageWidth();FitRow(_modes,page);FitRow(_toolbar,page);FitRow(_layerRow,page);FitRow(_participant,page);
        for(Node? node=GetParent();node is not null;node=node.GetParent())
            if(node is ScrollContainer scroll){scroll.ScrollHorizontal=0;break;}
    }
    private static void FitRow(Control? row,float width)
    {
        if(row is null||width<1)return;
        var buttons=row.GetChildren().OfType<Button>().Where(b=>b.Visible).ToArray();
        if(buttons.Length==0)return;
        int separation=row.GetThemeConstant("separation");
        float gap=separation*Mathf.Max(0,buttons.Length-1);
        float available=Mathf.Max(1,width-gap);
        float[] natural=buttons.Select(b=>b.GetThemeFont("font").GetStringSize(b.Text,fontSize:b.GetThemeFontSize("font_size")).X+20).ToArray();
        float sum=natural.Sum();
        if(sum<=available)return;
        float scale=available/sum;
        float[] widths=natural.Select(n=>Mathf.Max(28f,n*scale)).ToArray();
        if(widths.Sum()>available){float each=available/buttons.Length;widths=Enumerable.Repeat(each,buttons.Length).ToArray();}
        for(int i=0;i<buttons.Length;i++)
        {
            buttons[i].ClipText=true;
            buttons[i].CustomMinimumSize=new(widths[i],Mathf.Max(36,buttons[i].CustomMinimumSize.Y));
            buttons[i].SizeFlagsHorizontal=SizeFlags.ShrinkBegin;
        }
    }
    private void SyncLayers(){foreach(var (layer,toggle) in _layers)toggle.SetPressedNoSignal((_state.Layers&layer)!=0);SyncFocus();}
    private void SyncFocus(){foreach(var (focus,button) in _focus)button.SetPressedNoSignal(focus==_state.Focus);}
    public void ShowData(CityMapFrame map,PlayerView view,bool pending)
    {
        _map=map;_view=view;SyncFocus();
        var ids=new[]{0L}.Concat(map.Participants).ToArray();
        if(!_participants.SequenceEqual(ids))
        {
            _participants=ids;
            foreach(Node child in _participant.GetChildren()){_participant.RemoveChild(child);child.QueueFree();}
            var group=new ButtonGroup{AllowUnpress=false};
            foreach(long id in ids)
            {
                var choice=new Button{Name="MapParticipant_"+id,Text=id==0?L("Все","All"):WorldSkins.ActorName(id,_language),TooltipText=id==0?L("Все участники","All participants"):OwnerCaption(id),ToggleMode=true,ButtonGroup=group,CustomMinimumSize=new(40,36)};
                choice.SetPressedNoSignal(id==_state.Participant);
                choice.Toggled+=on=>{if(!on)return;_state.Participant=id;Canvas.QueueRedraw();UpdateDetails();};_participant.AddChild(choice);
            }
            foreach(Node child in _owners.GetChildren()){_owners.RemoveChild(child);child.QueueFree();}
            foreach(long id in map.Participants){var row=new HBoxContainer();row.AddChild(new ColorRect{Color=MapSymbols.ActorColor(id,id==map.Viewer),CustomMinimumSize=new(16,16),SizeFlagsVertical=SizeFlags.ShrinkCenter});row.AddChild(new Label{Text=OwnerCaption(id)});_owners.AddChild(row);}
        }
        _participantCaption.Text=L("По участнику: ","By participant: ")+(_state.Participant==0?L("Все участники","All participants"):OwnerCaption(_state.Participant));
        Canvas.ShowData(map);Canvas.CustomMinimumSize=new(160,120);
        UpdateDetails();_walk.Disabled|=pending;_open.Disabled|=pending;ApplyLayout();
        _legend.Text=L("Свободная земля — светлый контур, своя и чужая — цвет участника из мира, бронь — пунктир. Жёлтая полоса — готовность партии, заполнение или доставленная доля. Голубая линия — путь персонажа, пунктир — поставка.","Free land is a light outline. Own and other property use the same participant colours as the world. A reservation is dashed. The yellow bar is batch progress, cargo fill or the delivered share. The blue line is the person's route; a dashed line is a delivery.");
    }
    private string OwnerCaption(long id)=>id==0?L("Город / общее","City / public"):L("Участник #","Participant #")+id+" · "+WorldSkins.ActorName(id,_language);
    private void Choose(Cell cell)
    {
        if(_map is null)return;var choices=CityMap.At(_map,cell,_state.Layers,_state.Participant,_state.Focus);SetChoices(choices);
        if(choices.Length>1&&choices.Any(c=>c.Key==_state.SelectedKey))
        {int index=Array.FindIndex(choices,c=>c.Key==_state.SelectedKey);_state.SelectedKey=choices[(index+1)%choices.Length].Key;}
        else _state.SelectedKey=choices.FirstOrDefault()?.Key??$"cell:{cell.X}:{cell.Z}";
        if(choices.Length==0){_details.Text=L("Пустая клетка","Empty cell")+$" ({cell.X},{cell.Z})";_open.Disabled=true;Preview(cell,null,false);Canvas.QueueRedraw();return;}
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
        "pickup"=>L("Оплачено · забрать #","Paid · collect #")+o.Id+" · "+PlayerText.Item(_language,o.Label),"delivery" or "incoming" or "outgoing" or "offer" or "logistics"=>L("Заказ #","Order #")+o.Id+" · "+PlayerText.Item(_language,o.Label),"equipment"=>L("Добывающая техника #","Extraction equipment #")+o.Id,
        "civic"=>o.Label switch{"cityhall" or "town_hall"=>L("Ратуша","Town hall"),"capital_terminal"=>L("Терминал столицы","Capital terminal"),"bank"=>L("Банк","Bank"),"dealership"=>L("Автосалон","Dealership"),_=>L("Городской объект","City facility")},"capital"=>L("Биржа и столица","Exchange and capital"),_=>L("Участок #","Plot #")+o.Id};
    private string RoadTitle(int level)=>level switch{1=>L("Грунтовка","Dirt road"),2=>L("Дорога I","Road I"),3=>L("Магистраль II","Highway II"),_=>L("Нет покрытия","No surface")};
    private string Model(string id)=>_view?.Fleet?.Models.FirstOrDefault(m=>m.Id==id)?.Name??id;
    private void UpdateDetails(bool refreshChoices=true)
    {
        _participantCaption.Text=L("По участнику: ","By participant: ")+(_state.Participant==0?L("Все участники","All participants"):OwnerCaption(_state.Participant));
        var selected=Selected;
        if(selected is null)
        {
            _details.Text=_state.SelectedKey.Length==0?L("Выберите объект на карте. Масштаб, фильтр и выбор сохраняются.","Select an object. Zoom, filters and selection stay put."):L("Объект больше недоступен. Выбор сохранён.","The object is no longer available. Selection is kept.")+" "+_state.SelectedKey;
            _open.Disabled=_walk.Disabled=_showRoute.Disabled=true;_routeInfo.Text="";return;
        }
        if(selected.Layer!=0&&!CityMap.Shown(selected,_state.Layers,_state.Participant,_state.Focus))
        {_details.Text=Title(selected)+"\n"+L("Объект скрыт фильтром. Выбор, масштаб и положение сохранены.","The object is hidden by a filter. Selection, zoom and position are kept.");_open.Disabled=_walk.Disabled=true;_guide=null;_objects.Visible=false;if(_map is not null)Canvas.ShowData(_map);return;}
        if(refreshChoices&&_map is not null)SetChoices(CityMap.At(_map,selected.Entry,_state.Layers,_state.Participant,_state.Focus));
        int index=Array.IndexOf(_keys,selected.Key);if(index>=0)_objects.Select(index);
        _details.Text=Describe(selected);_open.Text=L("Открыть карточку","Open card");
        _open.Disabled=!(CityMap.Card(selected,_map!.Viewer) is not null||CityMap.Section(selected,_map.Viewer) is not null);
        _showRoute.Disabled=false;Preview(selected.Entry,selected.Route,false);Canvas.QueueRedraw();
    }
    private string Describe(CityMapObject o)
    {
        var lines=new List<string>{Title(o),OwnerCaption(o.Owner)+" · ("+o.Entry.X+", "+o.Entry.Z+")"};
        if(o.Facts is {} f)
        {
            string headline=State(f.State)+(f.Detail.Length==0?"":" · "+Detail(o,f));
            if(f.Percent() is int shown)headline+=" · "+shown+"%";
            lines.Add(headline);
            if(f.Family=="production")lines.Add(ProductionLine(o,f));
            else if(f.Family is "cargo" or "storage" or "harvest")lines.Add(VolumeLine(f));
            else if(f.Family is "contract" or "service" or "pickup" or "offer" or "logistics")lines.Add(OrderLine(f,o));
            if(f.Family=="land")lines.Add(LandLine(f));
            if(f.Reason.Length>0)lines.Add(PlayerText.Reason(_language,f.Reason));
            if(f.Arrives is long arrives&&_view is not null)lines.Add(L("Прибытие через ","Arrival in ")+GameDuration.Format(Math.Max(0,arrives-_view.Tick),_language));
            if(f.Late)lines.Add(L("Срок прошёл","Deadline passed"));
            else if(f.Soon)lines.Add(L("Срок близко по действующему окну","Deadline is inside the existing warning window"));
        }
        else if(o.Kind is "production" or "warehouse" or "shop" or "equipment" or "vehicle")
            lines.Add(o.Owner==_map!.Viewer?L("Нет данных","No data"):L("Чужие рецепты, запасы и внутренние показатели скрыты.","Foreign recipes, stocks and internal figures are hidden."));
        if(o.Level>0&&o.Kind is "production" or "equipment")lines.Add(L("Уровень ","Level ")+o.Level);
        if(o.Holder>0)lines.Add(L(o.Layer==CityLayer.Resources?"Арендатор: ":"Бронь: ",o.Layer==CityLayer.Resources?"Tenant: ":"Reserved by: ")+OwnerCaption(o.Holder)+(o.Deadline>0?L(" · до t"," · through t")+o.Deadline:""));
        if(o.Layer==CityLayer.Roads)
        {
            lines.Add(RoadCardText.Header(_language));
            lines.Add(string.Join("\n",(o.Contributions??[]).OrderBy(c=>c.Level).Select(c=>RoadCardText.Line(_language,RoadTitle(c.Level),OwnerCaption(c.Builder.Value),c))));
        }
        var reason=OwnReason(o);if(reason.Length>0&&(o.Facts?.Reason.Length??0)==0)lines.Add(reason);
        return string.Join("\n",lines.Where(l=>l.Length>0));
    }
    private string OwnReason(CityMapObject o)
    {
        if(_view is null||o.Owner!=_map?.Viewer)return "";
        if(o.Kind=="production"&&_view.Enterprises.FirstOrDefault(e=>e.Id.Value==o.Id) is {} e)return PlayerText.Reason(_language,e.ReasonCode);
        if(o.Kind=="equipment"&&_view.Equipment?.FirstOrDefault(e=>e.Id.Value==o.Id) is {} machine&&machine.ReasonCode.Length>0)return PlayerText.Reason(_language,machine.ReasonCode);
        return "";
    }
    private string Detail(CityMapObject o,CityMapFacts f)=>o.Kind is "production" or "resource" or "resource-depleted"||f.Family is "production" or "extraction" or "harvest"||f.Family is "contract" or "service" or "pickup" or "offer" or "logistics"?PlayerText.Item(_language,f.Detail.Length==0?o.Label:f.Detail):f.Detail;
    private string ProductionLine(CityMapObject o,CityMapFacts f)
    {
        var enterprise=_view?.Enterprises.FirstOrDefault(e=>e.Id.Value==o.Id&&e.OwnerId.Value==_map!.Viewer);
        string recipe=enterprise is null?"":L("Рецепт: ","Recipe: ")+PlayerText.Item(_language,enterprise.RecipeId);
        if(f.Percent() is int pct)return recipe+"\n"+L("Партия ","Batch ")+f.Done+"/"+f.Need+" · "+pct+"% · "+L("завершено ","completed ")+f.Finished;
        return recipe+"\n"+(f.State=="Stopped"?L("Активной партии нет","No active batch"):L("Нет данных о партии","No batch data"))+(f.Finished>=0?L(" · завершено "," · completed ")+f.Finished:"");
    }
    private string VolumeLine(CityMapFacts f)
    {
        if(f.Room<0||f.Used<0)return L("Нет данных о заполнении","No fill data");
        string text=L("Занято ","Filled ")+f.Used+" / "+f.Room;
        if(f.Percent() is int pct)text+=" · "+pct+"%";else text+=" · "+L("Нет данных","No data");
        if(f.Family=="harvest"&&f.Need>0&&f.Done>=0)text+="\n"+L("Цикл добычи ","Extraction cycle ")+f.Done+"/"+f.Need+(CityMap.BatchPercent(1,f.Done,f.Need) is int cycle?" · "+cycle+"%":"");
        else if(f.Family=="harvest")text+="\n"+L("Активного цикла нет","No active cycle");
        if(f.Ready>0)text+="\n"+L("Зарезервированный объём: ","Reserved volume: ")+f.Ready;
        return text;
    }
    private string OrderLine(CityMapFacts f,CityMapObject o)
    {
        if(f.Family=="logistics")return L("В пути: ","In transit: ")+f.Haul+" · "+State(f.State)+(f.Arrives is null?L(" · время прибытия не задано"," · no authoritative arrival"):"");
        if(f.Family=="pickup")return L("Оплачено, ещё не получено: ","Paid, not yet collected: ")+f.Ready+L(". Это не доставка."," This is not a delivery.");
        if(f.Family=="offer")return L("Открытая заявка. Выделено ","Open request. Allocated ")+Math.Max(0,f.Ready)+L(" из "," of ")+f.Need+L(". Это ещё не поставка."," This is not a delivery yet.");
        if(f.Done<0||f.Need<=0)return L("Нет данных о выполнении","No completion data")+" · "+State(f.State);
        string text=L("Доставлено ","Delivered ")+f.Done+L(" из "," of ")+f.Need;
        text+=f.Percent() is int pct?" · "+pct+"%":" · "+L("Нет данных","No data");
        text+="\n"+L("Осталось ","Remaining ")+Math.Max(0,f.Need-f.Done);
        if(f.Ready>=0)text+=" · "+L("готово или зарезервировано ","ready or reserved ")+f.Ready;
        if(f.Haul>=0)text+=" · "+L("в пути ","in transit ")+f.Haul;
        if(o.Deadline>0&&_view is not null)text+="\n"+L("Срок через ","Due in ")+GameDuration.Format(Math.Max(0,o.Deadline-_view.Tick),_language);
        return text+" · "+State(f.State);
    }
    private string LandLine(CityMapFacts f)=>f.State switch{"Free"=>L("Свободная земля","Free land"),"Own"=>L("Собственность игрока","Player property"),"Npc"=>L("Собственность другого участника","Another participant's property"),"Reserved"=>L("Участок зарезервирован","Plot is reserved"),"Shop"=>L("Место лавки","Shop site"),_=>L("Участок","Plot")}+(f.Detail=="Occupied"?L(" · на участке есть объект"," · a building stands here"):"");
    private string State(string state)=>state switch
    {
        "Working"=>L("Работает","Working"),"Stopped"=>L("Остановлено","Stopped"),"Blocked"=>L("Нет необходимых условий","Conditions are not met"),"Idle"=>L("Ожидает условий","Waiting for conditions"),
        "Harvesting"=>L("Добывает","Extracting"),"Moving"=>L("Едет","Moving"),"Waiting"=>L("Ожидает","Waiting"),"Parked"=>L("Свободна","Parked"),"Personal"=>L("Личное управление","Personal control"),
        "Available"=>L("Свободна","Available"),"Busy"=>L("Занята","Busy"),"Storage"=>L("Хранилище","Storage"),"Free"=>L("Свободно","Free"),"Own"=>L("Ваше","Yours"),"Npc"=>L("Другой участник","Another participant"),
        "Reserved"=>L("Бронь","Reserved"),"Shop"=>L("Лавка","Shop"),"Leased"=>L("Аренда","Lease"),"Owned"=>L("Своя территория","Owned ground"),"Open"=>L("Открыто","Open"),
        "AwaitingPickup"=>L("Ожидает получения","Awaiting collection"),"Active"=>L("Выполняется","In progress"),"PartiallyDelivered"=>L("Частично доставлено","Partially delivered"),"Fulfilled"=>L("Доставлено","Delivered"),"Expired"=>L("Просрочено","Overdue"),
        "Pending"=>L("Ожидает ответа","Waiting for a reply"),"Accepted"=>L("Принят","Accepted"),"Requote"=>L("Нужно подтверждение","Needs confirmation"),_=>PlayerText.Reason(_language,state)
    };
    private void Preview(Cell target,Cell[]? agreed=null,bool center=false)
    {
        _guide=_route(target);_walk.Disabled=!_guide.Allowed;
        _routeInfo.Text=_guide.Allowed?L("Маршрут: ","Route: ")+Math.Max(0,_guide.Path.Length-1)+L(" клеток. Движение начинается только по кнопке перехода."," cells. Movement starts only from the go button."):PlayerText.Reason(_language,_guide.Reason);
        if(center)_state.Center(target.X+.5,target.Z+.5);
        if(_map is not null)Canvas.ShowData(_map,_guide.Allowed&&_guide.Path.Length>1?[.._guide.Path]:agreed is {Length:>1}?agreed:[]);
    }
    private void OpenSelected()
    {
        if(Selected is not {} o||_map is null)return;
        if(CityMap.Card(o,_map.Viewer) is {} card)OpenObject?.Invoke(card.Kind,card.Id);
        else if(CityMap.Section(o,_map.Viewer) is {} section)OpenSection?.Invoke(section,o.Id);
    }
}

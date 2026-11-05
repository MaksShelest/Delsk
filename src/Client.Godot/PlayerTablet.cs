using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Kit pages over own/bilateral projections and public city totals.</summary>
public partial class PlayerTablet : VBoxContainer
{
    public event System.Action? HelpRequested;
    public event Action<string,long>? ObjectRequested;
    public OptionButton SectionPicker=>_mobile;
    public event Action<long>? RoadWorkRequested;
    public event System.Action? MapRequested;
    public event System.Action? TutorialRequested;
    public event System.Action? NoticesRequested;
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction,ActionAccess> _access;
    private readonly TabContainer _tabs=new(){Name="PlayerSections",TabsVisible=false,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly Dictionary<string,int> _pages=[];
    private readonly Dictionary<string,Button> _navigation=[];
    private readonly Dictionary<string,(Button Header,VBoxContainer Body,string Title)> _groups=[];
    private readonly Dictionary<string,string> _pageGroups=[];
    private readonly OptionButton _mobile=new(){Name="PlayerSectionPicker",FitToLongestItem=false,CustomMinimumSize=new(0,40)};
    private readonly ScrollContainer _navScroll=new(){Name="PlayerNavigation",CustomMinimumSize=new(220,0),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
    private readonly VBoxContainer _nav=new(){SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly VBoxContainer _summaryObjects=new();
    private readonly Label _notices=Wrapped();
    private Label? _summaryFleet;
    private string _summarySignature="uninitialized";
    private readonly VBoxContainer _overview=new(),_cardHost=new(),_holdings=new();
    private readonly Button _back=new();
    private int _holdingOffset;private readonly Button _previousHoldings=new(){Name="HoldingsPrevious"},_nextHoldings=new(){Name="HoldingsNext"};private readonly Label _holdingPage=new();
    private readonly Label _empty=Wrapped(),_participants=Wrapped(),_city=Wrapped(),_labor=Wrapped();
    private readonly Label _capital=TabletDesign.Heading("",27),_employed=TabletDesign.Heading("",27),_room=TabletDesign.Heading("",27);
    private readonly CurrentDealsPanel _deals;
    private readonly DevelopmentPanel _development;
    private readonly ServicePanel? _services;
    private string _section="overview";
    public string CurrentSection=>_section;
    public string HelpTopic=>_section switch{"account"=>"account","services"=>"services","fleet"=>"dealership","roads"=>"map",_=>"holdings"};
    public void OpenServices()=>OpenSection("services");
    private readonly FleetPanel? _fleet;
    private readonly BackpackPanel _backpack;
    private readonly RoadPanel? _roads;
    private readonly PersonalAccountPanel _account;
    public void OpenAccount()=>OpenSection("account");
    private readonly TradeNegotiationPanel _negotiations;
    private readonly PurchaseGuidePanel _pickups;
    private readonly EquipmentPurchasePanel _purchases;
    private readonly Dictionary<(string,long),(Label Stock,ProgressBar Progress,Button Walk)> _rows=[];
    private HouseholdObjectCard? _card;private StoragePanel? _groundCard;private (string Kind,long Id)? _cardObject;private readonly Func<long,bool,DemolitionQuote?>? _demolitionQuote;private readonly VBoxContainer _parked=new();private readonly Dictionary<long,RouteEditor> _parkedRoutes=[];
    private PlayerView _view=null!;
    private bool _pending,_paused;
    private string _signature="";
    public VBoxContainer PoliticsHost {get;}=new(){Name="CityPolitics"};
    public int SelectedTab{get=>_section switch{"participants"=>1,"negotiations" or "purchases"=>2,"city"=>3,_=>0};set=>OpenSection(value switch{1=>"participants",2=>"negotiations",3=>"city",_=>"overview"});}
    public PlayerTablet(ScenarioDefinition scenario,string language,System.Action<GameAction> submit,System.Func<GameAction,ActionAccess> access,Func<long,bool,DemolitionQuote?>? demolitionQuote=null,Func<int,int,PersonalAccount>? accountQuery=null,Func<ServiceTerms,ServiceQuoteResult>? serviceQuery=null)
    {
        _demolitionQuote=demolitionQuote;Name="PlayerPages";_scenario=scenario;_language=language;_submit=submit;_access=access;
        Theme=CompactTabletDesign.Theme;SizeFlagsVertical=SizeFlags.ExpandFill;
        AddChild(_mobile);var workspace=new HBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill};AddChild(workspace);
        workspace.AddChild(_navScroll);var inset=new MarginContainer{Name="PlayerNavigationInset",SizeFlagsHorizontal=SizeFlags.ExpandFill};
        inset.AddThemeConstantOverride("margin_left",13);inset.AddThemeConstantOverride("margin_right",13);_navScroll.AddChild(inset);inset.AddChild(_nav);workspace.AddChild(_tabs);
        string currentGroup="";
        void Group(string key,string ru,string en)
        {
            currentGroup=key;string title=L(ru,en);
            var header=new Button{Name="PlayerGroup_"+key,Text="▾ "+title,ToggleMode=true,ButtonPressed=true,Alignment=HorizontalAlignment.Left,CustomMinimumSize=new(0,32),SizeFlagsHorizontal=SizeFlags.ExpandFill,ClipText=true};
            header.AddThemeFontSizeOverride("font_size",13);header.AddThemeStyleboxOverride("pressed",CompactTabletDesign.Surface("eef1ef",6));header.AddThemeColorOverride("font_pressed_color",new("52625c"));
            var body=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};_nav.AddChild(header);_nav.AddChild(body);_groups.Add(key,(header,body,title));
            header.Toggled+=open=>{body.Visible=open;header.Text=(open?"▾ ":"▸ ")+title;header.TooltipText=L(open?"Свернуть группу":"Раскрыть группу",open?"Collapse group":"Expand group");};
        }
        void Nav(string key,string ru,string en)
        {
            var button=CompactTabletDesign.Button("PlayerNav_"+key,L(ru,en),()=>OpenSection(key));button.ToggleMode=true;button.Alignment=HorizontalAlignment.Left;
            button.SizeFlagsHorizontal=SizeFlags.ExpandFill;button.CustomMinimumSize=new(0,40);button.ClipText=true;button.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;button.TooltipText=L(ru,en);
            _groups[currentGroup].Body.AddChild(button);_navigation.Add(key,button);_pageGroups.Add(key,currentGroup);_mobile.AddItem(L(ru,en));
        }
        Group("holdings","ХОЗЯЙСТВО","HOLDINGS");Nav("overview","Обзор","Overview");Nav("holdings","Мои объекты","My objects");Nav("inventory","Рюкзак","Backpack");
        if(scenario.Economy?.Fleet is not null)Nav("fleet","Транспорт","Vehicles");
        if(scenario.Economy?.Services is not null)Nav("services","Доставка","Delivery");
        if(scenario.Economy?.Roads is not null)Nav("roads","Дороги","Roads");
        Group("economy","ЭКОНОМИКА","ECONOMY");Nav("account","Счёт","Account");Nav("quests","Задания","Quests");Nav("negotiations","Переговоры","Negotiations");Nav("purchases","Покупки","Purchases");
        Group("city","ГОРОД","CITY");Nav("participants","Участники","Participants");Nav("city","Город и политика","City & politics");
        Group("information","ИНФОРМАЦИЯ","INFORMATION");Nav("map","Карта","Map");Nav("notices","События","Notices");Nav("tutorial","Обучение","Guide");
        _mobile.ItemSelected+=index=>OpenSection(_navigation.Keys.ElementAt((int)index));
        var overview=Page("overview");overview.AddChild(_overview);_overview.AddChild(TabletDesign.Heading(L("Обзор хозяйства","Holdings overview"),21));
        var stats=new TabletColumns{Breakpoint=560};_overview.AddChild(stats);
        Stat(stats,"coin",_capital,L("Капитал","Capital"));Stat(stats,"people",_employed,L("Занятые работники","Employed staff"));Stat(stats,"bag",_room,L("Свободно в рюкзаке","Backpack room"));
        var quick=TabletDesign.Flow();_overview.AddChild(quick);
        quick.AddChild(CompactTabletDesign.Button("OverviewObjects",L("Мои объекты","My objects"),()=>OpenSection("holdings")));
        if(scenario.Economy?.Fleet is not null)quick.AddChild(CompactTabletDesign.Button("OverviewFleet",L("Транспорт","Vehicles"),()=>OpenSection("fleet")));
        quick.AddChild(CompactTabletDesign.Button("OverviewAccount",L("Счёт","Account"),()=>OpenAccount()));
        quick.AddChild(CompactTabletDesign.Button("OverviewNotices",L("События","Notices"),()=>NoticesRequested?.Invoke()));
        _overview.AddChild(CompactTabletDesign.Card(_notices));_overview.AddChild(_summaryObjects);
        var farm=Page("holdings");farm.AddChild(_empty);_empty.Text=L("Своих объектов пока нет. Добывайте сырьё, продавайте столице, покупайте участок и стройте.","No holdings yet. Harvest inputs, sell to the capital, buy a plot and build.");
        var holdingPages=TabletDesign.Flow();_previousHoldings.Text=L("‹ Назад","‹ Previous");_nextHoldings.Text=L("Далее ›","Next ›");holdingPages.AddChild(_previousHoldings);holdingPages.AddChild(_holdingPage);holdingPages.AddChild(_nextHoldings);farm.AddChild(holdingPages);farm.AddChild(_holdings);
        void HoldingsPage(int delta){_holdingOffset=Math.Max(0,_holdingOffset+delta);_signature="";if(_view is not null)ShowData(_view,_pending,_paused);}
        _previousHoldings.Pressed+=()=>HoldingsPage(-20);_nextHoldings.Pressed+=()=>HoldingsPage(20);
        _back.Text=L("‹ Мои объекты","‹ My objects");_back.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;_back.Visible=false;_back.Pressed+=Back;
        var detail=Page("object");detail.AddChild(_back);detail.AddChild(_cardHost);
        _backpack=new(scenario,language,submit,access);Page("inventory").AddChild(_backpack);
        _account=new(language,submit,accountQuery);Page("account").AddChild(_account);
        if(scenario.Economy?.Services is not null){_services=new(scenario,language,submit,serviceQuery,access);Page("services").AddChild(_services);}
        if(scenario.Economy?.Fleet is not null){_fleet=new(language,submit,access,scenario);_fleet.RoadWorkRequested+=id=>RoadWorkRequested?.Invoke(id);Page("fleet").AddChild(_fleet);Page("fleet").AddChild(_parked);}
        if(scenario.Economy?.Fleet is null)Page("holdings").AddChild(_parked);
        if(scenario.Economy?.Roads is not null){_roads=new(language,submit,access);Page("roads").AddChild(_roads);}
        _development=new(scenario,language,0,submit,access);Page("quests").AddChild(_development);
        _purchases=new(scenario,language,submit,access);_purchases.DealerRequested+=()=>{OpenSection("fleet");_fleet?.ShowCatalog(true);};Page("equipment").AddChild(_purchases);
        Page("holdings").AddChild(CompactTabletDesign.Button("PlayerBuyEquipment",L("Купить технику","Buy equipment"),()=>OpenSection("equipment")));
        Page("participants").AddChild(_participants);
        var talks=Page("negotiations");_negotiations=new(scenario,language,submit);talks.AddChild(_negotiations);
        _deals=new(scenario,language,submit,access);talks.AddChild(Fold(L("Закупки, договоры и поддержка","Offers, contracts and support"),_deals));
        _pickups=new(scenario,language,submit);Page("purchases").AddChild(_pickups);
        var city=Page("city");city.AddChild(_city);city.AddChild(_labor);city.AddChild(PoliticsHost);
        Resized+=LayoutNavigation;Ready+=LayoutNavigation;OpenSection("overview");TabletDesign.CompactInputs(this);
    }
    private void LayoutNavigation(){bool narrow=Size.X<700||GetViewportRect().Size.Y<540;_navScroll.Visible=!narrow;_mobile.Visible=narrow;GameUi.FitControls(_tabs,Size.X-(narrow?36:274));}
    private VBoxContainer Page(string key)
    {
        if(_pages.TryGetValue(key,out int index))return (VBoxContainer)_tabs.GetChild<ScrollContainer>(index).GetChild(0);
        var scroll=new ScrollContainer{Name="PlayerPage_"+key,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_pages[key]=_tabs.GetChildCount();_tabs.AddChild(scroll);
        var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(box);return box;
    }
    private void SelectSection(string section)
    {
        if(!_pages.TryGetValue(section,out int index))return;_section=section;_tabs.CurrentTab=index;
        string selected=section=="object"?"holdings":section=="equipment"?"fleet":section;
        if(_pageGroups.TryGetValue(selected,out string? group))_groups[group].Header.ButtonPressed=true;
        foreach(var entry in _navigation)entry.Value.SetPressedNoSignal(entry.Key==selected);
        int choice=Array.IndexOf(_navigation.Keys.ToArray(),selected);if(choice>=0)_mobile.Select(choice);
    }
    private static void Stat(Container host,string icon,Label value,string caption)
    {var col=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};var row=new HBoxContainer();row.AddChild(TabletDesign.Art(icon,new(22,22)));value.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(value);col.AddChild(row);col.AddChild(new Label{Text=caption,AutowrapMode=TextServer.AutowrapMode.WordSmart});value.AddThemeFontSizeOverride("font_size",23);host.AddChild(CompactTabletDesign.Card(col));}
    private static Control Fold(string title,Control content,bool open=false)
    {var box=new VBoxContainer();var toggle=new Button{Text=title,ToggleMode=true,ButtonPressed=open,FocusMode=FocusModeEnum.None,AutowrapMode=TextServer.AutowrapMode.WordSmart};box.AddChild(toggle);box.AddChild(content);content.Visible=open;toggle.Toggled+=on=>content.Visible=on;return CompactTabletDesign.Card(box);}
    private void Back(){_cardHost.Visible=false;_back.Visible=false;SelectSection("holdings");}
    private void Open(string kind,long id)
    {
        if(kind=="building"&&_view.Buildings.Any(b=>b.Id.Value==id&&b.Kind==BuildingKind.Production&&b.Footprint.Contains(_view.Participant.Position))&&ObjectRequested is not null){ObjectRequested.Invoke(kind,id);return;}
        foreach(Node child in _cardHost.GetChildren()){_cardHost.RemoveChild(child);child.QueueFree();}
        _cardObject=(kind,id);_card=null;_groundCard=null;
        bool woodworking=kind=="building"&&_view.Buildings.Any(b=>b.Id.Value==id&&b.Kind==BuildingKind.Production&&_view.Enterprises.Any(e=>e.Id.Value==b.Id.Value));
        // The sawmill owns its page scrolls; give its inline remote view the available height.
        var objectPage=Page("object");objectPage.SizeFlagsVertical=woodworking?SizeFlags.ExpandFill:SizeFlags.Fill;
        _cardHost.SizeFlagsVertical=woodworking?SizeFlags.ExpandFill:SizeFlags.Fill;
        ((ScrollContainer)objectPage.GetParent()).VerticalScrollMode=woodworking?ScrollContainer.ScrollMode.Disabled:ScrollContainer.ScrollMode.Auto;
        if(kind=="ground"){_groundCard=new(_scenario,_language,kind,id,_submit,_access,demolitionQuote:_demolitionQuote);_cardHost.AddChild(_groundCard);_groundCard.ShowData(_view,_pending);}
        else{_card=new(_scenario,_language,kind,id,_submit,_access,_demolitionQuote,woodworkingPrototype:woodworking,productionType:_view.Buildings.FirstOrDefault(b=>b.Id.Value==id)?.TypeId??"woodworking"){Theme=TabletDesign.Theme};_card.CloseRequested+=Back;_card.HelpRequested+=()=>HelpRequested?.Invoke();_card.MapRequested+=()=>MapRequested?.Invoke();_cardHost.AddChild(_card);_card.ShowData(_view,_pending,_paused);}
        _cardHost.Visible=true;_back.Visible=true;SelectSection("object");
    }
    public void OpenHolding(string kind,long id){if(_view is not null)Open(kind,id);}
    public void OpenSection(string section,long id=0)
    {
        switch(section)
        {
            case "map":MapRequested?.Invoke();return;
            case "notices":NoticesRequested?.Invoke();return;
            case "tutorial":TutorialRequested?.Invoke();return;
            case "service-vehicle":section="services";_services?.FocusVehicle(id);break;
            case "services":if(id>0)_services?.FocusOrder(id);break;
            case "account":if(id>0)_account.FocusInvoice(id);break;
            case "fleet":if(id>0)_fleet?.FocusVehicle(id);break;
            case "dealer":section="fleet";_fleet?.ShowCatalog();break;
        }
        SelectSection(section);
    }
    public void ShowData(PlayerView view,bool pending,bool paused)
    {
        if(_cardObject is {} selected&&selected.Kind=="building"&&!view.Buildings.Any(b=>b.Id.Value==selected.Id)){_card=null;_cardObject=null;Back();}
        _view=view;_pending=pending;_backpack.ShowData(view,pending);
        _services?.ShowData(view,pending);
        if(view.PersonalAccount is {} account)_account.ShowData(account,pending);
        _paused=paused;
        var entries=view.Buildings.Select(b=>(Kind:"building",Id:b.Id.Value,Position:b.Position,Title:PlayerText.Building(_language,b.TypeId),Icon:b.TypeId))
            .Concat((view.Equipment??[]).Select(e=>(Kind:"equipment",Id:e.Id.Value,Position:e.Position,Title:L("Добывающая техника","Extraction equipment"),Icon:"gear"))).ToArray();
        entries=entries.OrderBy(e=>e.Id).ThenBy(e=>e.Kind,StringComparer.Ordinal).ToArray();int totalEntries=entries.Length;
        _holdingOffset=Math.Clamp(_holdingOffset,0,Math.Max(0,(totalEntries-1)/20*20));entries=entries.Skip(_holdingOffset).Take(20).ToArray();
        _previousHoldings.Disabled=pending||_holdingOffset==0;_nextHoldings.Disabled=pending||_holdingOffset+20>=totalEntries;_holdingPage.Text=totalEntries==0?"0":$"{_holdingOffset+1}–{_holdingOffset+entries.Length} / {totalEntries}";
        string signature=string.Join(';',entries.Select(e=>e.Kind+e.Id));
        if(signature!=_signature)
        {
            _signature=signature;foreach(Node n in _holdings.GetChildren()){_holdings.RemoveChild(n);n.QueueFree();}_rows.Clear();
            foreach(var e in entries)
            {
                var row=new TabletColumns{Breakpoint=560};row.AddChild(TabletDesign.Art(e.Icon,new(30,30)));
                var description=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};description.AddChild(TabletDesign.Heading(e.Title+" #"+e.Id,18));var stock=Wrapped();description.AddChild(stock);row.AddChild(description);
                var progress=new ProgressBar{ShowPercentage=false,CustomMinimumSize=new(120,10),SizeFlagsVertical=SizeFlags.ShrinkCenter};row.AddChild(progress);
                var buttons=new VBoxContainer();var show=new Button{Name="HoldingOpen_"+e.Id,Text=L("Открыть ›","Open ›"),CustomMinimumSize=new(40,40),SizeFlagsHorizontal=SizeFlags.ShrinkBegin};show.Pressed+=()=>Open(e.Kind,e.Id);buttons.AddChild(show);
                var walk=new Button{Text=L("Подойти","Walk to"),CustomMinimumSize=new(40,40),SizeFlagsHorizontal=SizeFlags.ShrinkBegin};walk.Pressed+=()=>_submit(PlayerDesk.Move(e.Position.X,e.Position.Z));buttons.AddChild(walk);row.AddChild(buttons);
                _holdings.AddChild(CompactTabletDesign.Card(row));_rows.Add((e.Kind,e.Id),(stock,progress,walk));
            }
        }
        _empty.Visible=entries.Length==0;
        foreach(var e in entries)
        {
            var row=_rows[(e.Kind,e.Id)];var enterprise=view.Enterprises.SingleOrDefault(x=>e.Kind=="building" && x.Id.Value==e.Id);
            var b=view.Buildings.SingleOrDefault(x=>e.Kind=="building" && x.Id.Value==e.Id);
            var inventories=b is null?view.Inventories.Where(i=>(view.Equipment??[]).Any(m=>m.Id.Value==e.Id && m.StorageId==i.Id)):view.Inventories.Where(i=>i.Id==b.InputId || i.Id==b.OutputId);
            row.Stock.Text=(enterprise is null?"":TabletText.Reason(_language,enterprise.ReasonCode)+" · ")+string.Join(" · ",inventories.SelectMany(i=>i.Stock).Select(x=>PlayerText.Item(_language,x.ItemId)+" ×"+x.Quantity));
            if(enterprise is null && b is not null)row.Stock.Text=L("Объём: ","Volume: ")+string.Join(" · ",inventories.Select(i=>$"{i.OccupiedVolume}+{i.ReservedCapacity}/{i.Capacity}"));
            row.Progress.Visible=enterprise is not null;if(enterprise is not null){var p=TabletProgress.Production(enterprise);row.Progress.MaxValue=p.Total;row.Progress.Value=p.Done;}
            row.Walk.Disabled=pending || view.Participant.Busy;
        }
        var city=view.City!;var own=city.Participants.Single(p=>p.Actor==view.Participant.Id);var pack=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);
        _capital.Text=PlayerText.Money(own.CapitalMinor);_employed.Text=own.Employed.ToString();_room.Text=Math.Max(0,pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity).ToString();
        _notices.Text=L("Сейчас в хозяйстве","Your attention")+"\n"+
            (view.PersonalAccount is {} bill?L("Непрочитанных счетов: ","Unread invoices: ")+bill.UnreadCount+L(" · долг: "," · debt: ")+PlayerText.Money(bill.DebtMinor)+"\n":"")+
            L("Производств в работе: ","Productions enabled: ")+view.Enterprises.Count(e=>e.Enabled)+" / "+view.Enterprises.Length+
            L(" · активных передач: "," · active transfers: ")+view.Transfers.Length;
        string summarySignature=(view.Fleet?.Vehicles.Length??0)+":"+string.Join(';',view.Buildings.Select(b=>"b"+b.Id.Value).Concat((view.Equipment??[]).Select(e=>"e"+e.Id.Value)));
        if(summarySignature!=_summarySignature)
        {
            _summarySignature=summarySignature;foreach(Node child in _summaryObjects.GetChildren()){_summaryObjects.RemoveChild(child);child.QueueFree();}
            var columns=new TabletColumns{Breakpoint=600};_summaryObjects.AddChild(columns);
            var objects=new VBoxContainer();columns.AddChild(CompactTabletDesign.Card(objects));objects.AddChild(TabletDesign.Heading(L("Предприятия и объекты","Enterprises and objects"),18));
            foreach(var b in view.Buildings.Take(3))
            {var line=new VBoxContainer();line.AddChild(new Label{Text=PlayerText.Building(_language,b.TypeId)+" #"+b.Id.Value,AutowrapMode=TextServer.AutowrapMode.WordSmart});line.AddChild(CompactTabletDesign.Button("OverviewOpen_"+b.Id.Value,L("Открыть","Open"),()=>Open("building",b.Id.Value)));objects.AddChild(line);}
            if(view.Buildings.Length==0)objects.AddChild(new Label{Text=L("Собственных предприятий пока нет.","No enterprises owned yet."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
            var fleet=new VBoxContainer();columns.AddChild(CompactTabletDesign.Card(fleet));fleet.AddChild(TabletDesign.Heading(L("Собственная техника","Your equipment"),18));_summaryFleet=Wrapped();fleet.AddChild(_summaryFleet);
            if(_fleet is not null)fleet.AddChild(CompactTabletDesign.Button("OverviewOpenFleet",L("Автопарк","Fleet"),()=>OpenSection("fleet")));
        }
        if(_summaryFleet is not null)_summaryFleet.Text=L("Машин: ","Vehicles: ")+(view.Fleet?.Vehicles.Length??0)+L(" · добывающих: "," · extractors: ")+(view.Equipment?.Length??0)+"\n"+
            string.Join("\n",(view.Fleet?.Vehicles??[]).Take(2).Select(v=>v.Model.Name+" #"+v.Vehicle.Id));
        _roads?.ShowData(view,pending,paused);_fleet?.ShowData(view,pending);_development.ShowData(view,pending);_card?.ShowData(view,pending,paused);_groundCard?.ShowData(view,pending);_deals.ShowData(view,pending);_negotiations.ShowData(view,pending);_pickups.ShowData(view,pending);_purchases.ShowData(view,pending);
        _participants.Text=L("Рейтинг по капиталу · фиксированная оценка","Capital ranking · fixed reference valuation")+"\n\n"+string.Join("\n\n",city.Participants.OrderByDescending(p=>p.CapitalMinor).ThenBy(p=>p.Actor.Value).Select(p=>(p.Actor==view.Participant.Id?L("Вы","You"):WorldSkins.ActorName(p.Actor.Value,_language))+" · "+PlayerText.Money(p.CapitalMinor)+L(" · работников "," · workforce ")+p.Employed+(p.Actor==view.Participant.Id?"":"\n"+L("Предпочтение: ","Preference: ")+(p.PreferenceItem.Length==0?L("не объявлено","not declared"):PlayerText.Item(_language,p.PreferenceItem)))));
        _city.Text=Stage(city.Stage)+L(" · капитал города "," · city capital ")+PlayerText.Money(city.CapitalMinor)+"\n\n"+L("Население / занятые / безработные: ","Population / employed / unemployed: ")+$"{city.Population} / {city.Employed} / {city.Unemployed}\n"+L("Работают на вас: ","Working for you: ")+own.Employed+(city.Employed==0?"":$" ({100m*own.Employed/city.Employed:0.0}%)")+"\n"+L("Действующих производств: ","Active productions: ")+city.ActiveProductions+"\n"+L("Завершённый выпуск: ","Completed-period output: ")+PlayerText.Money(city.GrossOutputMinor)+"\n"+L("Период приёмки: ","Receipt period: ")+$"{city.PeriodStartTick}–{city.PeriodEndTick}\n"+L("Принято столицей: ","Capital receipts: ")+city.CapitalDeliveredQuantity+L(" · по договорам "," · contracted receipts ")+city.CapitalContractDeliveredQuantity+L(" · новых обязательств "," · new commitments ")+city.CapitalContractedQuantity;
        foreach(var system in view.Logistics.Where(l=>view.Demolition?.Receipts.Any(r=>!r.Construction&&r.Target==l.EnterpriseId.Value)==true))
        {
            if(!_parkedRoutes.TryGetValue(system.Id.Value,out var routes)){var box=new VBoxContainer();box.AddChild(new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,Text=L("Сохранённая машина #","Retained vehicle #")+system.Id.Value+L(" · задайте явный маршрут между действующими объектами. Находится у освобождённого участка."," · set an explicit route between active objects. Parked at the released site.")});routes=new(_scenario,_language,"building",system.EnterpriseId.Value,_submit);box.AddChild(routes);_parked.AddChild(Fold(L("Сохранённая машина #","Retained vehicle #")+system.Id.Value,box));_parkedRoutes.Add(system.Id.Value,routes);}routes.ShowData(view,pending);
        }
        _labor.Text=L("Действующая оплата занятых: ","Weighted current employed pay: ")+Wage(city.Labor.EmployedRate)+"\n"+L("Обеспеченные вакансии: ","Weighted ready-vacancy rate: ")+Wage(city.Labor.VacancyRate)+"\n\n"+string.Join("\n",city.Labor.Offers.Select(o=>"#"+o.Group.Value+" · "+L("участник ","participant ")+o.Owner.Value+L(" · мест "," · places ")+o.Vacancies+" · "+PlayerText.Money(o.AttractionWageMinor)+(o.PendingWageMinor is long wage?L(" · объявлено "," · announced ")+PlayerText.Money(wage):"")))+"\n\n"+L("Простой не оплачивается. Найм и изменение ставок происходят постепенно.","Idle work is unpaid. Hiring and wage changes take time.");
        if(IsInsideTree()&&Size.X>0)LayoutNavigation();
    }
    private string Stage(int stage)=>stage switch{0=>L("Деревня","Village"),1=>L("Посёлок","Settlement"),2=>L("Небольшой город","Small town"),_=>L("Развитый город","Developed town")};
    private string Wage(WeightedWage r)=>r.Weight==0?L("нет данных","no data"):(((decimal)(r.WeightedMinor/r.Weight)+(decimal)(r.WeightedMinor%r.Weight)/r.Weight)/100m).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
    private static Label Wrapped()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

public partial class EquipmentPurchasePanel : VBoxContainer
{
    public event Action? DealerRequested;
    private readonly bool _cityDealer;
    private readonly List<(Button Button, GameAction Action, long Price)> _buys = [];
    private PlayerView? _lastView;
    private bool _pending;
    private readonly System.Func<GameAction, ActionAccess> _access;
    private readonly string _language;
    public EquipmentPurchasePanel(ScenarioDefinition scenario, string language, System.Action<GameAction> submit, System.Func<GameAction, ActionAccess> access)
    {
        _access = access; _language = language;
        if(scenario.Economy?.Fleet?.EquipmentDealerVersion==1)
        { _cityDealer=true;AddChild(new Label{Text=PlayerText.Local(language,"Мобильная техника продаётся в городском автосалоне.","Mobile equipment is sold at the city dealer."),AutowrapMode=TextServer.AutowrapMode.WordSmart});var button=new Button{Name="EquipmentOpenDealer",Text=PlayerText.Local(language,"Открыть автосалон","Open dealer"),SizeFlagsHorizontal=SizeFlags.ShrinkBegin};button.Pressed+=()=>DealerRequested?.Invoke();AddChild(button);return; }
        AddChild(new Label { Text = PlayerText.Local(language, "Купить технику у ресурсной точки", "Buy equipment at a resource node") });
        foreach (var type in scenario.EquipmentTypes ?? [])
            foreach (var level in type.Levels)
            {
                var node = new OptionButton { FocusMode = FocusModeEnum.None };
                var positions = scenario.ResourceNodes.Where(n => n.ItemId == type.ItemId).Select(n => n.Position).ToArray();
                if (positions.Length == 0) continue;
                var row = new TabletColumns{Breakpoint=700}; AddChild(row);
                foreach (var cell in positions) node.AddItem($"({cell.X},{cell.Z})"); row.AddChild(node);
                var button = new Button { Text = PlayerText.Item(language, type.ItemId) + PlayerText.Local(language, " · техника уровня ", " · equipment level ") + level.Level + " — " + PlayerText.Money(level.PriceMinor), FocusMode = FocusModeEnum.None }; row.AddChild(button);
                int index = _buys.Count;
                _buys.Add((button, new(ActionKind.BuyEquipment, EquipmentTypeId: type.Id, EquipmentLevel: level.Level, Target: positions[0]), level.PriceMinor));
                node.ItemSelected += selected => { var old = _buys[index]; _buys[index] = (old.Button, old.Action with { Target = positions[selected] }, old.Price); if (_lastView is not null) ShowData(_lastView,_pending); };
                button.Pressed += () => submit(_buys[index].Action);
                var go = new Button { Text = PlayerText.Local(language, "Подойти", "Walk there"), FocusMode = FocusModeEnum.None }; row.AddChild(go);
                go.Pressed += () => { var cell = _buys[index].Action.Target; submit(PlayerDesk.Move(cell.X, cell.Z)); };
            }
    }
    public void ShowData(PlayerView view, bool pending)
    {
        _lastView = view; _pending = pending;
        if(_cityDealer)return;
        foreach (var row in _buys)
        {
            var access = _access(row.Action); row.Button.Disabled = pending || view.Participant.Busy || view.AvailableCashMinor < row.Price || !access.Allowed;
            row.Button.TooltipText = access.Allowed ? "" : PlayerText.Reason(_language, access.ReasonCode);
        }
    }
}

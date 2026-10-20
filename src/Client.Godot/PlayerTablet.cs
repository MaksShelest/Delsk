using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Kit pages over own/bilateral projections and public city totals.</summary>
public partial class PlayerTablet : VBoxContainer
{
    public event System.Action? HelpRequested;
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction,ActionAccess> _access;
    private readonly TabContainer _tabs=new(){SizeFlagsVertical=SizeFlags.ExpandFill,CustomMinimumSize=new(0,410)};
    private readonly VBoxContainer _overview=new(),_cardHost=new(),_holdings=new();
    private readonly Button _back=new();
    private readonly Label _empty=Wrapped(),_participants=Wrapped(),_city=Wrapped(),_labor=Wrapped();
    private readonly Label _capital=TabletDesign.Heading("",27),_employed=TabletDesign.Heading("",27),_room=TabletDesign.Heading("",27);
    private readonly CurrentDealsPanel _deals;
    private readonly TradeNegotiationPanel _negotiations;
    private readonly PurchaseGuidePanel _pickups;
    private readonly EquipmentPurchasePanel _purchases;
    private readonly Dictionary<(string,long),(Label Stock,ProgressBar Progress,Button Walk)> _rows=[];
    private HouseholdObjectCard? _card;
    private PlayerView _view=null!;
    private bool _pending,_paused;
    private string _signature="";
    public int SelectedTab{get=>_tabs.CurrentTab;set=>_tabs.CurrentTab=Math.Clamp(value,0,3);}
    public PlayerTablet(ScenarioDefinition scenario,string language,System.Action<GameAction> submit,System.Func<GameAction,ActionAccess> access)
    {
        Name="PlayerPages";_scenario=scenario;_language=language;_submit=submit;_access=access;AddChild(_tabs);
        var farm=Tab(L("Моё хозяйство","My holdings"));farm.AddChild(_overview);
        var stats=new TabletColumns{Breakpoint=660};_overview.AddChild(stats);
        Stat(stats,"coin",_capital,L("Капитал","Capital"));Stat(stats,"people",_employed,L("Работают на вас","Working for you"));Stat(stats,"bag",_room,L("Свободно в рюкзаке · объём","Free backpack volume"));
        _empty.Text=L("Своих объектов пока нет. Добывайте сырьё, продавайте столице, покупайте участок и стройте.","No holdings yet. Harvest inputs, sell to the capital, buy a plot and build.");_overview.AddChild(_empty);_overview.AddChild(_holdings);
        _purchases=new(scenario,language,submit,access);_overview.AddChild(Fold(L("Купить технику ›","Buy equipment ›"),_purchases));
        _back.Text=L("‹ Моё хозяйство","‹ My holdings");_back.FocusMode=FocusModeEnum.None;_back.Visible=false;_back.Pressed+=Back;farm.AddChild(_back);farm.AddChild(_cardHost);
        Tab(L("Участники","Participants")).AddChild(TabletDesign.Wrap(_participants));
        var talks=Tab(L("Переговоры","Negotiations"));
        talks.AddChild(TabletDesign.Wrap(new Label{Text=L("Частные условия видны только сторонам. Исполнение требует реальных денег, товаров и доставки.","Private terms are visible only to the parties. Fulfillment requires actual funds, goods and delivery."),AutowrapMode=TextServer.AutowrapMode.WordSmart},true));
        _negotiations=new(scenario,language,submit);talks.AddChild(Fold(L("Двусторонние предложения и встречные условия","Bilateral proposals and counteroffers"),_negotiations,true));
        _deals=new(scenario,language,submit,access);talks.AddChild(Fold(L("Закупки, договоры и политическая поддержка","Buy offers, contracts and political support"),_deals));
        _pickups=new(scenario,language,submit);talks.AddChild(Fold(L("Покупки и путь получения","Purchases and pickup route"),_pickups));
        var city=Tab(L("Город","City"));city.AddChild(TabletDesign.Wrap(_city));city.AddChild(TabletDesign.Wrap(_labor,true));
        TabletDesign.CompactInputs(this);
    }
    private static void Stat(Container host,string icon,Label value,string caption)
    {var col=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};var row=new HBoxContainer();row.AddChild(TabletDesign.Art(icon,new(36,36)));value.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(value);col.AddChild(row);col.AddChild(new Label{Text=caption,AutowrapMode=TextServer.AutowrapMode.WordSmart});host.AddChild(TabletDesign.Wrap(col));}
    private static Control Fold(string title,Control content,bool open=false)
    {var box=new VBoxContainer();var toggle=new Button{Text=title,ToggleMode=true,ButtonPressed=open,FocusMode=FocusModeEnum.None,AutowrapMode=TextServer.AutowrapMode.WordSmart};box.AddChild(toggle);box.AddChild(content);content.Visible=open;toggle.Toggled+=on=>content.Visible=on;return TabletDesign.Wrap(box);}
    private VBoxContainer Tab(string title)
    {var scroll=new ScrollContainer{Name=title,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_tabs.AddChild(scroll);var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(box);return box;}
    private void Back(){_overview.Visible=true;_cardHost.Visible=false;_back.Visible=false;}
    private void Open(string kind,long id)
    {
        foreach(Node child in _cardHost.GetChildren()){_cardHost.RemoveChild(child);child.QueueFree();}
        _card=new(_scenario,_language,kind,id,_submit,_access);_card.CloseRequested+=Back;_card.HelpRequested+=()=>HelpRequested?.Invoke();_cardHost.AddChild(_card);
        _overview.Visible=false;_cardHost.Visible=true;_back.Visible=true;_card.ShowData(_view,_pending,_paused);
    }
    public void ShowData(PlayerView view,bool pending,bool paused)
    {
        _view=view;_pending=pending;_paused=paused;_tabs.CustomMinimumSize=new(0,Mathf.Max(260,GetViewportRect().Size.Y-340));
        var entries=view.Buildings.Select(b=>(Kind:"building",Id:b.Id.Value,Position:b.Position,Title:PlayerText.Building(_language,b.TypeId),Icon:b.TypeId))
            .Concat((view.Equipment??[]).Select(e=>(Kind:"equipment",Id:e.Id.Value,Position:e.Position,Title:L("Добывающая техника","Extraction equipment"),Icon:"gear"))).ToArray();
        string signature=string.Join(';',entries.Select(e=>e.Kind+e.Id));
        if(signature!=_signature)
        {
            _signature=signature;foreach(Node n in _holdings.GetChildren()){_holdings.RemoveChild(n);n.QueueFree();}_rows.Clear();
            foreach(var e in entries)
            {
                var row=new TabletColumns{Breakpoint=760};row.AddChild(TabletDesign.Art(e.Icon,new(70,70)));
                var description=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};description.AddChild(TabletDesign.Heading(e.Title+" #"+e.Id,22));var stock=Wrapped();description.AddChild(stock);row.AddChild(description);
                var progress=new ProgressBar{ShowPercentage=false,CustomMinimumSize=new(120,10),SizeFlagsVertical=SizeFlags.ShrinkCenter};row.AddChild(progress);
                var buttons=new VBoxContainer();var show=new Button{Text=L("Открыть ›","Open ›"),FocusMode=FocusModeEnum.None};show.Pressed+=()=>Open(e.Kind,e.Id);buttons.AddChild(show);
                var walk=new Button{Text=L("Подойти","Walk to"),FocusMode=FocusModeEnum.None};walk.Pressed+=()=>_submit(PlayerDesk.Move(e.Position.X,e.Position.Z));buttons.AddChild(walk);row.AddChild(buttons);
                _holdings.AddChild(TabletDesign.Wrap(row));_rows.Add((e.Kind,e.Id),(stock,progress,walk));
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
        _card?.ShowData(view,pending,paused);_deals.ShowData(view,pending);_negotiations.ShowData(view,pending);_pickups.ShowData(view,pending);_purchases.ShowData(view,pending);
        _participants.Text=L("Рейтинг по капиталу · фиксированная оценка","Capital ranking · fixed reference valuation")+"\n\n"+string.Join("\n\n",city.Participants.OrderByDescending(p=>p.CapitalMinor).ThenBy(p=>p.Actor.Value).Select(p=>(p.Actor==view.Participant.Id?L("Вы","You"):"NPC "+p.Actor.Value)+" · "+PlayerText.Money(p.CapitalMinor)+L(" · работников "," · workforce ")+p.Employed+(p.Actor==view.Participant.Id?"":"\n"+L("Предпочтение: ","Preference: ")+(p.PreferenceItem.Length==0?L("не объявлено","not declared"):PlayerText.Item(_language,p.PreferenceItem)))));
        _city.Text=Stage(city.Stage)+L(" · капитал города "," · city capital ")+PlayerText.Money(city.CapitalMinor)+"\n\n"+L("Население / занятые / безработные: ","Population / employed / unemployed: ")+$"{city.Population} / {city.Employed} / {city.Unemployed}\n"+L("Работают на вас: ","Working for you: ")+own.Employed+(city.Employed==0?"":$" ({100m*own.Employed/city.Employed:0.0}%)")+"\n"+L("Действующих производств: ","Active productions: ")+city.ActiveProductions+"\n"+L("Завершённый выпуск: ","Completed-period output: ")+PlayerText.Money(city.GrossOutputMinor)+"\n"+L("Период приёмки: ","Receipt period: ")+$"{city.PeriodStartTick}–{city.PeriodEndTick}\n"+L("Принято столицей: ","Capital receipts: ")+city.CapitalDeliveredQuantity+L(" · по договорам "," · contracted receipts ")+city.CapitalContractDeliveredQuantity+L(" · новых обязательств "," · new commitments ")+city.CapitalContractedQuantity;
        _labor.Text=L("Действующая оплата занятых: ","Weighted current employed pay: ")+Wage(city.Labor.EmployedRate)+"\n"+L("Обеспеченные вакансии: ","Weighted ready-vacancy rate: ")+Wage(city.Labor.VacancyRate)+"\n\n"+string.Join("\n",city.Labor.Offers.Select(o=>"#"+o.Group.Value+" · "+L("участник ","participant ")+o.Owner.Value+L(" · мест "," · places ")+o.Vacancies+" · "+PlayerText.Money(o.AttractionWageMinor)+(o.PendingWageMinor is long wage?L(" · объявлено "," · announced ")+PlayerText.Money(wage):"")))+"\n\n"+L("Простой не оплачивается. Найм и изменение ставок происходят постепенно.","Idle work is unpaid. Hiring and wage changes take time.");
    }
    private string Stage(int stage)=>stage switch{0=>L("Деревня","Village"),1=>L("Посёлок","Settlement"),2=>L("Небольшой город","Small town"),_=>L("Развитый город","Developed town")};
    private string Wage(WeightedWage r)=>r.Weight==0?L("нет данных","no data"):(((decimal)(r.WeightedMinor/r.Weight)+(decimal)(r.WeightedMinor%r.Weight)/r.Weight)/100m).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
    private static Label Wrapped()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

public partial class EquipmentPurchasePanel : VBoxContainer
{
    private readonly List<(Button Button, GameAction Action, long Price)> _buys = [];
    private PlayerView? _lastView;
    private bool _pending;
    private readonly System.Func<GameAction, ActionAccess> _access;
    private readonly string _language;
    public EquipmentPurchasePanel(ScenarioDefinition scenario, string language, System.Action<GameAction> submit, System.Func<GameAction, ActionAccess> access)
    {
        _access = access; _language = language;
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
        foreach (var row in _buys)
        {
            var access = _access(row.Action); row.Button.Disabled = pending || view.Participant.Busy || view.AvailableCashMinor < row.Price || !access.Allowed;
            row.Button.TooltipText = access.Allowed ? "" : PlayerText.Reason(_language, access.ReasonCode);
        }
    }
}

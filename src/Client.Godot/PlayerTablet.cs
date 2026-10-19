using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Global tablet. Only own/bilateral DTOs and the same public totals as NPCs.</summary>
public partial class PlayerTablet : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction, ActionAccess> _access;
    private readonly TabContainer _tabs = new() { CustomMinimumSize = new(0, 470), SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly ItemList _objects = new() { CustomMinimumSize = new(220, 350), SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _cardHost = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
    private readonly Label _participants = Wrapped();
    private readonly Label _city = Wrapped();
    private readonly Label _labor = Wrapped();
    private readonly CurrentDealsPanel _deals;
    private readonly TradeNegotiationPanel _negotiations;
    private readonly PurchaseGuidePanel _pickups;
    private readonly EquipmentPurchasePanel _purchases;
    private readonly Label _empty = Wrapped();
    private readonly Button _navigate = new() { FocusMode = FocusModeEnum.None };
    private HouseholdObjectCard? _card;
    private (string Kind, long Id, Cell Position)[] _entries = [];
    private string _signature = "";
    private PlayerView _view = null!;
    private bool _pending;
    public int SelectedTab { get => _tabs.CurrentTab; set => _tabs.CurrentTab = value; }

    public PlayerTablet(ScenarioDefinition scenario, string language, System.Action<GameAction> submit, System.Func<GameAction, ActionAccess> access)
    {
        _scenario = scenario; _language = language; _submit = submit; _access = access;
        AddChild(_tabs);
        var farm = Tab(L("Моё хозяйство", "My holdings"));
        _empty.Text = L("Своих объектов пока нет. Добывайте сырьё, продавайте столице, покупайте участок и стройте. Технику можно купить у нужного ресурса.","No holdings yet. Harvest inputs, sell to the capital, buy a plot and build. Buy extraction equipment at its resource node.");
        farm.AddChild(_empty);
        var row = new HBoxContainer(); farm.AddChild(row);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(list); list.AddChild(_objects); list.AddChild(_navigate);
        row.AddChild(_cardHost);
        _objects.ItemSelected += index => SelectObject((int)index);
        _navigate.Pressed += () => { if (_objects.GetSelectedItems().FirstOrDefault(-1) is int index && index >= 0) _submit(PlayerDesk.Move(_entries[index].Position.X, _entries[index].Position.Z)); };
        _navigate.Text = L("Подойти к выбранному объекту", "Walk to the selected object");
        _purchases = new(scenario, language, submit, access); farm.AddChild(_purchases);
        Tab(L("Участники", "Participants")).AddChild(_participants);
        var deals = Tab(L("Переговоры", "Negotiations"));
        _negotiations=new(scenario,language,submit);deals.AddChild(_negotiations);
        _pickups=new(scenario,language,submit);deals.AddChild(_pickups);
        _deals = new(scenario, language, submit, access); deals.AddChild(_deals);
        var city = Tab(L("Город", "City")); city.AddChild(_city); city.AddChild(new HSeparator()); city.AddChild(_labor);
    }
    private VBoxContainer Tab(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tabs.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(content); return content;
    }
    public void ShowData(PlayerView view, bool pending, bool paused)
    {
        _view = view; _pending = pending;
        var entries = view.Buildings.Select(b => (Kind: "building", Id: b.Id.Value, b.Position))
            .Concat(view.Enterprises.Where(e => !view.Buildings.Any(b => b.Id.Value == e.Id.Value)).Select(e => (Kind: "building", Id: e.Id.Value, Position: view.Inventories.Single(i => i.Id == e.InputId).Position)))
            .Concat((view.Equipment ?? []).Select(e => (Kind: "equipment", Id: e.Id.Value, Position: e.Position))).ToArray();
        string signature = string.Join(";", entries.Select(e => e.Kind + e.Id));
        if (signature != _signature)
        {
            var selected = _objects.GetSelectedItems().FirstOrDefault(-1); var old = selected >= 0 ? _entries[selected] : default;
            _signature = signature; _entries = entries; _objects.Clear();
            foreach (var entry in entries)
            {
                var b = view.Buildings.SingleOrDefault(b => entry.Kind == "building" && b.Id.Value == entry.Id);
                _objects.AddItem((entry.Kind == "equipment" ? L("Техника", "Equipment") : b is null ? L("Производство", "Production") : PlayerText.Building(_language, b.TypeId)) + " #" + entry.Id);
            }
            if (entries.Length > 0) { int index = Math.Max(0, Array.FindIndex(entries, e => e.Kind == old.Kind && e.Id == old.Id)); _objects.Select(index); SelectObject(index); }
        }
        _empty.Visible = entries.Length == 0; _objects.Visible = _cardHost.Visible = _navigate.Visible = entries.Length > 0;
        _navigate.Disabled = pending || entries.Length == 0 || view.Participant.Busy;
        _negotiations.ShowData(view,pending);_pickups.ShowData(view,pending);
        _card?.ShowData(view, pending, paused); _deals.ShowData(view, pending); _purchases.ShowData(view, pending);
        var city = view.City!;
        _participants.Text = L("Рейтинг по капиталу · фиксированная справочная оценка", "Capital ranking · fixed reference valuation") + "\n\n" +
            string.Join("\n\n", city.Participants.OrderByDescending(p => p.CapitalMinor).ThenBy(p => p.Actor.Value).Select(p =>
                (p.Actor == view.Participant.Id ? L("Вы", "You") : "NPC " + p.Actor.Value) + L(" · капитал ", " · capital ") + PlayerText.Money(p.CapitalMinor) + L(" · занято ", " · employed ") + p.Employed +
                (p.Actor == view.Participant.Id ? "" : L("\nПредпочтение: ", "\nPreference: ") + (p.PreferenceItem.Length == 0 ? L("не объявлено", "not declared") : PlayerText.Item(_language,p.PreferenceItem)))));
        long own = city.Participants.Single(p => p.Actor == view.Participant.Id).Employed;
        _city.Text = Stage(city.Stage) + L(" · капитал города ", " · city capital ") + PlayerText.Money(city.CapitalMinor) + "\n" +
            L("Население / занятые / безработные: ", "Population / employed / unemployed: ") + $"{city.Population} / {city.Employed} / {city.Unemployed}\n" +
            L("На вас работают: ", "Working for you: ") + own + (city.Employed == 0 ? "" : $" ({100m * own / city.Employed:0.0}%)") + "\n" +
            L("Действующих производств: ", "Active productions: ") + city.ActiveProductions + "\n" +
            L("Выпуск за последний завершённый период: ", "Output in the latest completed period: ") + PlayerText.Money(city.GrossOutputMinor) + "\n" +
            L("Текущий период приёмки столицы: ", "Current capital receipt period: ") + $"{city.PeriodStartTick}–{city.PeriodEndTick}\n" +
            L("Принято столицей: ", "Received by capital: ") + city.CapitalDeliveredQuantity + L(" · по договорам: ", " · against contracts: ") + city.CapitalContractDeliveredQuantity +
            L(" · новых обязательств: ", " · newly contracted: ") + city.CapitalContractedQuantity;
        _labor.Text = L("Средняя действующая ставка занятых: ", "Weighted current employed wage: ") + Wage(city.Labor.EmployedRate, L("нет данных", "no data")) + "\n" +
            L("Среднее предложение вакансий: ", "Weighted vacancy offer: ") + Wage(city.Labor.VacancyRate, L("нет вакансий", "no vacancies")) + "\n\n" +
            string.Join("\n", city.Labor.Offers.Select(o => "#" + o.Group.Value + " · " + o.Owner.Value + L(" · мест ", " · vacancies ") + o.Vacancies + L(" · привлечение ", " · attraction ") + PlayerText.Money(o.AttractionWageMinor) +
                (o.PendingWageMinor is long wage ? L(" · объявлено ", " · announced ") + PlayerText.Money(wage) : ""))) + "\n\n" +
            L("Это ставка за рабочий тик, а не средний доход. При простое оплаты нет; найм постепенный.", "This is a working-tick rate, not average income. Idle work is unpaid; hiring takes time.");
    }
    private void SelectObject(int index)
    {
        if (index < 0 || index >= _entries.Length) return;
        foreach (Node child in _cardHost.GetChildren()) { _cardHost.RemoveChild(child); child.QueueFree(); }
        var entry = _entries[index]; _card = new(_scenario, _language, entry.Kind, entry.Id, _submit, _access); _cardHost.AddChild(_card); _card.ShowData(_view, _pending, true);
    }
    private string Stage(int stage) => stage switch { 0 => L("Деревня", "Village"), 1 => L("Посёлок", "Settlement"), 2 => L("Небольшой город", "Small town"), _ => L("Развитый город", "Developed town") };
    private static Label Wrapped() => new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private static string Wage(WeightedWage rate, string empty) => rate.Weight == 0 ? empty : (((decimal)(rate.WeightedMinor / rate.Weight) + (decimal)(rate.WeightedMinor % rate.Weight) / rate.Weight) / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
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
                var row = new HBoxContainer(); AddChild(row);
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

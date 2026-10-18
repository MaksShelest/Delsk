using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own enterprise only; progress is the Core batch, never a UI timer.</summary>
public partial class EnterpriseTablet : VBoxContainer
{
    private sealed partial class StockPanel : VBoxContainer
    {
        public readonly Label Goods = new();
        public readonly Label Capacity = new();
        public readonly ProgressBar Bar = new() { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12) };
        public StockPanel(string title, string item)
        {
            AddChild(new Label { Text = title });
            var row = new HBoxContainer(); AddChild(row);
            row.AddChild(new ResourceIcon { Item = item, CustomMinimumSize = new Vector2(24, 24) }); row.AddChild(Goods);
            AddChild(Bar); AddChild(Capacity);
        }
    }
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly System.Action<GameAction> _submit;
    private readonly long _id;
    private PlayerView _view;
    private EnterpriseView _enterprise;
    private LogisticsView? _system;
    private long _shownSystem = -1;
    private readonly Label _recipe = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _work = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _batch = new();
    private readonly Label _crew = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _missing = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _personal = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly ProgressBar _production = new() { ShowPercentage = true, CustomMinimumSize = new Vector2(0, 20) };
    private readonly StockPanel _inputs;
    private readonly StockPanel _outputs;
    private readonly Button _toggle;
    private readonly Button _supply;
    private readonly Button _take;
    private readonly Button _raise;
    private readonly Button _lower;
    private readonly VBoxContainer _logistics = new();
    private readonly List<Button> _routeButtons = [];
    private Label? _trip;
    private Label? _logCrew;
    private ProgressBar? _tripBar;
    private Button? _buyTruck;
    private Button? _outbound;
    private Button? _inbound;
    private Button? _logRaise;
    private Button? _logLower;

    public EnterpriseTablet(ScenarioDefinition scenario, string language, PlayerView view, long id, System.Action<GameAction> submit)
    {
        _scenario = scenario; _language = language; _view = view; _id = id; _submit = submit;
        _enterprise = view.Enterprises.Single(e => e.Id.Value == id);
        AddChild(_recipe);
        var columns = new HBoxContainer(); AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(440, 0) };
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 0) };
        columns.AddChild(left); columns.AddChild(right);
        left.AddChild(new Label { Text = L("Производство", "Production") }); left.AddChild(_work); left.AddChild(_production); left.AddChild(_batch);
        _toggle = Action(left, "", () => _submit(PlayerDesk.Work(_id, !_enterprise.Enabled)));
        var recipe = scenario.Recipes.Single(r => r.Id == _enterprise.RecipeId);
        _inputs = new StockPanel(L("Запас сырья", "Input stock"), recipe.InputItem); left.AddChild(_inputs);
        _supply = Action(left, L("Передать: ", "Supply: ") + PlayerText.Item(language, recipe.InputItem) + L(" ×1 → сырьё", " ×1 → input stock"),
            () => _submit(PlayerDesk.Transfer(_view.Participant.InventoryId.Value, _enterprise.InputId.Value, recipe.InputItem, 1)));
        left.AddChild(_missing);
        _outputs = new StockPanel(L("Готовая продукция", "Finished goods"), recipe.OutputItem); left.AddChild(_outputs);
        _take = Action(left, L("Забрать: ", "Take: ") + PlayerText.Item(language, recipe.OutputItem) + L(" ×1 → инвентарь", " ×1 → inventory"),
            () => _submit(PlayerDesk.Transfer(_enterprise.OutputId.Value, _view.Participant.InventoryId.Value, recipe.OutputItem, 1)));
        left.AddChild(_personal);
        right.AddChild(new Label { Text = L("Работники производства", "Production workers") }); right.AddChild(_crew);
        right.AddChild(new Label { Text = L("Найм постепенный. При простое зарплата не списывается; работники постепенно уходят.", "Hiring takes time. Idle workers are not paid and gradually leave."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var wages = new HBoxContainer(); right.AddChild(wages);
        _raise = Action(wages, L("Зарплата +", "Wage +"), () => ChangeWage(EmploymentRole.Production, (_scenario.Economy ?? new()).PlayerWageStepMinor));
        _lower = Action(wages, L("Зарплата −", "Wage −"), () => ChangeWage(EmploymentRole.Production, -(_scenario.Economy ?? new()).PlayerWageStepMinor));
        right.AddChild(new HSeparator()); right.AddChild(new Label { Text = L("Логистика", "Logistics") }); right.AddChild(_logistics);
    }
    public void ShowData(PlayerView view, bool pending, bool paused)
    {
        _view = view; _enterprise = view.Enterprises.Single(e => e.Id.Value == _id);
        var recipe = _scenario.Recipes.Single(r => r.Id == _enterprise.RecipeId);
        _recipe.Text = PlayerText.Item(_language,recipe.InputItem) + $" ×{recipe.InputQuantity} → " + PlayerText.Item(_language,recipe.OutputItem) + $" ×{recipe.OutputQuantity}" + L(" · энергия за партию: ", " · energy cost per batch: ") + PlayerText.Money(recipe.EnergyPerBatchMinor);
        _work.Text = TabletText.Reason(_language, _enterprise.ReasonCode);
        var progress = TabletProgress.Production(_enterprise); _production.MaxValue = progress.Total; _production.Value = progress.Done;
        _batch.Text = (_enterprise.BatchId == 0 ? L("Нет начатой партии", "No batch in progress") : L("Работа: ", "Work: ") + $"{progress.Done}/{progress.Total}") + L(" · завершено партий: ", " · completed batches: ") + _enterprise.CompletedBatches;
        _toggle.Text = L(_enterprise.Enabled ? "Остановить производство" : "Включить производство", _enterprise.Enabled ? "Stop production" : "Start production");
        _toggle.Disabled = _raise.Disabled = _lower.Disabled = pending;
        var input = view.Inventories.Single(i => i.Id == _enterprise.InputId); var output = view.Inventories.Single(i => i.Id == _enterprise.OutputId);
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        ShowStock(_inputs, input, recipe.InputItem); ShowStock(_outputs, output, recipe.OutputItem);
        _supply.Disabled = pending || view.Participant.Busy || Free(pack, recipe.InputItem) < 1 || !Room(input, recipe.InputItem);
        _take.Disabled = pending || view.Participant.Busy || Free(output, recipe.OutputItem) < 1 || !Room(pack, recipe.OutputItem);
        _supply.TooltipText = L("Нужны свободное сырьё в личном инвентаре и место на предприятии.", "Requires unreserved inputs in your inventory and space at the enterprise.");
        _take.TooltipText = L("Нужны свободная продукция на предприятии и место в личном инвентаре.", "Requires unreserved finished goods and space in your inventory.");
        _missing.Text = L("Для следующей партии: ", "For the next batch: ") + $"{Free(input,recipe.InputItem)}/{recipe.InputQuantity} " + PlayerText.Item(_language,recipe.InputItem);
        _personal.Text = L("В личном инвентаре: ", "In your inventory: ") + PlayerText.Item(_language,recipe.InputItem) + $" ×{Free(pack,recipe.InputItem)}, " + PlayerText.Item(_language,recipe.OutputItem) + $" ×{Free(pack,recipe.OutputItem)}";
        _crew.Text = Crew(ProductionGroup());
        _system = view.Logistics.SingleOrDefault(l => l.EnterpriseId.Value == _id);
        if (_shownSystem != (_system?.Id.Value ?? 0)) BuildLogistics();
        if (_system is null)
        { if (_buyTruck is not null) _buyTruck.Disabled = pending || view.Participant.Busy || view.AvailableCashMinor < (_scenario.Logistics ?? new()).PurchasePriceMinor; return; }
        if (_outbound is not null) { _outbound.Disabled = pending; _outbound.Text = L(_system.Enabled ? "Выключить отправку" : "Включить отправку", _system.Enabled ? "Disable outbound" : "Enable outbound"); }
        if (_inbound is not null) { _inbound.Disabled = pending; _inbound.Text = L(_system.InboundEnabled ? "Выключить подвоз" : "Включить подвоз", _system.InboundEnabled ? "Disable inbound" : "Enable inbound"); }
        if (_logRaise is not null) _logRaise.Disabled = pending;
        if (_logLower is not null) _logLower.Disabled = pending;
        foreach (var button in _routeButtons) button.Disabled = pending || _system.Trip is not null;
        if (_logCrew is not null) _logCrew.Text = Crew(view.Employment.Single(g => g.Id == _system.GroupId));
        string route = L("Отправка: ", "Outbound: ") + (_system.ContractId > 0 ? L("договор #", "contract #") + _system.ContractId : Destination(_system.OwnDestination)) + L(" · подвоз: ", " · inbound: ") + Destination(_system.InboundSource);
        if (_system.Trip is { } trip)
        {
            _trip!.Text = TabletText.Reason(_language,trip.State) + "\n" + (trip.State is "Outbound" or "ReturningCargo" or "ReturningInput" ? L("Груз: ", "Cargo: ") : L("Машина пустая; задание: ", "Truck empty; assignment: ")) + PlayerText.Item(_language,trip.ItemId) + $" ×{trip.Quantity} · ({trip.Position.X},{trip.Position.Z})\n" +
                L("Рейс завершится через ", "Trip completes in ") + TabletProgress.Remaining(trip.EndTick,view.Tick) + L(" с", " s") + (paused ? L(" · пауза", " · paused") : "") + "\n" + route;
            _tripBar!.MaxValue = Math.Max(1,trip.EndTick-trip.PreparedTick); _tripBar.Value = Math.Clamp(view.Tick-trip.PreparedTick,0,trip.EndTick-trip.PreparedTick);
        }
        else
        { _trip!.Text = TabletText.Reason(_language,_system.ReasonCode) + "\n" + route + "\n" + L("Грузовик ждёт у предприятия. Выполнено рейсов: ", "Truck waits at its enterprise. Completed trips: ") + _system.CompletedTrips; _tripBar!.MaxValue = 1; _tripBar.Value = 0; }
    }
    private void BuildLogistics()
    {
        _shownSystem = _system?.Id.Value ?? 0;
        foreach (Node child in _logistics.GetChildren()) { _logistics.RemoveChild(child); child.QueueFree(); }
        _routeButtons.Clear(); _buyTruck = _outbound = _inbound = _logRaise = _logLower = null; _trip = _logCrew = null; _tripBar = null;
        _logistics.AddChild(new Label { Text = L("Машина сама возит готовую продукцию получателю и подвозит сырьё из вашего склада или лавки. Выберите маршрут, включите нужное направление и обеспечьте товар, место и зарплату.",
            "The truck delivers finished goods and collects inputs from your warehouse or shop. Select a route, enable a direction, and provide goods, storage space and wages."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (_system is null)
        { _buyTruck = Action(_logistics, L("Купить грузовик — ", "Buy truck — ") + PlayerText.Money((_scenario.Logistics ?? new()).PurchasePriceMinor), () => _submit(PlayerDesk.BuyLogistics(_id))); return; }
        _trip = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _logistics.AddChild(_trip);
        _tripBar = new ProgressBar { ShowPercentage = true, CustomMinimumSize = new Vector2(0,14) }; _logistics.AddChild(_tripBar);
        _logCrew = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _logistics.AddChild(_logCrew);
        var wages = new HBoxContainer(); _logistics.AddChild(wages);
        _logRaise = Action(wages, L("Зарплата +", "Wage +"), () => ChangeWage(EmploymentRole.Logistics,(_scenario.Economy ?? new()).PlayerWageStepMinor));
        _logLower = Action(wages, L("Зарплата −", "Wage −"), () => ChangeWage(EmploymentRole.Logistics,-(_scenario.Economy ?? new()).PlayerWageStepMinor));
        var switches = new HBoxContainer(); _logistics.AddChild(switches);
        _outbound = Action(switches, "", () => _submit(PlayerDesk.Supplies(_system!.Id.Value,LogisticsDirection.Output,!_system.Enabled)));
        _inbound = Action(switches, "", () => _submit(PlayerDesk.Supplies(_system!.Id.Value,LogisticsDirection.Inbound,!_system.InboundEnabled)));
        var destinations = _view.Buildings.Where(b => b.Kind is BuildingKind.Warehouse or BuildingKind.Shop).ToArray();
        foreach (var b in destinations)
        {
            string name = PlayerText.Building(_language,b.TypeId) + " #" + b.Id.Value;
            _routeButtons.Add(Action(_logistics, L("Отправлять продукцию → ", "Deliver finished goods → ") + name, () => _submit(PlayerDesk.SendProduct(_system!.Id.Value,b.InputId.Value,true))));
            _routeButtons.Add(Action(_logistics, L("Подвозить сырьё ← ", "Collect inputs ← ") + name, () => _submit(PlayerDesk.FetchInput(_system!.Id.Value,b.InputId.Value,true))));
        }
        if (destinations.Length == 0) _logistics.AddChild(new Label { Text = L("Для своих маршрутов постройте склад или лавку. Договорные рейсы обслуживают реальные обязательства.", "Build a warehouse or shop for your own routes. Contract trips serve actual obligations."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _logistics.AddChild(new Label { Text = L("Начатый рейс завершается даже при выключении направления. Маршрут активного рейса менять нельзя.", "An active trip finishes even if its direction is disabled. Its route cannot be changed."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
    private string Destination(InventoryId inventory)
    {
        if (inventory.Value == 0) return L("не выбран", "not selected");
        var building = _view.Buildings.SingleOrDefault(b => b.InputId == inventory);
        return building is null ? L("по договору", "by contract") : PlayerText.Building(_language,building.TypeId) + " #" + building.Id.Value;
    }
    private EmploymentView ProductionGroup() => _view.Employment.Single(g => g.Role == EmploymentRole.Production && g.Id.Value == _id);
    private void ChangeWage(EmploymentRole role, long delta)
    {
        var group = role == EmploymentRole.Production ? ProductionGroup() : _view.Employment.Single(g => g.Id == _system!.GroupId);
        _submit(PlayerDesk.Wage(group.Id.Value,Math.Max(0,checked((group.PendingWageMinor ?? group.WageMinor)+delta))));
    }
    private string Crew(EmploymentView group) => L("Работников: ", "Workers: ") + $"{group.Headcount}/{group.Capacity}" + L(" · за работника и рабочий тик: ", " · per worker and working tick: ") + PlayerText.Money(group.WageMinor) +
        (group.PendingWageMinor is long pending ? "\n" + L("Новая зарплата: ", "New wage: ") + PlayerText.Money(pending) + L(" · с тика ", " · from tick ") + group.PayEffectiveTick : "");
    private void ShowStock(StockPanel panel, InventoryView stock, string item)
    {
        long reserved = stock.Stock.Where(s => s.ItemId == item).Sum(s => s.Reserved);
        panel.Goods.Text = PlayerText.Item(_language,item) + L(": свободно ", ": free ") + Free(stock,item) + L(", резерв ", ", reserved ") + reserved;
        panel.Bar.MaxValue = Math.Max(1,stock.Capacity); panel.Bar.Value = stock.OccupiedVolume + stock.ReservedCapacity;
        panel.Capacity.Text = L("Место: занято ", "Space: used ") + stock.OccupiedVolume + L(", обещано ", ", reserved ") + stock.ReservedCapacity + $" / {stock.Capacity}";
    }
    private static long Free(InventoryView stock,string item) => stock.Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity-s.Reserved);
    private bool Room(InventoryView stock,string item) => stock.OccupiedVolume + stock.ReservedCapacity + _scenario.Items.Single(i => i.Id == item).UnitVolume <= stock.Capacity;
    private static Button Action(Container parent,string text,System.Action action)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left }; button.Pressed += action; parent.AddChild(button); return button; }
    private string L(string ru,string en) => PlayerText.Local(_language,ru,en);
}

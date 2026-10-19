using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own objects only. Commands and access come from Core, not a client economy.</summary>
public partial class HouseholdObjectCard : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly string _kind;
    private readonly long _id;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction, ActionAccess> _access;
    private readonly Label _heading = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _stock = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _workers = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _delivery = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly ProgressBar _progress = new() { CustomMinimumSize = new(0, 18), ShowPercentage = true };
    private readonly SpinBox _quantity = new() { MinValue = 1, MaxValue = 1000000, Value = 1, Step = 1, CustomMinimumSize = new(125, 0) };
    private readonly VBoxContainer _actions = new();
    private readonly Dictionary<string, (Button Button, GameAction Action)> _buttons = [];
    private readonly RouteEditor _routes;
    private PlayerView? _lastView;
    private bool _pending;
    private bool _paused;
    private string? _activeTransferKey;
    private readonly Dictionary<string, VBoxContainer> _actionGroups = [];

    public HouseholdObjectCard(ScenarioDefinition scenario, string language, string kind, long id,
        System.Action<GameAction> submit, System.Func<GameAction, ActionAccess> access)
    {
        _scenario = scenario; _language = language; _kind = kind; _id = id; _submit = submit; _access = access;
        AddChild(_heading); AddChild(_progress); AddChild(_stock); AddChild(_workers); AddChild(_delivery);
        var amount = new HBoxContainer(); AddChild(amount);
        amount.AddChild(new Label { Text = L("Количество для переноса", "Transfer quantity") }); amount.AddChild(_quantity);
        AddChild(_actions);
        var routeToggle=new Button{Text=L("Маршруты ▸","Routes ▸"),FocusMode=FocusModeEnum.None};AddChild(routeToggle);
        _routes=new(scenario,language,kind,id,submit){Visible=false};AddChild(_routes);routeToggle.Pressed+=()=>_routes.Visible=!_routes.Visible;
        _quantity.ValueChanged += _ => { if (_lastView is not null) ShowData(_lastView,_pending,_paused); };
    }

    public void ShowData(PlayerView view, bool pending, bool paused)
    {
        _lastView = view; _pending = pending; _paused = paused;_routes.ShowData(view,pending);
        var actions = new List<(string Key, string Text, GameAction Action, bool Enabled)>();
        long quantity = (long)_quantity.Value;
        void Add(string key, string text, GameAction action, bool enabled = true) => actions.Add((key, text, action, enabled));
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        EnterpriseView? enterprise = null; EquipmentView? machine = null; BuildingView? building = null;
        InventoryView[] stocks;
        if (_kind == "equipment")
        {
            machine = view.Equipment!.Single(e => e.Id.Value == _id);
            var type = _scenario.EquipmentTypes!.Single(t => t.Id == machine.TypeId);
            var level = type.Levels.Single(l => l.Level == machine.Level);
            stocks = [view.Inventories.Single(i => i.Id == machine.StorageId)];
            _heading.Text = L("Добывающая техника", "Extraction equipment") + " #" + _id + L(" · уровень ", " · level ") + machine.Level + "\n" +
                PlayerText.Item(_language, type.ItemId) + $" ×{level.CycleQuantity} / {level.CycleTicks}" + L(" рабочих тиков · ", " working ticks · ") + TabletText.Reason(_language, machine.ReasonCode);
            _progress.MaxValue = Math.Max(1, machine.WorkRequired); _progress.Value = machine.WorkDone;
            foreach (var mode in Enum.GetValues<EquipmentMode>())
                Add("mode:" + mode, mode switch { EquipmentMode.Off => L("Остановить", "Stop"), EquipmentMode.Personal => L("Личный режим", "Personal mode"), _ => L("Автоматическая добыча", "Automatic extraction") },
                    new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: mode), mode != machine.Mode);
            Add("harvest", L("Добыть лично ×", "Harvest personally ×") + quantity, new(ActionKind.Harvest, ItemId: type.ItemId, Quantity: quantity, Equipment: machine.Id), machine.Mode == EquipmentMode.Personal);
            if (type.Levels.FirstOrDefault(l => l.Level == machine.Level + 1) is { } next)
                Add("upgrade", L("Улучшить технику — ", "Upgrade equipment — ") + PlayerText.Money(next.PriceMinor), new(ActionKind.UpgradeEquipment, Equipment: machine.Id), machine.Mode == EquipmentMode.Off && machine.CycleId == 0);
        }
        else
        {
            building = view.Buildings.SingleOrDefault(b => b.Id.Value == _id);
            enterprise = view.Enterprises.SingleOrDefault(e => e.Id.Value == _id);
            stocks = building is null ? view.Inventories.Where(i => i.Id == enterprise!.InputId || i.Id == enterprise.OutputId).ToArray()
                : view.Inventories.Where(i => i.Id == building.InputId || i.Id == building.OutputId).ToArray();
            _heading.Text = (building is null ? L("Производство", "Production") : PlayerText.Building(_language, building.TypeId)) + " #" + _id + L(" · уровень ", " · level ") + (building?.Level ?? enterprise!.Level);
            _progress.Visible = enterprise is not null;
            if (enterprise is not null)
            {
                var recipe = _scenario.Recipes.Single(r => r.Id == enterprise.RecipeId);
                _heading.Text += "\n" + string.Join(" + ",recipe.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId)+" ×"+i.Quantity))+" → "+PlayerText.Item(_language,recipe.OutputItem)+" ×"+recipe.OutputQuantity +
                    L(" · энергия/партия ", " · energy/batch ") + PlayerText.Money(recipe.EnergyPerBatchMinor) + "\n" + TabletText.Reason(_language, enterprise.ReasonCode);
                var progress = TabletProgress.Production(enterprise); _progress.MaxValue = progress.Total; _progress.Value = progress.Done;
                Add("work", enterprise.Enabled ? L("Остановить производство", "Stop production") : L("Включить производство", "Start production"), PlayerDesk.Work(_id, !enterprise.Enabled));
                if (building is not null)
                    foreach (var recipeId in _scenario.BuildingTypes!.Single(t => t.Id == building.TypeId).Levels.Where(l => l.Level <= building.Level)
                        .SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal).Where(id=>id!=enterprise.RecipeId))
                        Add("recipe:" + recipeId, L("Рецепт: ", "Recipe: ") + Recipe(recipeId), new(ActionKind.SetRecipe, Enterprise: new(building.Id.Value), RecipeId: recipeId), !enterprise.Enabled && enterprise.BatchId == 0);
            }
            if (building is not null && _scenario.BuildingTypes!.Single(t => t.Id == building.TypeId).Levels.FirstOrDefault(l => l.Level == building.Level + 1) is { } upgrade)
                Add("upgrade", L("Улучшить здание — ", "Upgrade building — ") + PlayerText.Money(upgrade.PriceMinor) + " + " + string.Join(", ", upgrade.Materials.Select(m => PlayerText.Item(_language, m.ItemId) + " ×" + m.Quantity)),
                    new(ActionKind.UpgradeBuilding, Building: building.Id), enterprise is null || !enterprise.Enabled && enterprise.BatchId == 0);
        }
        _stock.Text = string.Join("\n", stocks.Select(i => Stock(view, i))) + "\n" + L("Рюкзак: ", "Backpack: ") + string.Join(", ", pack.Stock.Select(s => PlayerText.Item(_language, s.ItemId) + $" ×{s.Quantity - s.Reserved}"));
        foreach (var inventory in stocks)
            foreach (var item in _scenario.Items)
            {
                string name = PlayerText.Item(_language, item.Id), destination = InventoryName(view, inventory.Id);
                void TransferChoice(string direction,InventoryView from,InventoryView to,long wanted,string suffix)
                {
                    long possible=Math.Min(Free(from,item.Id),Math.Max(0,to.Capacity-to.OccupiedVolume-to.ReservedCapacity)/item.UnitVolume);
                    long q=Math.Min(wanted,possible);
                    string amount=suffix=="one"?L("1 ед.: ","1 unit: "):suffix=="all"?L("Всё: ","All: "):L("Выбранное: ","Selected: ");
                    Add($"{direction}:{inventory.Id.Value}:{item.Id}:{suffix}",amount+(direction=="put"?L("Передать ","Supply "):L("Забрать ","Take "))+name+$" ×{q} "+(direction=="put"?"→ ":"← ")+destination,
                        PlayerDesk.Transfer(from.Id.Value,to.Id.Value,item.Id,Math.Max(1,q)),q>0);
                }
                TransferChoice("put",pack,inventory,quantity,"selected");TransferChoice("take",inventory,pack,quantity,"selected");
                TransferChoice("put",pack,inventory,1,"one");TransferChoice("take",inventory,pack,1,"one");
                TransferChoice("put",pack,inventory,long.MaxValue,"all");TransferChoice("take",inventory,pack,long.MaxValue,"all");
            }
        var groups = view.Employment.Where(g => machine is null ? g.Role == EmploymentRole.Production && g.Id.Value == _id ||
            view.Logistics.Any(l => l.EnterpriseId.Value == _id && l.GroupId == g.Id) : g.Id == machine.GroupId).ToArray();
        _workers.Text = string.Join("\n", groups.Select(g => Role(g.Role) + L(": на вас работают ", ": working for you ") + $"{g.Headcount}/{g.Capacity}" + L(" · ставка ", " · wage ") + PlayerText.Money(g.WageMinor) +
            (g.PendingWageMinor is long pay ? L(" · новая ", " · pending ") + PlayerText.Money(pay) + L(" с тика ", " from tick ") + g.PayEffectiveTick : "")));
        foreach (var group in groups)
        {
            long wage = group.PendingWageMinor ?? group.WageMinor, step = view.Economy!.PlayerWageStepMinor;
            Add("wage+:" + group.Id.Value, Role(group.Role) + L(": зарплата +", ": wage +") + PlayerText.Money(step), PlayerDesk.Wage(group.Id.Value, wage > long.MaxValue-step ? wage : wage + step), wage <= long.MaxValue-step);
            Add("wage-:" + group.Id.Value, Role(group.Role) + L(": зарплата −", ": wage −") + PlayerText.Money(step), PlayerDesk.Wage(group.Id.Value, Math.Max(0, wage - step)), wage > 0);
        }
        _delivery.Text = "";
        if (enterprise is not null) LogisticsActions(view, enterprise, Add);
        if(view.Transfers.Length>0)Add("transfer-stop",L("Остановить передачу","Stop transfer"),new(ActionKind.StopTransfer));
        UpdateActions(actions, pending);
        if(view.Transfers.Length==0 && !pending)_activeTransferKey=null;
        foreach(var (key,pair) in _buttons)
            if(pair.Button is ContextActionButton button)
            {
                var action=pair.Action;var process=view.Transfers.FirstOrDefault(t=>t.Source==action.Source.Value && t.Destination==action.Destination.Value && t.ItemId==action.ItemId);
                long volume=_scenario.Items.Single(i=>i.Id==action.ItemId).UnitVolume;
                if(process is not null && _activeTransferKey is null && key.EndsWith(":selected",StringComparison.Ordinal))_activeTransferKey=key;
                bool active=process is not null && _activeTransferKey==key;
                button.ShowProgress(!active?0:100d*(double)((Int128)process!.Completed*volume+process.WorkRemainder)/(double)((Int128)process.Quantity*volume));
                if(active)button.ShowCaption(L("Передано ","Transferred ")+process!.Completed+"/"+process.Quantity+" · "+PlayerText.Item(_language,process.ItemId));
            }
    }

    private void LogisticsActions(PlayerView view, EnterpriseView enterprise, System.Action<string, string, GameAction, bool> add)
    {
        var system = view.Logistics.SingleOrDefault(l => l.EnterpriseId == enterprise.Id);
        if (system is null)
        {
            add("truck", L("Купить грузовик — ", "Buy truck — ") + PlayerText.Money((_scenario.Logistics ?? new()).PurchasePriceMinor), PlayerDesk.BuyLogistics(_id), true); return;
        }
        _delivery.Text = L("Доставка · уровень ", "Logistics · level ") + system.Level + L(" · вместимость ", " · capacity ") + view.Inventories.Single(i=>i.Id==system.CargoInventory).Capacity + "\n" + (system.Trip is { } trip
            ? TabletText.Reason(_language, system.ReasonCode) + " · " + PlayerText.Item(_language, trip.ItemId) + $" ×{trip.Quantity}" +
                (trip.Freight is {} freight ? L(" · загружено "," · loaded ")+freight.Loaded+L(", передано ",", delivered ")+freight.Received:"")+
                L(" · ориентир без ожиданий: ", " · estimate without waits: ") + Math.Max(0, trip.EndTick - view.Tick) + L(" тиков", " ticks")
            : TabletText.Reason(_language, system.ReasonCode)) + "\n" + string.Join("\n", system.Queue.Select(j => (j.Enabled ? "✓ " : "— ") + Route(view, j)));
        add("output", system.Enabled ? L("Остановить отправку", "Stop outbound") : L("Включить отправку", "Enable outbound"), PlayerDesk.Supplies(system.Id.Value, LogisticsDirection.Output, !system.Enabled), true);
        add("inbound", system.InboundEnabled ? L("Остановить подвоз", "Stop inbound") : L("Включить подвоз", "Enable inbound"), PlayerDesk.Supplies(system.Id.Value, LogisticsDirection.Inbound, !system.InboundEnabled), true);
        if (system.Level == 1 && _scenario.Logistics?.Upgrade is { } upgrade)
            add("truck-upgrade", L("Улучшить грузовик — ", "Upgrade truck — ") + PlayerText.Money(upgrade.PriceMinor), new(ActionKind.UpgradeLogistics, Logistics: system.Id), system.Trip is null && !system.Enabled && !system.InboundEnabled);
        foreach (var b in view.Buildings.Where(b => b.Id.Value != _id))
        {
            bool production = b.Kind == BuildingKind.Production;
            if (!production || _scenario.Recipes.Single(r => r.Id == view.Enterprises.Single(e => e.Id.Value == b.Id.Value).RecipeId).Uses(_scenario.Recipes.Single(r => r.Id == enterprise.RecipeId).OutputItem))
                add("route-out:" + b.Id.Value, L("Отправлять → ", "Deliver → ") + InventoryName(view, b.InputId), new(production ? ActionKind.SetLogisticsProduction : ActionKind.SetLogisticsStorage, Logistics: system.Id, Destination: b.InputId, Enabled: true), system.Trip is null);
            if (!production) add("route-in:" + b.Id.Value, L("Подвозить ← ", "Collect ← ") + InventoryName(view, b.InputId), PlayerDesk.FetchInput(system.Id.Value, b.InputId.Value, true), system.Trip is null);
        }
        foreach (var contract in view.Contracts!.Where(c => c.Supplier == view.Participant.Id && c.ClosedTick is null))
            add("contract:" + contract.Id, L("Отправлять по договору #", "Deliver contract #") + contract.Id + L(" · осталось ", " · remaining ") + contract.RemainingQuantity,
                new(ActionKind.SetLogisticsContract, Logistics: system.Id, ContractId: contract.Id, Enabled: true), system.Trip is null);
        foreach (var job in system.Queue)
            add("queue:" + job.Key, (job.Enabled ? L("Убрать задание: ", "Disable job: ") : L("Добавить задание: ", "Enable job: ")) + Route(view, job),
                job.Source.Value!=0?new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:job.Source,Destination:job.Destination,ItemId:job.ItemId,Enabled:!job.Enabled):new(ActionKind.QueueLogisticsJob, Logistics: system.Id, ContractId: job.ContractId, Destination: job.OwnDestination, Source: job.InboundSource, Enabled: !job.Enabled), true);
        // Queue additional work independently of the primary route, using Core's normal priorities.
        foreach (var b in view.Buildings.Where(b => b.Kind is BuildingKind.Warehouse or BuildingKind.Shop))
            add("queue-out:" + b.Id.Value, L("В очередь отправки → ", "Queue outbound → ") + InventoryName(view, b.InputId),
                new(ActionKind.QueueLogisticsJob, Logistics: system.Id, Destination: b.InputId, Enabled: true), true);
    }

    private void UpdateActions(List<(string Key, string Text, GameAction Action, bool Enabled)> actions, bool pending)
    {
        foreach (var key in _buttons.Keys.Except(actions.Select(a => a.Key)).ToArray())
        { var button = _buttons[key].Button; button.GetParent().RemoveChild(button); button.QueueFree(); _buttons.Remove(key); }
        foreach (var row in actions)
        {
            if (!_buttons.TryGetValue(row.Key, out var old))
            {
                Button button = row.Action.Kind==ActionKind.Transfer ? new ContextActionButton(row.Text) : new Button { FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                string key = row.Key; button.Pressed += () => {var action=_buttons[key].Action;if(action.Kind==ActionKind.Transfer)_activeTransferKey=key;_submit(action);};
                string section = row.Action.Kind switch
                {
                    ActionKind.Transfer => L("Запасы и перенос", "Stock and transfers"),
                    ActionKind.SetWage => L("Работники и зарплата", "Workers and wages"),
                    ActionKind.BuyLogistics or ActionKind.UpgradeLogistics or ActionKind.QueueLogisticsJob or ActionKind.SetLogisticsContract or ActionKind.SetLogisticsInbound or ActionKind.SetLogisticsStorage or ActionKind.SetLogisticsProduction or ActionKind.SetLogisticsEnabled => L("Доставка", "Logistics"),
                    _ => L("Работа и улучшения", "Work and upgrades")
                };
                if (!_actionGroups.TryGetValue(section,out var group))
                {
                    var toggle = new Button {Text = section + " ▸",FocusMode=FocusModeEnum.None,Alignment=HorizontalAlignment.Left};
                    _actions.AddChild(toggle); group = new VBoxContainer {Visible = section == L("Работа и улучшения", "Work and upgrades")}; _actions.AddChild(group);
                    var target=group;toggle.Pressed += () => target.Visible=!target.Visible;_actionGroups.Add(section,group);
                }
                group.AddChild(button); old = (button, row.Action);
            }
            var access = _access(row.Action); if(old.Button is ContextActionButton transferButton)transferButton.ShowCaption(row.Text);else old.Button.Text = row.Text;
            old.Button.Disabled = pending || !row.Enabled || !access.Allowed;
            old.Button.TooltipText = access.Allowed ? row.Enabled ? "" : L("Проверьте запас, место и безопасное состояние объекта.", "Check stock, capacity and the object's safe state.") : PlayerText.Reason(_language, access.ReasonCode) + " " + string.Join(", ", access.Locations.Select(c => $"({c.X},{c.Z})"));
            _buttons[row.Key] = (old.Button, row.Action);
        }
    }
    private string Stock(PlayerView view, InventoryView stock) => InventoryName(view, stock.Id) + $" · {stock.OccupiedVolume}+{stock.ReservedCapacity}/{stock.Capacity}\n" +
        string.Join(", ", stock.Stock.Select(s => PlayerText.Item(_language, s.ItemId) + L(": свободно ", ": free ") + (s.Quantity - s.Reserved) + L(", резерв ", ", reserved ") + s.Reserved));
    private string InventoryName(PlayerView view, InventoryId id)
    {
        var building = view.Buildings.SingleOrDefault(b => b.InputId == id || b.OutputId == id);
        return building is null ? L("Запас #", "Storage #") + id.Value : PlayerText.Building(_language, building.TypeId) + " #" + building.Id.Value + (building.OutputId == id ? L(" · выход", " · output") : L(" · вход", " · input"));
    }
    private string Route(PlayerView view, LogisticsJobView job) => job.Source.Value!=0?InventoryName(view,job.Source)+" → "+InventoryName(view,job.Destination)+" · "+PlayerText.Item(_language,job.ItemId):job.ContractId > 0 ? L("Договор #", "Contract #") + job.ContractId : job.InboundSource.Value > 0 ? L("Подвоз из ", "Collect from ") + InventoryName(view, job.InboundSource) : L("Отправка в ", "Deliver to ") + InventoryName(view, job.OwnDestination);
    private string Recipe(string id) { var recipe = _scenario.Recipes.Single(r => r.Id == id); return string.Join(" + ",recipe.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId)+" ×"+i.Quantity))+" → "+PlayerText.Item(_language,recipe.OutputItem)+" ×"+recipe.OutputQuantity; }
    private string Role(EmploymentRole role) => role switch { EmploymentRole.Production => L("Производство", "Production"), EmploymentRole.Extraction => L("Добыча", "Extraction"), _ => L("Доставка", "Logistics") };
    private static long Free(InventoryView stock, string item) => stock.Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity - s.Reserved);
    private static bool Room(InventoryView stock, ItemDefinition item, long q) => (Int128)stock.OccupiedVolume + stock.ReservedCapacity + (Int128)q * item.UnitVolume <= stock.Capacity;
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

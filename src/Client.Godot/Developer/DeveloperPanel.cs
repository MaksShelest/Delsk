using Godot;
using Simulation.Core;
using Simulation.Developer;
using Simulation.Hosting;

public partial class DeveloperPanel : PanelContainer
{
    public event System.Action? Closed;
    private readonly DeveloperTools _tools;
    private readonly GameSession _session;
    private readonly string _language;
    private readonly System.Action _refresh;
    private readonly OptionButton _actor = new() { Name = "GodActor", FocusMode = FocusModeEnum.None };
    private readonly OptionButton _inventory = new() { Name = "GodInventory", FocusMode = FocusModeEnum.None };
    private readonly OptionButton _item = new() { Name = "GodItem", FocusMode = FocusModeEnum.None };
    private readonly SpinBox _money = new() { Name = "GodMoneyTarget", MinValue = 0, MaxValue = 10000000000, Step = 0.01, UpdateOnTextChanged = true, CustomMinimumSize = new(180, 0) };
    private readonly OptionButton _industry=new(){Name="GodIndustry"};
    private readonly SpinBox _experience=new(){Name="GodExperienceTarget",MinValue=0,MaxValue=1000000000000,Step=1,CustomMinimumSize=new(150,0)};
    private readonly Label _experienceCurrent=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly SpinBox _stock = new() { Name = "GodStockTarget", MinValue = 0, MaxValue = 1000000000000, Step = 1, UpdateOnTextChanged = true, CustomMinimumSize = new(150, 0) };
    private readonly SpinBox _exponent = new() { MinValue = 1, MaxValue = 5, Step = 1 };
    private readonly SpinBox _target = new() { MinValue = 1, MaxValue = 1000000, Step = 1, Value = 600 };
    private readonly SpinBox _count = new() { MinValue = 1, MaxValue = 100000, Step = 1, Value = 100 };
    private readonly CheckButton _self = new() { FocusMode = FocusModeEnum.None };
    private readonly Label _status = new() { Name = "DeveloperStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _moneyCurrent = new() { Name = "GodMoneyCurrent", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _stockCurrent = new() { Name = "GodStockCurrent", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly ItemList _log = new() { CustomMinimumSize = new(0, 130) };
    private readonly TextEdit _detail = new() { Editable = false, CustomMinimumSize = new(0, 260), WrapMode = TextEdit.LineWrappingMode.Boundary };
    private DecisionLogEntry[] _entries = [];
    private InventoryId[] _inventories = [];
    private string _inventoryKey = "", _journalKey = "", _notice = "";
    private bool _loadMoney = true, _loadStock = true;

    public DeveloperPanel(DeveloperTools tools, GameSession session, ParticipantId human, string language, System.Action refresh)
    {
        _tools = tools; _session = session; _language = language; _refresh = refresh;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("243a3e"), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12 });
        _detail.AddThemeColorOverride("font_readonly_color", new Color("e8eee8"));
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); OffsetLeft = 30; OffsetRight = -30; OffsetTop = 30; OffsetBottom = -30;
        var box = new VBoxContainer(); AddChild(box);
        var header = new HBoxContainer(); box.AddChild(header);
        header.AddChild(new Label { Text = L("Разработчик · отладочная партия", "Developer · research-modified game"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Button(header, L("Закрыть", "Close"), () => Closed?.Invoke(), "DeveloperClose");
        box.AddChild(_status);
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; box.AddChild(tabs);
        var edit = Tab(tabs, L("Режим бога", "God mode"));
        edit.AddChild(new Label { Text = L("Установить точное количество, а не прибавить к текущему.", "Set an exact total, rather than add to the current amount."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var actorRow = new HBoxContainer(); edit.AddChild(actorRow);
        actorRow.AddChild(new Label { Text = L("Участник", "Participant") }); actorRow.AddChild(_actor);
        int humanIndex = 0, index = 0;
        foreach (var p in session.Engine.Inspect().Participants)
        {
            _actor.AddItem((p.Id == human ? L("Вы", "You") : "NPC") + " #" + p.Id.Value, checked((int)p.Id.Value));
            if (p.Id == human) humanIndex = index;
            index++;
        }
        _actor.Select(humanIndex);
        _actor.ItemSelected += _ => { _inventoryKey = ""; _loadMoney = _loadStock = true; RefreshData(); };
        edit.AddChild(_moneyCurrent);
        Row(edit, L("Деньги, монет", "Money, coins"), _money,
            () => _tools.Money(Actor, checked((long)decimal.Round((decimal)_money.Value * 100m, 0, MidpointRounding.AwayFromZero))), "GodMoneyApply");
        var goods = new HFlowContainer(); edit.AddChild(goods);
        goods.AddChild(new Label { Text = L("Хранилище", "Storage") }); goods.AddChild(_inventory);
        goods.AddChild(new Label { Text = L("Ресурс", "Resource") }); goods.AddChild(_item);
        foreach (var item in session.Engine.Configuration().Items) _item.AddItem(PlayerText.Item(language, item.Id));
        _inventory.ItemSelected += _ => { _loadStock = true; RefreshData(); };
        _item.ItemSelected += _ => { _loadStock = true; RefreshData(); };
        edit.AddChild(_stockCurrent);
        Row(edit, L("Количество, шт.", "Quantity, units"), _stock,
            () => _tools.Stock(Actor, _inventories[_inventory.Selected], SelectedItem, (long)_stock.Value), "GodStockApply");
        if(session.Engine.Configuration().Economy?.Development is {} development)
        {
            edit.AddChild(_industry);foreach(var industry in development.Industries)_industry.AddItem(PlayerText.Building(language,industry.Id));edit.AddChild(_experienceCurrent);_industry.ItemSelected+=_=>RefreshData();
            Row(edit,L("Опыт отрасли, всего","Industry experience, total"),_experience,()=>_tools.IndustryExperience(Actor,development.Industries[_industry.Selected].Id,(long)_experience.Value),"GodExperienceApply");
            Button(edit,L("Прибавить указанное количество XP","Add the entered XP amount"),()=>Guard(()=>
            {
                string industry=development.Industries[_industry.Selected].Id;long current=_session.Engine.DevelopmentOf(Actor)!.Experience.Single(e=>e.Industry==industry).Total;
                _tools.IndustryExperience(Actor,industry,checked(current+(long)_experience.Value));
            }),"GodExperienceAdd");
        }
        _exponent.Value = session.Engine.EconomyRules.WageAttractionExponent;
        Row(edit, L("Привлекательность зарплаты (степень 1–5)", "Wage attraction (exponent 1–5)"), _exponent, () => _tools.WageExponent((int)_exponent.Value));
        edit.AddChild(new Label { Text = L(
            "Мир остаётся на паузе. Изменения записываются в аудит. Зарезервированные деньги и товары защищены, вместимость не увеличивается. На голосовании и внутри незавершённого хода изменения недоступны.",
            "The world stays paused. Changes are audited. Reserved money and stock remain protected; capacity is unchanged. Edits are unavailable during voting or an unfinished tick."), AutowrapMode = TextServer.AutowrapMode.WordSmart });

        var advance = Tab(tabs, L("Перемотка", "Advance time"));
        _target.MaxValue = session.Engine.Configuration().MaxTicks;
        _self.Text = L("Временно голосовать за себя (с учётом договора)", "Temporarily vote for yourself (respect signed support)"); advance.AddChild(_self);
        Row(advance, L("До тика", "To tick"), _target, () => { _tools.StartAdvance((long)_target.Value, _self.ButtonPressed); return null; });
        Row(advance, L("На N тиков", "By N ticks"), _count, () => { _tools.StartAdvance(checked(session.Engine.Tick + (long)_count.Value), _self.ButtonPressed); return null; });
        Button(advance, L("Остановить перемотку", "Cancel advance"), () => { _tools.CancelAdvance(); RefreshData(); });
        advance.AddChild(new Label { Text = L("Закрытие экрана останавливает перемотку. Это реальные ходы вперёд, без отката.", "Closing this screen cancels advance. These are real forward ticks, without rewind."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var journal = new VBoxContainer { Name = L("Решения NPC", "NPC decisions") }; tabs.AddChild(journal);
        journal.AddChild(_log); journal.AddChild(_detail);
        _log.ItemSelected += selected => ShowEntry((int)selected);
    }

    private ParticipantId Actor => new(_actor.GetSelectedId());
    private string SelectedItem => _session.Engine.Configuration().Items[_item.Selected].Id;
    private static VBoxContainer Tab(TabContainer tabs, string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; tabs.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(body); return body;
    }
    private void Row(Container parent, string label, Control input, System.Func<ResearchModification?> action, string name = "")
    {
        var row = new HFlowContainer(); parent.AddChild(row); row.AddChild(new Label { Text = label }); row.AddChild(input);
        Button(row, L("Применить", "Apply"), () => Guard(() => action()), name);
    }
    private void Guard(System.Action action)
    {
        try { action(); _notice = L("Выполнено; изменение записано в аудит.", "Applied; change recorded in the audit."); _refresh(); }
        catch (Exception e)
        {
            _notice = e.Message == "UnsafeInterventionBoundary"
                ? L("Завершите ход кнопкой шага в Debug; при выборах сначала проголосуйте.", "Complete the tick with the Debug step button; finish any pending vote first.")
                : L("Не применено: ", "Not applied: ") + e.Message;
        }
        RefreshData();
    }
    private static void Button(Container parent, string title, System.Action action, string name = "")
    {
        var button = new Button { Text = title, FocusMode = FocusModeEnum.None };
        if (name.Length > 0) button.Name = name;
        button.Pressed += action; parent.AddChild(button);
    }
    public void RefreshData(bool updateStatus = true)
    {
        if (updateStatus) _status.Text = L("Тик ", "Tick ") + _session.Engine.Tick + " · " + _tools.AdvanceStatus +
            " · " + L("цель ", "target ") + (_tools.AdvanceTarget?.ToString() ?? "—") +
            " · " + L("изменений: ", "edits: ") + _session.Engine.ResearchModifications.Length + "\n" + _notice;
        var world = _session.Engine.Inspect();
        var person = world.Participants.Single(p => p.Id == Actor);
        var account = world.Accounts.Single(a => a.Id == person.AccountId);
        _moneyCurrent.Text = L("Сейчас: ", "Current: ") + PlayerText.Money(account.BalanceMinor) +
            " · " + L("зарезервировано: ", "reserved: ") + PlayerText.Money(account.ReservedMinor);
        if (_loadMoney) { _money.Value = account.BalanceMinor / 100.0; _loadMoney = false; }
        var stocks = world.Inventories.Where(i => i.OwnerId == Actor).ToArray();
        string key = Actor.Value + "|" + string.Join(";", stocks.Select(i => i.Id.Value));
        if (key != _inventoryKey)
        {
            _inventoryKey = key; _inventories = stocks.Select(i => i.Id).ToArray(); _inventory.Clear();
            foreach (var inventory in stocks)
                _inventory.AddItem((inventory.Id == person.InventoryId ? L("Рюкзак", "Backpack") : L("Хранилище", "Storage")) +
                    " #" + inventory.Id.Value + " · (" + inventory.Position.X + ", " + inventory.Position.Z + ")");
            int backpack = Array.FindIndex(_inventories, id => id == person.InventoryId);
            if (_inventories.Length > 0) _inventory.Select(Math.Max(0, backpack));
            _loadStock = true;
        }
        if (_inventory.Selected >= 0 && _item.Selected >= 0)
        {
            var inventory = stocks.Single(i => i.Id == _inventories[_inventory.Selected]);
            long quantity = inventory.Stock.Where(s => s.ItemId == SelectedItem).Sum(s => s.Quantity);
            long reserved = inventory.Stock.Where(s => s.ItemId == SelectedItem).Sum(s => s.Reserved);
            _stockCurrent.Text = PlayerText.Item(_language, SelectedItem) + ": " + quantity + " · " + L("зарезервировано: ", "reserved: ") + reserved +
                " · " + L("объём хранилища: ", "storage volume: ") + inventory.OccupiedVolume + "/" + inventory.Capacity;
            if (_loadStock) { _stock.Value = quantity; _loadStock = false; }
        }
        if(_session.Engine.DevelopmentOf(Actor) is {} development&&_industry.Selected>=0)
        {
            string industry=_session.Engine.Configuration().Economy!.Development!.Industries[_industry.Selected].Id;
            var xp=development.Experience.Single(e=>e.Industry==industry);_experienceCurrent.Text=PlayerText.Building(_language,industry)+$": {xp.Total} XP · "+L("технология ","technology ")+xp.TechnologyLevel;
        }
        var entries = _tools.Journal;
        string journalKey = entries.Length + ":" + (entries.LastOrDefault()?.Tick ?? -1) + ":" + (entries.LastOrDefault()?.Actions ?? "");
        if (journalKey != _journalKey)
        {
            _journalKey = journalKey; _entries = entries; _log.Clear();
            foreach (var entry in entries) _log.AddItem($"{entry.Tick} · NPC {entry.Actor} · {entry.Goal} · step {entry.PlanStep}");
            if (entries.Length > 0) { _log.Select(entries.Length - 1); ShowEntry(entries.Length - 1); }
        }
    }
    private void ShowEntry(int index)
    {
        if (index < 0 || index >= _entries.Length) return;
        var e = _entries[index];
        _detail.Text = $"Tick {e.Tick} · actor {e.Actor} · goal {e.Goal} · reason {e.Reason} · step {e.PlanStep}\nCandidates / scores:\n{e.Candidates}\nActions:\n{e.Actions}\nCommand results / refusal reasons:\n{e.Results}\nObservation:\n{e.Observation}\nModel input:\n{e.ModelRequest}\nModel response:\n{e.ModelReply}\nLatency: {e.Latency} ms · Fallback: {e.Fallback}";
    }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

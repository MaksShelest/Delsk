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
    private readonly OptionButton? _industry;
    private readonly Label? _experienceCurrent;
    private readonly SpinBox _stock = new() { Name = "GodStockTarget", MinValue = 0, MaxValue = 1000000000000, Step = 1, UpdateOnTextChanged = true, CustomMinimumSize = new(150, 0) };
    private readonly SpinBox _exponent = new() { MinValue = 1, MaxValue = 5, Step = 1 };
    private readonly SpinBox _target = new() { Name = "AdvanceTarget", MinValue = 1, Step = 1 };
    private readonly SpinBox _count = new() { Name = "AdvanceCount", MinValue = 1, Step = 1 };
    private readonly CheckButton _automatic = new() { Name = "AdvanceAutoBallot", FocusMode = FocusModeEnum.None };
    private readonly OptionButton _candidate = new() { Name = "AdvanceCandidate" };
    private readonly CheckButton _continue = new() { Name = "AdvanceContinueVictory", FocusMode = FocusModeEnum.None };
    private readonly ProgressBar _progress = new() { Name = "AdvanceProgress", ShowPercentage = true };
    private readonly Button _cancel = new() { Name = "AdvanceCancel" };
    private readonly VBoxContainer _advance;
    private readonly ParticipantId _human;
    private VotingPanel? _manualBallot;
    private bool _wasAdvancing;
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
        _tools = tools; _session = session; _human = human; _language = language; _refresh = refresh;
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
            _industry=new(){Name="GodIndustry"};_experienceCurrent=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
            var experienceInput=new SpinBox{Name="GodExperienceTarget",MinValue=0,MaxValue=1000000000000,Step=1,CustomMinimumSize=new(150,0)};
            edit.AddChild(_industry);foreach(var industry in development.Industries)_industry.AddItem(PlayerText.Building(language,industry.Id));edit.AddChild(_experienceCurrent);_industry.ItemSelected+=_=>RefreshData();
            Row(edit,L("Опыт отрасли, всего","Industry experience, total"),experienceInput,()=>_tools.IndustryExperience(Actor,development.Industries[_industry.Selected].Id,(long)experienceInput.Value),"GodExperienceApply");
            Button(edit,L("Прибавить указанное количество XP","Add the entered XP amount"),()=>Guard(()=>
            {
                string industry=development.Industries[_industry.Selected].Id;long current=_session.Engine.DevelopmentOf(Actor)!.Experience.Single(e=>e.Industry==industry).Total;
                _tools.IndustryExperience(Actor,industry,checked(current+(long)experienceInput.Value));
            }),"GodExperienceAdd");
        }
        _exponent.Value = session.Engine.EconomyRules.WageAttractionExponent;
        Row(edit, L("Привлекательность зарплаты (степень 1–5)", "Wage attraction (exponent 1–5)"), _exponent, () => _tools.WageExponent((int)_exponent.Value));
        edit.AddChild(new Label { Text = L(
            "Мир остаётся на паузе. Изменения записываются в аудит. Зарезервированные деньги и товары защищены, вместимость не увеличивается. Применение не продвигает время. Во время голосования изменения недоступны.",
            "The world stays paused. Changes are audited. Reserved money and stock remain protected; capacity is unchanged. Applying does not advance time. Edits are unavailable during voting."), AutowrapMode = TextServer.AutowrapMode.WordSmart });

        _advance = Tab(tabs, L("Перемотка", "Advance time"));
        _target.MaxValue = _count.MaxValue = session.Engine.Configuration().MaxTicks;
        _target.Value = session.Engine.Tick + 1000;
        _count.Value = 1000;
        var horizons = new HFlowContainer(); _advance.AddChild(horizons);
        foreach (int ticks in new[] { 1000, 3000, 10_000, 50_000 })
            Button(horizons, ticks.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(language)), () => _count.Value = ticks);
        _automatic.Text = L("Автоматическое голосование", "Automatic voting");
        _advance.AddChild(_automatic);
        foreach (var person in session.Engine.Configuration().Participants)
            _candidate.AddItem((person.Id == human.Value ? L("За себя · ", "For yourself · ") : "") +
                WorldSkins.ActorName(person.Id, language), checked((int)person.Id));
        _candidate.Select(_candidate.GetItemIndex(checked((int)human.Value)));
        _advance.AddChild(_candidate);
        _automatic.Toggled += _ => RefreshAdvance();
        _advance.AddChild(new Label { Text = L("Обязательный политический договор имеет приоритет над выбранным кандидатом.",
            "A binding political agreement takes priority over the selected candidate."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _continue.Text = L("Продолжать симуляцию после победы на выборах", "Continue simulation after an election victory");
        _advance.AddChild(_continue);
        AdvanceRow(L("До тика", "To tick"), _target, () => (long)_target.Value, "AdvanceStartTo");
        AdvanceRow(L("На N тиков", "By N ticks"), _count, () => checked(session.Engine.Tick + (long)_count.Value), "AdvanceStartBy");
        _advance.AddChild(_progress);
        _cancel.Text = L("Остановить перемотку", "Cancel advance");
        _cancel.Pressed += () => { _tools.CancelAdvance(); RefreshData(); };
        _advance.AddChild(_cancel);
        var journal = new VBoxContainer { Name = L("Решения NPC", "NPC decisions") }; tabs.AddChild(journal);
        journal.AddChild(_log); journal.AddChild(_detail);
        _log.ItemSelected += selected => ShowEntry((int)selected);
    }

    private void AdvanceRow(string label, Control input, Func<long> target, string name)
    {
        var row = new HFlowContainer(); _advance.AddChild(row);
        row.AddChild(new Label { Text = label }); row.AddChild(input);
        Button(row, L("Начать", "Start"), () =>
        {
            try
            {
                _tools.StartAdvance(target(), _automatic.ButtonPressed ? new ParticipantId(_candidate.GetSelectedId()) : null,
                    _continue.ButtonPressed);
                _notice = "";
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
            { _notice = L("Перемотка не запущена · ", "Advance did not start · ") + e.Message; }
            RefreshAdvance();
        }, name);
    }

    public void RefreshAdvance()
    {
        bool active = _tools.AdvanceTarget.HasValue;
        if (_wasAdvancing && !active) _automatic.SetPressedNoSignal(false);
        _wasAdvancing = active;
        _automatic.Disabled = _continue.Disabled = active || _session.Engine.TickLimitReached;
        _candidate.Disabled = active || !_automatic.ButtonPressed;
        _cancel.Disabled = !active;
        long tick = _session.Engine.Tick;
        long advanced = Math.Max(0, tick - (_tools.AdvanceStart ?? tick));
        long span = Math.Max(0, (_tools.AdvanceGoal ?? tick) - (_tools.AdvanceStart ?? tick));
        double speed = _tools.AdvanceElapsedSeconds > 0 ? advanced / _tools.AdvanceElapsedSeconds : 0;
        _progress.Value = span > 0 ? advanced * 100.0 / span : 0;
        string eta = active && speed > 0 && _tools.AdvanceStatus == "Running"
            ? TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds - 1, Math.Max(0, span - advanced) / speed)).ToString(@"d\.hh\:mm\:ss") : "—";
        string status = _tools.AdvanceStatus switch
        {
            "Running" => L("Идёт", "Running"), "Completed" => L("Цель достигнута", "Target reached"),
            "WaitingForBallot" => L("Ожидается ваш голос", "Waiting for your ballot"),
            "WaitingForModel" => L("Ожидается решение модели", "Waiting for the model"),
            "Victory" => L("Остановлено на победе", "Stopped at victory"),
            "Cancelled" => L("Остановлено", "Cancelled"), "Failed" => L("Прервано", "Failed"),
            _ => L("Готово к запуску", "Ready")
        };
        _status.Text = L("Старт ", "Start ") + (_tools.AdvanceStart?.ToString() ?? "—") + L(" · Тик ", " · Tick ") + tick +
            L(" · Цель ", " · Target ") + (_tools.AdvanceGoal?.ToString() ?? "—") + " · " + status + "\n" +
            $"{speed:0.0} " + L("тик/с · Осталось ", "ticks/s · Remaining ") + eta +
            L(" · Выборов ", " · Elections ") + _tools.AdvanceElections +
            (_tools.AdvanceError is { } reason ? "\n" + reason : "") + (_notice.Length > 0 ? "\n" + _notice : "");
        if (active && _tools.AdvanceStatus == "WaitingForBallot")
        {
            var election = _session.Engine.Observe(_human).Elections;
            if (_manualBallot is null)
            {
                _manualBallot = new VotingPanel(_language, election, candidate =>
                {
                    var voting = _session.Engine.Observe(_human).Elections.Voting!;
                    _session.Send(_human, new(ActionKind.SetVoteChoice, Election: voting.Election, Candidate: candidate));
                }, overlay: false) { CustomMinimumSize = new(0, 280) };
                _advance.AddChild(_manualBallot);
            }
            _manualBallot.ShowData(_human, election);
        }
        else if (_manualBallot is not null)
        { _advance.RemoveChild(_manualBallot); _manualBallot.QueueFree(); _manualBallot = null; }
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
                ? L("Изменение недоступно во время операции ядра; при выборах сначала проголосуйте.", "An engine operation is in progress; finish any pending vote first.")
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
        if (updateStatus) RefreshAdvance();
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
        if(_session.Engine.DevelopmentOf(Actor) is {} development&&_industry is {Selected:>=0}&&_experienceCurrent is not null)
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

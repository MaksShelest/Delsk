using System.Globalization;
using System.Text.Json;
using Agents.Algorithmic;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Минимальный клиент: человек шлёт те же команды, карточка делит работу, поставки и ставки.</summary>
public partial class Playfield : Control
{
    private GameSession _session = null!;
    private TickScheduler _scheduler = null!;
    private ScenarioDefinition _scenario = null!;
    private ParticipantId _human;
    private string _language = "ru";
    private string _mode = "economy";
    private string _feedback = "";
    private string _cardsSignature = "";
    private bool _suspended;
    private bool _unfocused;
    private bool _skipNextFrame;
    private bool _left;
    private bool _waitRelease = true;
    private TravelHeading _sent;
    private long _selectedEnterprise;
    private long _publicCursor;
    private GameAction? _pending;
    private string? _trackedId;
    private ActionKind? _trackedKind;
    private string _trackedItem = "";
    private string _gain = "";
    private int? _cellX;
    private int? _cellZ;
    private Surface _surface = Surface.Play;
    private SubViewportContainer? _view;
    private PresentationStage? _stage;
    private Control? _playChrome;
    private Control? _panel;
    private Control? _menu;
    private DebugBoard? _board;
    private Label _status = null!;
    private Label _feedbackLabel = null!;
    private Label? _inventoryLabel;
    private Label? _cellLabel;
    private Label? _playInventory;
    private Label? _playNotice;
    private Label? _tooltip;
    private VBoxContainer? _contextHost;
    private VBoxContainer? _debugContext;
    private Control? _playCard;
    private VBoxContainer? _playCards;
    private VBoxContainer _cards = null!;
    private ContextChoice[] _choices = [];
    private int _contextIndex;
    private string _contextKey = "";
    private bool _cardOpen;
    private bool _playFilled;
    private int? _hoverX;
    private int? _hoverZ;
    private Label? _fpsLabel;
    private double _frameSampleSeconds;
    private int _frameSampleCount;
    private double _worstFrame;
    private int _contextFirst;
    private string SavePath => ProjectSettings.GlobalizePath($"user://play-{_mode}-{SimulationEngine.RulesVersion}.json");

    private enum Surface { Play, Menu, Debug }

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
        {
            // _Ready ещё внутри add_child. Немедленный ChangeSceneToFile вызывает remove_child у занятого родителя.
            _left = true;
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://PlatformProbe.tscn");
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Engine.MaxFps = 60;
        Theme = PresentationTheme.Create();
        EnsureStage();
        Start(OS.GetCmdlineUserArgs().Contains("--trip") ? "trip" : "economy", false);
        BuildChrome();
        Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_left || _session is null || e is not InputEventKey key) return;
        if (!key.Pressed)
        {
            if (_waitRelease && !MovementHeld()) _waitRelease = false;
            return;
        }
        if (key.Echo) return;
        if (_surface is Surface.Play or Surface.Debug && key.Keycode is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (_choices.Length > 0)
                _contextIndex = ( _contextIndex + (key.Keycode is Key.Right or Key.Down ? 1 : _choices.Length - 1)) % _choices.Length;
            _contextKey = "";
            Refresh();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_surface is Surface.Play or Surface.Debug && key.Keycode is Key.Enter or Key.KpEnter or Key.Space)
        {
            ConfirmContext();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Escape)
        {
            if (_surface == Surface.Debug) OpenMenu();
            else if (_surface == Surface.Menu) ContinueGame();
            else OpenMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (_left || _session is null) return;
        try
        {
            _frameSampleSeconds += delta;
            _frameSampleCount++;
            _worstFrame = Math.Max(_worstFrame, delta);
            if (_frameSampleSeconds >= 1)
            {
                if (_fpsLabel is not null) _fpsLabel.Text = $"{_frameSampleCount / _frameSampleSeconds:0} FPS · max {_worstFrame * 1000:0.0} ms";
                if (OS.GetCmdlineUserArgs().Contains("--profile-frames"))
                    GD.Print(FormattableString.Invariant($"DELSK_FRAME fps={_frameSampleCount / _frameSampleSeconds:0.0} max_ms={_worstFrame * 1000:0.0} surface={_surface}"));
                _frameSampleSeconds = 0; _frameSampleCount = 0; _worstFrame = 0;
            }
            if (_skipNextFrame) { _skipNextFrame = false; return; }
            if (_waitRelease && !MovementHeld()) _waitRelease = false;
            SampleHeading();
            long prior = _session.Engine.Tick;
            int priorQuantum = _session.Engine.TravelQuantum;
            var priorTravel = _session.Engine.TravelOf(_human);
            _scheduler.AdvanceElapsed(TimeSpan.FromSeconds(Math.Min(delta, 0.25)));
            var travel = _session.Engine.TravelOf(_human);
            if (_session.Engine.Tick != prior || _session.Engine.TravelQuantum != priorQuantum || travel != priorTravel) Refresh();
        }
        catch (Exception failure) { _feedback = failure.Message; Refresh(); }
    }

    public override void _Notification(int what)
    {
        if (_left || _session is null) return;
        if (what == NotificationApplicationPaused) { _suspended = true; _waitRelease = true; StopHeading(); }
        else if (what == NotificationApplicationFocusOut) { _unfocused = true; _waitRelease = true; StopHeading(); }
        else if (what == NotificationApplicationResumed) { _suspended = false; _skipNextFrame = true; }
        else if (what == NotificationApplicationFocusIn) { _unfocused = false; _skipNextFrame = true; }
        else return;
        ApplyPause();
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut)
        { try { CheckpointStore.Save(_session, SavePath); } catch (Exception failure) { _feedback = failure.Message; } }
        Refresh();
    }

    private void Start(string mode, bool hold)
    {
        _mode = mode;
        _selectedEnterprise = 0;
        _pending = null;
        _trackedId = null;
        _trackedKind = null;
        _trackedItem = "";
        _cellX = null;
        _cellZ = null;
        _waitRelease = true;
        _sent = default;
        if (mode == "trip")
        {
            _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(Text("Play.logistics-cycle.json"), SimulationJson.Options)
                ?? throw new InvalidOperationException("Play.logistics-cycle.json");
            _human = new(1);
            _session = new GameSession(new SimulationEngine(_scenario));
            _scheduler = new TickScheduler(_session);
            _feedback = "";
            _cardsSignature = "";
            try { ScriptedScenarios.PrepareVisibleTrip(_session, _scenario); }
            catch (Exception failure) { _feedback = failure.Message; }
            _surface = Surface.Menu;
            CatchPublicCursor();
            _feedback = T("new.session") + " " + T("trip");
            ApplyPause();
            if (_status is not null) CallDeferred(nameof(RebuildChrome));
            return;
        }
        string scenarioName = mode == "politics" ? "Play.baseline-politics.json" : "Play.baseline-economy.json";
        string controllerName = mode == "politics" ? "Play.baseline-politics-controllers.json" : "Play.baseline-controllers.json";
        _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(Text(scenarioName), SimulationJson.Options)
            ?? throw new InvalidOperationException(scenarioName);
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(Text(controllerName), SimulationJson.Options)
            ?? throw new InvalidOperationException(controllerName);
        _human = new(config.HumanActorId);
        _session = new GameSession(new SimulationEngine(_scenario));
        _session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(
            n with { HumanActorId = config.HumanActorId },
            AlgorithmicController.RuntimeSeed(_scenario.Seed, n.ActorId),
            _scenario.Participants.Select(p => p.Id))));
        _scheduler = new TickScheduler(_session);
        _feedback = hold ? T("new.session") + " " + T(mode) : "";
        _cardsSignature = "";
        _surface = hold ? Surface.Menu : Surface.Play;
        CatchPublicCursor();
        ApplyPause();
        if (_status is not null) CallDeferred(nameof(RebuildChrome));
    }

    private void RebuildChrome()
    {
        if (_left || _session is null) return;
        BuildChrome();
        Refresh();
    }

    private void ApplyPause() => _scheduler.SetPaused(_surface != Surface.Play || _suspended || _unfocused);

    private void StepOne()
    {
        if (_surface != Surface.Debug) return;
        try
        {
            if (_pending is { } action)
            {
                Track(_session.Send(_human, action), action);
                _pending = null;
            }
            _scheduler.SetPaused(false);
            _session.CompleteTravelTick();
        }
        catch (Exception failure) { _feedback = failure.Message; }
        ApplyPause();
        Refresh();
    }

    private void Select(GameAction action)
    {
        _pending = action;
        _feedback = T("command.selected");
        Refresh();
    }

    private void Offer(GameAction action)
    {
        if (_surface == Surface.Play) Submit(action);
        else Select(action);
    }

    private void ConfirmContext()
    {
        if (_choices.Length == 0) return;
        var current = PlayContext.At(PlayerDesk.Read(_session.Engine, _human), _scenario, _language);
        var choice = _choices[Math.Clamp(_contextIndex, 0, _choices.Length - 1)];
        var valid = current.FirstOrDefault(c => c.Key == choice.Key);
        if (valid is null) { Refresh(); return; }
        choice = valid;
        if (!choice.Enabled) { _feedback = choice.Details; _gain = ""; _trackedId = null; Refresh(); return; }
        if (choice.Key.StartsWith("card:", StringComparison.Ordinal))
        {
            _selectedEnterprise = long.Parse(choice.Key["card:".Length..], CultureInfo.InvariantCulture);
            _cardOpen = true;
            _playFilled = false;
            _cardsSignature = "";
            Refresh();
            return;
        }
        if (choice.Command is { } action) Offer(action);
    }

    private void RebuildContext()
    {
        if (_contextHost is not null) FillContext(_contextHost);
        if (_debugContext is not null) FillContext(_debugContext);
    }

    private void FillContext(VBoxContainer host)
    {
        foreach (Node child in host.GetChildren()) child.QueueFree();
        _contextFirst = Math.Clamp(_contextIndex - 1, 0, Math.Max(0, _choices.Length - 3));
        for (int i = _contextFirst; i < Math.Min(_choices.Length, _contextFirst + 3); i++)
        {
            int index = i;
            var button = new Button
            {
                Text = (i == _contextIndex ? "› " : "") + _choices[i].Label,
                TooltipText = _choices[i].Label + "\n" + _choices[i].Details,
                Disabled = !_choices[i].Enabled,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            button.Pressed += () =>
            {
                _contextIndex = index;
                ConfirmContext();
            };
            host.AddChild(button);
        }
            if (_choices.Length > 0)
            host.AddChild(new Label { Text = $"{_contextIndex + 1}/{_choices.Length} · " +
                (_surface == Surface.Debug
                    ? PlayerText.Local(_language, "↑↓ выбор · Enter / Space выбрать, затем Один тик · ", "↑↓ select · Enter / Space queue, then One tick · ")
                    : PlayerText.Local(_language, "↑↓ выбор · Enter / Space подтвердить · ", "↑↓ select · Enter / Space confirm · ")) + _choices[_contextIndex].Details,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
    }

    private void Submit(GameAction action)
    {
        try
        {
            Track(_session.Send(_human, PlayerDesk.Check(action)), action);
            _pending = null;
            _feedback = T("command.sent");
        }
        catch (Exception failure) { _feedback = failure.Message; }
        Refresh();
    }

    private void Track(string id, GameAction action)
    {
        _trackedId = id;
        _trackedKind = action.Kind;
        _trackedItem = action.ItemId;
        _gain = "";
    }

    private void Refresh()
    {
        if (_status is null) return;
        var view = PlayerDesk.Read(_session.Engine, _human);
        var actor = view.Participant;
        var travelOffset = _session.Engine.TravelOf(_human);
        bool paused = _surface != Surface.Play || _suspended || _unfocused;
        var frame = Presentation.Read(_scenario, _session.Engine, _human.Value);
        if (_surface == Surface.Play || paused) _stage?.Show(frame, paused, _selectedEnterprise, T("you"), _surface == Surface.Play);
        if (_surface == Surface.Debug) _board?.Show(frame, _cellX, _cellZ);
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        string stock = pack.Stock.Length == 0 ? T("inventory.empty")
            : string.Join(", ", pack.Stock.OrderBy(s => s.ItemId, StringComparer.Ordinal).Select(s => $"{PlayerText.Item(_language, s.ItemId)} ×{s.Quantity}"));
        string inventory = $"{T("inventory.title")}: {stock}\n{T("inventory.capacity")} {pack.OccupiedVolume}/{pack.Capacity}";
        if (_inventoryLabel is not null) _inventoryLabel.Text = inventory;
        if (_playInventory is not null) _playInventory.Text = HudStock(view, pack);
        var standing = view.Buildings.FirstOrDefault(b => b.Position == actor.Position && b.Kind == BuildingKind.Production);
        if (_cardOpen && (standing is null || standing.Id.Value != _selectedEnterprise)) { _cardOpen = false; _playFilled = false; }
        if (_playCard is not null) _playCard.Visible = _cardOpen;
        var next = PlayContext.At(view, _scenario, _language);
        _contextIndex = PlayContext.Keep(_contextIndex, _choices, next);
        _choices = next;
        string contextKey = _contextIndex.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", next.Select(c => $"{c.Key}:{c.Label}:{c.Enabled}:{c.Details}"));
        if (contextKey != _contextKey) { _contextKey = contextKey; RebuildContext(); }
        if (_tooltip is not null)
            _tooltip.Text = _hoverX is int hx && _hoverZ is int hz
                ? PlayContext.Hover(frame, view, hx, hz, _language)
                : PlayContext.Hover(frame, view, actor.Position.X, actor.Position.Z, _language);
        PollHarvest();
        _feedback = Describe(view);
        _status.Text = $"{T(paused ? "paused" : "running")}  {T("tick")}: {_session.Engine.Tick}\n" +
            $"{T("position")}: ({actor.Position.X}, {actor.Position.Z}) +({travelOffset.OffsetX}, {travelOffset.OffsetZ})  {T("busy")}: {T(actor.Busy ? "yes" : "no")}\n" +
            $"{T("npc.title")}: " + string.Join("; ", view.LaborMarket.Where(g => g.OwnerId != _human).Select(g => $"{g.OwnerId.Value} {g.Role} {g.Headcount}/{g.Capacity}"));
        _feedbackLabel.Text = _feedback;
        if (_playNotice is not null) _playNotice.Text = _feedback;
        if (_cellLabel is not null) _cellLabel.Text = CellText(frame);
        string signature = _selectedEnterprise + "|" + CardSignature(view);
        if (signature != _cardsSignature)
        {
            _cardsSignature = signature;
            FillCards(view, _cards);
            _playFilled = false;
        }
        if (_cardOpen && !_playFilled && _playCards is not null)
        {
            FillCards(view, _playCards);
            _playFilled = true;
        }
    }

    private string HudStock(PlayerView view, InventoryView pack)
    {
        var ids = _scenario.ResourceNodes.Select(n => n.ItemId).Concat(pack.Stock.Select(s => s.ItemId))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        string lines = string.Join("  ", ids.Select(id =>
        {
            long quantity = pack.Stock.Where(s => s.ItemId == id).Sum(s => s.Quantity);
            return PlayerText.Item(_language, id) + " ×" + quantity.ToString(CultureInfo.InvariantCulture);
        }));
        return $"{T("money")} {PlayerText.Money(view.CashMinor)}\n{lines}\n{T("inventory.capacity")} {pack.OccupiedVolume}/{pack.Capacity}";
    }

    private static string CardSignature(PlayerView view) =>
        string.Join(";", view.Enterprises.Select(e => $"{e.Id.Value}:{e.Enabled}:{e.ReasonCode}:{e.RecipeId}")) + "|" +
        string.Join(";", view.Employment.Select(g => $"{g.Id.Value}:{g.Role}:{g.Headcount}:{g.WageMinor}:{g.PendingWageMinor}")) + "|" +
        string.Join(";", view.Logistics.Select(l => $"{l.Id.Value}:{l.EnterpriseId.Value}:{l.Enabled}:{l.InboundEnabled}:{l.ReasonCode}")) + "|" +
        string.Join(";", view.Buildings.Select(b => b.Id.Value)) + "|" +
        string.Join(";", view.Inventories.Select(i => $"{i.Id}:{i.OccupiedVolume}:" + string.Join(",", i.Stock.Select(s => $"{s.ItemId}:{s.Quantity}:{s.Reserved}")))) + "|" +
        string.Join(";", view.Support.Proposals.Select(p => $"{p.Id}:{p.Status}")) + "|" +
        view.Elections.NextElectionId?.Value + ":" + view.Elections.OwnChoice?.Value;

    private void FillCards(PlayerView view, Container host)
    {
        foreach (Node child in host.GetChildren()) child.QueueFree();
        if (view.Enterprises.Length == 0) host.AddChild(new Label { Text = T("no.enterprise") });
        foreach (var enterprise in view.Enterprises)
        {
            var card = new VBoxContainer();
            host.AddChild(card);
            card.AddChild(new Label { Text = (enterprise.Id.Value == _selectedEnterprise ? T("selected") + " " : "") + $"#{enterprise.Id.Value}  {enterprise.RecipeId}" });
            var work = Section("card.work");
            card.AddChild(work);
            work.AddChild(new Label { Text = $"{enterprise.ReasonCode}  {enterprise.Staff}/{enterprise.StaffCapacity}" });
            foreach (var inventory in view.Inventories.Where(i => i.Id == enterprise.InputId || i.Id == enterprise.OutputId))
            {
                string goods = string.Join(", ", inventory.Stock.Select(s => PlayerText.Item(_language, s.ItemId) + $" ×{s.Quantity}"));
                work.AddChild(new Label { Text = PlayerText.Local(_language, inventory.Id == enterprise.InputId ? "Вход: " : "Выход: ", inventory.Id == enterprise.InputId ? "Input: " : "Output: ") +
                    (goods.Length == 0 ? T("inventory.empty") : goods) + $" · {inventory.OccupiedVolume}/{inventory.Capacity}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            }
            if (enterprise.Staff == 0) work.AddChild(new Label { Text = PlayerText.Local(_language, "Для найма включите работу, внесите сырьё и обеспечьте зарплату. Найм постепенный.", "Enable production, supply inputs and afford wages. Hiring is gradual."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            Button(work, enterprise.Enabled ? "work.stop" : "work.start", () => Offer(PlayerDesk.Work(enterprise.Id.Value, !enterprise.Enabled)));
            var recipe = _scenario.Recipes.Single(r => r.Id == enterprise.RecipeId);
            Button(work, "transfer.input", () => Offer(PlayerDesk.Transfer(view.Participant.InventoryId.Value, enterprise.InputId.Value, recipe.InputItem, 1)));
            foreach (var stock in view.Inventories.Single(i => i.Id == enterprise.OutputId).Stock.Where(s => s.Quantity > s.Reserved))
            {
                var take = new Button { Text = PlayerText.Local(_language, "Забрать ", "Take ") + PlayerText.Item(_language, stock.ItemId) + " ×1", FocusMode = FocusModeEnum.None };
                string item = stock.ItemId;
                take.Pressed += () => Offer(PlayerDesk.Transfer(enterprise.OutputId.Value, view.Participant.InventoryId.Value, item, 1));
                work.AddChild(take);
            }
            var wages = Section("card.wages");
            card.AddChild(wages);
            var production = view.Employment.Single(g => g.Role == EmploymentRole.Production && g.Id.Value == enterprise.Id.Value);
            WageRow(wages, production);
            var supplies = Section("card.supplies");
            card.AddChild(supplies);
            var logistics = view.Logistics.SingleOrDefault(l => l.EnterpriseId == enterprise.Id);
            if (logistics is null) Button(supplies, "supplies.buy", () => Offer(PlayerDesk.BuyLogistics(enterprise.Id.Value)));
            else
            {
                supplies.AddChild(new Label { Text = logistics.ReasonCode });
                Button(supplies, logistics.Enabled ? "supplies.output.off" : "supplies.output.on",
                    () => Offer(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Output, !logistics.Enabled)));
                Button(supplies, logistics.InboundEnabled ? "supplies.inbound.off" : "supplies.inbound.on",
                    () => Offer(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Inbound, !logistics.InboundEnabled)));
                var crew = view.Employment.Single(g => g.Id == logistics.GroupId);
                WageRow(wages, crew);
                foreach (var building in view.Buildings.Where(b => b.Kind is BuildingKind.Warehouse or BuildingKind.Shop))
                {
                    Button(supplies, "supplies.send", () => Offer(PlayerDesk.SendProduct(logistics.Id.Value, building.InputId.Value, true)));
                    Button(supplies, "supplies.fetch", () => Offer(PlayerDesk.FetchInput(logistics.Id.Value, building.InputId.Value, true)));
                }
            }
        }
        host.AddChild(new Label { Text = T("support.title") });
        if (view.Support.Proposals.Length == 0) host.AddChild(new Label { Text = T("support.none") });
        foreach (var proposal in view.Support.Proposals)
        {
            var row = new HBoxContainer();
            host.AddChild(row);
            string terms = string.Join(", ", proposal.Compensation.Select(m => $"{m.Quantity} {m.ItemId}"));
            row.AddChild(new Label { Text = $"#{proposal.Id} {proposal.Status} {proposal.Candidate.Value}->{proposal.Seller.Value} {terms}" });
            if (proposal.Seller == _human && proposal.Status == SupportProposalStatus.Proposed)
                Button(row, "support.accept", () => Offer(PlayerDesk.AcceptSupport(proposal.Id)));
            if (proposal.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)
                Button(row, "support.reject", () => Offer(PlayerDesk.RejectSupport(proposal.Id)));
        }
        if (view.Elections.NextElectionId is { } election)
        {
            host.AddChild(new Label { Text = $"{T("vote.title")} #{election.Value}  {view.Elections.OwnChoice?.Value.ToString() ?? T("no")}" });
            var votes = new HBoxContainer();
            host.AddChild(votes);
            foreach (var candidate in view.Elections.Participants.Where(p => p != _human))
                Button(votes, "vote.choose", () => Offer(PlayerDesk.Vote(election.Value, candidate.Value)), candidate.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WageRow(Container parent, EmploymentView group)
    {
        long shown = group.PendingWageMinor ?? group.WageMinor;
        parent.AddChild(new Label { Text = $"{group.Role} {group.Headcount}/{group.Capacity} {PlayerText.Money(shown)}" });
        var row = new HBoxContainer();
        parent.AddChild(row);
        Button(row, "wage.raise", () => Offer(PlayerDesk.Wage(group.Id.Value, shown + PlayerDesk.WageStepMinor)));
        Button(row, "wage.lower", () => Offer(PlayerDesk.Wage(group.Id.Value, Math.Max(0, shown - PlayerDesk.WageStepMinor))));
    }

    private VBoxContainer Section(string key)
    {
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = T(key) });
        return box;
    }

    private void EnsureStage()
    {
        if (_stage is not null) return;
        _view = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Stop };
        _view.SetAnchorsPreset(LayoutPreset.FullRect);
        _view.OffsetTop = 108;
        _view.OffsetBottom = -205;
        var viewport = new SubViewport
        {
            Size = new Vector2I(1100, 720),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
            GuiDisableInput = true
        };
        _stage = new PresentationStage();
        _stage.CellChosen += OnCell;
        viewport.AddChild(_stage);
        _view.AddChild(viewport);
        _view.GuiInput += OnViewInput;
        AddChild(_view);
        MoveChild(_view, 0);
    }

    private void OnViewInput(InputEvent e)
    {
        if (_surface != Surface.Play || _stage is null || _view is null) return;
        Vector2 position = e switch
        {
            InputEventMouseButton button when button.Pressed && button.ButtonIndex == MouseButton.Left => button.Position,
            InputEventMouseMotion motion => motion.Position,
            _ => new Vector2(-1, -1)
        };
        if (position.X < 0) return;
        var viewport = (SubViewport)_stage.GetParent();
        var size = _view.Size;
        if (size.X < 1 || size.Y < 1) return;
        _stage.Pick(new Vector2(position.X / size.X * viewport.Size.X, position.Y / size.Y * viewport.Size.Y));
        if (e is InputEventMouseButton) GetViewport().SetInputAsHandled();
    }

    private void OnCell(int x, int z)
    {
        if (_surface != Surface.Play || x < 0 || z < 0 || x >= _scenario.MapWidth || z >= _scenario.MapHeight) return;
        _hoverX = x;
        _hoverZ = z;
        Refresh();
    }

    private void BuildChrome()
    {
        _playChrome?.QueueFree();
        _panel?.QueueFree();
        _menu?.QueueFree();
        EnsureStage();
        _playChrome = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _playChrome.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_playChrome);
        var bar = new ColorRect { Color = new Color(0.14f, 0.20f, 0.22f), MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 108;
        _playChrome.AddChild(bar);
        var settings = new Button { Text = T("menu.settings"), FocusMode = FocusModeEnum.None };
        settings.Position = new Vector2(8, 8);
        settings.Pressed += OpenMenu;
        bar.AddChild(settings);
        var language = new Button { Text = T("language"), FocusMode = FocusModeEnum.None };
        language.Position = new Vector2(150, 8);
        language.Pressed += () => { _language = _language == "ru" ? "en" : "ru"; CallDeferred(nameof(RebuildChrome)); };
        bar.AddChild(language);
        _playInventory = new Label
        {
            Position = new Vector2(310, 6),
            Size = new Vector2(770, 64),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        bar.AddChild(_playInventory);
        _playInventory.SetAnchorsPreset(LayoutPreset.TopRight);
        _playInventory.OffsetLeft = -700; _playInventory.OffsetRight = -14;
        _playInventory.OffsetTop = 6; _playInventory.OffsetBottom = 68;
        _playInventory.HorizontalAlignment = HorizontalAlignment.Right;
        _playNotice = new Label
        {
            Position = new Vector2(8, 68),
            Size = new Vector2(790, 36),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        bar.AddChild(_playNotice);
        _fpsLabel = new Label { Position = new Vector2(810, 78), Size = new Vector2(275, 22), HorizontalAlignment = HorizontalAlignment.Right };
        bar.AddChild(_fpsLabel);
        var bottom = new ColorRect { Color = new Color(0.14f, 0.20f, 0.22f), MouseFilter = MouseFilterEnum.Stop };
        bottom.SetAnchorsPreset(LayoutPreset.BottomWide);
        bottom.OffsetTop = -205;
        _playChrome.AddChild(bottom);
        _tooltip = new Label
        {
            Position = new Vector2(8, 4),
            Size = new Vector2(1020, 48),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        bottom.AddChild(_tooltip);
        _contextHost = new VBoxContainer { Position = new Vector2(8, 56), Size = new Vector2(1020, 149) };
        bottom.AddChild(_contextHost);
        var paging = new VBoxContainer { Position = new Vector2(1040, 56) };
        bottom.AddChild(paging);
        foreach (int direction in new[] { -1, 1 })
        {
            int step = direction;
            var button = new Button { Text = step < 0 ? "↑" : "↓", FocusMode = FocusModeEnum.None };
            button.Pressed += () =>
            {
                if (_choices.Length == 0) return;
                _contextIndex = (_contextIndex + step + _choices.Length) % _choices.Length;
                _contextKey = "";
                Refresh();
            };
            paging.AddChild(button);
        }
        _playCard = new ColorRect { Color = new Color(0.20f, 0.28f, 0.30f), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _playCard.SetAnchorsPreset(LayoutPreset.RightWide);
        _playCard.OffsetLeft = -360;
        _playCard.OffsetTop = 116;
        _playCard.OffsetBottom = -213;
        _playChrome.AddChild(_playCard);
        var cardScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Stop };
        cardScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _playCard.AddChild(cardScroll);
        _playCards = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cardScroll.AddChild(_playCards);
        _contextKey = "";
        _choices = [];
        _cardOpen = false;
        _playFilled = false;

        _panel = new ColorRect { Color = new Color("26383d"), MouseFilter = MouseFilterEnum.Stop };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_panel);
        var split = new VSplitContainer { SplitOffsets = [425] };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.OffsetLeft = 10; split.OffsetRight = -10; split.OffsetTop = 8; split.OffsetBottom = -8;
        _panel.AddChild(split);
        var mapColumn = new VBoxContainer { CustomMinimumSize = new Vector2(0, 230), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(mapColumn);
        var mapToolbar = new HBoxContainer();
        mapColumn.AddChild(mapToolbar);
        mapToolbar.AddChild(new Label { Text = PlayerText.Local(_language, "Карта · пошаговый режим", "Map · step mode"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var legendToggle = new Button { Text = PlayerText.Local(_language, "Легенда ▸", "Legend ▸"), FocusMode = FocusModeEnum.None };
        mapToolbar.AddChild(legendToggle);
        var mapRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        mapColumn.AddChild(mapRow);
        _board = new DebugBoard { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _board.CellChosen += OnDebugCell;
        mapRow.AddChild(_board);
        var legendScroll = new ScrollContainer { Visible = false, CustomMinimumSize = new Vector2(290, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        mapRow.AddChild(legendScroll);
        legendToggle.Pressed += () =>
        {
            legendScroll.Visible = !legendScroll.Visible;
            legendToggle.Text = PlayerText.Local(_language, "Легенда ", "Legend ") + (legendScroll.Visible ? "▾" : "▸");
        };
        var legend = new GridContainer { Columns = 2 };
        legendScroll.AddChild(legend);
        foreach (var entry in new[]
        {
            ("player", "", "Вы", "You"), ("npc", "2", "Участник NPC 2", "NPC participant 2"),
            ("npc", "3", "Участник NPC 3", "NPC participant 3"), ("npc", "4", "Участник NPC 4", "NPC participant 4"),
            ("resource", "wood", "Древесина", "Wood"), ("resource", "grain", "Зерно", "Grain"),
            ("plot-free", "", "Свободный участок", "Free plot"), ("plot-own", "", "Ваш участок", "Your plot"),
            ("plot-foreign", "", "Чужой участок", "Other participant's plot"), ("plot-reserved", "", "Бронь", "Reserved plot"),
            ("shop-plot", "", "Место лавочки", "Assigned shop site"), ("corridor", "", "Проход", "Protected passage"),
            ("blocked", "", "Непроходимо", "Blocked"), ("production", "", "Предприятие", "Production"),
            ("warehouse", "", "Склад", "Warehouse"), ("shop", "", "Лавочка", "Shop"),
            ("capital", "", "Терминал столицы", "Capital terminal"), ("cargo", "", "Груз в пути", "Cargo in transit")
        })
        {
            legend.AddChild(new LegendIcon { Kind = entry.Item1, Item = entry.Item2, CustomMinimumSize = new Vector2(34, 32) });
            legend.AddChild(new Label { Text = PlayerText.Local(_language, entry.Item3, entry.Item4), VerticalAlignment = VerticalAlignment.Center });
        }

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 210), MouseFilter = MouseFilterEnum.Stop };
        split.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        var controls = new HBoxContainer();
        column.AddChild(controls);
        Button(controls, "step", StepOne);
        Button(controls, "save", () => { CheckpointStore.Save(_session, SavePath); _feedback = T("save"); Refresh(); });
        Button(controls, "load", Load);
        Button(controls, "language", () => { _language = _language == "ru" ? "en" : "ru"; CallDeferred(nameof(RebuildChrome)); });
        controls.AddChild(new VSeparator());
        controls.AddChild(new Label { Text = T("new.session"), VerticalAlignment = VerticalAlignment.Center });
        Button(controls, "economy", () => Start("economy", true));
        Button(controls, "politics", () => Start("politics", true));
        Button(controls, "trip", () => Start("trip", true));
        var stateRow = new HBoxContainer();
        column.AddChild(stateRow);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stateRow.AddChild(_status);
        _inventoryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stateRow.AddChild(_inventoryLabel);
        _cellLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_cellLabel);
        _feedbackLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_feedbackLabel);
        var moves = new HBoxContainer();
        column.AddChild(moves);
        Button(moves, "move.north", () => Move(0, -1));
        Button(moves, "move.south", () => Move(0, 1));
        Button(moves, "move.west", () => Move(-1, 0));
        Button(moves, "move.east", () => Move(1, 0));
        foreach (string item in _scenario.ResourceNodes.Select(n => n.ItemId).Distinct(StringComparer.Ordinal))
            Button(moves, "harvest", () => Select(PlayerDesk.Harvest(item, 1)), PlayerText.Item(_language, item));
        moves.AddChild(new Label { Text = PlayerText.Local(_language, "Выбор команды → Один тик", "Select action → One tick"), VerticalAlignment = VerticalAlignment.Center });
        _debugContext = new VBoxContainer();
        column.AddChild(_debugContext);
        _cards = new VBoxContainer();
        column.AddChild(_cards);
        _cardsSignature = "";

        _menu = new ColorRect { Color = new Color(0.15f, 0.20f, 0.23f, 0.97f), MouseFilter = MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        _menu.AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        center.AddChild(box);
        box.AddChild(new Label { Text = T("menu.title"), HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label { Text = T("menu.escape"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Button(box, "menu.continue", ContinueGame);
        Button(box, "menu.debug", OpenDebug);
        Button(box, "language", () => { _language = _language == "ru" ? "en" : "ru"; CallDeferred(nameof(RebuildChrome)); });
        Button(box, "menu.exit", ExitGame);
        ApplySurface();
    }

    private void ApplySurface()
    {
        if (_view is not null) _view.Visible = _surface == Surface.Play;
        if (_stage?.GetParent() is SubViewport viewport)
            viewport.RenderTargetUpdateMode = _surface == Surface.Play ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (_playChrome is not null) _playChrome.Visible = _surface == Surface.Play;
        if (_panel is not null) _panel.Visible = _surface == Surface.Debug;
        if (_menu is not null) _menu.Visible = _surface == Surface.Menu;
    }

    private void OpenMenu()
    {
        _surface = Surface.Menu;
        _waitRelease = true;
        StopHeading();
        ApplyPause();
        ApplySurface();
        Refresh();
    }

    private void OpenDebug()
    {
        _surface = Surface.Debug;
        _waitRelease = true;
        StopHeading();
        ApplyPause();
        ApplySurface();
        Refresh();
    }

    private void ContinueGame()
    {
        _surface = Surface.Play;
        _waitRelease = true;
        ApplyPause();
        ApplySurface();
        Refresh();
    }

    private void ExitGame()
    {
        try { CheckpointStore.Save(_session, SavePath); }
        catch (Exception failure) { _feedback = failure.Message; Refresh(); return; }
        GetTree().Quit();
    }

    private void OnDebugCell(int x, int z)
    {
        _cellX = x;
        _cellZ = z;
        Refresh();
    }

    private void Move(int dx, int dz)
    {
        var actor = PlayerDesk.Read(_session.Engine, _human).Participant;
        int x = Math.Clamp(actor.Position.X + dx, 0, _scenario.MapWidth - 1);
        int z = Math.Clamp(actor.Position.Z + dz, 0, _scenario.MapHeight - 1);
        var action = PlayerDesk.Move(x, z);
        if (_surface == Surface.Debug) Select(action);
        else Submit(action);
    }

    private void Load()
    {
        var loaded = CheckpointStore.Load(SavePath);
        _stage?.ResetMotion();
        _session = loaded.Session;
        _session.RestoreControllers(AlgorithmicController.Restore);
        _scheduler = new TickScheduler(_session);
        _pending = null;
        _trackedId = null;
        _trackedKind = null;
        _surface = Surface.Menu;
        _waitRelease = true;
        _sent = default;
        StopHeading();
        CatchPublicCursor();
        ApplyPause();
        _feedback = T("load");
        _cardsSignature = "";
        _contextKey = "";
        _choices = [];
        _cardOpen = false;
        _playFilled = false;
        _hoverX = null;
        _hoverZ = null;
        ApplySurface();
        Refresh();
    }

    private void CatchPublicCursor()
    {
        var events = _session.Engine.PublicEventsSince(0);
        _publicCursor = events.Length == 0 ? 0 : events[^1].Sequence;
    }

    private void PollHarvest()
    {
        var events = _session.Engine.PublicEventsSince(_publicCursor);
        if (events.Length == 0) return;
        _publicCursor = events[^1].Sequence;
        if (PlayerText.OwnHarvest(events, _human.Value, _language) is { } gain) _gain = gain;
        foreach (var done in events.Where(e => e.ActorId?.Value == _human.Value))
        {
            if (done.Type == "CapitalSaleSettled")
                _gain = PlayerText.Local(_language, "Продано: ", "Sold: ") + PlayerText.Item(_language, done.ItemId) + $" ×{done.Quantity} · +{PlayerText.Money(done.AmountMinor)}";
            if (done.Type == "BuildingConstructed")
            {
                var materials = _scenario.BuildingTypes?.FirstOrDefault(t => t.Id == done.ReasonCode)?.Levels.Single(l => l.Level == 1).Materials;
                _gain = PlayerText.Local(_language, "Построено: ", "Built: ") + PlayerText.Building(_language, done.ReasonCode) +
                    $" · -{PlayerText.Money(done.AmountMinor)}" +
                    (materials is null ? "" : " · " + string.Join(", ", materials.Select(m => "-" + PlayerText.Item(_language, m.ItemId) + " ×" + m.Quantity)));
            }
        }
    }

    private string Describe(PlayerView view)
    {
        if (_pending is not null) return T("command.selected");
        if (_trackedId is null) return _gain.Length == 0 ? _feedback : _gain;
        var result = _session.Engine.Result(_trackedId);
        if (result is null) return T("command.sent");
        if (result.Status == ResultStatus.Rejected) return T("command.rejected") + " " + PlayerText.Reason(_language, result.ReasonCode);
        if (_trackedKind == ActionKind.SellAvailableToCapital && view.Participant.Busy)
            return PlayerText.Local(_language, "Заявка на продажу принята. Ожидаем закрытия торгового окна.", "Sale request accepted. Waiting for the trading window to close.");
        if (view.Participant.Busy && view.Participant.ArrivalTick is long end)
        {
            long left = Math.Max(0, end - _session.Engine.Tick);
            return _trackedKind == ActionKind.Harvest
                ? PlayerText.HarvestLeft(_language, _trackedItem, left)
                : string.Format(CultureInfo.InvariantCulture, T("command.running"), left);
        }
        return _gain.Length == 0 ? T("command.done") : _gain;
    }

    private string CellText(PresentationFrame frame)
    {
        if (_cellX is not int x || _cellZ is not int z) return "";
        var markers = frame.Markers.Where(m => m.X == x && m.Z == z).Select(m => m.Label.Length == 0 ? m.Kind : $"{m.Kind}: {m.Label}");
        var actors = frame.Actors.Where(a => a.X == x && a.Z == z).Select(a => a.Human ? T("you") : a.Id.ToString(CultureInfo.InvariantCulture));
        return $"{T("cell.selected")} ({x}, {z}): " + string.Join("; ", actors.Concat(markers));
    }

    private void SampleHeading()
    {
        if (_surface != Surface.Play || _waitRelease || _suspended || _unfocused) return;
        var heading = TravelHeading.FromKeys(
            Input.IsPhysicalKeyPressed(Key.D), Input.IsPhysicalKeyPressed(Key.A),
            Input.IsPhysicalKeyPressed(Key.S), Input.IsPhysicalKeyPressed(Key.W));
        if (heading == _sent) return;
        _sent = heading;
        _session.SetTravelHeading(_human, heading.X, heading.Z);
    }

    private void StopHeading()
    {
        _sent = default;
        if (_session is null) return;
        var travel = _session.Engine.TravelOf(_human);
        if (travel.HeadingX == 0 && travel.HeadingZ == 0) return;
        _session.SetTravelHeading(_human, 0, 0);
    }

    private static bool MovementHeld() =>
        Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.D);

    private void Button(Container parent, string key, Action action, string suffix = "")
    {
        var button = new Button { Text = suffix.Length == 0 ? T(key) : $"{T(key)} {suffix}" };
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        button.FocusMode = FocusModeEnum.None;
        button.Pressed += () => { try { action(); } catch (Exception failure) { _feedback = failure.Message; Refresh(); } };
        parent.AddChild(button);
    }

    private string T(string key) => PlayerText.Get(_language, key);

    private static string Text(string name)
    {
        using var stream = typeof(Playfield).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

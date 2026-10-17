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
    private VBoxContainer _cards = null!;
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
        _stage?.Show(frame, paused, _selectedEnterprise, T("you"), _surface == Surface.Play);
        _board?.Show(frame, _cellX, _cellZ);
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        string stock = pack.Stock.Length == 0 ? T("inventory.empty")
            : string.Join(", ", pack.Stock.OrderBy(s => s.ItemId, StringComparer.Ordinal).Select(s => $"{s.ItemId} ×{s.Quantity}"));
        string inventory = $"{T("inventory.title")}: {stock}\n{T("inventory.capacity")} {pack.OccupiedVolume}/{pack.Capacity}";
        if (_inventoryLabel is not null) _inventoryLabel.Text = inventory;
        if (_playInventory is not null) _playInventory.Text = inventory;
        PollHarvest();
        _feedback = Describe(view);
        _status.Text = $"{T(paused ? "paused" : "running")}  {T("tick")}: {_session.Engine.Tick}\n" +
            $"{T("position")}: ({actor.Position.X}, {actor.Position.Z}) +({travelOffset.OffsetX}, {travelOffset.OffsetZ})  {T("busy")}: {T(actor.Busy ? "yes" : "no")}\n" +
            $"{T("npc.title")}: " + string.Join("; ", view.LaborMarket.Where(g => g.OwnerId != _human).Select(g => $"{g.OwnerId.Value} {g.Role} {g.Headcount}/{g.Capacity}"));
        _feedbackLabel.Text = _feedback;
        if (_playNotice is not null) _playNotice.Text = _feedback;
        if (_cellLabel is not null) _cellLabel.Text = CellText(frame);
        string signature = _selectedEnterprise + "|" + CardSignature(view);
        if (signature != _cardsSignature) { _cardsSignature = signature; FillCards(view); }
    }

    private static string CardSignature(PlayerView view) =>
        string.Join(";", view.Enterprises.Select(e => $"{e.Id.Value}:{e.Enabled}:{e.ReasonCode}:{e.RecipeId}")) + "|" +
        string.Join(";", view.Employment.Select(g => $"{g.Id.Value}:{g.Role}:{g.Headcount}:{g.WageMinor}:{g.PendingWageMinor}")) + "|" +
        string.Join(";", view.Logistics.Select(l => $"{l.Id.Value}:{l.EnterpriseId.Value}:{l.Enabled}:{l.InboundEnabled}:{l.ReasonCode}")) + "|" +
        string.Join(";", view.Buildings.Select(b => b.Id.Value)) + "|" +
        string.Join(";", view.Support.Proposals.Select(p => $"{p.Id}:{p.Status}")) + "|" +
        view.Elections.NextElectionId?.Value + ":" + view.Elections.OwnChoice?.Value;

    private void FillCards(PlayerView view)
    {
        foreach (Node child in _cards.GetChildren()) child.QueueFree();
        if (view.Enterprises.Length == 0) _cards.AddChild(new Label { Text = T("no.enterprise") });
        foreach (var enterprise in view.Enterprises)
        {
            var card = new VBoxContainer();
            _cards.AddChild(card);
            card.AddChild(new Label { Text = (enterprise.Id.Value == _selectedEnterprise ? T("selected") + " " : "") + $"#{enterprise.Id.Value}  {enterprise.RecipeId}" });
            var work = Section("card.work");
            card.AddChild(work);
            work.AddChild(new Label { Text = $"{enterprise.ReasonCode}  {enterprise.Staff}/{enterprise.StaffCapacity}" });
            Button(work, enterprise.Enabled ? "work.stop" : "work.start", () => Select(PlayerDesk.Work(enterprise.Id.Value, !enterprise.Enabled)));
            var recipe = _scenario.Recipes.Single(r => r.Id == enterprise.RecipeId);
            Button(work, "transfer.input", () => Select(PlayerDesk.Transfer(view.Participant.InventoryId.Value, enterprise.InputId.Value, recipe.InputItem, 1)));
            var wages = Section("card.wages");
            card.AddChild(wages);
            var production = view.Employment.Single(g => g.Role == EmploymentRole.Production && g.Id.Value == enterprise.Id.Value);
            WageRow(wages, production);
            var supplies = Section("card.supplies");
            card.AddChild(supplies);
            var logistics = view.Logistics.SingleOrDefault(l => l.EnterpriseId == enterprise.Id);
            if (logistics is null) Button(supplies, "supplies.buy", () => Select(PlayerDesk.BuyLogistics(enterprise.Id.Value)));
            else
            {
                supplies.AddChild(new Label { Text = logistics.ReasonCode });
                Button(supplies, logistics.Enabled ? "supplies.output.off" : "supplies.output.on",
                    () => Select(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Output, !logistics.Enabled)));
                Button(supplies, logistics.InboundEnabled ? "supplies.inbound.off" : "supplies.inbound.on",
                    () => Select(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Inbound, !logistics.InboundEnabled)));
                var crew = view.Employment.Single(g => g.Id == logistics.GroupId);
                WageRow(wages, crew);
                foreach (var building in view.Buildings.Where(b => b.Kind is BuildingKind.Warehouse or BuildingKind.Shop))
                {
                    Button(supplies, "supplies.send", () => Select(PlayerDesk.SendProduct(logistics.Id.Value, building.InputId.Value, true)));
                    Button(supplies, "supplies.fetch", () => Select(PlayerDesk.FetchInput(logistics.Id.Value, building.InputId.Value, true)));
                }
            }
        }
        _cards.AddChild(new Label { Text = T("support.title") });
        if (view.Support.Proposals.Length == 0) _cards.AddChild(new Label { Text = T("support.none") });
        foreach (var proposal in view.Support.Proposals)
        {
            var row = new HBoxContainer();
            _cards.AddChild(row);
            string terms = string.Join(", ", proposal.Compensation.Select(m => $"{m.Quantity} {m.ItemId}"));
            row.AddChild(new Label { Text = $"#{proposal.Id} {proposal.Status} {proposal.Candidate.Value}->{proposal.Seller.Value} {terms}" });
            if (proposal.Seller == _human && proposal.Status == SupportProposalStatus.Proposed)
                Button(row, "support.accept", () => Select(PlayerDesk.AcceptSupport(proposal.Id)));
            if (proposal.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)
                Button(row, "support.reject", () => Select(PlayerDesk.RejectSupport(proposal.Id)));
        }
        if (view.Elections.NextElectionId is { } election)
        {
            _cards.AddChild(new Label { Text = $"{T("vote.title")} #{election.Value}  {view.Elections.OwnChoice?.Value.ToString() ?? T("no")}" });
            var votes = new HBoxContainer();
            _cards.AddChild(votes);
            foreach (var candidate in view.Elections.Participants.Where(p => p != _human))
                Button(votes, "vote.choose", () => Select(PlayerDesk.Vote(election.Value, candidate.Value)), candidate.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WageRow(Container parent, EmploymentView group)
    {
        long shown = group.PendingWageMinor ?? group.WageMinor;
        parent.AddChild(new Label { Text = $"{group.Role} {group.Headcount}/{group.Capacity} {shown}" });
        var row = new HBoxContainer();
        parent.AddChild(row);
        Button(row, "wage.raise", () => Select(PlayerDesk.Wage(group.Id.Value, shown + PlayerDesk.WageStepMinor)));
        Button(row, "wage.lower", () => Select(PlayerDesk.Wage(group.Id.Value, Math.Max(0, shown - PlayerDesk.WageStepMinor))));
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
        if (_surface != Surface.Play || _stage is null || _view is null || e is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left)
            return;
        var viewport = (SubViewport)_stage.GetParent();
        var size = _view.Size;
        if (size.X < 1 || size.Y < 1) return;
        _stage.Pick(new Vector2(button.Position.X / size.X * viewport.Size.X, button.Position.Y / size.Y * viewport.Size.Y));
        GetViewport().SetInputAsHandled();
    }

    private void OnCell(int x, int z)
    {
        if (_surface != Surface.Play || x < 0 || z < 0 || x >= _scenario.MapWidth || z >= _scenario.MapHeight) return;
        var frame = Presentation.Read(_scenario, _session.Engine, _human.Value);
        var own = frame.Markers.FirstOrDefault(m => m.X == x && m.Z == z && m.OwnerId == _human.Value && m.Kind == "production");
        if (own is not null)
        {
            _selectedEnterprise = own.EntityId;
            _feedback = $"{T("selected")} #{own.EntityId}";
            _cardsSignature = "";
            Refresh();
            return;
        }
        var resource = frame.Markers.FirstOrDefault(m => m.Kind == "resource" && m.X == x && m.Z == z);
        if (resource is not null)
        {
            Submit(PlayerDesk.Harvest(resource.Label, 1));
            return;
        }
        Submit(PlayerDesk.Move(x, z));
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
        var settings = new Button { Text = T("menu.settings") };
        settings.Position = new Vector2(8, 8);
        settings.Pressed += OpenMenu;
        _playChrome.AddChild(settings);
        _playInventory = new Label { Position = new Vector2(620, 8), Size = new Vector2(460, 80), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _playChrome.AddChild(_playInventory);
        _playNotice = new Label { Position = new Vector2(8, 640), Size = new Vector2(760, 70), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _playChrome.AddChild(_playNotice);

        _panel = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_panel);
        var row = new HBoxContainer();
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        _panel.AddChild(row);
        var mapColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.6f };
        row.AddChild(mapColumn);
        _board = new DebugBoard();
        _board.SizeFlagsVertical = SizeFlags.ExpandFill;
        _board.CellChosen += OnDebugCell;
        mapColumn.AddChild(_board);
        mapColumn.AddChild(new Label { Text = T("legend.title") });
        var legend = new GridContainer { Columns = 2 };
        mapColumn.AddChild(legend);
        foreach (string key in new[] { "legend.player", "legend.npc", "legend.wood", "legend.grain", "legend.plot.free", "legend.plot.own", "legend.plot.foreign", "legend.plot.reserved", "legend.corridor", "legend.blocked", "legend.production", "legend.warehouse", "legend.shop", "legend.capital", "legend.cargo" })
            legend.AddChild(new Label { Text = T(key) });

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(340, 0), MouseFilter = MouseFilterEnum.Stop };
        row.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        column.AddChild(new Label { Text = T("title"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        column.AddChild(new Label { Text = T("debug.hint"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Button(column, "language", () => { _language = _language == "ru" ? "en" : "ru"; CallDeferred(nameof(RebuildChrome)); });
        Button(column, "step", StepOne);
        Button(column, "save", () => { CheckpointStore.Save(_session, SavePath); _feedback = T("save"); Refresh(); });
        Button(column, "load", Load);
        column.AddChild(new Label { Text = T("new.session"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Button(column, "economy", () => Start("economy", true));
        Button(column, "politics", () => Start("politics", true));
        Button(column, "trip", () => Start("trip", true));
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        _inventoryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_inventoryLabel);
        _cellLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_cellLabel);
        _feedbackLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_feedbackLabel);
        Button(column, "move.north", () => Move(0, -1));
        Button(column, "move.south", () => Move(0, 1));
        Button(column, "move.west", () => Move(-1, 0));
        Button(column, "move.east", () => Move(1, 0));
        foreach (string item in _scenario.ResourceNodes.Select(n => n.ItemId).Distinct(StringComparer.Ordinal))
            Button(column, "harvest", () => Select(PlayerDesk.Harvest(item, 1)), item);
        _cards = new VBoxContainer();
        column.AddChild(_cards);
        _cardsSignature = "";

        _menu = new ColorRect { Color = new Color(0.08f, 0.09f, 0.1f, 0.92f), MouseFilter = MouseFilterEnum.Stop };
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
    }

    private string Describe(PlayerView view)
    {
        if (_pending is not null) return T("command.selected");
        if (_trackedId is null) return _gain.Length == 0 ? _feedback : _gain;
        var result = _session.Engine.Result(_trackedId);
        if (result is null) return T("command.sent");
        if (result.Status == ResultStatus.Rejected) return T("command.rejected") + " " + PlayerText.Reason(_language, result.ReasonCode);
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
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

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
    private VirtualJoystick? _stick;
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
    private Button? _openStorage;
    private Button? _openPlot;
    private bool _showOwnership;
    private string _holdingKind="";
    private long _holdingId;
    private long _plotId;
    private int _plotX,_plotZ;
    private VBoxContainer? _contextHost;
    private ContextActionsPanel? _contextActions;
    private ColorRect? _contextShield;
    private string? _contextCommandId;
    private string _contextMessage="";
    private void CloseContextActions()=>_contextActions?.SetExpanded(false);
    private ContextChoice[] CurrentContext()=>_surface==Surface.Play?ContextActions.Read(_session.Engine,_human,_scenario,_language):PlayContext.At(PlayerDesk.Read(_session.Engine,_human),_scenario,_language);

    private VBoxContainer? _debugContext;
    private VBoxContainer _cards = null!;
    private ContextChoice[] _choices = [];
    private int _contextIndex;
    private string _contextKey = "";
    private int? _hoverX;
    private int? _hoverZ;
    private Label? _fpsLabel;
    private double _frameSampleSeconds;
    private int _frameSampleCount;
    private double _worstFrame;
    private int _contextFirst;
    private string SavePath => ProjectSettings.GlobalizePath($"user://play-{_mode}-{SimulationEngine.RulesVersion}{(SimulationEngine.IsDeveloperBuild?"-developer":"")}.json");

    private enum Surface { Play, Menu, Debug, Maps }
    private string _mapKey = PlayMaps.First;
    private string _newMapKey = PlayMaps.CompactValley;
    private MapSelectionScreen? _mapSelection;
    private ResourceInventoryHud? _hudGoods;

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
        {
            // _Ready ещё внутри add_child. Немедленный ChangeSceneToFile вызывает remove_child у занятого родителя.
            _left = true;
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://PlatformProbe.tscn");
            return;
        }
        InitializeAppearance();
        InitializeFeedback();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Engine.MaxFps = 60;
        Theme = PresentationTheme.Create();
        LoadInterface();
        CallDeferred(nameof(ConfigureDisplay));
        EnsureStage();
        if (OS.GetCmdlineUserArgs().Contains("--trip")) Start("trip", false);
        else ShowMaps();
    }

    public override void _Input(InputEvent e)
    {
        // Before GUI focus and contextual Enter: available on maps, in help and in forms too.
        if (_left || e is not InputEventKey { AltPressed: true, Keycode: Key.Enter or Key.KpEnter } key) return;
        GetViewport().SetInputAsHandled();
        if (key.Pressed && !key.Echo) ToggleFullscreen();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_left || e is not InputEventKey key) return;
        if (_surface == Surface.Maps)
        {
            if (key.Pressed && !key.Echo && key.Keycode == Key.Escape && _session is not null)
            { DismissMaps(); OpenMenu(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (_session is null) return;
        if (_helpOverlay is not null)
        {
            if (key.Pressed && !key.Echo && key.Keycode == Key.Escape) CloseHelp();
            GetViewport().SetInputAsHandled(); return;
        }
        if(_surface==Surface.Play&&_contextActions?.Expanded==true&&key.Pressed&&!key.Echo&&key.Keycode==Key.Escape)
        {CloseContextActions();GetViewport().SetInputAsHandled();return;}
        if(_roadWorkCard is not null&&key.Pressed&&!key.Echo&&key.Keycode==Key.Escape)
        {CloseRoadWork();GetViewport().SetInputAsHandled();return;}
        if(_roadWorkCard is not null&&key.Keycode is Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.KpEnter or Key.Space)return;
        bool developerHandled = false;
        DeveloperInput(key, ref developerHandled);
        if (developerHandled) { GetViewport().SetInputAsHandled(); return; }
        if (!key.Pressed)
        {
            if (_waitRelease && !MovementHeld()) _waitRelease = false;
            return;
        }
        if (key.Echo) return;
        if (_surface == Surface.Play && _tabletState.IsOpen && key.Keycode != Key.Escape) return;
        // A focused context button already processes its own keyboard activation/navigation.
        // Do not also dispatch the global selected command from the same key event.
        if(_surface==Surface.Play&&_contextActions is {} context&&GetViewport().GuiGetFocusOwner() is Control focused&&context.IsAncestorOf(focused)&&key.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.Left or Key.Right or Key.Up or Key.Down)
        {GetViewport().SetInputAsHandled();return;}
        if (_surface is Surface.Play or Surface.Debug && key.Keycode is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if(_surface==Surface.Play&&_contextActions is not null){_contextActions.Navigate(key.Keycode is Key.Right or Key.Down?1:-1);GetViewport().SetInputAsHandled();return;}
            if (_choices.Length > 0)
                _contextIndex = ( _contextIndex + (key.Keycode is Key.Right or Key.Down ? 1 : _choices.Length - 1)) % _choices.Length;
            _contextKey = "";
            Refresh();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_surface is Surface.Play or Surface.Debug && key.Keycode is Key.Enter or Key.KpEnter or Key.Space)
        {
            if(_surface==Surface.Play&&_contextActions is not null)_contextActions.ConfirmSelected();else ConfirmContext();
            GetViewport().SetInputAsHandled();
            return;
        }
        if(key.Keycode==Key.M&&_surface==Surface.Play&&!_tabletState.IsOpen){OpenCityMap();GetViewport().SetInputAsHandled();return;}
        if(key.Keycode==Key.Escape&&_surface==Surface.Play&&_tabletState.IsOpen){CloseTablet();GetViewport().SetInputAsHandled();return;}
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
            bool modelWaiting=ModelFrame();AdvanceFeedback(delta);
            if(modelWaiting){AnimateInterface(delta);return;}
            SampleHeading();
            DeveloperFrame();
            if(_session.Engine.WaitingForBallots){_session.PumpVoting();Refresh();return;}
            long prior = _session.Engine.Tick;
            int priorQuantum = _session.Engine.TravelQuantum;
            var priorTravel = _session.Engine.TravelOf(_human);
            _scheduler.AdvanceElapsed(TimeSpan.FromSeconds(Math.Min(delta, 0.25)));
            var travel = _session.Engine.TravelOf(_human);
            if (_session.Engine.Tick != prior || _session.Engine.TravelQuantum != priorQuantum || travel != priorTravel) Refresh();
            AnimateInterface(delta);
        }
        catch (Exception failure) { _feedback = failure.Message; Refresh(); }
    }

    public override void _Notification(int what)
    {
        if (_left || _session is null) return;
        if (what == NotificationApplicationPaused) { _suspended = true; _waitRelease = true; CloseContextActions();StopHeading(); }
        else if (what == NotificationApplicationFocusOut) { _unfocused = true; _waitRelease = true; CloseContextActions();StopHeading(); }
        else if (what == NotificationApplicationResumed) { _suspended = false; _skipNextFrame = true; }
        else if (what == NotificationApplicationFocusIn) { _unfocused = false; _skipNextFrame = true; }
        else return;
        ApplyPause();
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut)
        { SaveCityExperience();try { CheckpointStore.Save(_session, SavePath); } catch (Exception failure) { _feedback = failure.Message; } }
        Refresh();
    }

    private int _newNpcCount=3;
    private string? _userMapPath;
    private OrdinaryGameResolution ResolveNewGame(string key,int count)
    {
        var map=ReadMap(key);
        var economy=ReadEconomy(key);
        VNextBalance? full=null;
        if(economy.VNext is {} provenance)
        {var read=VNextBalanceReader.Read(System.Text.Encoding.UTF8.GetBytes(Text("Play.vnext-balance.json")));if(!read.IsValid || read.Sha256!=provenance.FullBalanceSha256)throw new InvalidOperationException("Full balance/profile mismatch");full=read.Balance;}
        return OrdinaryNewGame.Resolve(map,economy,new(count,_newDifficulty.ToString()),vnextBalance:full);
    }
    private OrdinaryGameResolution ResolveUserMap(string path,EconomyProfile economy)
    {
        var draft=MapDraft.Parse(System.IO.File.ReadAllText(path));
        var problems=draft.Problems();
        if(problems.Count>0)throw new InvalidOperationException(string.Join('\n',problems));
        int npcs=Math.Clamp(_newNpcCount,3,Math.Max(3,Math.Min(10,draft.MaxParticipants-1)));
        VNextBalance? full=null;
        if(economy.VNext is {} provenance)
        {var read=VNextBalanceReader.Read(System.Text.Encoding.UTF8.GetBytes(Text("Play.vnext-balance.json")));if(!read.IsValid || read.Sha256!=provenance.FullBalanceSha256)throw new InvalidOperationException("Full balance/profile mismatch");full=read.Balance;}
        return UserMaps.Start(draft,economy,new(npcs,_newDifficulty.ToString()),full);
    }
    private ScenarioDefinition ReadMap(string key)
    {
        // An explicit pre-I4 research profile retains its historical map snapshot.
        // Ordinary starts without an override always use the current versioned worlds.
        bool legacy=key==PlayMaps.CompactValley&&OS.GetCmdlineUserArgs().Contains("--economy")&&ReadEconomy(key).Id is not (VNextMobileEquipmentProfile.ProfileId or VNextEquipmentClassesProfile.ProfileId);
        return JsonSerializer.Deserialize<ScenarioDefinition>(Text(legacy?PlayMaps.Resource(key):PlayMaps.NewGameResource(key)),SimulationJson.Options)??throw new InvalidOperationException(key);
    }

    private void DismissMaps()
    { if (_mapSelection is not null) {_newNpcCount=_mapSelection.NpcCount;_newMapKey = _mapSelection.SelectedMap;_newDifficulty=_mapSelection.Difficulty;_newModel=_mapSelection.ModelConfiguration;} _mapSelection?.QueueFree(); _mapSelection = null; }

    private void ShowMaps()
    {
        DismissHelp();
        if (_session is not null) { StopHeading(); _scheduler.SetPaused(true); }
        _surface = Surface.Maps;
        ResetTablet();
        ApplySurface();
        DismissMaps();
        _mapSelection = new MapSelectionScreen(_language, _session is not null || System.IO.File.Exists(SavePath), ReadMap, _newMapKey,_newDifficulty,_newModel,_newNpcCount,(key,count)=>ResolveNewGame(key,count).Scenario);
        _mapSelection.MapChosen += key =>
        {
            try{_mapSelection.ModelConfiguration?.Validate();}catch(ArgumentException){_mapSelection.TooltipText=L("Проверьте имя модели и настройки.","Check model name and settings.");return;}
            if (_session is null && !System.IO.File.Exists(SavePath)) { BeginMap(key); return; }
            var confirm = new ConfirmationDialog { DialogText = PlayerText.Local(_language,
                "Начать новую партию? Текущая партия будет заменена.", "Start a new game? The current game will be replaced.") };
            AddChild(confirm);
            confirm.Confirmed += () => { confirm.QueueFree(); BeginMap(key); };
            confirm.Canceled += () => confirm.QueueFree();
            UiSafeArea.Popup(confirm,this,new(580,170));
        };
        _mapSelection.Continued += () =>
        {
            if (_session is not null) { DismissMaps(); ContinueGame(); }
            else { try { Load(); } catch (Exception failure) { _mapSelection.TooltipText = failure.Message; GD.PushWarning(failure.Message); } }
        };
        _mapSelection.LanguageChanged += () => { _language = _language == "ru" ? "en" : "ru"; ShowMaps(); };
        _mapSelection.DisplaySettingsRequested += ToggleFullscreen;
        _mapSelection.FeedbackSettingsRequested += OpenFeedbackSettings;
        _mapSelection.AppearanceSettingsRequested += OpenAppearanceSettings;
        _mapSelection.QuitRequested += ExitGame;
        _mapSelection.MapEditorRequested += () => GetTree().ChangeSceneToFile("res://MapEditor.tscn");
        _mapSelection.UserMapEdit += path => { MapHandoff.QueueEdit(path); GetTree().ChangeSceneToFile("res://MapEditor.tscn"); };
        _mapSelection.UserMapChosen += path => { _userMapPath=path;_newNpcCount=_mapSelection.NpcCount;_newDifficulty=_mapSelection.Difficulty;OfferPendingUserPlay(); };
        if(_session is not null)DeveloperMenu(_mapSelection.AdditionalSettings);
        AddChild(_mapSelection);
        if(MapHandoff.Play is {} pending)
        {
            _userMapPath=pending;_newNpcCount=MapHandoff.Npcs;_newDifficulty=MapHandoff.Difficulty;
            CallDeferred(nameof(OfferPendingUserPlay));
        }
    }

    private void OfferPendingUserPlay()
    {
        if(_userMapPath is not {} path)return;
        if(_session is null && !System.IO.File.Exists(SavePath)){BeginUserMap(path);return;}
        var confirm=new ConfirmationDialog{DialogText=PlayerText.Local(_language,
            "Начать новую партию на своей карте? Текущая партия будет заменена.","Start a new game on your map? The current game will be replaced.")};
        AddChild(confirm);
        confirm.Confirmed += () => { confirm.QueueFree(); BeginUserMap(path); };
        confirm.Canceled += () => { _userMapPath=null; MapHandoff.ClearPlay(); confirm.QueueFree(); };
        UiSafeArea.Popup(confirm,this,new(580,170));
    }

    private void BeginUserMap(string path)
    {
        _userMapPath=path;_mapKey=PlayMaps.CompactValley;
        _stage?.ResetMotion();
        DismissMaps();
        MapHandoff.ClearPlay();
        Start("economy", false);
        try { CheckpointStore.Save(_session, SavePath); }
        catch (Exception failure) { _feedback = failure.Message; }
        _userMapPath=null;
    }

    private void BeginMap(string key)
    {
        _mapKey = key; _newMapKey = key;
        _stage?.ResetMotion();
        DismissMaps();
        Start("economy", false);
        try { CheckpointStore.Save(_session, SavePath); }
        catch (Exception failure) { _feedback = failure.Message; }
    }

    private void Start(string mode, bool hold)
    {
        DisposeModel();
        _storageCursor=0;_storageLossUntil=0;_storageLoss="";
        DismissHelp();
        ResetTablet(true);
        _mode = mode;LoadCityExperience(true);
        _gain = "";
        _hoverX = _hoverZ = null;
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
            _contextCollapsed=false;
            _session = new GameSession(new SimulationEngine(_scenario));
            _scheduler = new TickScheduler(_session,controllerBarrier:()=>ModelBarrier);
            _feedback = "";
            _cardsSignature = "";
            try { ScriptedScenarios.PrepareVisibleTrip(_session, _scenario); }
            catch (Exception failure) { _feedback = failure.Message; }
            _surface = Surface.Menu;
            CatchPublicCursor();
            _feedback = T("new.session") + " " + T("trip");
            ApplyPause();
            CallDeferred(nameof(RebuildChrome));
            return;
        }
        string scenarioName = mode == "politics" ? "Play.baseline-politics.json" : PlayMaps.Resource(_mapKey);
        string controllerName = mode == "politics" ? "Play.baseline-politics-controllers.json" : "Play.baseline-controllers.json";
        _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(Text(scenarioName), SimulationJson.Options)
            ?? throw new InvalidOperationException(scenarioName);
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(Text(controllerName), SimulationJson.Options)
            ?? throw new InvalidOperationException(controllerName);
        if (mode == "economy")
        {
            var economy = ReadEconomy(_userMapPath is null?_mapKey:PlayMaps.CompactValley);
            var resolved=_userMapPath is {} userPath?ResolveUserMap(userPath,economy):ResolveNewGame(_mapKey,_newNpcCount);
            _scenario=resolved.Scenario;
            config=config.ForOrdinary(_scenario,resolved.HumanActorId,resolved.Controllers);
            config = config with { Npcs = config.Npcs.Select(n => AlgorithmicController.WithEconomy(n, economy.Npc)).ToArray() };
        }
        _human = new(config.HumanActorId);
        _contextCollapsed=false;
        _session = new GameSession(new SimulationEngine(_scenario));
        AttachGameControllers(config,mode=="economy");
        _scheduler = new TickScheduler(_session,controllerBarrier:()=>ModelBarrier);
        _feedback = hold ? T("new.session") + " " + T(mode) : "";
        _cardsSignature = "";
        _surface = hold ? Surface.Menu : Surface.Play;
        ObserveModelFallbacks();
        CatchPublicCursor();
        ApplyPause();
        CallDeferred(nameof(RebuildChrome));
    }

    private void RebuildChrome()
    {
        if (_left || _session is null) return;
        CloseRoadWork();DismissHelp();
        BuildChrome();
        Refresh();
    }

    private void ApplyPause()
    {
        if (_session is null) return;
        bool paused = _userPaused || _tabletState.IsPaused(_surface != Surface.Play || _suspended || _unfocused || _helpOverlay is not null || _session.Engine.WaitingForBallots || ModelBarrier);
        if(_stick is not null)_stick.Visible=!paused&&_contextActions?.Expanded!=true&&_roadWorkCard is null&&!(_tabletState.Kind==TabletKind.Object&&_objectKind=="equipment"&&_scenario.Economy?.Fleet?.EquipmentDealerVersion is 1 or 2);
        if(paused&&!_session.Paused){StopHeading();_waitRelease=true;}
        if (_session.Paused != paused) _scheduler.SetPaused(paused);
        FeedbackPause();
    }

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
        ConfirmContextChoice(_choices[Math.Clamp(_contextIndex,0,_choices.Length-1)]);
    }
    private void ConfirmContextChoice(ContextChoice choice)
    {
        var current=CurrentContext();
        if(_surface==Surface.Play&&_contextActions is {} panel)
        {
            string scope=ContextActions.Scope(PlayerDesk.Read(_session.Engine,_human));
            if(scope!=panel.Selection.Scope){_contextMessage=L("Контекст изменился. Выберите действие заново.","Context changed. Select the action again.");Refresh();return;}
            panel.Selection.Update(scope,current);panel.Selection.Pending=WaitingCommand;
            if(!panel.Selection.TryChoose(choice,out _)){_contextMessage=WaitingCommand?T("command.sent"):choice.Enabled?L("Действие изменилось. Выберите его заново.","Action changed. Select it again."):choice.Details;Refresh();return;}
        }
        var valid = current.FirstOrDefault(c => c.Key == choice.Key);
        if(valid is null||valid.Command!=choice.Command){_contextMessage=L("Действие изменилось. Выберите его заново.","Action changed. Select it again.");Refresh();return;}
        if(choice.Command is not null&&WaitingCommand)return;
        _contextMessage="";
        choice = valid;
        if (!choice.Enabled) { _feedback = choice.Details; _gain = ""; _trackedId = null; Refresh(); return; }
        if(choice.Key.StartsWith("local-transfer:",StringComparison.Ordinal))
        {var parts=choice.Key.Split(':');OpenPlayerTablet();_playerTablet?.OpenTransfer(long.Parse(parts[1],CultureInfo.InvariantCulture),long.Parse(parts[2],CultureInfo.InvariantCulture));return;}
        if(choice.Key.StartsWith("section:",StringComparison.Ordinal)){OpenCitySection(choice.Key[8..]);return;}
        if(choice.Key.StartsWith("road-work:",StringComparison.Ordinal)){OpenRoadWork(long.Parse(choice.Key[10..],CultureInfo.InvariantCulture));return;}
        if (choice.Key.StartsWith("object:", StringComparison.Ordinal))
        {
            var parts = choice.Key.Split(':'); OpenObjectTablet(parts[1],long.Parse(parts[2],CultureInfo.InvariantCulture)); return;
        }
        if (choice.Key.StartsWith("card:", StringComparison.Ordinal))
        {
            _selectedEnterprise = long.Parse(choice.Key["card:".Length..], CultureInfo.InvariantCulture);
            if (_surface == Surface.Play) { OpenEnterpriseTablet(_selectedEnterprise); return; }
            _cardsSignature = "";
            Refresh();
            return;
        }
        if (choice.Key.StartsWith("market:", StringComparison.Ordinal))
        { OpenMarketTablet(long.Parse(choice.Key[7..], CultureInfo.InvariantCulture)); return; }
        if(choice.Key=="city"){_playerTab=3;OpenPlayerTablet();return;}
        if(choice.Key=="dealership"){if(_scenario.Economy?.Fleet is not null){OpenCitySection("dealer");}else OpenHelp("dealership");return;}
        if (choice.Key == "exchange") { OpenCapitalTablet(); return; }
        if (choice.Command is { } action)
        {string? prior=_trackedId;Offer(action);if(_surface==Surface.Play&&_trackedId!=prior)_contextCommandId=_trackedId;Refresh();}
    }

    private readonly List<ProgressBar> _harvestBars = [];

    private void RebuildContext()
    {
        _harvestBars.Clear();
        if (_contextHost is not null) FillPlayContext(_contextHost);
        if (_debugContext is not null) FillContext(_debugContext);
    }

    private void FillContext(VBoxContainer host)
    {
        _harvestBars.Clear();
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
            if (_choices[i].Command?.Kind == ActionKind.StopHarvest)
            {
                button.CustomMinimumSize = new Vector2(0, 38);
                var bar = new ProgressBar { ShowPercentage = false, MinValue = 0, MaxValue = 100,
                    MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
                    OffsetLeft = 6, OffsetRight = -6, OffsetTop = -7, OffsetBottom = -2 };
                bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color("77ba88"),
                    CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 });
                button.AddChild(bar);
                _harvestBars.Add(bar);
            }
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
        if (action.Kind is ActionKind.StartHarvesting or ActionKind.StopHarvest && WaitingCommand) return;
        if (_tabletState.IsOpen)
        {
            if (!_tabletState.ValidateLocation(PlayerDesk.Read(_session.Engine,_human),_scenario)) { CloseTablet(); return; }
            if (WaitingCommand) return;
        }
        _tabletError = "";
        try
        {
            Track(_session.Send(_human, PlayerDesk.Check(action)), action);
            if (_tabletState.IsOpen) _tabletCommand = _trackedId;
            _pending = null;
            _feedback = T("command.sent");
        }
        catch (Exception failure) { _feedback = failure.Message; if (_tabletState.IsOpen) _tabletError = failure.Message; }
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
        if(_surface!=Surface.Play||_tabletState.IsOpen||_helpOverlay is not null||_roadWorkCard is not null)CloseContextActions();
        if(_bottomChrome is not null)_bottomChrome.Visible=_surface==Surface.Play&&!_tabletState.IsOpen&&_helpOverlay is null&&_roadWorkCard is null;
        RefreshVoting();
        var actor = view.Participant;
        var travelOffset = _session.Engine.TravelOf(_human);
        if (_tabletState.IsOpen && !_tabletState.ValidateLocation(view,_scenario))
        { DropTabletPanel(); ApplyPause(); ApplySurface(); }
        bool paused = _session.Paused;
        var frame = Presentation.Read(_scenario, _session.Engine, _human.Value);
        if (_surface == Surface.Play || paused) _stage?.Show(frame, paused, _selectedEnterprise, T("you"), _surface == Surface.Play, _language, _session.Engine.TravelQuantum, _showOwnership, _hoverX??-1, _hoverZ??-1);
        _stage?.ShowBuildingStates(view,_language);
        _stage?.ShowActivity(view);
        _stage?.ShowRoadWorkEffects(view);
        if (_surface == Surface.Debug) _board?.Show(frame, _cellX, _cellZ);
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        string stock = pack.Stock.Length == 0 ? T("inventory.empty")
            : string.Join(", ", pack.Stock.OrderBy(s => s.ItemId, StringComparer.Ordinal).Select(s => $"{PlayerText.Item(_language, s.ItemId)} ×{s.Quantity}"));
        string inventory = $"{T("inventory.title")}: {stock}\n{T("inventory.capacity")} {pack.OccupiedVolume}/{pack.Capacity}";
        if (_inventoryLabel is not null) _inventoryLabel.Text = inventory;
        if (_playInventory is not null) _playInventory.Text = _friendly ? L("Рюкзак ", "Pack ") + pack.OccupiedVolume + "/" + pack.Capacity : HudStock(view, pack);
        UpdateHudGoods(pack);
        var next = CurrentContext();
        _contextIndex = PlayContext.Keep(_contextIndex, _choices, next);
        if (WaitingCommand)
            next = next.Select(c => c.Command is not null
                ? c with { Enabled = false, Details = T("command.sent") } : c).ToArray();
        _choices = next;
        string contextKey = _contextIndex.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", next.Select(c => $"{c.Key}:{c.Label}:{c.Enabled}:{c.Details}"));
        if(_surface==Surface.Play&&_contextHost is not null)FillPlayContext(_contextHost);
        if (contextKey != _contextKey) { _contextKey = contextKey; if(_debugContext is not null)FillContext(_debugContext); }
        foreach (var bar in _harvestBars)
        {
            bar.Value = HarvestProgress.Percent(view.Harvest, view.Tick, _session.Engine.TravelQuantum);
            bar.TooltipText = PlayerText.Local(_language, "Текущий цикл", "Current cycle") + $": {bar.Value:0}%";
        }
        if (_tooltip is not null)
        {
            int hx = _hoverX ?? actor.Position.X, hz = _hoverZ ?? actor.Position.Z;
            _tooltip.Text = PlayContext.Hover(frame, view, hx, hz, _language);
            _tooltip.Visible = _hoverX.HasValue && _hoverZ.HasValue && !_tabletState.IsOpen && _helpOverlay is null && frame.Markers.Any(m => m.X == hx && m.Z == hz);
            var cell=new Cell(hx,hz);
            var ownPlot=view.Plots.FirstOrDefault(p=>p.Position==cell);
            bool ownGround=ownPlot?.OwnerId==view.Participant.Id && view.Storage?.Ground.Any(g=>g.PlotId==ownPlot.Id.Value)==true;
            var ownStore=view.Buildings.FirstOrDefault(b=>b.OwnerId==view.Participant.Id && b.Footprint.Contains(cell) && b.Kind is BuildingKind.Warehouse or BuildingKind.Shop);
            _holdingKind=ownGround?"ground":ownStore is not null?"building":"";
            _holdingId=ownGround?ownPlot!.Id.Value:ownStore?.Id.Value??0;
            _plotId=ownPlot?.Id.Value??0;_plotX=hx;_plotZ=hz;
            bool hover=_hoverX.HasValue && !_tabletState.IsOpen && _helpOverlay is null;
            if(_openStorage is not null)_openStorage.Visible=hover && _holdingKind.Length>0;
            if(_openPlot is not null)_openPlot.Visible=hover && _plotId>0;
        }
        RefreshRoadWork(view);
        PollHarvest(view);
        _feedback = Describe(view);
        _status.Text = $"{T(paused ? "paused" : "running")}  {T("tick")}: {_session.Engine.Tick}\n" +
            $"{T("position")}: ({actor.Position.X}, {actor.Position.Z}) +({travelOffset.OffsetX}, {travelOffset.OffsetZ})  {T("busy")}: {T(actor.Busy ? "yes" : "no")}\n" +
            $"{T("npc.title")}: " + string.Join("; ", view.LaborMarket.Where(g => g.OwnerId != _human).Select(g => $"{g.OwnerId.Value} {g.Role} {g.Headcount}/{g.Capacity}"));
        _feedbackLabel.Text = _feedback;
        UpdatePlayNotice();
        StorageNotice(view);UpdateCityExperience(view);
        int unread=_cityNotices.Count(n=>n.Unread);
        if(_hudNotices is not null){_hudNotices.Visible=unread>0;_hudNotices.Text="● "+unread;}
        if(_accountNotice is not null)
        {
            long debt=view.PersonalAccount?.DebtMinor??0;
            _accountNotice.Text=_friendly?PlayerText.Money(view.AvailableCashMinor):L("Счёт","Account")+(view.PersonalAccount?.UnreadCount>0?" ●":"");
            _accountNotice.TooltipText=L("Свободно ","Available ")+PlayerText.Money(view.AvailableCashMinor)+L(" · баланс "," · balance ")+PlayerText.Money(view.CashMinor)+(debt>0?L(" · долг "," · debt ")+PlayerText.Money(debt):"");
            var ink=debt>0?new Color("6a4a16"):new Color("24312d");
            foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})
                _accountNotice.AddThemeColorOverride(state,ink);
            _accountNotice.Modulate=Colors.White;
        }
        if (_gameClock is not null)
        {
            string time=GameTime(view.Tick);
            bool narrowClock=GetViewportRect().Size.X<520&&_friendly;
            _gameClock.Text=(narrowClock?time[..5]:"◷ "+time)+(_session.Paused?"  Ⅱ":"  ▸");
            _gameClock.TooltipText=_userPaused?L("Продолжить","Resume"):L("Пауза","Pause");
        }
        UpdateGoal(view);
        if (_cellLabel is not null) _cellLabel.Text = CellText(frame);
        string signature = _selectedEnterprise + "|" + CardSignature(view);
        if (signature != _cardsSignature)
        {
            _cardsSignature = signature;
            FillCards(view, _cards);
            if (_tabletPolitics is not null)
            { FillPolitics(view,_tabletPolitics); }
        }
        RefreshTablet(view);SyncSideFrame();
    }

    private string HudStock(PlayerView view, InventoryView pack) =>
        $"{PlayerText.Money(view.CashMinor)} · {L("Рюкзак","Pack")} {pack.OccupiedVolume}/{pack.Capacity}";

    private void UpdateHudGoods(InventoryView pack) => _hudGoods?.ShowInventory(pack, _language);

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
            foreach(var ingredient in recipe.Ingredients)Button(work, "transfer.input", () => Offer(PlayerDesk.Transfer(view.Participant.InventoryId.Value, enterprise.InputId.Value, ingredient.ItemId, 1)));
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
        FillPolitics(view,host);
    }

    private void FillPolitics(PlayerView view,Container host)
    {
        var panel=host.GetChildren().OfType<CityPoliticsPanel>().FirstOrDefault();if(panel is null){panel=new(_language,action=>Offer(action));host.AddChild(panel);}panel.ShowData(view,WaitingCommand);
    }

    private void WageRow(Container parent, EmploymentView group)
    {
        long shown = group.PendingWageMinor ?? group.WageMinor;
        parent.AddChild(new Label { Text = $"{group.Role} {group.Headcount}/{group.Capacity} {PlayerText.Money(shown)}" });
        var row = new HBoxContainer();
        parent.AddChild(row);
        Button(row, "wage.raise", () => Offer(PlayerDesk.Wage(group.Id.Value, shown + (_scenario.Economy ?? new()).PlayerWageStepMinor)));
        Button(row, "wage.lower", () => Offer(PlayerDesk.Wage(group.Id.Value, Math.Max(0, shown - (_scenario.Economy ?? new()).PlayerWageStepMinor))));
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
        _view.OffsetTop = 54;
        _view.OffsetBottom = 0;
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
        if (_surface != Surface.Play || _tabletState.IsOpen || _helpOverlay is not null || _stage is null || _view is null) return;
        if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.WheelUp or MouseButton.WheelDown} zoom){_stage.ZoomView(zoom.ButtonIndex==MouseButton.WheelUp?.85f:1.18f);_view.AcceptEvent();return;}
        if(e is InputEventMagnifyGesture gesture){_stage.ZoomView(1/gesture.Factor);_view.AcceptEvent();return;}
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
        DeveloperReset();
        if(_votingPanel is not null){RemoveChild(_votingPanel);_votingPanel.QueueFree();_votingPanel=null;}
        DropTabletPanel();
        _playChrome?.QueueFree();
        _panel?.QueueFree();
        _menu?.QueueFree();
        EnsureStage();
        _playChrome = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _playChrome.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_playChrome);
        BuildPlayHud();

        _panel = new ColorRect { Color = new Color("26383d"), MouseFilter = MouseFilterEnum.Stop };
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_panel);
        var split = new VSplitContainer { SplitOffsets = [425] };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.OffsetLeft = 10; split.OffsetRight = -10; split.OffsetTop = 8; split.OffsetBottom = -8;
        _panel.AddChild(split);
        var mapColumn = new VBoxContainer { CustomMinimumSize = new Vector2(0, 230), SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(mapColumn);
#if DEBUG && !DELSK_DEVELOPER
        mapColumn.AddChild(new Label { Name = "DebugBuildHint", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = L("Для режима бога экспортируйте проект с галочкой «Экспорт с отладкой» (Export With Debug).",
                "For god mode, export the project with Export With Debug checked.") });
#endif
        var mapToolbar = new HBoxContainer();
        mapColumn.AddChild(mapToolbar);
        DeveloperDebug(mapToolbar);
        mapToolbar.AddChild(new Label { Text = PlayerText.Local(_language, "Карта · пошаговый режим · " + PlayMaps.Name(_mapKey, _language), "Map · step mode · " + PlayMaps.Name(_mapKey, _language)), SizeFlagsHorizontal = SizeFlags.ExpandFill });
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
        foreach (var entry in _scenario.Participants.Select(p=>(p.Id==_human.Value?"player":"npc",p.Id.ToString(),WorldSkins.ActorName(p.Id,"ru"),WorldSkins.ActorName(p.Id,"en"))).Concat(new[]
        {
            ("resource", "wood", "Древесина", "Wood"), ("resource", "grain", "Зерно", "Grain"),
            ("resource", "milk", "Приём молочного сырья", "Dairy collection"), ("resource", "beet", "Свёкла", "Beet"),
            ("plot-free", "", "Свободный участок", "Free plot"), ("plot-own", "", "Ваш участок", "Your plot"),
            ("plot-foreign", "", "Чужой участок", "Other participant's plot"), ("plot-reserved", "", "Бронь", "Reserved plot"),
            ("shop-plot", "", "Место лавочки", "Assigned shop site"), ("corridor", "", "Проход", "Protected passage"),
            ("blocked", "", "Непроходимо", "Blocked"), ("production", "", "Предприятие", "Production"),
            ("warehouse", "", "Склад", "Warehouse"), ("shop", "", "Лавочка", "Shop"),
            ("capital", "", "Терминал столицы", "Capital terminal"), ("cargo", "", "Груз в пути", "Cargo in transit"),
            ("truck-home", "", "Грузовик без груза", "Empty truck"), ("equipment", "", "Добывающая техника", "Extraction equipment")
        }))
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
        _fpsLabel = new Label(); controls.AddChild(_fpsLabel);
        Button(controls, "step", StepOne);
        Button(controls, "save", () => { CheckpointStore.Save(_session, SavePath); _feedback = T("save"); Refresh(); });
        Button(controls, "load", Load);
        Button(controls, "language", ChangeLanguage);
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

        _menu = new ColorRect { Theme=TabletDesign.Theme,Color = new Color("edf1eb"), MouseFilter = MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        var menuScroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};menuScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_menu.AddChild(menuScroll);
        var center = new MarginContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};menuScroll.AddChild(center);UiSafeArea.CenterColumn(menuScroll,center,440);
        var box = new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };center.AddChild(box);
        box.AddChild(new Label { Text = T("menu.title") + " · " + PlayMaps.Name(_mapKey, _language), HorizontalAlignment = HorizontalAlignment.Center,AutowrapMode=TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label { Text = T("menu.escape"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        Button(box, "menu.continue", ContinueGame);GameUi.Primary(box.GetChildren().OfType<Button>().Last());
#if DEBUG
        Button(box, "menu.debug", OpenDebug);
#endif
        DeveloperMenu(box);
        var chooseMap = new Button { Text = PlayerText.Local(_language, "Выбрать карту / новая партия", "Choose map / new game"), FocusMode = FocusModeEnum.None };
        chooseMap.Pressed += ShowMaps;
        box.AddChild(chooseMap);
        Button(box, "language", ChangeLanguage);
        var feedbackSettings=new Button{Text=L("Реплики и звук","Speech and audio"),FocusMode=FocusModeEnum.None};feedbackSettings.Pressed+=OpenFeedbackSettings;box.AddChild(feedbackSettings);
        var appearance = new Button { Text = L("Оформление и персонажи", "Appearance and characters"), FocusMode = FocusModeEnum.None };
        appearance.Pressed += OpenAppearanceSettings; box.AddChild(appearance);
        var display = new Button { Text = L("Полный экран / окно", "Fullscreen / window"), FocusMode = FocusModeEnum.None };
        display.Pressed += ToggleFullscreen; box.AddChild(display);
        var advanced=new CheckButton{Name="InterfaceAdvanced",Text=L("Расширенный интерфейс","Advanced interface"),ButtonPressed=!_friendly,FocusMode=FocusModeEnum.None};
        advanced.Toggled+=on=>SetSimple(!on);box.AddChild(advanced);
        var outlines=new CheckButton{Name="OwnershipOutlines",Text=L("Контуры участков на поле","Plot outlines in the world"),ButtonPressed=_showOwnership,FocusMode=FocusModeEnum.None};
        outlines.Toggled+=on=>{_showOwnership=on;_hudPlots?.SetPressedNoSignal(on);_cityMapPanel?.SetWorldOwnership(on);SaveCityExperience();Refresh();};box.AddChild(outlines);
        var guide=new CheckButton{Name="StarterGuide",Text=L("Подсказка текущей цели","Current-goal hint"),ButtonPressed=_guideVisible,FocusMode=FocusModeEnum.None};
        guide.Toggled+=on=>{_guideVisible=on;SaveInterface();Refresh();};box.AddChild(guide);
        var handbook = new Button { Text = L("Справка", "Help"), FocusMode = FocusModeEnum.None };
        handbook.Pressed += () => OpenHelp(); box.AddChild(handbook);
        Button(box, "menu.exit", ExitGame);GameUi.FitControls(box,440);
        RebuildTablet();
        ApplySurface();
    }

    private void ApplySurface()
    {
        if (_view is not null) { _view.Visible = _surface == Surface.Play; _view.OffsetBottom = 0; }
        if(_surface!=Surface.Play||_tabletState.IsOpen||_roadWorkCard is not null||_helpOverlay is not null)CloseContextActions();
        if (_bottomChrome is not null) _bottomChrome.Visible = !_tabletState.IsOpen&&_helpOverlay is null&&_roadWorkCard is null;
        if (_tabletPanel is not null) _tabletPanel.Visible = _surface == Surface.Play && _tabletState.IsOpen;
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

#if DEBUG
    private void OpenDebug()
    {
        _surface = Surface.Debug;
        ResetTablet();
        _waitRelease = true;
        StopHeading();
        ApplyPause();
        ApplySurface();
        Refresh();
    }
#endif

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
        try { if (_session is not null) CheckpointStore.Save(_session, SavePath); }
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
        DismissHelp();
        ResetTablet(true);
        _stage?.ResetMotion();
        DisposeModel();
        _storageCursor=0;_storageLossUntil=0;_storageLoss="";
        _session = loaded.Session;
        _scenario = _session.Engine.Configuration();LoadCityExperience(false);
        _mapKey = PlayMaps.KeyForScenario(_scenario.Id);
        _human = new(_scenario.NewGame?.HumanActorId??1);
        RestoreGameControllers();
        ObserveModelFallbacks();
        _scheduler = new TickScheduler(_session,controllerBarrier:()=>ModelBarrier);
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
        _hoverX = null;
        _hoverZ = null;
        DismissMaps();
        CallDeferred(nameof(RebuildChrome));
    }

    private void CatchPublicCursor()
    {
        var events = _session.Engine.PublicEventsSince(0);
        NoteMilestones(events);
        _publicCursor = events.Length == 0 ? 0 : events[^1].Sequence;
        _effects.Reset(_publicCursor);_effects.Advance(0,true);
    }

    private void PollHarvest(PlayerView view)
    {
        var events = _session.Engine.PublicEventsSince(_publicCursor);
        _effects.ObserveLabor(view.LaborMarket,_human.Value,_feedbackPreferences);
        _effects.Consume(events,_human.Value,_feedbackPreferences);
        if (events.Length == 0) return;
        NoteMilestones(events);
        _publicCursor = events[^1].Sequence;
        if (PlayerText.OwnHarvest(events, _human.Value, _language) is { } gain) _gain = gain;
        foreach (var done in events.Where(e => e.ActorId?.Value == _human.Value))
        {
            if (done.Type is "HarvestCancelled" or "HarvestLoopStopped")
                _gain = done.ReasonCode == "NoCapacity"
                    ? PlayerText.Local(_language, "Добыча остановлена: инвентарь заполнен.", "Harvesting stopped: inventory full.")
                    : PlayerText.Local(_language, "Добыча остановлена. Незавершённый прогресс сброшен.", "Harvesting stopped. Unfinished progress discarded.");
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

    private void NoteMilestones(IEnumerable<PublicSimulationEvent> events)
    {
        bool changed = false;
        foreach (var done in events)
        {
            if (done.ActorId?.Value != _human.Value || done.Type != "CapitalSaleSettled") continue;
            if (_cityRead.Note("sale")) changed = true;
        }
        if (changed) SaveCityExperience();
    }

    private string Describe(PlayerView view)
    {
        if(_session.Engine.UsesFreeVehicleControl&&_session.Engine.TravelOf(_human) is {HeadingX:var hx,HeadingZ:var hz}&&(hx!=0||hz!=0))
        {
            var fleet=_session.Engine.FleetOf(_human);long vehicle=fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Driver==_human.Value)?.Vehicle.Id??0;
            var traffic=_session.Engine.RoadsOf(_human)?.OwnTraffic.FirstOrDefault(t=>t.VehicleId==vehicle);
            var reason=traffic is {ExitQuanta:>0}&&traffic.Steering is not {TurnQuanta:>0}?"RoadExitPending":traffic?.Reason;
            if(reason is "RoadExitPending" or "VehicleTurning" or "TurnSpaceOccupied" or "TrafficWaiting" or "ObstacleBraking")return reason=="RoadExitPending"?L("Съезд с дороги… удерживайте направление 0,5 с.","Leaving the road… hold the direction for 0.5 s."):PlayerText.Reason(_language,reason);
        }
        if (_pending is not null) return T("command.selected");
        if (_trackedId is null) return _gain.Length == 0 ? _feedback : _gain;
        var result = _session.Engine.Result(_trackedId);
        if (result is null) return T("command.sent");
        if (result.Status == ResultStatus.Rejected) return T("command.rejected") + " " + PlayerText.Reason(_language, result.ReasonCode);
        if (_trackedKind == ActionKind.SellAvailableToCapital && view.Participant.Busy)
            return PlayerText.Local(_language, "Заявка на продажу принята. Ожидаем закрытия торгового окна.", "Sale request accepted. Waiting for the trading window to close.");
        if (view.Harvest is { Repeating: true } harvest)
            return PlayerText.Local(_language, "Добываем: ", "Harvesting: ") + PlayerText.Item(_language, harvest.ItemId) +
                PlayerText.Local(_language, " · по 1 за цикл", " · 1 per cycle") + (_gain.Length == 0 ? "" : " · " + _gain);
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
        if (_surface != Surface.Play || _contextActions?.Expanded==true || _roadWorkCard is not null || _waitRelease || _suspended || _unfocused || _helpOverlay is not null || _session.Engine.WaitingForBallots) return;
        if(_tabletState.Kind is TabletKind.CityMap or TabletKind.Notices or TabletKind.Tutorial||GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
        {StopHeading();_waitRelease=true;return;}
        var heading = TravelHeading.FromKeys(
            Input.IsPhysicalKeyPressed(Key.D), Input.IsPhysicalKeyPressed(Key.A),
            Input.IsPhysicalKeyPressed(Key.S), Input.IsPhysicalKeyPressed(Key.W));
        if(heading==default&&_stick is not null)heading=_stick.Heading;
        bool canMove = _session.Engine.CanTravel(_human,heading.X,heading.Z);
        if (_tabletState.BlocksTime && !canMove) heading = default;
        if (_tabletState.ResumeForMovement(canMove)) { ApplyPause(); Refresh(); }
        if (heading == _sent) return;
        _sent = heading;
        _session.SetTravelHeading(_human, heading.X, heading.Z);
    }

    private void StopHeading()
    {
        _stick?.Clear();
        _sent = default;
        if (_session is null) return;
        var travel = _session.Engine.TravelOf(_human);
        if (travel.HeadingX == 0 && travel.HeadingZ == 0) return;
        _session.SetTravelHeading(_human, 0, 0);
    }

    private bool MovementHeld() =>_stick?.Held==true||
        Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.D);

    private void Button(Container parent, string key, Action action, string suffix = "")
    {
        var button = new Button { Text = suffix.Length == 0 ? T(key) : $"{T(key)} {suffix}" };
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        button.FocusMode = FocusModeEnum.None;
        button.Pressed += () => { try { action(); } catch (Exception failure) { _feedback = failure.Message; Refresh(); } };
        parent.AddChild(button);
    }

    private void ChangeLanguage()
    {
        _language = _language == "ru" ? "en" : "ru";
        _feedback = _gain = "";
        CallDeferred(nameof(RebuildChrome));
    }

    private string T(string key) => PlayerText.Get(_language, key);

    private static string Text(string name)
    {
        using var stream = typeof(Playfield).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private SavedWorldStore _worlds = null!;
    private SavedWorldsScreen? _worldSelection;
    private string? _worldId;
    private string? _lastWorld;
    private string? _newWorldName;
    private ulong? _newSeed;
    private long? _recoveredSaveTick;
    private bool _quitHandling;
    private bool _previousAutoQuit;
    private bool _previousQuitOnBack;
    private string SavePath => _worldId is { } id ? _worlds.CheckpointPath(id) : _worlds.LegacyPath("economy");

    private void InstallQuitHandling()
    {
        var tree = GetTree();
        _previousAutoQuit = tree.AutoAcceptQuit;
        _previousQuitOnBack = tree.QuitOnGoBack;
        tree.AutoAcceptQuit = tree.QuitOnGoBack = false;
        _quitHandling = true;
    }

    private void RestoreQuitHandling()
    {
        if (!_quitHandling) return;
        GetTree().AutoAcceptQuit = _previousAutoQuit;
        GetTree().QuitOnGoBack = _previousQuitOnBack;
    }

    private void PauseForWorldSelection(Surface surface)
    {
        DismissHelp();
        CloseRoadWork();
        DropModelWait();
        ResetTablet();
        _surface = surface;
        if (_session is not null)
        {
            StopHeading();
            _scheduler.SetPaused(true);
            FeedbackPause();
        }
        ApplySurface();
        if (_votingPanel is not null) _votingPanel.Hide();
    }

    private (IReadOnlyList<SavedWorld> Worlds, string Notice) FindSavedWorlds()
    {
        var notices = new List<string>();
        foreach (string mode in new[] { "economy", "politics", "trip" })
        {
            try
            {
                _worlds.ImportLegacy(mode, session => { using var controllers = RestoreGameControllers(session); });
            }
            catch (Exception failure)
            {
                GD.PushWarning(failure.Message);
                notices.Add(L($"Прежняя партия «{T(mode)}». {OpenWorldError(failure)}",
                    $"Previous game “{T(mode)}”. {OpenWorldError(failure)}"));
            }
        }

        IReadOnlyList<SavedWorld> worlds;
        try { worlds = _worlds.List(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning(failure.Message);
            worlds = [];
            notices.Add(L("Не удалось прочитать список миров.", "Could not read the list of worlds."));
        }

        _lastWorld = _worlds.LastActive() ?? worlds.FirstOrDefault()?.Id;
        return (worlds, string.Join("\n", notices));
    }

    private void ShowWorlds()
    {
        if (_surface != Surface.Worlds && !TrySaveCurrent()) return;
        PauseForWorldSelection(Surface.Worlds);
        DismissMaps();
        DismissWorlds();
        var (worlds, notice) = FindSavedWorlds();
        _worldSelection = new SavedWorldsScreen(_language, _friendly, worlds, notice,
            ContinueWorld, RenameWorld, DeleteWorld, ShowMaps, CloseWorlds);
        AddChild(_worldSelection);
    }

    private void DismissWorlds()
    {
        if (_worldSelection is not { } screen) return;
        _worldSelection = null;
        RemoveChild(screen);
        screen.QueueFree();
    }

    private void CloseWorlds()
    {
        DismissWorlds();
        if (_session is null) ShowMaps();
        else OpenMenu();
    }

    private void ContinueLastWorld()
    {
        string? id = _worldId ?? _lastWorld;
        if (id is not null) ContinueWorld(id);
    }

    private void ContinueWorld(string id)
    {
        if (id == _worldId)
        {
            DismissMaps();
            DismissWorlds();
            ContinueGame();
            return;
        }

        try
        {
            OpenSavedWorld(id, hold: false);
            if (_recoveredSaveTick is long tick)
                ShowSaveError(L($"Открыта резервная копия партии на тике {tick}.", $"Opened the game's backup at tick {tick}."));
        }
        catch (Exception failure)
        {
            GD.PushWarning(failure.Message);
            ShowSaveError(OpenWorldError(failure));
        }
    }

    private string OpenWorldError(Exception failure)
    {
        if (failure is CheckpointException checkpoint && CheckpointStore.IsIncompatible(checkpoint))
            return L("Для этой партии нужна совместимая версия игры.", "This world requires a compatible game version.");
        if (failure is CheckpointException)
            return L("Не удалось прочитать сохранение и доступную резервную копию.", "Could not read the save or an available backup.");
        return L("Не удалось открыть сохранённую партию.", "Could not open the saved game.");
    }

    private void OpenSavedWorld(string id, bool hold)
    {
        var loaded = _worlds.Load(id);
        var scenario = loaded.Session.Engine.Configuration();
        var controllers = RestoreGameControllers(loaded.Session);
        try
        {
            _worlds.SetActive(id);
            string mode = _worlds.Get(id).Mode;
            AdoptWorld(id, mode, loaded.Session, scenario, controllers, hold, loaded.UsedBackup);
        }
        catch
        {
            if (!ReferenceEquals(_controllers, controllers)) controllers?.Dispose();
            throw;
        }
    }

    private void Load()
    {
        FindSavedWorlds();
        if ((_worldId ?? _lastWorld) is { } id) OpenSavedWorld(id, hold: true);
    }

    private bool TryStart(string mode, bool hold, string? mapKey = null)
    {
        if (!TrySaveCurrent()) return false;
        try
        {
            if (mapKey is null) Start(mode, hold);
            else StartOnMap(mode, hold, mapKey);
            return true;
        }
        catch (Exception failure)
        {
            GD.PushWarning(failure.Message);
            ShowSaveError(L("Не удалось создать мир. Проверьте настройки и свободное место.", "Could not create the world. Check settings and free space."));
            return false;
        }
    }

    private void AdoptWorld(string id, string mode, GameSession session, ScenarioDefinition scenario,
        GameControllerRuntime? controllers, bool hold, bool usedBackup)
    {
        DismissMaps();
        DismissWorlds();
        ClearWorldPresentation();
        DisposeModel();
        _controllers = controllers;
        _worldId = _lastWorld = id;
        _session = session;
        _scenario = scenario;
        _mapKey = PlayMaps.KeyForScenario(scenario.Id);
        _human = new(scenario.NewGame?.HumanActorId ?? 1);
        _scheduler = new TickScheduler(session, controllerBarrier: () => ModelBarrier);
        _recoveredSaveTick = usedBackup ? session.Engine.Tick : null;
        LoadCityExperience(mode);
        _feedback = "";
        _surface = hold ? Surface.Menu : Surface.Play;
        _waitRelease = _skipNextFrame = true;
        ObserveModelFallbacks();
        CatchPublicCursor();
        if (!hold) StopHeading();
        ApplyPause();
        CallDeferred(nameof(RebuildChrome));
    }

    private void ClearWorldPresentation()
    {
        DismissHelp();
        CloseRoadWork();
        DeveloperReset();
        ResetTablet(clearPreferences: true);
        ResetProvincial();
        ClearGuide();
        if (_votingPanel is not null) { RemoveChild(_votingPanel); _votingPanel.QueueFree(); _votingPanel = null; }
        // A fresh viewport drops terrain, actor interpolation, map previews, road effects and camera state.
        foreach (var node in new Control?[] { _view, _playChrome, _panel, _menu })
            if (node is not null) { RemoveChild(node); node.QueueFree(); }
        _view = null; _stage = null;
        _playChrome = _panel = _menu = null;
        _status = null!;
        _stick = null;
        _userPaused = _contextCollapsed = _contextDismissTouch = _goalOpen = false;
        _pending = null;
        _trackedId = _contextCommandId = null;
        _trackedKind = null;
        _trackedItem = _gain = _cardsSignature = _contextKey = _contextMessage = _previewKey = "";
        _cellX = _cellZ = _hoverX = _hoverZ = null;
        _choices = [];
        _contextIndex = _contextFirst = 0;
        _selectedEnterprise = _plotId = _holdingId = 0;
        _holdingKind = "";
        _playerTab = 0; _playerSection = "overview";
        _tabletCommand = null; _tabletError = "";
        _storageCursor = _storageLossUntil = 0; _storageLoss = "";
        _announcedElection = 0; _electionNotice = "";
        _harvestCycle = _noticeValue = "";
        _harvestVisual = _noticeSeconds = 0;
        _sent = default;
    }

    private void RenameWorld(SavedWorld world)
    {
        var dialog = new ConfirmationDialog { Name = "RenameWorldDialog", Title = L("Переименовать мир", "Rename world"),
            OkButtonText = L("Сохранить", "Save"), CancelButtonText = L("Отмена", "Cancel") };
        var name = new LineEdit { Name = "RenameWorldField", Text = SavedWorldsScreen.DisplayName(world, _language),
            MaxLength = SavedWorldStore.MaximumNameLength };
        dialog.AddChild(name);
        void Validate() => dialog.GetOkButton().Disabled = !SavedWorldStore.ValidName(name.Text);
        name.TextChanged += _ => Validate();
        name.TextSubmitted += _ => { if (!dialog.GetOkButton().Disabled) dialog.GetOkButton().EmitSignal(Godot.Button.SignalName.Pressed); };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            try { _worlds.Rename(world.Id, name.Text); ShowWorlds(); }
            catch (Exception failure) { ReportWorldOperation(failure, L("Не удалось переименовать мир.", "Could not rename the world.")); }
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        UiSafeArea.Popup(dialog, this, new(440, 150));
        Validate();
        name.GrabFocus();
        name.SelectAll();
    }

    private void DeleteWorld(SavedWorld world)
    {
        string name = SavedWorldsScreen.DisplayName(world, _language);
        string message = L($"Мир «{name}» и его резервная копия будут удалены.", $"“{name}” and its backup will be deleted.");
        if (SavedWorldStore.IsImported(world.Id))
            message += L(" Исходный файл прежней версии останется на диске.", " The original legacy file will stay on disk.");
        var dialog = new ConfirmationDialog { Name = "DeleteWorldDialog", Title = L("Удалить мир?", "Delete world?"),
            DialogText = message,
            OkButtonText = L("Удалить", "Delete"), CancelButtonText = L("Отмена", "Cancel") };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            try { _worlds.Delete(world.Id); }
            catch (Exception failure)
            {
                ReportWorldOperation(failure, L("Не удалось удалить все файлы мира.", "Could not delete all files for this world."));
            }
            if (_worldId == world.Id && !Directory.Exists(Path.GetDirectoryName(SavePath)))
            {
                ClearWorldPresentation();
                DisposeModel();
                _worldId = null;
                _session = null!;
                _scheduler = null!;
                _scenario = null!;
                FeedbackPause();
            }
            ShowWorlds();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        UiSafeArea.Popup(dialog, this, new(440, 170));
        dialog.GetCancelButton().GrabFocus();
    }

    private void ReportWorldOperation(Exception failure, string message)
    {
        GD.PushWarning(failure.Message);
        ShowSaveError(message);
    }

    private void SaveGame()
    {
        if (_worldId is { } id) _worlds.Save(id, _session);
    }

    private bool TrySaveCurrent()
    {
        if (_session is null) return true;
        try
        {
            StopHeading();
            SaveGame();
            SaveCityExperience();
            return true;
        }
        catch (Exception failure)
        {
            ReportWorldOperation(failure, L("Не удалось сохранить партию.", "Could not save the game."));
            return false;
        }
    }

    private void OpenMapEditor(string? editPath)
    {
        if (!TrySaveCurrent()) return;
        if (editPath is not null) MapHandoff.QueueEdit(editPath);
        GetTree().ChangeSceneToFile("res://MapEditor.tscn");
    }

    private void ExitGame()
    {
        if (!TrySaveCurrent()) return;
        _left = true;
        GetTree().Quit();
    }
}

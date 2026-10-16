using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Platform fixture: recorded commands call the same session and rules as the runner.</summary>
public partial class PlatformProbe : Control
{
    private const string DefaultReportPath = "user://platform-probe.json";
    private GameSession _session = null!;
    private TickScheduler _scheduler = null!;
    private ScenarioDefinition _scenario = null!;
    private Dictionary<long, RecordedCommand[]> _commands = [];
    private Dictionary<long, string> _checkpoints = [];
    private Label _status = null!;
    private Label _details = null!;
    private bool _smoke;
    private bool _suspended;
    private bool _unfocused;
    private bool _manualPause;
    private bool _finished;
    private bool _failed;
    private bool _skipNextFrame;
    private bool _lifecycleChecked;
    private readonly List<long> _restoredTicks = [];
    private int _electionCheckedTicks;
    private string? _electionHash;
    private readonly List<long> _electionRestoredTicks = [];
    private int _supportCheckedTicks;
    private string? _supportHash;
    private readonly List<long> _supportRestoredTicks = [];
    private string SavePath => ProjectSettings.GlobalizePath($"user://platform-checkpoint-{SimulationEngine.RulesVersion}.json");
    private string _reportPath = DefaultReportPath;

    public override void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            _smoke = arguments.Contains("--smoke");
            int output = Array.IndexOf(arguments, "--report");
            if (output >= 0)
            {
                if (output + 1 >= arguments.Length) throw new ArgumentException("--report requires a path");
                _reportPath = arguments[output + 1];
            }
            _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.scenario.json"), SimulationJson.Options)
                ?? throw new InvalidOperationException("Missing scenario");
            var tape = ResourceText("Fixture.commands.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)
                    ?? throw new InvalidOperationException("Invalid command tape")).ToArray();
            _commands = tape.GroupBy(c => c.SubmittedAtTick).ToDictionary(g => g.Key, g => g.ToArray());
            _checkpoints = ResourceText("Fixture.checkpoints.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(line => line.Trim().Split(','))
                .ToDictionary(row => long.Parse(row[0], CultureInfo.InvariantCulture), row => row[1]);
            BuildView();
            Restart();
        }
        catch (Exception failure) { Fail(failure); }
    }

    public override void _Process(double delta)
    {
        if (_failed || _finished) return;
        try
        {
            if (_smoke)
            {
                CheckLifecycle();
                CheckElections();
                CheckSupport();
                // No wall-clock sleep; the same 62 ticks and physical paths remain intact.
                while (!_finished) StepOne();
                GetTree().Quit(0);
                return;
            }
            if (_skipNextFrame) { _skipNextFrame = false; return; }
            long priorTick = _session.Engine.Tick;
            _scheduler.AdvanceElapsed(TimeSpan.FromSeconds(Math.Min(delta, 0.25)));
            if (_session.Engine.Tick != priorTick) CheckTick();
        }
        catch (Exception failure) { Fail(failure); }
    }

    public override void _Notification(int what)
    {
        if (_session is null || _smoke) return;
        if (what == NotificationApplicationPaused) _suspended = true;
        else if (what == NotificationApplicationFocusOut) _unfocused = true;
        else if (what == NotificationApplicationResumed) { _suspended = false; _skipNextFrame = true; }
        else if (what == NotificationApplicationFocusIn) { _unfocused = false; _skipNextFrame = true; }
        else return;
        ApplyPause();
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut)
        { try { CheckpointStore.Save(_session, SavePath); } catch (Exception failure) { Fail(failure); } }
    }

    public override void _ExitTree()
    {
        if (_session is null || _failed) return;
        try { CheckpointStore.Save(_session, SavePath); }
        catch (Exception failure) { GD.PushError($"Checkpoint on exit failed: {failure.Message}"); }
    }

    private void Restart()
    {
        _finished = false;
        _session = new GameSession(new SimulationEngine(_scenario));
        _session.ConfigurePlayback(_commands.Values.SelectMany(x => x).OrderBy(c => c.SubmittedAtTick).ToArray(), _checkpoints.Keys.Max());
        _scheduler = new TickScheduler(_session);
        _manualPause = !_smoke;
        ApplyPause();
        ShowState();
    }

    private void CheckLifecycle()
    {
        // Exercise the actual adapter with notifications; this does not replace a device check.
        _smoke = false;
        _manualPause = false;
        long prior = _session.Engine.Tick;
        try
        {
            _Notification((int)NotificationApplicationFocusOut);
            _scheduler.AdvanceElapsed(TimeSpan.FromDays(1));
            RequireUnchanged();
            _Notification((int)NotificationApplicationPaused);
            _Notification((int)NotificationApplicationFocusIn);
            _scheduler.AdvanceElapsed(TimeSpan.FromDays(1));
            RequireUnchanged();
            _Notification((int)NotificationApplicationResumed);
            _Process(86400); // The first resumed frame must discard its stale delta.
            RequireUnchanged();
            _manualPause = true;
            ApplyPause();
            _scheduler.AdvanceElapsed(TimeSpan.FromDays(1));
            RequireUnchanged();
            _lifecycleChecked = true;
        }
        finally { _smoke = true; _manualPause = false; ApplyPause(); }
        void RequireUnchanged()
        {
            if (_failed) throw new InvalidOperationException("Lifecycle adapter check failed");
            if (_session.Engine.Tick != prior) throw new InvalidOperationException("Pause/resume advanced the world");
        }
    }

    private void LoadSaved(bool recoverBackup = false)
    {
        var loaded = CheckpointStore.Load(SavePath, recoverBackup);
        _session = loaded.Session; _scheduler = new TickScheduler(_session);
        _finished = _session.Engine.Tick >= _checkpoints.Keys.Max(); _failed = false;
        _manualPause = _session.Paused; _skipNextFrame = true;
        ApplyPause(); ShowState();
    }

    private void CheckElections()
    {
        var definition = JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.elections.json"), SimulationJson.Options)!;
        var tape = ResourceText("Fixture.elections.commands.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)!).ToArray();
        var checkpoints = ResourceText("Fixture.elections.checkpoints.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.Trim().Split(','))
            .ToDictionary(row => long.Parse(row[0], CultureInfo.InvariantCulture), row => row[1]);
        var session = new GameSession(new SimulationEngine(definition));
        session.ConfigurePlayback(tape, checkpoints.Keys.Max(), "elections");
        string path = ProjectSettings.GlobalizePath($"user://election-checkpoint-{SimulationEngine.RulesVersion}.json");
        while (session.Engine.Tick < checkpoints.Keys.Max())
        {
            session.Step(); long tick = session.Engine.Tick;
            string hash = session.Engine.ComputeStateHash();
            if (hash != checkpoints[tick]) throw new InvalidOperationException($"Election hash differs from runner at tick {tick}");
            if (tick is 9 or 19 or 20 or 21)
            {
                CheckpointStore.Save(session, path); session = CheckpointStore.Load(path).Session;
                if (session.Engine.Tick != tick || session.Engine.ComputeStateHash() != hash)
                    throw new InvalidOperationException("Election checkpoint changed the committed world");
                _electionRestoredTicks.Add(tick);
            }
            _electionCheckedTicks++;
        }
        var result = session.Engine.Inspect().Elections;
        if (result.Results.Length != 2 || result.Results[0].Winner.HasValue ||
            result.FirstVictory != new ElectionVictory(new(2), new(1), 20) || result.Mode != ElectionMode.Sandbox)
            throw new InvalidOperationException("Election probe did not reach the expected first victory and sandbox");
        _electionHash = session.Engine.ComputeStateHash();
        GD.Print($"DELSK_ELECTION_PASS ticks={_electionCheckedTicks} hash={_electionHash}");
    }

    private void CheckSupport()
    {
        var definition = JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.support.json"), SimulationJson.Options)!;
        var tape = ResourceText("Fixture.support.commands.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)!).ToArray();
        var checkpoints = ResourceText("Fixture.support.checkpoints.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.Trim().Split(','))
            .ToDictionary(row => long.Parse(row[0], CultureInfo.InvariantCulture), row => row[1]);
        var session = new GameSession(new SimulationEngine(definition));
        session.ConfigurePlayback(tape, checkpoints.Keys.Max(), "support");
        string path = ProjectSettings.GlobalizePath($"user://support-checkpoint-{SimulationEngine.RulesVersion}.json");
        while (session.Engine.Tick < checkpoints.Keys.Max())
        {
            session.Step(); long tick = session.Engine.Tick;
            string hash = session.Engine.ComputeStateHash();
            if (hash != checkpoints[tick]) throw new InvalidOperationException($"Support hash differs from runner at tick {tick}");
            if (tick is 17 or 18 or 19 or 27 or 99 or 100)
            {
                CheckpointStore.Save(session, path); session = CheckpointStore.Load(path).Session;
                if (session.Engine.Tick != tick || session.Engine.ComputeStateHash() != hash)
                    throw new InvalidOperationException("Support checkpoint changed the committed world");
                _supportRestoredTicks.Add(tick);
            }
            _supportCheckedTicks++;
        }
        var politics = session.Engine.InspectSupport();
        if (politics.Commitments.Length != 2 || politics.Commitments.Any(c => c.Status != PoliticalCommitmentStatus.Executed) ||
            session.Engine.Inspect().Elections.FirstVictory != new ElectionVictory(new(1), new(1), 100))
            throw new InvalidOperationException("Support probe did not execute S5");
        _supportHash = session.Engine.ComputeStateHash();
        GD.Print($"DELSK_SUPPORT_PASS ticks={_supportCheckedTicks} hash={_supportHash}");
    }

    private void StepOne()
    {
        if (_finished || _failed || _suspended || _unfocused) return;
        _scheduler.SetPaused(false);
        _session.Step();
        ApplyPause();
        CheckTick();
    }

    private void CheckTick()
    {
        long tick = _session.Engine.Tick;
        if (!_checkpoints.TryGetValue(tick, out var expected) || _session.Engine.ComputeStateHash() != expected)
            throw new InvalidOperationException($"State hash differs from runner at tick {tick}");
        if (_smoke && tick is 12 or 34 or 55 or 60)
        {
            string hash = _session.Engine.ComputeStateHash();
            CheckpointStore.Save(_session, SavePath); LoadSaved();
            if (_session.Engine.Tick != tick || _session.Engine.ComputeStateHash() != hash)
                throw new InvalidOperationException("Checkpoint load changed the committed world");
            _restoredTicks.Add(tick);
        }
        if (!_smoke && tick % 30 == 0) CheckpointStore.Save(_session, SavePath);
        if (tick == _checkpoints.Keys.Max())
        {
            _finished = true;
            ApplyPause();
            WriteReport("passed", null);
            GD.Print($"DELSK_PLATFORM_PASS ticks={tick} hash={expected}");
        }
        ShowState();
    }

    private void ApplyPause() => _scheduler.SetPaused(_manualPause || _suspended || _unfocused || _finished);

    private void ShowState()
    {
        var observation = _session.Engine.Observe(new ParticipantId(_scenario.Participants[0].Id));
        _status.Text = _finished ? "Проверка завершена: состояние совпало с runner." :
            _session.Paused ? "Пауза. Можно выполнить один тик или запустить повтор." : "Повтор записанных команд.";
        var actor = observation.Participant;
        _details.Text = $"Тик: {_session.Engine.Tick} / {_checkpoints.Keys.Max()}\n" +
            $"Участник: ({actor.Position.X}, {actor.Position.Z}); занят: {actor.Busy}\n" +
            $"Деньги: {observation.Account.BalanceMinor / 100m:0.00}\n" +
            $"Хеш состояния: {_session.Engine.ComputeStateHash()}\n\n" +
            "Это технический сценарий проверки платформы.\n" +
            "Добыча → производство → физическая доставка → продажа.\n" +
            "Автономные NPC и игровой интерфейс добавляются по плану.";
    }

    private void BuildView()
    {
        var column = new VBoxContainer { OffsetLeft = 24, OffsetTop = 24, OffsetRight = 870, OffsetBottom = 580 };
        AddChild(column);
        column.AddChild(new Label { Text = "Delsk — общее C#-ядро в Godot" });
        _status = new Label(); column.AddChild(_status);
        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; column.AddChild(_details);
        var controls = new HBoxContainer(); column.AddChild(controls);
        AddButton("Один тик", StepOne);
        AddButton("Пуск / пауза", () => { _manualPause = !_manualPause; ApplyPause(); ShowState(); });
        AddButton("Повтор сначала", Restart);
        AddButton("Сохранить", () => CheckpointStore.Save(_session, SavePath));
        AddButton("Загрузить", () => LoadSaved());
        AddButton("Восстановить из backup", () => LoadSaved(recoverBackup: true));
        void AddButton(string text, Action action)
        {
            var button = new Button { Text = text };
            button.Pressed += () => { try { action(); } catch (Exception failure) { Fail(failure); } };
            controls.AddChild(button);
        }
    }

    private void WriteReport(string status, string? failure)
    {
        string path = _reportPath.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(_reportPath) : Path.GetFullPath(_reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Status = status, Failure = failure, Tick = _session?.Engine.Tick,
            StateHash = _session?.Engine.ComputeStateHash(), CheckedTicks = _session?.Samples.Length,
            LifecycleAdapterChecked = _lifecycleChecked,
            CheckpointRestoreChecked = _restoredTicks.SequenceEqual(new long[] { 12, 34, 55, 60 }),
            RestoredTicks = _restoredTicks.ToArray(),
            ElectionCheckedTicks = _electionCheckedTicks, ElectionStateHash = _electionHash,
            ElectionRestoredTicks = _electionRestoredTicks.ToArray(),
            SupportCheckedTicks = _supportCheckedTicks, SupportStateHash = _supportHash, SupportRestoredTicks = _supportRestoredTicks.ToArray(),
            Runtime = System.Environment.Version.ToString(), Platform = OS.GetName(),
            GodotVersion = Engine.GetVersionInfo()["string"].AsString(),
            Author = "Шелестов Максим Евгеньевич"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Fail(Exception failure)
    {
        _failed = true;
        GD.PushError(failure.ToString());
        try { WriteReport("failed", failure.ToString()); }
        catch (Exception reportFailure) { GD.PushError(reportFailure.ToString()); }
        if (_status is not null) _status.Text = $"Ошибка проверки: {failure.Message}";
        if (_smoke) GetTree().Quit(1);
    }

    private static string ResourceText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded fixture: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

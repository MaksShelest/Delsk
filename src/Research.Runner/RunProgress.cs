using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Simulation.Hosting;
using Simulation.Core;

/// <summary>Лёгкий heartbeat исследования. Не читает экономику и не меняет Core.
/// Пока тик считается, файл обновляется, чтобы долгий шаг не выглядел как обрыв связи.
/// Author: Шелестов Максим Евгеньевич.</summary>
internal sealed class RunProgress : IDisposable
{
    private readonly string _path;
    private readonly GameSession _session;
    private readonly long _target, _start;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private readonly Timer _timer;
    private long _last = -1;
    private double _lastSeconds = -2;
    private string _status = "running";
    private bool _closed;
    private DateTimeOffset? _lastAdvancedAt;
    private double? _exportMilliseconds;
    public RunProgress(string directory, GameSession session, long target)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "run-progress.json");
        _session = session;
        _target = target;
        _start = session.Engine.Tick;
        Write(true);
        _timer = new Timer(_ => { try { Write(true); } catch (IOException) { } catch (ObjectDisposedException) { } }, null, 2000, 2000);
    }
    public void Write(bool force = false, string? status = null)
    {
        lock (_gate)
        {
            if (_closed) return;
            _ = force;
            bool changed = status is not null && status != _status;
            if (status is not null) _status = status;
            long tick = _session.Engine.Tick;
            double since = _clock.Elapsed.TotalSeconds - _lastSeconds;
            bool moved = tick != _last;
            // Смена статуса сразу. Новый тик не чаще четырёх раз в секунду. Тот же тик — heartbeat раз в две секунды.
            if (!changed && !(moved && since >= 0.25) && !(since >= 2)) return;
            double seconds = _clock.Elapsed.TotalSeconds;
            Persist(tick, seconds);
            _last = tick;
            _lastSeconds = seconds;
        }
    }
    public void NoteExport(double milliseconds)
    {
        lock (_gate) { if (!_closed) _exportMilliseconds = milliseconds; }
    }
    private void Persist(long tick, double seconds)
    {
        using var process = Process.GetCurrentProcess();
        if (tick != _last) _lastAdvancedAt = DateTimeOffset.UtcNow;
        double progressed = tick - _start;
        double speed = seconds > 0 ? progressed / seconds : 0;
        var value = new
        {
            Version = 1, Status = _status, Phase = _status, Tick = tick, LastCompletedTick = tick, Target = _target, StartTick = _start,
            Percent = 100d * progressed / Math.Max(1, _target - _start), ElapsedSeconds = seconds,
            TicksPerSecond = speed,
            AverageTickMilliseconds = progressed > 0 && seconds > 0 ? Math.Round(seconds * 1000d / progressed, 3) : (double?)null,
            EstimatedRemainingSeconds = speed > 0.05 && tick < _target ? Math.Round((_target - tick) / speed, 1) : (double?)null,
            LastAdvancedAt = _lastAdvancedAt, ExportMilliseconds = _exportMilliseconds,
            WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64,
            _session.WaitingForModel, _session.Engine.WaitingForBallots, HeartbeatAt = DateTimeOffset.UtcNow,
            Scope = "Runner process memory and host elapsed time. EstimatedRemainingSeconds follows the current speed and is not a promise."
        };
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(value, SimulationJson.Options));
        File.Move(_path + ".tmp", _path, true);
    }
    public void Close()
    {
        lock (_gate) _closed = true;
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _timer.Dispose();
    }
    private int _disposed;
    public void Dispose() => Close();
}

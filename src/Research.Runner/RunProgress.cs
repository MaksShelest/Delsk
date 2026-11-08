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
    private void Persist(long tick, double seconds)
    {
        using var process = Process.GetCurrentProcess();
        var value = new
        {
            Version = 1, Status = _status, Phase = _status, Tick = tick, LastCompletedTick = tick, Target = _target, StartTick = _start,
            Percent = 100d * (tick - _start) / Math.Max(1, _target - _start), ElapsedSeconds = seconds,
            TicksPerSecond = seconds > 0 ? (tick - _start) / seconds : 0,
            WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64,
            _session.WaitingForModel, _session.Engine.WaitingForBallots, HeartbeatAt = DateTimeOffset.UtcNow,
            Scope = "Runner process memory; host elapsed time, not a device benchmark"
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

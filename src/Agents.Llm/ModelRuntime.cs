using System.Diagnostics;

namespace Agents.Llm;

/// <summary>Shared, bounded executor. Workers only publish immutable replies, never mutate a world.</summary>
public sealed class ModelRuntime : IDisposable
{
    private sealed class Work(ModelRequest request)
    {
        public ModelRequest Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Started, Finished;
    }
    private readonly object _gate = new();
    private readonly ILocalModelAdapter _adapter;
    private readonly ModelRuntimeSettings _settings;
    private readonly long[] _actors;
    private readonly Dictionary<long, Work> _pending = [];
    private readonly List<Work> _waiting = [];
    private readonly Dictionary<long, ModelReply> _ready = [];
    private Dictionary<long, ModelReply> _frozen = [];
    private Work? _active;
    private string? _session;
    private long _epoch, _frozenTick = -1, _pumpedTick = -1;
    private bool _disposed;
    public bool Enabled { get; }
    public ModelRuntime(ILocalModelAdapter adapter, IEnumerable<long> actors, ModelRuntimeSettings? settings = null, bool enabled = true)
    {
        _settings = settings ?? new(); _settings.Validate(); _adapter = adapter; Enabled = enabled;
        _actors = actors.Distinct().Order().ToArray();
        if (_actors.Length is < 1 or > 64 || _actors.Any(a => a <= 0)) throw new ArgumentException("Invalid runtime actors");
    }
    public int ActiveRequests { get { lock (_gate) return _active is null ? 0 : 1; } }
    public int WaitingRequests { get { lock (_gate) return _waiting.Count; } }
    public void Bind(string session, long epoch)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null && _session != session || epoch < _epoch) throw new ArgumentException("Model runtime session mismatch");
            if (_session == session && _epoch == epoch) return;
            _session = session; _epoch = epoch;
            foreach (var work in _pending.Values) CancelWork(work);
            _pending.Clear(); _waiting.Clear(); _ready.Clear(); _frozen.Clear(); _frozenTick = _pumpedTick = -1;
            // An adapter that ignores cancellation still occupies the single physical slot.
        }
    }
    public bool Enqueue(ModelRequest request)
    {
        lock (_gate)
        {
            if (_disposed || request.SessionId != _session || request.SessionEpoch != _epoch ||
                !_actors.Contains(request.ActorId) || _pending.ContainsKey(request.ActorId)) return false;
            var work = new Work(request); _pending.Add(request.ActorId, work); _waiting.Add(work);
            // Admission is resolved after every participant has submitted, not by caller order.
            return true;
        }
    }
    public void Freeze(long tick)
    {
        lock (_gate)
        {
            if (_frozenTick == tick) return;
            _frozenTick = tick; _frozen = new(_ready);
        }
    }
    public ModelReply? Read(ModelRequest request)
    {
        lock (_gate) return request.SessionEpoch == _epoch && request.SessionId == _session &&
            _pending.TryGetValue(request.ActorId, out var work) && work.Request.DecisionId == request.DecisionId
                ? _frozen.GetValueOrDefault(request.ActorId) : null;
    }
    public void Pump(long tick)
    {
        lock (_gate)
        {
            if (_disposed || _pumpedTick == tick) return;
            _pumpedTick = tick;
            int offset = (int)(tick / 30 % _actors.Length);
            var ordered = _waiting.OrderBy(w => w.Request.RequestedTick)
                .ThenBy(w => (Array.IndexOf(_actors, w.Request.ActorId) + _actors.Length - offset) % _actors.Length).ToArray();
            int admitted = _settings.QueueCapacity + (_active is null ? 1 : 0);
            _waiting.Clear(); _waiting.AddRange(ordered.Take(admitted));
            foreach (var rejected in ordered.Skip(admitted)) _ready[rejected.Request.ActorId] = new(null, "QueueFull");
            if (_active is null && _waiting.Count > 0)
            {
                var work = _waiting[0]; _waiting.RemoveAt(0); _active = work; work.Started = true;
                _ = Task.Run(() => ExecuteAsync(work));
            }
        }
    }
    public void Cancel(ModelRequest request)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(request.ActorId, out var work) || work.Request != request) return;
            _pending.Remove(request.ActorId); _waiting.Remove(work); _ready.Remove(request.ActorId); _frozen.Remove(request.ActorId);
            CancelWork(work);
        }
    }
    private async Task ExecuteAsync(Work work)
    {
        var watch = Stopwatch.StartNew();
        // A badly behaved adapter can block even before returning its Task. It never runs on Step.
        var operation = Task.Run(() => _adapter.CompleteAsync(work.Request, work.Cancellation.Token));
        try
        {
            ModelReply reply;
            try { reply = await operation.WaitAsync(TimeSpan.FromMilliseconds(_settings.TimeoutMilliseconds)).ConfigureAwait(false); }
            catch (TimeoutException) { _ = work.Cancellation.CancelAsync(); reply = new(null, "HttpTimeout"); }
            catch (OperationCanceledException) { reply = new(null, "Cancelled"); }
            catch (Exception) { reply = new(null, "ModelUnavailable"); }
            if (reply.Text?.Length > 4096) reply = new(null, "ResponseTooLarge");
            reply = reply with { LatencyMilliseconds = watch.ElapsedMilliseconds };
            lock (_gate)
                if (!_disposed && work.Request.SessionEpoch == _epoch &&
                    _pending.TryGetValue(work.Request.ActorId, out var current) && ReferenceEquals(work, current))
                    _ready[work.Request.ActorId] = reply;
            // Timeout does not authorize a second physical inference while the first ignores cancellation.
            try { await operation.ConfigureAwait(false); } catch (Exception) { }
        }
        finally
        {
            lock (_gate)
            {
                work.Finished = true;
                if (ReferenceEquals(_active, work)) _active = null;
                if (!_pending.TryGetValue(work.Request.ActorId, out var current) || !ReferenceEquals(current, work))
                    work.Cancellation.Dispose();
            }
        }
    }
    private static void CancelWork(Work work)
    {
        if (!work.Started || work.Finished) work.Cancellation.Dispose();
        else _ = work.Cancellation.CancelAsync();
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var work in _pending.Values) CancelWork(work);
            _pending.Clear(); _waiting.Clear(); _ready.Clear(); _frozen.Clear();
        }
    }
}

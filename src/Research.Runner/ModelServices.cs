using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;

internal record ModelRunConfiguration(HybridSettings? Policy = null, ModelRuntimeSettings? Runtime = null, OllamaSettings? Ollama = null, Dictionary<string,OllamaSettings?>? Bindings = null);

/// <summary>Composition and external scheduling stay outside Core and controller code.</summary>
internal sealed class ModelServices : IDisposable
{
    private ModelRuntime? _runtime;
    private IDisposable? _adapterOwner;
    private SafetyAdapter? _safety;
    private ModelRunConfiguration _config = new();
    private string _mode = "off";
    public object? Metadata => _runtime is null ? null : new
    {
        Mode = _mode, Configuration = _config, Policy = HybridController.PolicyKind, PromptVersion = ModelPrompt.Version,
        // Имя тега не является digest. Серия измеряет его отдельно и не подставляет сюда.
        ModelDigest = (string?)null, ModelDigestReason = "not measured by the runner; research series records the measured digest",
        RamBytes = (long?)null, RamMeasurementReason = "not measured", Runtime = Environment.Version.ToString()
    };
    private void Setup(Dictionary<string, string> options, long[] actors, bool safety)
    {
        _mode = safety ? "fake-s7" : options.GetValueOrDefault("--model-mode", "off");
        if (_mode is not ("off" or "ollama" or "fake-s7") || !safety && _mode == "fake-s7") throw new ArgumentException("Invalid model mode");
        if (options.TryGetValue("--model-config", out var path))
        {
            if (new FileInfo(path).Length > 65536) throw new ArgumentException("Model configuration too large");
            _config = JsonSerializer.Deserialize<ModelRunConfiguration>(File.ReadAllText(path), SimulationJson.Options)
                ?? throw new ArgumentException("Empty model configuration");
        }
        if (_mode == "ollama" && !options.ContainsKey("--model-config")) throw new ArgumentException("Live Ollama requires --model-config");
        ILocalModelAdapter adapter;
        if (_config.Bindings is {} bindings && bindings.Keys.Any(k=>!long.TryParse(k,out long actor) || !actors.Contains(actor)))
            throw new ArgumentException("Model bindings must identify configured NPCs");
        if (_mode == "ollama") { var router = new RoutingAdapter(_config,options); adapter = router; _adapterOwner = router; }
        else if (safety) { _safety = new(); adapter = _safety; _config = new(new(ApplyDelayTicks: 5, DecisionTtlTicks: 5, MaximumRequestsPerPeriod: 1), new()); }
        else adapter = new UnavailableAdapter();
        _runtime = new(adapter, actors, _config.Runtime ?? new(), enabled: _mode != "off");
    }
    public long Configure(GameSession session, ScenarioDefinition definition, string controllers, Dictionary<string, string> options, bool safety, EconomyProfile? economics = null)
    {
        var config = AutonomousRun.ReadConfiguration(definition, controllers);
        Setup(options, config.Npcs.Select(p => p.ActorId).ToArray(), safety);
        var policy = _config.Policy ?? new();
        if (_mode == "off") policy = policy with { Enabled = false };
        return AutonomousRun.Configure(session, definition, controllers, c => _config.Bindings is {} b && (!b.TryGetValue(c.Actor.Value.ToString(),out var binding) || binding is null) ? c : new HybridController(c, _runtime!, policy), economics, options.GetValueOrDefault("--human-ballot")=="self");
    }
    public void Restore(GameSession session, Dictionary<string, string> options)
    {
        var actors = session.ControllerStates.Where(c => c.Kind == HybridController.PolicyKind).Select(c => c.Actor.Value).ToArray();
        if (actors.Length == 0 && (options.ContainsKey("--model-mode") || options.ContainsKey("--model-config")))
            throw new ArgumentException("The checkpoint has no hybrid controllers");
        if (actors.Length > 0) Setup(options, session.ControllerStates.Where(c=>c.Kind!=SelfBallotController.Kind).Select(c=>c.Actor.Value).ToArray(), false);
        session.RestoreControllers(c => c.Kind == HybridController.PolicyKind ? HybridController.Restore(c, _runtime!) : c.Kind==SelfBallotController.Kind ? SelfBallotController.Restore(c) : AlgorithmicController.Restore(c));
    }
    public void ProgressWaiting()=>_runtime?.ProgressWaiting();
    public async Task RunSafety(GameSession session, long finalTick)
    {
        while (session.Engine.Tick < finalTick)
        {
            session.Step();
            // A controlled fixture coordinates fake completions outside Step, never waiting in the world.
            if (session.Engine.Tick is 1 or 2)
            {
                int expected = (int)session.Engine.Tick;
                await Until(() => _safety!.Calls.Count >= expected && _runtime!.ActiveRequests == 0);
            }
            if (session.Engine.Tick == 3) await Until(() => _safety!.Calls.Count == 3);
            if (session.Engine.Tick == 6)
            { _safety!.ReleaseLate(); await Until(() => _runtime!.ActiveRequests == 0); }
        }
        CheckSafety(session);
    }
    public static void CheckSafety(GameSession session)
    {
        var resolved = session.ControllerTurns.Where(t => t.Decision.ModelTrace?.Resolution is "ModelResolved" or "FallbackResolved").ToArray();
        if (resolved.Length != 3 || resolved.Count(t => t.Decision.ModelTrace!.Resolution == "ModelResolved") != 1 ||
            resolved.Count(t => t.Decision.ModelTrace!.ReasonCode == "DecisionIdentityMismatch") != 1 ||
            resolved.Count(t => t.Decision.ModelTrace!.ReasonCode == "DecisionDeadline") != 1 ||
            resolved.Any(t => t.Tick + 1 != t.Decision.ModelTrace!.ApplyTick || t.CommandIds.Length != 1 ||
                session.Engine.Result(t.CommandIds[0])?.Status != ResultStatus.Accepted))
            throw new InvalidOperationException("S7 did not prove valid, invalid and late decisions at the common apply barrier");
    }
    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 3000) await Task.Delay(1);
        if (!condition()) throw new InvalidOperationException("Fake model fixture exceeded its bound");
    }
    public void Dispose() { _safety?.ReleaseLate(); _runtime?.Dispose(); _adapterOwner?.Dispose(); }
    private sealed class RoutingAdapter : ILocalModelAdapter, IDisposable
    {
        private readonly OllamaAdapter? _default;
        private readonly Dictionary<long,OllamaAdapter> _bindings=[];
        private readonly StreamWriter? _transcripts;
        private bool _closed;
        public RoutingAdapter(ModelRunConfiguration config,Dictionary<string,string> options)
        {
            if(config.Bindings is null)_default=new(config.Ollama??new());
            else foreach(var (actor,binding) in config.Bindings)if(binding is not null)_bindings.Add(long.Parse(actor),new(binding));
            if(options.GetValueOrDefault("--research")=="true")
            {
                string directory=options.GetValueOrDefault("--output","artifacts/cycle");Directory.CreateDirectory(directory);
                _transcripts=new(new FileStream(Path.Combine(directory,"model-transcripts.jsonl"),FileMode.Create,FileAccess.Write,FileShare.Read),new System.Text.UTF8Encoding(false)){AutoFlush=true};
            }
        }
        public async Task<ModelReply> CompleteAsync(ModelRequest request,CancellationToken cancellationToken)
        {
            Transcript(new {Tick=request.RequestedTick,ActorId=request.ActorId,Request=request,Status="Requested"});
            var adapter=_bindings.GetValueOrDefault(request.ActorId)??_default;
            var reply=adapter is null?new ModelReply(null,"ModelDisabled"):await adapter.CompleteAsync(request,cancellationToken).ConfigureAwait(false);
            Transcript(new {Tick=request.RequestedTick,ActorId=request.ActorId,DecisionId=request.DecisionId,Reply=reply,Status="Completed"});
            return reply;
        }
        private void Transcript(object row){if(_transcripts is not null)lock(_transcripts){if(!_closed)_transcripts.WriteLine(JsonSerializer.Serialize(row,SimulationJson.Options));}}
        public void Dispose(){_default?.Dispose();foreach(var adapter in _bindings.Values)adapter.Dispose();if(_transcripts is not null)lock(_transcripts){_closed=true;_transcripts.Dispose();}}
    }
    private sealed class UnavailableAdapter : ILocalModelAdapter
    { public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) => Task.FromResult(new ModelReply(null, "ModelUnavailable")); }
    private sealed class SafetyAdapter : ILocalModelAdapter
    {
        public ConcurrentQueue<ModelRequest> Calls { get; } = new();
        private readonly TaskCompletionSource<ModelReply> _late = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ModelRequest? _lateRequest;
        private static string Reply(ModelRequest r, long? actor = null) => JsonSerializer.Serialize(new ModelIntent(
            r.DecisionId, actor ?? r.ActorId, r.ObservationVersion, r.SessionEpoch, r.Options.First(p => p.Id != "hold").Id, "Controlled S7 choice"), SimulationJson.Options);
        public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            if (request.ActorId == 4) _lateRequest = request;
            Calls.Enqueue(request);
            if (request.ActorId == 2) return Task.FromResult(new ModelReply(Reply(request), Model: "fake-s7"));
            if (request.ActorId == 3) return Task.FromResult(new ModelReply(Reply(request, 999), Model: "fake-s7"));
            return _late.Task; // Deliberately ignores cancellation to test late delivery.
        }
        public void ReleaseLate() => _late.TrySetResult(new(_lateRequest is { } r ? Reply(r) : "{}", Model: "fake-s7"));
    }
}

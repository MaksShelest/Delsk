using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agents.Algorithmic;
using Simulation.Core;

namespace Agents.Llm;

public record DecisionSlot(ModelRequest Request, string Status = "Pending", ModelReply? Reply = null, bool Interrupted = false,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int Retries=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? InterruptionReason=null);
public record HybridState(HybridSettings Settings, ControllerCheckpoint Baseline, long NextDecisionNumber = 1,
    long NextEligibleTick = 0, long BudgetPeriod = 0, int RequestsInPeriod = 0, string LastTrigger = "",
    string Memory = "", long ResolvedDecisions = 0, DecisionSlot? Slot = null, DecisionSlot? BallotSlot = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] LocalGameModel? GameModel=null);

/// <summary>Model choice is optional; the existing algorithm executes all plans and fallbacks.</summary>
public sealed partial class HybridController : IController
{
    public const string PolicyKind = "hybrid-plan-v1";
    private readonly ModelRuntime _runtime;
    private readonly AlgorithmicController _baseline;
    private HybridState _state;
    private ControllerSessionContext? _context;
    public ParticipantId Actor => _baseline.Actor;
    public bool WaitingForModel => new[]{_state.Slot,_state.BallotSlot}.Any(s=>s is {Interrupted:false,Reply:null} && !_runtime.HasReply(s.Request));
    public HybridController(AlgorithmicController baseline, ModelRuntime runtime, HybridSettings? settings = null, LocalGameModel? gameModel=null)
    {
        var config = settings ?? gameModel?.Policy ?? new(); config.Validate(); gameModel?.Validate();
        if(gameModel is not null && config!=gameModel.Policy)throw new ArgumentException("Model policy mismatch");
        _baseline = baseline; _runtime = runtime;
        _state = new(config, baseline.Capture(),GameModel:gameModel);
    }
    public void BindSession(ControllerSessionContext context)
    {
        if (context.Epoch < 1 || context.Tick < 0) throw new ArgumentException("Invalid session context");
        _context = context; _runtime.Bind(context.SessionId, context.Epoch);
        if(context.Restored && _state.BallotSlot is { } ballot)
        {
            if(ballot.Request.RequestedTick!=context.Tick || ballot.Request.SessionId!=context.SessionId || ballot.Request.SessionEpoch>=context.Epoch)
                throw new CheckpointException("InvalidBallotSlotTime");
            _state=_state with {BallotSlot=ballot with {Interrupted=true}};
        }
        if (context.Restored && _state.Slot is { } slot)
        {
            if (slot.Request.ApplyTick <= context.Tick || slot.Request.RequestedTick > context.Tick ||
                slot.Request.SessionId != context.SessionId || slot.Request.SessionEpoch >= context.Epoch)
                throw new CheckpointException("InvalidDecisionSlotTime");
            _state = _state with { Slot = slot with { Interrupted = true } };
        }
    }
    public void BeginDecisionBarrier(long tick) => _runtime.Freeze(tick);
    public void EndDecisionBarrier(long tick) => _runtime.Pump(tick);
    public ControllerCheckpoint Capture() => new(PolicyKind, 1, Actor,
        JsonSerializer.Serialize(_state with { Baseline = _baseline.Capture() }, SimulationJson.Options), _baseline.Capture().PendingCommandIds);
    public static HybridController Restore(ControllerCheckpoint checkpoint, ModelRuntime runtime)
    {
        if (checkpoint.Kind != PolicyKind || checkpoint.Version != 1) throw new CheckpointException("UnknownControllerVersion");
        try
        {
            var state = JsonSerializer.Deserialize<HybridState>(checkpoint.StateJson, SimulationJson.Options)!;
            state.Settings.Validate(); state.GameModel?.Validate();
            if(state.GameModel is {} gm && (state.Settings with {Enabled=gm.Policy.Enabled}) != gm.Policy)throw new CheckpointException("ModelPolicyMismatch");
            if(new[]{state.Slot,state.BallotSlot}.Any(s=>s is not null && (s.Retries is <0 or >1 || s.InterruptionReason is not (null or "UserFallback") || s.InterruptionReason is not null && !s.Interrupted)))throw new CheckpointException("InvalidModelRetry");
            var baseline = AlgorithmicController.Restore(state.Baseline);
            if (baseline.Actor != checkpoint.Actor || !checkpoint.PendingCommandIds.SequenceEqual(state.Baseline.PendingCommandIds) ||
                state.NextDecisionNumber < 1 || state.NextEligibleTick < 0 || state.BudgetPeriod < 0 || state.RequestsInPeriod < 0 ||
                state.RequestsInPeriod > state.Settings.MaximumRequestsPerPeriod || state.LastTrigger.Length > 160 || state.Memory.Length > 200 || state.ResolvedDecisions < 0)
                throw new CheckpointException("InvalidHybridState");
            if (state.Slot is { } slot)
            {
                var r = slot.Request;
                if (slot.Status is not ("Pending" or "Ready") || (slot.Status == "Ready") != (slot.Reply is not null) ||
                    r.ActorId != baseline.Actor.Value || r.SessionEpoch < 1 || r.RequestedTick < 0 || r.ObservationVersion != r.RequestedTick ||
                    r.ApplyTick != checked(r.RequestedTick + state.Settings.ApplyDelayTicks) || r.ExpiresTick != checked(r.RequestedTick + state.Settings.DecisionTtlTicks) ||
                    r.Options.IsDefaultOrEmpty || r.Options.Length > 5 || r.Options.Select(p => p.Id).Distinct().Count() != r.Options.Length ||
                    r.Options.Any(p => p.Actions.IsDefault || p.Actions.Length > 100 || p.Actions.Any(a => !Enum.IsDefined(a.Kind))) ||
                    Encoding.UTF8.GetByteCount(r.Prompt) > 16384 || r.PromptHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(r.Prompt))) ||
                    slot.Reply?.Text?.Length > 4096 || r.DecisionId != $"{r.SessionId}:model:{r.ActorId}:{state.NextDecisionNumber - 1}")
                    throw new CheckpointException("InvalidDecisionSlot");
            }
            ValidateBallotSlot(state,baseline.Actor);
            return new(baseline, runtime, state.Settings) { _state = state };
        }
        catch (CheckpointException) { throw; }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or OverflowException)
        { throw new CheckpointException("InvalidHybridState", e); }
    }
    public void CommandSubmitted(string commandId) => _baseline.CommandSubmitted(commandId);
    public ControllerDecision Decide(ControllerObservation o, ImmutableArray<CommandResult> ownResults)
    {
        if (o.Participant.Id != Actor) throw new ArgumentException("Observation actor mismatch");
        var context = _context ?? throw new InvalidOperationException("BindSession required");
        if (_state.Slot is { } pending)
        {
            if (!pending.Interrupted && pending.Reply is null && _runtime.Read(pending.Request) is { } reply)
                _state = _state with { Slot = pending = pending with { Status = "Ready", Reply = reply } };
            if (o.Tick + 1 < pending.Request.ApplyTick)
                return _baseline.RequiresUninterruptedAlgorithm(o) || ownResults.Length > 0 ? _baseline.Decide(o, ownResults)
                    : new("ModelPending", "DecisionSlotPending", [], Record: false);
            return Resolve(o, ownResults, pending, context);
        }
        if (!_state.Settings.Enabled) return _baseline.Decide(o, ownResults);
        var fallback = _baseline.Decide(o, ownResults);
        if (!_runtime.Enabled) return fallback;
        if (o.Tick < _state.NextEligibleTick || o.Participant.Busy || fallback.Actions.Length == 0) return fallback;
        long period = o.Tick / _state.Settings.BudgetPeriodTicks;
        if (period != _state.BudgetPeriod) _state = _state with { BudgetPeriod = period, RequestsInPeriod = 0 };
        if (_state.RequestsInPeriod >= _state.Settings.MaximumRequestsPerPeriod) return fallback;
        var changes = new
        {
            Negotiations=o.Negotiations.Where(n=>n.Status==NegotiationStatus.Proposed).Select(n=>new {n.Id,n.Current.Version}),
            Support = o.Support.Proposals.Where(p => p.Seller == Actor && p.Status == SupportProposalStatus.Proposed).Select(p => p.Id),
            Prices = o.CapitalPrices.Select(p => new { p.ItemId, Bucket = p.UnitPriceMinor / Math.Max(1, o.Map.Items.Single(i => i.Id == p.ItemId).BasePriceMinor / 5) })
        };
        string trigger = fallback.Goal + ":" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(changes, SimulationJson.Options)))[..16];
        if (trigger == _state.LastTrigger && !ownResults.Any(r => r.Status == ResultStatus.Rejected)) return fallback;
        var choices = _baseline.ModelOptions(o);
        if (choices.Length < 2) return fallback;
        ModelRequest request;
        try { request = ModelPrompt.Build(o, choices, $"{context.SessionId}:model:{Actor.Value}:{_state.NextDecisionNumber}",
            context, _state.Settings, trigger, _state.Memory, _baseline.ModelPolicySettings, _baseline.ModelTrust); }
        catch (ArgumentException)
        {
            _state = _state with { NextEligibleTick = checked(o.Tick + _state.Settings.MinimumIntervalTicks) };
            return fallback with { Reason = "ModelContextTooLarge" };
        }
        bool queued = _runtime.Enqueue(request);
        _baseline.DeferUnsubmittedDecision();
        _state = _state with { Slot = new(request, queued ? "Pending" : "Ready", queued ? null : new(null, "QueueFull")),
            NextDecisionNumber = checked(_state.NextDecisionNumber + 1), RequestsInPeriod = _state.RequestsInPeriod + 1,
            NextEligibleTick = checked(o.Tick + _state.Settings.MinimumIntervalTicks), LastTrigger = trigger };
        return new("ModelPending", "DecisionSlotOpened", [], ModelTrace: Trace(request, "Requested", "DecisionSlotOpened"));
    }
    private ControllerDecision Resolve(ControllerObservation o, ImmutableArray<CommandResult> results, DecisionSlot slot, ControllerSessionContext context)
    {
        var r = slot.Request; var fallback = _baseline.Decide(o, results);
        string reason = slot.Interrupted ? slot.InterruptionReason??"SessionRestored" : o.Tick + 1 != r.ApplyTick ? "ApplyTickMissed" :
            o.Tick + 1 > r.ExpiresTick ? "DecisionExpired" : r.SessionEpoch != context.Epoch ? "StaleSessionEpoch" :
            slot.Reply?.Outcome ?? "DecisionDeadline";
        var decision = fallback.Actions.Length == 0 ? new ControllerDecision("Hold", "NoFeasibleFallback", []) : fallback;
        bool applied = false;
        if (reason == "Completed" && StrictIntentParser.TryParse(slot.Reply!.Text, r, out var intent, out reason))
        {
            var option = r.Options.Single(p => p.Id == intent!.OptionId);
            applied = _baseline.TrySelectModelOption(o, option, out var selected);
            if (applied) decision = selected; else reason = "StaleRelevantState";
        }
        _runtime.Cancel(r);
        _state = _state with { Slot = null, ResolvedDecisions = checked(_state.ResolvedDecisions + 1), Memory = "Last executed goal: " + decision.Goal };
        return decision with { Record = true, ModelTrace = Trace(r, applied ? "ModelResolved" : "FallbackResolved", applied ? "Accepted" : reason, slot.Reply) with {RetryCount=slot.Retries} };
    }
    private static ModelDecisionTrace Trace(ModelRequest r, string resolution, string reason, ModelReply? reply = null)
        => new(r.DecisionId, r.RequestedTick, r.ApplyTick, r.SessionEpoch, resolution, reason,
            JsonSerializer.Serialize(r, SimulationJson.Options), reply?.Text, reply?.LatencyMilliseconds, reply?.PromptTokens, reply?.CompletionTokens, reply?.Model);
}

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public record ControllerTurn(long Tick, ParticipantId Actor, ControllerObservation Observation,
    ImmutableArray<CommandResult> Feedback, ControllerDecision Decision, ImmutableArray<string> CommandIds, bool Voting = false);
public record ControllerSample(long Tick, long ActorId, long CashMinor, long StockBaseValueMinor,
    long StockUnits, int Headcount, int Enterprises, int Equipment, bool Busy, string Goal, string Reason);
internal record SavedControllers(ControllerCheckpoint[] States, KeyValuePair<long, string[]>[] Awaiting,
    ControllerTurn[] Turns, ControllerSample[] Samples, string StateHash);

public sealed partial class GameSession
{
    private IController[] _controllers = [];
    private ControllerCheckpoint[] _unrestoredControllers = [];
    private readonly Dictionary<long, List<string>> _controllerAwaiting = [];
    private readonly List<ControllerTurn> _controllerTurns = [];
    private readonly List<ControllerSample> _controllerSamples = [];
    private long _ballotBarrier;
    public bool PumpVoting()
    {
        if (!Engine.WaitingForBallots) return true;
        if (_unrestoredControllers.Length > 0) throw new InvalidOperationException("RestoreControllers required");
        long barrier = ++_ballotBarrier;
        foreach (var c in _controllers) c.BeginBallotBarrier(barrier);
        var observations = _controllers.Select(c=>Engine.ObserveController(c.Actor)).ToArray();
        var decisions = _controllers.Select((c,i)=>observations[i].Elections.Voting!.Awaiting.Contains(c.Actor) ? c.DecideBallot(observations[i]) : null).ToArray();
        long election = observations.FirstOrDefault()?.Elections.Voting?.Election.Value ?? 0;
        foreach (var c in _controllers) c.EndBallotBarrier(barrier,election);
        for (int i=0;i<_controllers.Length;i++)
        {
            if (decisions[i] is not { } decision) continue;
            var actions = decision.Candidate is { } candidate ? ImmutableArray.Create(new GameAction(ActionKind.SetVoteChoice,
                Election: observations[i].Elections.Voting!.Election, Candidate: candidate)) : [];
            var ids=actions.Select(a=>Send(_controllers[i].Actor,a)).ToImmutableArray();
            if (ids.Length>0 || decision.ModelTrace is not null)
                RecordTurn(new(Engine.Tick,_controllers[i].Actor,observations[i],[],new("Ballot",decision.Reason,actions,ModelTrace:decision.ModelTrace),ids,true));
        }
        return !Engine.WaitingForBallots;
    }
    public bool HasControllers => _controllers.Length > 0 || _unrestoredControllers.Length > 0;
    public bool WaitingForModel => _controllers.Any(c=>c.WaitingForModel);
    public event Action<ControllerTurn>? ControllerDecided;
    private void RecordTurn(ControllerTurn turn) { _controllerTurns.Add(turn);ControllerDecided?.Invoke(turn); }
    public ControllerCheckpoint[] ControllerStates => _unrestoredControllers.Length > 0 ? _unrestoredControllers.ToArray() :
        _controllers.OrderBy(c => c.Actor.Value).Select(c => c.Capture()).ToArray();
    public ControllerTurn[] ControllerTurns => _controllerTurns.ToArray();
    public ControllerSample[] ControllerSamples => _controllerSamples.ToArray();
    public void AttachControllers(IEnumerable<IController> controllers)
    {
        if (Engine.Tick != 0 || HasControllers || _playback is not null) throw new InvalidOperationException("Controllers/playback already configured");
        var array = controllers.OrderBy(c => c.Actor.Value).ToArray();
        var participants = Engine.Inspect().Participants.Select(p => p.Id).ToHashSet();
        if (array.Length == 0 || array.Select(c => c.Actor).Distinct().Count() != array.Length || array.Any(c => !participants.Contains(c.Actor)))
            throw new ArgumentException("Invalid controller participants");
        _controllers = array;
        foreach (var controller in array)
        {
            _controllerAwaiting.Add(controller.Actor.Value, []);
            controller.BindSession(new(SessionId, SessionEpoch, Engine.Tick, false));
        }
    }
    // The composition root knows concrete policies; Hosting has no dependency on Algorithmic/LLM.
    public void RestoreControllers(Func<ControllerCheckpoint, IController> restore)
    {
        if (_unrestoredControllers.Length == 0) return;
        var restored = _unrestoredControllers.Select(restore).ToArray();
        if (restored.Where((c, i) => c.Actor != _unrestoredControllers[i].Actor || JsonSerializer.Serialize(c.Capture(), SimulationJson.Options) !=
                JsonSerializer.Serialize(_unrestoredControllers[i], SimulationJson.Options)).Any())
            throw new CheckpointException("ControllerRestoreMismatch");
        _controllers = restored; _unrestoredControllers = [];
        foreach (var controller in restored) controller.BindSession(new(SessionId, SessionEpoch, Engine.Tick, true));
    }
    private void SubmitControllerInputs()
    {
        if (_unrestoredControllers.Length > 0) throw new InvalidOperationException("RestoreControllers required before advancing this checkpoint");
        if (_controllers.Length == 0) return;
        foreach (var controller in _controllers) controller.BeginDecisionBarrier(Engine.Tick);
        // Freeze every observation before any policy or submission is called. World mutations still occur only in Step.
        var observations = _controllers.Select(c => Engine.ObserveController(c.Actor)).ToArray();
        var decisions = new ControllerDecision[_controllers.Length];
        var feedback = new ImmutableArray<CommandResult>[_controllers.Length];
        for (int i = 0; i < _controllers.Length; i++)
        {
            var awaiting = _controllerAwaiting[_controllers[i].Actor.Value];
            feedback[i] = awaiting.Select(Engine.Result).Where(r => r is not null).Select(r => r!).ToImmutableArray();
            decisions[i] = _controllers[i].Decide(observations[i], feedback[i]);
            if (decisions[i].Actions.IsDefault || decisions[i].Actions.Length > Engine.Configuration().MaxCommandsPerActorTick)
                throw new InvalidOperationException("Controller exceeded equal command budget");
            awaiting.RemoveAll(id => feedback[i].Any(r => r.CommandId == id));
        }
        foreach (var controller in _controllers) controller.EndDecisionBarrier(Engine.Tick);
        for (int i = 0; i < _controllers.Length; i++)
        {
            var ids = ImmutableArray.CreateBuilder<string>();
            foreach (var action in decisions[i].Actions)
            {
                string id = Send(_controllers[i].Actor, action); ids.Add(id);
                _controllerAwaiting[_controllers[i].Actor.Value].Add(id); _controllers[i].CommandSubmitted(id);
            }
            if (decisions[i].Record || feedback[i].Length > 0 || ids.Count > 0)
                RecordTurn(new(Engine.Tick, _controllers[i].Actor, observations[i], feedback[i], decisions[i], ids.ToImmutable()));
        }
    }
    private void SampleControllers(WorldView world)
    {
        var items = Engine.Configuration().Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var c in _controllers)
        {
            var actor = world.Participants.Single(p => p.Id == c.Actor);
            var stocks = world.Inventories.Where(i => i.OwnerId == c.Actor).SelectMany(i => i.Stock).ToArray();
            var last = _controllerTurns.LastOrDefault(t => t.Actor == c.Actor);
            _controllerSamples.Add(new(world.Tick, c.Actor.Value, world.Accounts.Single(a => a.Id == actor.AccountId).BalanceMinor,
                stocks.Sum(s => checked(s.Quantity * items[s.ItemId].BasePriceMinor)), stocks.Sum(s => s.Quantity),
                world.EmploymentGroups.Where(g => g.OwnerId == c.Actor).Sum(g => g.Headcount),
                world.Enterprises.Count(e => e.OwnerId == c.Actor), world.Equipment.Count(e => e.OwnerId == c.Actor),
                actor.Busy, last?.Decision.Goal ?? "Idle", last?.Decision.Reason ?? "Initial"));
        }
    }
    private static string DigestControllerState(ControllerCheckpoint[] states, KeyValuePair<long, string[]>[] awaiting)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { States = states, Awaiting = awaiting }, SimulationJson.Options))));
    private SavedControllers SaveControllers()
    {
        var states = ControllerStates;
        var awaiting = _controllerAwaiting.OrderBy(x => x.Key).Select(x => new KeyValuePair<long, string[]>(x.Key, x.Value.ToArray())).ToArray();
        return new(states, awaiting, ControllerTurns, ControllerSamples, DigestControllerState(states, awaiting));
    }
    public string ComputeControllerStateHash()
    {
        var saved = SaveControllers(); return saved.StateHash;
    }
    private void LoadControllers(SavedControllers saved)
    {
        var participants = Engine.Inspect().Participants.Select(p => p.Id.Value).ToHashSet();
        var ids = saved.States.Select(c => c.Actor.Value).ToHashSet();
        if (saved.States.Length != ids.Count || saved.States.Any(c => !participants.Contains(c.Actor.Value) ||
            string.IsNullOrWhiteSpace(c.Kind) || c.Kind.Length > 80 || c.Version < 1 || c.StateJson.Length > 1024 * 1024 ||
            c.PendingCommandIds.IsDefault || !c.PendingCommandIds.SequenceEqual(saved.Awaiting.SingleOrDefault(p => p.Key == c.Actor.Value).Value ?? [])) ||
            saved.Awaiting.Length != ids.Count || saved.Awaiting.Select(p => p.Key).Distinct().Count() != ids.Count ||
            saved.Awaiting.Any(p => !ids.Contains(p.Key) || p.Value.Distinct().Count() != p.Value.Length ||
                p.Value.Any(id => !_tape.Any(t => t.AuthenticatedActor.Value == p.Key && t.Command.CommandId == id))) ||
            saved.Turns.Where((t, i) => i > 0 && t.Tick < saved.Turns[i - 1].Tick).Any() ||
            saved.Turns.Any(t => !ids.Contains(t.Actor.Value) || t.Tick < 0 || t.Tick > Engine.Tick || (t.Tick == Engine.Tick && !t.Voting) ||
                t.Observation.Tick != t.Tick || t.Observation.Participant.Id != t.Actor ||
                t.CommandIds.Any(id => !_tape.Any(c => c.AuthenticatedActor == t.Actor && c.SubmittedAtTick == t.Tick && c.Command.CommandId == id))) ||
            saved.Samples.GroupBy(s => s.ActorId).Any(g => g.Count() != Engine.Tick ||
                g.Where((s, i) => s.Tick != i + 1).Any()) ||
            (saved.States.Length > 0 && saved.Samples.Select(s => s.ActorId).Distinct().Count() != saved.States.Length && Engine.Tick > 0) ||
            saved.Samples.Any(s => !ids.Contains(s.ActorId) || s.Tick < 1 || s.Tick > Engine.Tick || s.CashMinor < 0 || s.Headcount < 0) ||
            saved.StateHash != DigestControllerState(saved.States, saved.Awaiting) || (_playback is not null && ids.Count > 0))
            throw new CheckpointException("InvalidSessionControllers");
        _unrestoredControllers = saved.States;
        foreach (var pair in saved.Awaiting) _controllerAwaiting.Add(pair.Key, pair.Value.ToList());
        _controllerTurns.AddRange(saved.Turns); _controllerSamples.AddRange(saved.Samples);
    }
}

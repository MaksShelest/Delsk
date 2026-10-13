using Simulation.Core;

namespace Simulation.Hosting;

public record RecordedCommand(long SubmittedAtTick, ParticipantId AuthenticatedActor, CommandEnvelope Command);
public record TickSample(long Tick, long ParticipantCashMinor, long WageExpenseMinor, long EnergyExpenseMinor,
    long Harvested, long Produced, long Exported, string StateHash);

/// <summary>Common adapter for console and future client. The core owns every game rule.</summary>
public sealed class GameSession(SimulationEngine engine, string sessionId = "s1", Action? beforeStep = null)
{
    private readonly Dictionary<long, long> _sequences = [];
    private readonly List<RecordedCommand> _tape = [];
    private readonly List<TickSample> _samples = [];
    public SimulationEngine Engine { get; } = engine;
    public bool Paused { get; private set; }
    public RecordedCommand[] CommandTape => _tape.ToArray();
    public TickSample[] Samples => _samples.ToArray();
    public string Send(ParticipantId actor, GameAction action, long delayTicks = 1, long ttlTicks = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(ttlTicks);
        long sequence = checked(_sequences.GetValueOrDefault(actor.Value) + 1);
        long notBefore = checked(Engine.Tick + delayTicks);
        var envelope = new CommandEnvelope($"{sessionId}:{actor.Value}:{sequence}", actor, sequence,
            Engine.Tick, notBefore, checked(notBefore + ttlTicks), action);
        var immediate = Submit(actor, envelope);
        if (immediate is { Status: ResultStatus.Rejected })
            throw new InvalidOperationException($"Submission rejected: {immediate.ReasonCode}");
        return envelope.CommandId;
    }
    public CommandResult? Submit(ParticipantId actor, CommandEnvelope command)
    {
        _tape.Add(new(Engine.Tick, actor, command));
        _sequences[actor.Value] = Math.Max(_sequences.GetValueOrDefault(actor.Value), command.LocalSequence);
        return Engine.Enqueue(actor, command);
    }
    public void Step()
    {
        if (Paused) return;
        beforeStep?.Invoke();
        Engine.Step();
        var state = Engine.Inspect();
        var actor = state.Participants[0];
        _samples.Add(new(state.Tick, state.Accounts.Single(a => a.Id == actor.AccountId).BalanceMinor,
            state.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor,
            state.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor,
            state.Flows.Sum(f => f.Harvested), state.Flows.Sum(f => f.Produced),
            state.Flows.Sum(f => f.Exported), Engine.ComputeStateHash()));
    }
    public void SetPaused(bool paused) => Paused = paused;
    public static GameSession Replay(ScenarioDefinition definition, RecordedCommand[] tape, long finalTick)
    {
        if (finalTick < 0 || finalTick > definition.MaxTicks || tape.Any(x => x.SubmittedAtTick < 0 || x.SubmittedAtTick > finalTick))
            throw new ArgumentException("Invalid replay horizon");
        var session = new GameSession(new SimulationEngine(definition), "replay");
        var groups = tape.GroupBy(x => x.SubmittedAtTick).ToDictionary(g => g.Key, g => g.ToArray());
        while (true)
        {
            if (groups.TryGetValue(session.Engine.Tick, out var inputs))
                foreach (var input in inputs) session.Submit(input.AuthenticatedActor, input.Command);
            if (session.Engine.Tick == finalTick) break;
            session.Step();
        }
        return session;
    }
}

/// <summary>The caller supplies monotonic elapsed time; nothing in the core reads real time.</summary>
public sealed class TickScheduler
{
    private readonly GameSession _session;
    private readonly long _tickDuration;
    private long _accumulator;
    public TickScheduler(GameSession session, TimeSpan? tickDuration = null)
    {
        _session = session; _tickDuration = (tickDuration ?? TimeSpan.FromSeconds(1)).Ticks;
        if (_tickDuration <= 0) throw new ArgumentException("Tick duration must be positive");
    }
    public void Headless(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        for (long i = 0; i < count && !_session.Paused; i++) _session.Step();
    }
    public void AdvanceElapsed(TimeSpan elapsed)
    {
        if (elapsed.Ticks < 0) throw new ArgumentException("Elapsed time cannot be negative");
        if (_session.Paused) { _accumulator = 0; return; }
        _accumulator = checked(_accumulator + elapsed.Ticks);
        while (_accumulator >= _tickDuration)
        { _session.Step(); _accumulator -= _tickDuration; }
    }
    public void SetPaused(bool paused)
    { _accumulator = 0; _session.SetPaused(paused); }
}

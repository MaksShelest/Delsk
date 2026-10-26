using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

public record RecordedCommand(long SubmittedAtTick, ParticipantId AuthenticatedActor, CommandEnvelope Command,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TravelQuantum = 0);
public record TickSample(long Tick, long ParticipantCashMinor, long WageExpenseMinor, long EnergyExpenseMinor,
    long Harvested, long Produced, long Exported, string StateHash, long Population, long Unemployed);
public record LaborSample(long Tick, EmploymentView[] EmploymentGroups);

/// <summary>Common adapter for console and future client. The core owns every game rule.</summary>
public sealed partial class GameSession(SimulationEngine engine, string sessionId = "s1", Action? beforeStep = null)
{
    private readonly Dictionary<long, long> _sequences = [];
    private readonly List<RecordedCommand> _tape = [];
    private readonly List<TickSample> _samples = [];
    private readonly List<LaborSample> _laborSamples = [];
    public SimulationEngine Engine { get; } = engine;
    public string SessionId { get; } = sessionId;
    public long SessionEpoch { get; private set; } = 1;
    public event Action<GameSession>? TickCompleted;
    internal event Action<StepTiming>? StepMeasured;
    public bool Paused { get; private set; }
    public RecordedCommand[] CommandTape => _tape.ToArray();
    public TickSample[] Samples => _samples.ToArray();
    public LaborSample[] LaborSamples => _laborSamples.Select(s => s with { EmploymentGroups = s.EmploymentGroups.ToArray() }).ToArray();
    public string Send(ParticipantId actor, GameAction action, long delayTicks = 1, long ttlTicks = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(ttlTicks);
        long sequence = checked(_sequences.GetValueOrDefault(actor.Value) + 1);
        bool ballot = Engine.WaitingForBallots && action.Kind == ActionKind.SetVoteChoice;
        long notBefore = ballot ? Engine.Tick : checked(Engine.Tick + delayTicks);
        var envelope = new CommandEnvelope($"{SessionId}:{actor.Value}:{sequence}", actor, sequence,
            Engine.Tick, notBefore, ballot ? notBefore : checked(notBefore + ttlTicks), action);
        var immediate = Submit(actor, envelope);
        if (immediate is { Status: ResultStatus.Rejected } && Engine.Result(envelope.CommandId) is null)
            throw new InvalidOperationException($"Submission rejected: {immediate.ReasonCode}");
        return envelope.CommandId;
    }
    public CommandResult? Submit(ParticipantId actor, CommandEnvelope command)
    {
        _tape.Add(new(Engine.Tick, actor, command));
        _sequences[actor.Value] = Math.Max(_sequences.GetValueOrDefault(actor.Value), command.LocalSequence);
        var result = Engine.Enqueue(actor, command);
        if (result is not null && _samples.Count > 0 && _samples[^1].Tick == Engine.Tick)
            _samples[^1] = _samples[^1] with { StateHash = Engine.ComputeStateHash() };
        return result;
    }
    public void Step()
    {
        if (Engine.WaitingForBallots) { SubmitPlaybackInputs(); PumpVoting(); return; }
        if (Paused) return;
        beforeStep?.Invoke();
        SubmitPlaybackInputs();
        bool measured = StepMeasured is not null;
        long start = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        SubmitControllerInputs();
        long coreStart = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        Engine.Step();
        long sampleStart = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var state = Engine.Inspect();
        var actor = state.Participants[0];
        _samples.Add(new(state.Tick, state.Accounts.Single(a => a.Id == actor.AccountId).BalanceMinor,
            state.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor,
            state.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor,
            state.Flows.Sum(f => f.Harvested), state.Flows.Sum(f => f.Produced),
            state.Flows.Sum(f => f.Exported), Engine.ComputeStateHash(), state.Population, state.Unemployed));
        _laborSamples.Add(new(state.Tick, state.EmploymentGroups));
        SampleControllers(state);
        TickCompleted?.Invoke(this);
        if (measured) StepMeasured?.Invoke(new(
            System.Diagnostics.Stopwatch.GetElapsedTime(start,coreStart).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(coreStart,sampleStart).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(sampleStart).TotalMilliseconds));
    }
    public void SetPaused(bool paused) => Paused = paused;
    public void SetTravelHeading(ParticipantId actor, int x, int z)
    {
        if (x is < -1 or > 1 || z is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(x));
        long sequence = checked(_sequences.GetValueOrDefault(actor.Value) + 1);
        var envelope = new CommandEnvelope($"{SessionId}:{actor.Value}:{sequence}", actor, sequence,
            Engine.Tick, Engine.Tick, Engine.Tick, new GameAction(ActionKind.SetTravelHeading, new Cell(x, z)));
        Engine.SetTravelHeading(actor, x, z);
        _sequences[actor.Value] = sequence;
        _tape.Add(new(Engine.Tick, actor, envelope, Engine.TravelQuantum));
    }

    public void AdvanceTravelQuantum()
    {
        if (Engine.WaitingForBallots) { PumpVoting(); return; }
        if (Paused) return;
        PumpResearchPlayback();
        ApplyHeadingPlayback();
        if (Engine.MoveTravelQuantum()) Step();
    }

    public void CompleteTravelTick()
    {
        if (Paused) return;
        int guard = 0;
        do
        {
            AdvanceTravelQuantum();
            if (++guard > SimulationEngine.TravelQuantaPerTick)
                throw new InvalidOperationException("Travel tick did not close");
        }
        while (Engine.TravelQuantum != 0);
    }

    public void UseHeadingPlayback(RecordedCommand[] tape)
    {
        if (tape.Any(c => c.Command.Action.Kind == ActionKind.SetTravelHeading &&
            (c.TravelQuantum < 0 || c.TravelQuantum >= SimulationEngine.TravelQuantaPerTick ||
                c.Command.Action.Target.X is < -1 or > 1 || c.Command.Action.Target.Z is < -1 or > 1)))
            throw new ArgumentException("Invalid travel playback");
        _headingPlayback = tape.Where(c => c.Command.Action.Kind == ActionKind.SetTravelHeading)
            .OrderBy(c => c.SubmittedAtTick).ThenBy(c => c.TravelQuantum).ThenBy(c => c.Command.LocalSequence).ToArray();
        _headingPlaybackIndex = 0;
    }

    private void ApplyHeadingPlayback()
    {
        while (_headingPlaybackIndex < _headingPlayback.Length)
        {
            var input = _headingPlayback[_headingPlaybackIndex];
            if (input.SubmittedAtTick > Engine.Tick ||
                (input.SubmittedAtTick == Engine.Tick && input.TravelQuantum > Engine.TravelQuantum))
                return;
            if (input.SubmittedAtTick != Engine.Tick || input.TravelQuantum != Engine.TravelQuantum)
                throw new InvalidOperationException("Playback input missed its barrier");
            Engine.SetTravelHeading(input.AuthenticatedActor, input.Command.Action.Target.X, input.Command.Action.Target.Z);
            _tape.Add(input);
            PumpResearchPlayback();
            _sequences[input.AuthenticatedActor.Value] = Math.Max(
                _sequences.GetValueOrDefault(input.AuthenticatedActor.Value), input.Command.LocalSequence);
            _headingPlaybackIndex++;
        }
    }

    public static GameSession Replay(ScenarioDefinition definition, RecordedCommand[] tape, long finalTick,ResearchModification[]? modifications=null)
    {
        if (finalTick < 0 || finalTick > definition.MaxTicks || tape.Any(x => x.SubmittedAtTick < 0 || x.SubmittedAtTick > finalTick))
            throw new ArgumentException("Invalid replay horizon");
        var session = new GameSession(new SimulationEngine(definition), "replay");
        session._researchPlayback=modifications??[];
        bool travel = tape.Any(x => x.Command.Action.Kind == ActionKind.SetTravelHeading)||(modifications??[]).Any(m=>m.TravelQuantum!=0);
        session.ConfigurePlayback(travel ? tape.Where(x => x.Command.Action.Kind != ActionKind.SetTravelHeading).ToArray() : tape, finalTick);
        if (travel) session.UseHeadingPlayback(tape);
        while (session.Engine.Tick < finalTick)
        {
            long prior = session.Engine.Tick;
            if (travel) session.AdvanceTravelQuantum();
            else session.Step();
            if (session.Engine.Tick == prior && session.Engine.WaitingForBallots)
                throw new InvalidOperationException("Replay requires recorded ballots at the frozen boundary");
        }
        session.SubmitPlaybackInputs(); // Preserve inputs submitted at the final published barrier.
        if (travel) session.ApplyHeadingPlayback();
        int finalQuantum=(modifications??[]).Where(m=>m.Tick==finalTick).Select(m=>m.TravelQuantum).DefaultIfEmpty(0).Max();
        while(session.Engine.TravelQuantum<finalQuantum)session.AdvanceTravelQuantum();
        session.PumpResearchPlayback();
        return session;
    }
}

/// <summary>The caller supplies monotonic elapsed time; nothing in the core reads real time.</summary>
public sealed class TickScheduler
{
    private readonly GameSession _session;
    private readonly long _tickDuration;
    private readonly long _quantumDuration;
    private long _accumulator;
    private readonly Func<bool>? _controllerBarrier;
    public TickScheduler(GameSession session, TimeSpan? tickDuration = null, Func<bool>? controllerBarrier=null)
    {
        _session = session; _controllerBarrier=controllerBarrier; _tickDuration = (tickDuration ?? TimeSpan.FromSeconds(1)).Ticks;
        if (_tickDuration <= 0 || _tickDuration % SimulationEngine.TravelQuantaPerTick != 0)
            throw new ArgumentException("Tick duration must be positive and divide into equal travel quanta");
        _quantumDuration = _tickDuration / SimulationEngine.TravelQuantaPerTick;
    }
    public void Headless(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        for (long i = 0; i < count && !_session.Paused; i++) _session.Step();
    }
    public void AdvanceElapsed(TimeSpan elapsed)
    {
        if (elapsed.Ticks < 0) throw new ArgumentException("Elapsed time cannot be negative");
        if (_session.Engine.WaitingForBallots) { _accumulator=0; _session.PumpVoting(); return; }
        if (_session.Paused || _controllerBarrier?.Invoke()==true) { _accumulator = 0; return; }
        _accumulator = checked(_accumulator + elapsed.Ticks);
        while (_accumulator >= _quantumDuration)
        { _session.AdvanceTravelQuantum(); _accumulator -= _quantumDuration;
          if (_session.Engine.WaitingForBallots || _controllerBarrier?.Invoke()==true) { _accumulator=0; break; } }
    }
    public void SetPaused(bool paused)
    { _accumulator = 0; _session.SetPaused(paused); }
}

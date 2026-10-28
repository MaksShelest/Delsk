using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public record RecordedPlaybackState(RecordedCommand[] Commands, long FinalTick, int NextIndex, string Label = "recorded-inputs");
internal record SavedSession(int SchemaVersion, string SessionId, long Epoch, bool Paused,
    string WorldJson, KeyValuePair<long, long>[] Sequences, RecordedCommand[] Tape, TickSample[] Samples,
    LaborSample[] LaborSamples, RecordedPlaybackState? Playback, SavedControllers? Controllers = null,
    ResearchModification[]? ResearchPlayback=null,int ResearchIndex=0);

public sealed partial class GameSession
{
    private RecordedPlaybackState? _playback;
    // One persisted cursor preserves global input order, including headings and god edits.
    private bool _closingTravelTick, _playbackNeedsTravel;
    private bool PlaybackUsesTravel => _playbackNeedsTravel || _researchPlayback.Any(m => m.TravelQuantum != 0);
    public long? PlaybackFinalTick => _playback?.FinalTick;
    public string? PlaybackLabel => _playback?.Label;
    public void ConfigurePlayback(RecordedCommand[] tape, long finalTick, string label = "recorded-inputs")
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 80) throw new ArgumentException("Invalid playback label");
        if (HasControllers || _playback is not null) throw new InvalidOperationException("Playback already configured");
        if (finalTick < Engine.Tick || finalTick > Engine.Configuration().MaxTicks || tape.Any(c =>
            c.SubmittedAtTick < Engine.Tick || c.SubmittedAtTick > finalTick ||
            c.TravelQuantum < 0 || c.TravelQuantum >= SimulationEngine.TravelQuantaPerTick))
            throw new ArgumentException("Invalid playback horizon");
        _playbackNeedsTravel = tape.Any(c => c.TravelQuantum != 0 || c.Command.Action.Kind == ActionKind.SetTravelHeading);
        _playback = new(tape.OrderBy(c => c.SubmittedAtTick).ToArray(), finalTick, 0, label);
    }
    private void SubmitPlaybackInputs()
    {
        PumpResearchPlayback();
        if (_playback is not { } p) return;
        int next = p.NextIndex;
        while (next < p.Commands.Length && p.Commands[next].SubmittedAtTick <= Engine.Tick)
        {
            var input = p.Commands[next];
            if (input.SubmittedAtTick < Engine.Tick) throw new InvalidOperationException("Playback input missed its barrier");
            if (input.TravelQuantum > Engine.TravelQuantum) break;
            // A research edit is a barrier in the same ordered input stream. In older
            // tapes ordinary inputs omit their quantum; the edit supplies its lower bound.
            if (_researchIndex < _researchPlayback.Length &&
                _researchPlayback[_researchIndex] is var edit && edit.AfterCommandCount == _tape.Count && edit.Tick == Engine.Tick)
                break;
            if (input.Command.Action.Kind == ActionKind.SetTravelHeading)
            {
                Engine.SetTravelHeading(input.AuthenticatedActor, input.Command.Action.Target.X, input.Command.Action.Target.Z);
                _tape.Add(input);
                _sequences[input.AuthenticatedActor.Value] = Math.Max(_sequences.GetValueOrDefault(input.AuthenticatedActor.Value), input.Command.LocalSequence);
            }
            else { Submit(input.AuthenticatedActor, input.Command); _tape[^1] = input; }
            ++next; PumpResearchPlayback();
        }
        _playback = p with { NextIndex = next };
    }
    public void AdvanceTo(long finalTick)
    {
        if (Paused || finalTick < Engine.Tick || finalTick > Engine.Configuration().MaxTicks)
            throw new ArgumentException("Invalid or paused advance horizon");
        while (Engine.Tick < finalTick)
        {
            long prior=Engine.Tick; Step();
            if (Engine.Tick==prior && Engine.WaitingForBallots) throw new InvalidOperationException("ElectionWaitingForBallots");
        }
        FlushPlaybackBoundary();
    }
    private void FlushPlaybackBoundary()
    {
        int quantum = Math.Max(
            _playback?.Commands.Skip(_playback.NextIndex).Where(c => c.SubmittedAtTick == Engine.Tick).Select(c => c.TravelQuantum).DefaultIfEmpty(0).Max() ?? 0,
            _researchPlayback.Skip(_researchIndex).Where(m => m.Tick == Engine.Tick).Select(m => m.TravelQuantum).DefaultIfEmpty(0).Max());
        while (Engine.TravelQuantum < quantum)
        {
            if (Paused || Engine.WaitingForBallots) throw new InvalidOperationException("Cannot advance a frozen playback boundary");
            AdvanceTravelQuantum();
        }
        SubmitPlaybackInputs();
    }
    public CommandResult? SubmitFromEpoch(long epoch, ParticipantId actor, CommandEnvelope command)
        => epoch == SessionEpoch ? Submit(actor, command)
            : new(command.CommandId, ResultStatus.Rejected, "StaleSessionEpoch", Engine.Tick);

    internal string CreateSessionJson()
    {
        var controllers=SaveControllers();
        return JsonSerializer.Serialize(new SavedSession(controllers.ObservationArchive is null?2:3, SessionId, SessionEpoch, Paused,
            Engine.CreateCheckpointJson(), _sequences.OrderBy(x => x.Key).ToArray(), CommandTape, Samples,
            LaborSamples, _playback, controllers,_researchPlayback,_researchIndex), SimulationJson.Options);
    }
    internal static GameSession FromSessionJson(string json, Action? beforeStep = null)
    {
        try
        {
            var s = JsonSerializer.Deserialize<SavedSession>(json, SimulationJson.Options)
                ?? throw new CheckpointException("EmptySessionCheckpoint");
            if (s.SchemaVersion is not (1 or 2 or 3)) throw new CheckpointException("UnknownSessionSchema");
            if (string.IsNullOrWhiteSpace(s.SessionId) || s.SessionId.Length > 80 || s.Epoch < 1 || s.Epoch == long.MaxValue)
                throw new CheckpointException("InvalidSessionIdentity");
            var session = new GameSession(SimulationEngine.FromCheckpointJson(s.WorldJson), s.SessionId, beforeStep)
                { SessionEpoch = s.Epoch + 1, Paused = s.Paused, _playback = s.Playback };
            session._playbackNeedsTravel = s.Playback?.Commands.Any(c => c.TravelQuantum != 0 || c.Command.Action.Kind == ActionKind.SetTravelHeading) ?? false;
            session._researchPlayback=s.ResearchPlayback??[];session._researchIndex=s.ResearchIndex;
            if(s.ResearchIndex<0 || s.ResearchIndex>session._researchPlayback.Length ||
                session._researchPlayback.Take(s.ResearchIndex).Any(m=>!session.Engine.ResearchModifications.Contains(m)) ||
                session._researchPlayback.Skip(s.ResearchIndex).Any(m=>m.Tick<session.Engine.Tick))throw new CheckpointException("InvalidResearchPlayback");
            foreach (var pair in s.Sequences) session._sequences.Add(pair.Key, pair.Value);
            session._tape.AddRange(s.Tape); session._samples.AddRange(s.Samples); session._laborSamples.AddRange(s.LaborSamples);
            long tick = session.Engine.Tick;
            if (s.Tape.Any(c => c.SubmittedAtTick < 0 || c.SubmittedAtTick > tick || c.TravelQuantum < 0 || c.TravelQuantum >= SimulationEngine.TravelQuantaPerTick) ||
                s.Tape.Where((c, i) => i > 0 && c.SubmittedAtTick < s.Tape[i - 1].SubmittedAtTick).Any() ||
                s.Samples.Where((sample, i) => sample.Tick < 1 || sample.Tick > tick || (i > 0 && sample.Tick != s.Samples[i - 1].Tick + 1)).Any() ||
                s.LaborSamples.Length != s.Samples.Length || s.LaborSamples.Where((sample, i) => sample.Tick != s.Samples[i].Tick).Any() ||
                session.Engine.Inspect().Participants.Any(p => session._sequences.GetValueOrDefault(p.Id.Value) <
                    session.Engine.MaximumAcceptedSequence(p.Id)) ||
                s.Sequences.Any(p => p.Value < 0) || s.Tape.GroupBy(c => c.AuthenticatedActor.Value)
                    .Any(g => session._sequences.GetValueOrDefault(g.Key) < Math.Max(0, g.Max(c => c.Command.LocalSequence))))
                throw new CheckpointException("InvalidSessionTrace");
            if (s.Playback is { } p && (string.IsNullOrWhiteSpace(p.Label) || p.Label.Length > 80 || p.NextIndex < 0 || p.NextIndex > p.Commands.Length ||
                p.FinalTick < 0 || p.FinalTick > session.Engine.Configuration().MaxTicks ||
                p.Commands.Any(c => c.SubmittedAtTick < 0 || c.SubmittedAtTick > p.FinalTick || c.TravelQuantum < 0 || c.TravelQuantum >= SimulationEngine.TravelQuantaPerTick) ||
                p.Commands.Where((c, i) => i > 0 && c.SubmittedAtTick < p.Commands[i - 1].SubmittedAtTick).Any() ||
                p.Commands.Take(p.NextIndex).Any(c => c.SubmittedAtTick > tick || !session._tape.Contains(c)) ||
                p.Commands.Skip(p.NextIndex).Any(c => c.SubmittedAtTick < tick)))
                throw new CheckpointException("InvalidPlaybackCursor");
            if(s.SchemaVersion==3&&s.Controllers?.ObservationArchive is null)throw new CheckpointException("MissingObservationArchive");
            if (s.SchemaVersion is 2 or 3 && s.Controllers is null) throw new CheckpointException("MissingSessionControllers");
            if (s.Controllers is not null) session.LoadControllers(s.Controllers);
            return session;
        }
        catch (CheckpointException) { throw; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
            NullReferenceException or OverflowException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new CheckpointException("InvalidSessionCheckpoint", ex); }
    }
}

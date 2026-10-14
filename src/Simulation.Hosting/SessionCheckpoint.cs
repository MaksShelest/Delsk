using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public record RecordedPlaybackState(RecordedCommand[] Commands, long FinalTick, int NextIndex, string Label = "recorded-inputs");
internal record SavedSession(int SchemaVersion, string SessionId, long Epoch, bool Paused,
    string WorldJson, KeyValuePair<long, long>[] Sequences, RecordedCommand[] Tape, TickSample[] Samples,
    LaborSample[] LaborSamples, RecordedPlaybackState? Playback);

public sealed partial class GameSession
{
    private RecordedPlaybackState? _playback;
    public long? PlaybackFinalTick => _playback?.FinalTick;
    public string? PlaybackLabel => _playback?.Label;
    public void ConfigurePlayback(RecordedCommand[] tape, long finalTick, string label = "recorded-inputs")
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 80) throw new ArgumentException("Invalid playback label");
        if (_playback is not null) throw new InvalidOperationException("Playback already configured");
        if (finalTick < Engine.Tick || finalTick > Engine.Configuration().MaxTicks || tape.Any(c =>
            c.SubmittedAtTick < Engine.Tick || c.SubmittedAtTick > finalTick))
            throw new ArgumentException("Invalid playback horizon");
        _playback = new(tape.OrderBy(c => c.SubmittedAtTick).ToArray(), finalTick, 0, label);
    }
    private void SubmitPlaybackInputs()
    {
        if (_playback is not { } p) return;
        int next = p.NextIndex;
        while (next < p.Commands.Length && p.Commands[next].SubmittedAtTick <= Engine.Tick)
        {
            var input = p.Commands[next];
            if (input.SubmittedAtTick < Engine.Tick) throw new InvalidOperationException("Playback input missed its barrier");
            Submit(input.AuthenticatedActor, input.Command); ++next;
        }
        _playback = p with { NextIndex = next };
    }
    public void AdvanceTo(long finalTick)
    {
        if (Paused || finalTick < Engine.Tick || finalTick > Engine.Configuration().MaxTicks)
            throw new ArgumentException("Invalid or paused advance horizon");
        while (Engine.Tick < finalTick) Step();
        SubmitPlaybackInputs();
    }
    public CommandResult? SubmitFromEpoch(long epoch, ParticipantId actor, CommandEnvelope command)
        => epoch == SessionEpoch ? Submit(actor, command)
            : new(command.CommandId, ResultStatus.Rejected, "StaleSessionEpoch", Engine.Tick);

    internal string CreateSessionJson()
    {
        return JsonSerializer.Serialize(new SavedSession(1, SessionId, SessionEpoch, Paused,
            Engine.CreateCheckpointJson(), _sequences.OrderBy(x => x.Key).ToArray(), CommandTape, Samples,
            LaborSamples, _playback), SimulationJson.Options);
    }
    internal static GameSession FromSessionJson(string json, Action? beforeStep = null)
    {
        try
        {
            var s = JsonSerializer.Deserialize<SavedSession>(json, SimulationJson.Options)
                ?? throw new CheckpointException("EmptySessionCheckpoint");
            if (s.SchemaVersion != 1) throw new CheckpointException("UnknownSessionSchema");
            if (string.IsNullOrWhiteSpace(s.SessionId) || s.SessionId.Length > 80 || s.Epoch < 1 || s.Epoch == long.MaxValue)
                throw new CheckpointException("InvalidSessionIdentity");
            var session = new GameSession(SimulationEngine.FromCheckpointJson(s.WorldJson), s.SessionId, beforeStep)
                { SessionEpoch = s.Epoch + 1, Paused = s.Paused, _playback = s.Playback };
            foreach (var pair in s.Sequences) session._sequences.Add(pair.Key, pair.Value);
            session._tape.AddRange(s.Tape); session._samples.AddRange(s.Samples); session._laborSamples.AddRange(s.LaborSamples);
            long tick = session.Engine.Tick;
            if (s.Tape.Any(c => c.SubmittedAtTick < 0 || c.SubmittedAtTick > tick) ||
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
                p.Commands.Any(c => c.SubmittedAtTick < 0 || c.SubmittedAtTick > p.FinalTick) ||
                p.Commands.Where((c, i) => i > 0 && c.SubmittedAtTick < p.Commands[i - 1].SubmittedAtTick).Any() ||
                p.Commands.Take(p.NextIndex).Any(c => c.SubmittedAtTick > tick || !session._tape.Contains(c)) ||
                p.Commands.Skip(p.NextIndex).Any(c => c.SubmittedAtTick < tick)))
                throw new CheckpointException("InvalidPlaybackCursor");
            return session;
        }
        catch (CheckpointException) { throw; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
            NullReferenceException or OverflowException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new CheckpointException("InvalidSessionCheckpoint", ex); }
    }
}

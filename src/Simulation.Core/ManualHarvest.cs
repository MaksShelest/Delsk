namespace Simulation.Core;

public sealed record OwnHarvest(long JobId, string ItemId, long Quantity, bool Repeating, long StartTick, long CompletionTick);

public sealed partial class SimulationEngine
{
    /// <summary>Progress of this participant's personal extraction; no research projection.</summary>
    public OwnHarvest? HarvestOf(ParticipantId actor)
    {
        if (!_actors.TryGetValue(actor.Value, out var state)) throw new ArgumentException("Participant not available");
        return state.Job is { } job && IsHarvest(job)
            ? new(job.Id, job.Item, job.Quantity, job.Kind == "HarvestLoop", job.StartTick, job.CompletionTick) : null;
    }

    private static bool IsHarvest(PersonalJob? job) => job?.Kind is "Harvest" or "HarvestLoop" or "EquipmentHarvest";

    private void StartHarvesting(ActorState actor, CommandEnvelope command)
    {
        RequireIdle(actor);
        if (command.Action.Quantity != 1 || command.Action.Equipment.Value != 0)
            throw new RuleViolation("InvalidQuantity");
        RequireActionLocation(actor, command.Action, "NoResourceNode");
        BeginHarvestCycle(actor, command.Action.ItemId, command.CommandId);
    }

    private void BeginHarvestCycle(ActorState actor, string item, string commandId)
    {
        long end = checked(Tick + _config.HarvestTicksPerUnit);
        long nextId = checked(_nextId + 1);
        string hold = $"harvest-loop:{_nextId}";
        var tx = _economy.Begin(Tick);
        tx.ReserveCapacity(actor.Inventory, hold, item, 1); tx.Commit();
        actor.Job = new(_nextId, "HarvestLoop", Tick, end, [], item, 1, hold, commandId);
        _nextId = nextId;
        Emit("HarvestStarted", actor.Id, actor.Job.Id, item, 1, reason: "Repeating", command: commandId);
    }

    private void StopHarvest(ActorState actor, CommandEnvelope command)
    {
        if (actor.Job is null) return; // An already completed/stopped job is a harmless no-op.
        if (!IsHarvest(actor.Job)) throw new RuleViolation("ParticipantBusy");
        CancelHarvest(actor, "UserStopped");
    }

    private void CancelHarvest(ActorState actor, string reason)
    {
        var job = actor.Job!;
        var tx = _economy.Begin(Tick);
        tx.ReleaseCapacity(actor.Inventory, job.CapacityHold); tx.Commit();
        if (job.EquipmentId != 0) _equipment[job.EquipmentId].PersonalJobId = 0;
        actor.Job = null;
        Emit("HarvestCancelled", actor.Id, job.Id, job.Item, job.Quantity, reason: reason, command: job.CommandId);
    }

    // Only atomic travel cancellation may follow the current tick's commit. Other unfinished
    // phases remain unsaveable. This preserves focus/save and replay between travel quanta.
    private bool HasCommittedBoundary()
    {
        if (Tick == 0) return true;
        int commit = _events.FindLastIndex(e => e.Type == "TickCommitted" && e.Tick == Tick && e.Phase == 9);
        return commit >= 0 && _events.Skip(commit + 1).All(e =>
            e.Tick == Tick && e.Phase == 9 && e.Type == "HarvestCancelled" && e.ReasonCode == "LeftResourceArea" &&
            e.ActorId is { } actor && _actors.ContainsKey(actor.Value) && e.EntityId >= 1000 && e.Quantity > 0 &&
            e.CommandId is { } id && _seenCommands.TryGetValue(id, out var c) && c.ActorId == actor &&
            c.Action.Kind is ActionKind.Harvest or ActionKind.StartHarvesting && c.Action.ItemId == e.ItemId &&
            _results.TryGetValue(id, out var result) && result.Status == ResultStatus.Accepted);
    }
}

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private static string JobKey(long contract, long destination, long source) => contract != 0 ? $"contract:{contract}" :
        source != 0 ? $"input:{source}" : $"output:{destination}";
    private static void UpsertLogisticsJob(LogisticsState l, long contract, long destination, long source, bool enabled)
    {
        string key = JobKey(contract, destination, source);
        int index = l.Queue.FindIndex(j => j.Key == key);
        if (index < 0)
        {
            if (l.Queue.Count >= 64) throw new RuleViolation("LogisticsQueueFull");
            l.Queue.Add(new(key, contract, new(destination), new(source), enabled, 0));
        }
        else l.Queue[index] = l.Queue[index] with { Enabled = enabled };
    }
    private static void ReplacePrimaryOutput(LogisticsState l, long contract, long destination, bool enabled)
    {
        string key = JobKey(contract, destination, 0);
        if ((l.Contract != 0 || l.Destination != 0) && JobKey(l.Contract, l.Destination, 0) != key)
            l.Queue.RemoveAll(j => j.Key == JobKey(l.Contract, l.Destination, 0));
        UpsertLogisticsJob(l, contract, destination, 0, enabled);
    }
    private void QueueLogisticsJob(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        var a = c.Action;
        if (a.Quantity != 0) throw new RuleViolation("QuantityFromCapacity");
        if (a.ItemId != "") throw new RuleViolation("ItemFromProduction");
        if (a.ContractId < 0 || a.Source.Value < 0 || a.Destination.Value < 0 ||
            (a.ContractId > 0 ? 1 : 0) + (a.Source.Value > 0 ? 1 : 0) + (a.Destination.Value > 0 ? 1 : 0) != 1)
            throw new RuleViolation("InvalidLogisticsJob");
        if (a.ContractId != 0 && !_deliveryContracts.Any(d => d.Id == a.ContractId && d.Supplier.Value == actor.Id &&
                (!a.Enabled || d.ClosedTick is null)) ||
            a.Source.Value != 0 && !IsOwnLogisticsStorage(actor.Id, a.Source.Value) ||
            a.Destination.Value != 0 && !IsOwnLogisticsDestination(l, a.Destination.Value))
            throw new RuleViolation("NotAvailable");
        if (a.Enabled && OwnProductionDestination(l, a.Destination.Value) is { } production &&
            production.Recipe.InputItem != _enterprises[l.Enterprise].Recipe.OutputItem)
            throw new RuleViolation("IncompatibleRecipe");
        UpsertLogisticsJob(l, a.ContractId, a.Destination.Value, a.Source.Value, a.Enabled);
        Emit("LogisticsJobQueued", actor.Id, l.Id, reason: JobKey(a.ContractId, a.Destination.Value, a.Source.Value), command: c.CommandId);
    }
    private void SetLogisticsEnabled(ActorState actor, CommandEnvelope c)
    {
        if (!_logistics.TryGetValue(c.Action.Logistics.Value, out var l) || l.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (!Enum.IsDefined(c.Action.Direction)) throw new RuleViolation("InvalidLogisticsDirection");
        if (c.Action.Direction == LogisticsDirection.Inbound) l.InboundEnabled = c.Action.Enabled;
        else l.Enabled = c.Action.Enabled;
        Emit("LogisticsDirectionSet", actor.Id, l.Id, reason: $"{c.Action.Direction}:{c.Action.Enabled}", command: c.CommandId);
    }
    private LogisticsPlanData LogisticsPlan(LogisticsState l)
    {
        var candidates = new List<(LogisticsJobView Job, LogisticsPlanData Plan, int Priority, long Deadline)>();
        string reason = "UserPaused";
        var enterprise = _enterprises[l.Enterprise];
        bool shortage = enterprise.Enabled && _economy.Inventories[enterprise.Input].Free(enterprise.Recipe.InputItem) < enterprise.Recipe.InputQuantity;
        foreach (var job in l.Queue.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            bool input = job.InboundSource.Value != 0;
            if (!job.Enabled || !(input ? l.InboundEnabled : l.Enabled)) continue;
            try
            {
                var plan = input ? InboundLogisticsPlan(l, job.InboundSource.Value) :
                    OutputLogisticsPlan(l, job.ContractId, job.OwnDestination.Value) with { OwnDestination = job.OwnDestination.Value, GoodsSource = l.Source };
                int priority = job.ContractId != 0 ? 0 : input ? (shortage ? 1 : 3) : 2;
                long deadline = job.ContractId != 0 ? _deliveryContracts[plan.Index].DeadlineTick : long.MaxValue;
                candidates.Add((job, plan with { JobKey = job.Key }, priority, deadline));
            }
            catch (RuleViolation ex) { reason = ex.ReasonCode; }
        }
        if (candidates.Count == 0) throw new RuleViolation(reason);
        int bestPriority = candidates.Min(c => c.Priority);
        long bestDeadline = candidates.Where(c => c.Priority == bestPriority).Min(c => c.Deadline);
        var equal = candidates.Where(c => c.Priority == bestPriority && c.Deadline == bestDeadline)
            .OrderBy(c => c.Job.LastServed).ThenBy(c => c.Job.Key, StringComparer.Ordinal).ToArray();
        long oldest = equal[0].Job.LastServed;
        var ties = equal.Where(c => c.Job.LastServed == oldest).ToArray();
        // Pure selection: vacancy/cost reads consume no PRNG and cannot change the winner.
        // Initial equal jobs use a seed-derived offset; successful dispatches rotate oldest first.
        int offset = (int)((_config.Seed ^ (ulong)l.Id) % (ulong)ties.Length);
        return ties[offset].Plan;
    }
    private void ValidateLogisticsQueue(LogisticsState l)
    {
        if (l.Queue.Count > 64 || l.DispatchCount < 0 || l.DispatchCount > Tick || l.Completed > l.DispatchCount ||
            l.Queue.Select(j => j.Key).Distinct(StringComparer.Ordinal).Count() != l.Queue.Count ||
            l.Queue.Where(j => j.LastServed > 0).Select(j => j.LastServed).Distinct().Count() != l.Queue.Count(j => j.LastServed > 0))
            throw new InvalidOperationException("Invalid logistics queue");
        foreach (var j in l.Queue)
        {
            long contract = j.ContractId, destination = j.OwnDestination.Value, source = j.InboundSource.Value;
            if (contract < 0 || destination < 0 || source < 0 || (contract > 0 ? 1 : 0) + (destination > 0 ? 1 : 0) + (source > 0 ? 1 : 0) != 1 ||
                j.Key != JobKey(contract, destination, source) || j.LastServed < 0 || j.LastServed > l.DispatchCount ||
                contract != 0 && !_deliveryContracts.Any(d => d.Id == contract && d.Supplier.Value == l.Owner) ||
                source != 0 && !IsOwnLogisticsStorage(l.Owner, source) || destination != 0 && !IsOwnLogisticsDestination(l, destination))
                throw new InvalidOperationException("Invalid queued logistics job");
        }
        if (l.Trip is { } t && !l.Queue.Any(j => j.Key == t.JobKey && j.ContractId == t.ContractId &&
                j.InboundSource == t.InboundSource && (t.InboundSource.Value != 0 || j.OwnDestination == t.OwnDestination) && j.LastServed == l.DispatchCount))
            throw new InvalidOperationException("Logistics trip has no dispatched queue job");
    }
}

using System.Collections.Immutable;
using Agents.Algorithmic;

namespace Agents.Llm;

public record HybridSettings(int MinimumIntervalTicks = 30, int ApplyDelayTicks = 15, int DecisionTtlTicks = 15,
    int BudgetPeriodTicks = 120, int MaximumRequestsPerPeriod = 4, bool Enabled = true)
{
    public void Validate()
    {
        if (MinimumIntervalTicks is < 1 or > 100000 || ApplyDelayTicks is < 2 or > 10000 ||
            DecisionTtlTicks < ApplyDelayTicks || DecisionTtlTicks > 100000 || BudgetPeriodTicks < MinimumIntervalTicks ||
            MaximumRequestsPerPeriod is < 1 or > 100 || BudgetPeriodTicks > 1000000)
            throw new ArgumentException("Invalid hybrid settings");
    }
}
public record ModelRuntimeSettings(int QueueCapacity = 3, int TimeoutMilliseconds = 4000)
{
    public void Validate()
    {
        if (QueueCapacity is < 0 or > 64 || TimeoutMilliseconds is < 1 or > 120000)
            throw new ArgumentException("Invalid model runtime settings");
    }
}
public record ModelRequest(string DecisionId, long ActorId, long ObservationVersion, long SessionEpoch,
    long RequestedTick, long ApplyTick, long ExpiresTick, string SessionId, string Trigger,
    ImmutableArray<PolicyOption> Options, string Prompt, string PromptHash);
public record ModelReply(string? Text, string Outcome = "Completed", long LatencyMilliseconds = 0,
    long? PromptTokens = null, long? CompletionTokens = null, string? Model = null);
public record ModelIntent(string DecisionId, long ActorId, long ObservationVersion, long SessionEpoch,
    string OptionId, string ReasonSummary);

/// <summary>One replaceable local runtime adapter; it has no engine or controller reference.</summary>
public interface ILocalModelAdapter
{
    Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

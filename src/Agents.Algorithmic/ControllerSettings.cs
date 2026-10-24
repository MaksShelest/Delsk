using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Simulation.Core;

namespace Agents.Algorithmic;

public record ControllerSettings(long ActorId, string PreferredInput, int DecisionIntervalTicks = 5,
    long CashReserveMinor = 3000, int BatchQuantity = 20, long BaseWageMinor = 25,
    long MaximumWageMinor = 50, int WageReviewTicks = 60, int BlockedRetryTicks = 30,
    int ExpansionWeight = 100, int CashWeight = 100, int ProductionWeight = 100,
    int OwnVictoryWeight = 100, int HumanDefeatWeight = 0,
    int HarvestCoefficient = 1, int ProductionCoefficient = 1,
    int MaximumBuyPriceBasisPoints = 12500, int SellPriceBasisPoints = 10000,
    long SupportPricePerVoteMinor = 500, int PoliticalHorizonTicks = 180,
    bool TradeEnabled = true, bool LogisticsEnabled = true, bool PoliticsEnabled = true, long HumanActorId = 1, bool FutureContractsEnabled = true, int MarketLotQuantity = 10, long WageRaiseMinor = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] NpcDifficulty Difficulty = NpcDifficulty.Legacy,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long PoliticalBudgetMinor=0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PlanningLimits? Planning=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? NegotiationsEnabled=null);
public record AutonomousConfiguration(int Version, long HumanActorId, long FinalTick, ControllerSettings[] Npcs)
{
    public void Validate(ScenarioDefinition scenario)
    {
        var ids=scenario.Participants.Select(p=>p.Id).ToHashSet();
        if(Version!=1 || !ids.Contains(HumanActorId) || FinalTick<1 || FinalTick>scenario.MaxTicks || Npcs is null || Npcs.Length!=ids.Count-1 ||
            Npcs.Select(n=>n.ActorId).Distinct().Count()!=Npcs.Length || Npcs.Any(n=>n.ActorId==HumanActorId || !ids.Contains(n.ActorId) || !scenario.ResourceNodes.Any(r=>r.ItemId==n.PreferredInput)))
            throw new ArgumentException("Invalid autonomous configuration");
    }
    public AutonomousConfiguration ForOrdinary(ScenarioDefinition scenario,long human,IEnumerable<ControllerAssignment> assignments)
    {
        if(Npcs is not {Length:>0})throw new ArgumentException("Missing NPC settings template");
        var result=this with {HumanActorId=human,FinalTick=Math.Min(FinalTick,scenario.MaxTicks),Npcs=assignments.Select((a,i)=>
            Npcs[i%Npcs.Length] with {ActorId=a.ActorId,HumanActorId=human,PreferredInput=a.PreferredInput,
                Difficulty=Enum.Parse<NpcDifficulty>(a.Difficulty),PoliticalBudgetMinor=5000,Planning=null}).ToArray()};
        result.Validate(scenario);return result;
    }
}
public record TrustEntry(long ParticipantId, int Value);
public record PlanStep(GameAction Action);
public record BidReview(long OfferId, int Round);
public record AlgorithmState(ControllerSettings Settings, ulong RandomState, string Goal, string Reason,
    PlanStep[] Plan, int NextStep, string? PendingCommandId, long NextDecisionTick, long NextWageTick,
    long BlockedUntilTick, string BlockedGoal, long Decisions, long Rejections, TrustEntry[] Trust, BidReview[] BidReviews, PlanStep[] SuspendedPlan, int SuspendedNextStep, string SuspendedGoal, string SuspendedReason, long[] FailedSupport)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PolicyProfile? Profile {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StrategyAssessment[]? StrategyTrace {get;init;}
}

public sealed partial class AlgorithmicController : IController
{
    public const string PolicyKind = "utility-economy-v2";
    private AlgorithmState _state;
    public ParticipantId Actor => new(_state.Settings.ActorId);
    /// <summary>Тот же вывод семени, что у runner: порядок присоединения контроллеров его не меняет.</summary>
    public static ulong RuntimeSeed(ulong scenarioSeed, long actor)
        => BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"controller-v1:{scenarioSeed}:{actor}"))));
    public AlgorithmicController(ControllerSettings settings, ulong seed, IEnumerable<long> participants)
    {
        ValidateSettings(settings);
        _state = new(settings, seed, "Idle", "Initial", [], 0, null, 0, 0, 0, "", 0, 0,
            participants.Where(p => p != settings.ActorId).Distinct().Order().Select(p => new TrustEntry(p, 0)).ToArray(), [], [], 0, "", "", []) {Profile=ResolveProfile(settings.Difficulty)};
    }
    public ControllerCheckpoint Capture() => new(Profile is null?PolicyKind:ModularPolicyKind, Profile is null?2:3, Actor,
        System.Text.Json.JsonSerializer.Serialize(_state, SimulationJson.Options),
        _state.PendingCommandId is { } id ? [id] : []);
    public static AlgorithmicController Restore(ControllerCheckpoint saved)
    {
        if (!((saved.Kind == PolicyKind && saved.Version == 2) || (saved.Kind == ModularPolicyKind && saved.Version == 3))) throw new CheckpointException("UnknownControllerVersion");
        try
        {
            var s = System.Text.Json.JsonSerializer.Deserialize<AlgorithmState>(saved.StateJson, SimulationJson.Options)
                ?? throw new CheckpointException("EmptyControllerState");
            ValidateSettings(s.Settings);
            if(s.StrategyTrace is {} trace && (s.Settings.Difficulty!=NpcDifficulty.Hard || trace.Length>(s.Settings.Planning??new()).MaxCandidates ||
                trace.Any(t=>t is null || t.Eta<0 || t.Evaluations<0 || t.Evaluations>(s.Settings.Planning??new()).MaxEvaluations || t.CapitalMinor<0 || t.ExecutionReserveMinor<0 || t.RiskMinor<0 || t.Continuations<0 || t.Continuations>=(s.Settings.Planning??new()).Depth || t.Reason is null || t.Reason.Length>256)))throw new CheckpointException("InvalidStrategyTrace");
            if((s.Profile is null)!=(saved.Kind==PolicyKind) ||
                System.Text.Json.JsonSerializer.Serialize(s.Profile,SimulationJson.Options)!=System.Text.Json.JsonSerializer.Serialize(ResolveProfile(s.Settings.Difficulty),SimulationJson.Options))
                throw new CheckpointException("UnknownPolicyModuleVersion");
            if (s.Settings.ActorId != saved.Actor.Value || s.NextStep < 0 || s.NextStep > s.Plan.Length ||
                s.NextDecisionTick < 0 || s.NextWageTick < 0 || s.BlockedUntilTick < 0 || s.Decisions < 0 ||
                s.Rejections < 0 || s.Rejections > s.Decisions || s.Plan.Length > 100 || s.BidReviews is null || s.BidReviews.Length > 1000 ||
                s.SuspendedPlan is null || s.SuspendedPlan.Length>100 || s.SuspendedNextStep<0 || s.SuspendedNextStep>s.SuspendedPlan.Length || s.SuspendedGoal is null || s.SuspendedGoal.Length>80 || s.SuspendedReason is null || s.SuspendedReason.Length>256 || s.FailedSupport is null || s.FailedSupport.Length>2048 ||
                s.FailedSupport.Any(id=>id<=0) || s.FailedSupport.Distinct().Count()!=s.FailedSupport.Length || s.SuspendedPlan.Any(p=>p is null || !Enum.IsDefined(p.Action.Kind)) ||
                s.BidReviews.Any(b => b.OfferId <= 0 || b.Round < 1) || s.BidReviews.Distinct().Count() != s.BidReviews.Length ||
                string.IsNullOrWhiteSpace(s.Goal) || s.Goal.Length > 80 || s.Reason is null || s.Reason.Length > 256 ||
                s.BlockedGoal is null || s.BlockedGoal.Length > 80 || saved.PendingCommandIds.IsDefault ||
                !saved.PendingCommandIds.SequenceEqual(s.PendingCommandId is { } pendingId ? new[] { pendingId } : []) ||
                s.Plan.Any(p => p is null || !Enum.IsDefined(p.Action.Kind)) ||
                s.Trust.Any(t => t.ParticipantId <= 0 || t.ParticipantId == saved.Actor.Value || t.Value is < -100 or > 100) ||
                s.Trust.Select(t => t.ParticipantId).Distinct().Count() != s.Trust.Length ||
                (s.PendingCommandId is not null && s.NextStep >= s.Plan.Length))
                throw new CheckpointException("InvalidControllerState");
            var controller = new AlgorithmicController(s.Settings, s.RandomState, []); controller._state = s; return controller;
        }
        catch (CheckpointException) { throw; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or NullReferenceException)
        { throw new CheckpointException("InvalidControllerState", ex); }
    }
    private static void ValidateSettings(ControllerSettings s)
    {
        s.Planning?.Validate();
        if (s.PoliticalBudgetMinor is <0 or >1_000_000_000 || !Enum.IsDefined(s.Difficulty) || s.WageRaiseMinor is < 0 or > 10000 || s.ActorId <= 0 || string.IsNullOrWhiteSpace(s.PreferredInput) || s.PreferredInput.Length > 80 ||
            s.DecisionIntervalTicks is < 1 or > 100 || s.BatchQuantity is < 1 or > 1000 ||
            s.CashReserveMinor is < 0 or > 1_000_000_000 || s.BaseWageMinor is < 1 or > 10000 ||
            s.MaximumWageMinor < s.BaseWageMinor || s.MaximumWageMinor > 10000 ||
            s.WageReviewTicks < 20 || s.BlockedRetryTicks < 1 || s.MaximumBuyPriceBasisPoints is < 1000 or > 30000 ||
            s.SellPriceBasisPoints is < 1000 or > 30000 || s.SupportPricePerVoteMinor is < 1 or > 1_000_000 ||
            s.PoliticalHorizonTicks is < 10 or > 1000000 || s.HumanActorId is < 1 or > 999 || s.MarketLotQuantity is < 1 or > 1000 ||
            new[] { s.ExpansionWeight, s.CashWeight, s.ProductionWeight, s.OwnVictoryWeight, s.HumanDefeatWeight }.Any(x => x is < 0 or > 1000) ||
            s.HarvestCoefficient != 1 || s.ProductionCoefficient != 1)
            throw new ArgumentException("Invalid policy settings; economic coefficients currently support only x1");
    }
    // PRNG belongs to this policy, never consumes the world's fairness stream.
    private ulong RandomTie()
    {
        unchecked
        {
            ulong state = _state.RandomState + 0x9E3779B97F4A7C15UL;
            ulong z = state; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            _state = _state with { RandomState = state }; return z ^ (z >> 31);
        }
    }
    public void CommandSubmitted(string commandId)
    {
        if (_state.PendingCommandId is not null || _state.NextStep >= _state.Plan.Length)
            throw new InvalidOperationException("No plan action awaiting submission");
        _state = _state with { PendingCommandId = commandId,
            NextWageTick = _state.Goal == "AdjustWage" ? checked(_state.NextDecisionTick -
                _state.Settings.DecisionIntervalTicks + _state.Settings.WageReviewTicks) : _state.NextWageTick };
    }
}

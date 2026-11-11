using System.Security.Cryptography;
using System.Text;

namespace Simulation.Core;

/// <summary>
/// Четыре направления будущей поддержки. Субсидий здесь нет: слот только резервируется.
/// </summary>
public sealed record StartingDirection(string IndustryId, string RecipeId, string InputItem, string OutputItem);

public sealed record StartingSpecializationRules(int Version, bool Enabled = true)
{
    public const int CurrentVersion = 1;
    public bool Applies => Version == CurrentVersion && Enabled;
    public void Validate()
    {
        if (Version != CurrentVersion) throw new ArgumentException("Starting specialization version is not supported");
    }
}

public sealed record StartingSpecializationAssignment(long ActorId, string IndustryId);

public sealed record StartingSpecializationState(long ActorId, string IndustryId, int SupportSlotsRemaining);

public static class StartingSpecializations
{
    public static readonly StartingDirection[] Directions =
    [
        new("woodworking", "saw_planks", "wood", "plank"),
        new("food", "mill_flour", "grain", "flour"),
        new("dairy", "make_cheese", "milk", "cheese"),
        new("materials", "crush_stone", "stone", "gravel")
    ];

    public static bool IsDirection(string? industry) =>
        industry is not null && Directions.Any(d => d.IndustryId == industry);

    public static StartingDirection? Direction(string? industry) =>
        Directions.FirstOrDefault(d => d.IndustryId == industry);

    public static bool ChainReady(ScenarioDefinition scenario, string industry)
    {
        var direction = Direction(industry);
        var type = scenario.BuildingTypes?.FirstOrDefault(t => t.Id == industry && t.Kind == BuildingKind.Production);
        var level = type?.Levels.FirstOrDefault(l => l.Level == 1);
        if (direction is null || level is null || !level.UnlockedRecipes.Contains(direction.RecipeId, StringComparer.Ordinal)) return false;
        var recipe = scenario.Recipes.FirstOrDefault(r => r.Id == direction.RecipeId);
        return recipe is not null && recipe.OutputItem == direction.OutputItem && recipe.Ingredients.Any(i => i.ItemId == direction.InputItem);
    }

    public static void ValidateRoster(ScenarioDefinition scenario)
    {
        var rules = scenario.Economy?.StartingSpecializations;
        rules?.Validate();
        var roster = scenario.StartingSpecializationRoster;
        bool on = rules is { Applies: true };
        if (!on)
        {
            if (roster is { Length: > 0 }) throw new ArgumentException("Specializations are disabled");
            return;
        }
        if (roster is null) return;
        if (roster.Length == 0 || roster.Length > scenario.Participants.Length) throw new ArgumentException("Invalid starting specialization roster");
        var seen = new HashSet<long>();
        foreach (var row in roster)
        {
            if (row is null || !seen.Add(row.ActorId) || !scenario.Participants.Any(p => p.Id == row.ActorId) || !ChainReady(scenario, row.IndustryId))
                throw new ArgumentException("Invalid starting specialization");
        }
    }

    /// <summary>
    /// Выбор NPC зависит только от seed, карты, участника и числа клеток сырья.
    /// Порядок коллекции и чужие назначения на результат не влияют.
    /// </summary>
    public static string? ChooseNpc(ScenarioDefinition scenario, long actorId)
    {
        StartingDirection? best = null;
        ulong bestRank = ulong.MaxValue;
        foreach (var direction in Directions.OrderBy(d => d.IndustryId, StringComparer.Ordinal))
        {
            int nodes = scenario.ResourceNodes.Count(n => n.ItemId == direction.InputItem);
            if (nodes <= 0 || !ChainReady(scenario, direction.IndustryId)) continue;
            ulong rank = Rank(scenario.Seed, scenario.Id, actorId, direction.IndustryId) / (ulong)nodes;
            if (best is null || rank < bestRank || (rank == bestRank && string.CompareOrdinal(direction.IndustryId, best.IndustryId) < 0))
            {
                best = direction;
                bestRank = rank;
            }
        }
        return best?.IndustryId;
    }

    private static ulong Rank(ulong seed, string mapId, long actorId, string industry)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"starting-specialization-1:{seed}:{mapId}:{actorId}:{industry}")));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, StartingSpecializationState> _specializations = new();

    public StartingSpecializationState[] SpecializationChoices() =>
        _specializations.Values.OrderBy(s => s.ActorId).ToArray();

    private string? OwnStartingIndustry(long actor) =>
        _specializations.TryGetValue(actor, out var row) ? row.IndustryId : null;

    private StartingSpecializationState[]? CaptureStartingSpecializations() =>
        _specializations.Count == 0 ? null : SpecializationChoices();

    private void ApplyStartingRoster()
    {
        if (_config.StartingSpecializationRoster is not { } roster) return;
        foreach (var row in roster.OrderBy(r => r.ActorId))
        {
            if (!_actors.ContainsKey(row.ActorId) || !StartingSpecializations.ChainReady(_config, row.IndustryId) || !_specializations.TryAdd(row.ActorId, new(row.ActorId, row.IndustryId, 1)))
                throw new ArgumentException("Invalid starting specialization");
        }
    }

    private void RestoreStartingSpecializations(StartingSpecializationState[]? saved)
    {
        _specializations.Clear();
        if (saved is null) return;
        if (EconomyRules.StartingSpecializations is not { Applies: true }) throw new CheckpointException("UnexpectedSpecializations");
        var seen = new HashSet<long>();
        foreach (var row in saved)
        {
            if (row is null || row.SupportSlotsRemaining != 1 || !seen.Add(row.ActorId) || !_actors.ContainsKey(row.ActorId) || !StartingSpecializations.ChainReady(_config, row.IndustryId))
                throw new CheckpointException("InvalidSpecialization");
            _specializations.Add(row.ActorId, row!);
        }
    }

    private void ChooseStartingSpecialization(ActorState actor, CommandEnvelope c)
    {
        if (EconomyRules.StartingSpecializations is not { Applies: true }) throw new RuleViolation("SpecializationDisabled");
        if (!StartingSpecializations.ChainReady(_config, c.Action.BuildingTypeId)) throw new RuleViolation("UnknownSpecialization");
        if (_specializations.ContainsKey(actor.Id)) throw new RuleViolation("SpecializationAlreadyChosen");
        _specializations.Add(actor.Id, new(actor.Id, c.Action.BuildingTypeId, 1));
        Emit("StartingSpecializationChosen", actor.Id, item: c.Action.BuildingTypeId, reason: "SupportSlotReserved");
    }
}

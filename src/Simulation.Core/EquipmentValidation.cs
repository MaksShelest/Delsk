namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateEquipmentCatalog(ScenarioDefinition s)
    {
        var types = s.EquipmentTypes ?? [];
        if (types.Length > 32 || types.Any(t => t is null) || types.Select(t => t.Id).Distinct().Count() != types.Length)
            throw new ArgumentException("Invalid equipment catalog");
        try
        {
            foreach (var t in types)
            {
                if (string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 50 ||
                    !s.Items.Any(i => i.Id == t.ItemId) || !s.ResourceNodes.Any(n => n.ItemId == t.ItemId) ||
                    t.Levels is null || t.Levels.Length is < 1 or > 2 || t.Levels.Any(l => l is null) ||
                    !t.Levels.Select(l => l.Level).Order().SequenceEqual(Enumerable.Range(1, t.Levels.Length)))
                    throw new ArgumentException("Invalid equipment type or levels");
                EquipmentLevelDefinition? previous = null;
                foreach (var l in t.Levels.OrderBy(l => l.Level))
                {
                    if (l.PriceMinor <= 0 || l.CycleQuantity <= 0 || l.CycleTicks <= 0 || l.StorageCapacity <= 0 ||
                        l.MinimumCrew < 1 || l.CrewCapacity < l.MinimumCrew || l.CrewCapacity > 1000000 || l.WageMinor < 0 ||
                        (previous is not null && (l.CrewCapacity < previous.CrewCapacity || l.StorageCapacity < previous.StorageCapacity)))
                        throw new ArgumentException("Invalid equipment characteristics");
                    long volume = checked(l.CycleQuantity * s.Items.Single(i => i.Id == t.ItemId).UnitVolume);
                    if (volume > l.StorageCapacity) throw new ArgumentException("Equipment output cannot fit its storage");
                    _ = checked(l.CycleTicks * l.CrewCapacity);
                    _ = checked(l.WageMinor * l.CrewCapacity);
                    previous = l;
                }
            }
        }
        catch (OverflowException ex) { throw new ArgumentException("Equipment arithmetic overflow", ex); }
    }
}

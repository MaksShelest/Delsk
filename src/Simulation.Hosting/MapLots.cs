using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Связный участок новой карты. Исторический 2×3 остаётся отдельной проверкой адаптера 1 и 2.</summary>
public static class MapLots
{
    public const int MaximumCells = 64;

    public static bool LegacyRectangle(IReadOnlyCollection<Cell> cells)
    {
        var set = cells.Distinct().ToArray();
        return set.Length == 6 && set.Max(c => c.X) - set.Min(c => c.X) == 1 && set.Max(c => c.Z) - set.Min(c => c.Z) == 2;
    }

    public static bool Connected(IReadOnlyCollection<Cell> cells)
    {
        var set = cells.ToHashSet();
        if (set.Count == 0) return false;
        var seen = new HashSet<Cell>();
        var queue = new Queue<Cell>();
        var start = set.First();
        queue.Enqueue(start);
        seen.Add(start);
        while (queue.TryDequeue(out var cell))
            foreach (var next in new[] { new Cell(cell.X - 1, cell.Z), new(cell.X + 1, cell.Z), new(cell.X, cell.Z - 1), new(cell.X, cell.Z + 1) })
                if (set.Contains(next) && seen.Add(next)) queue.Enqueue(next);
        return seen.Count == set.Count;
    }

    public static Cell[] Offsets(BuildingTypeDefinition type, int level)
    {
        var spec = type.Levels.Single(l => l.Level == level);
        return spec.Footprint.IsDefaultOrEmpty ? type.Footprint : spec.Footprint.ToArray();
    }

    public static bool Covers(IReadOnlyCollection<Cell> lot, Cell anchor, IReadOnlyCollection<Cell> offsets)
        => offsets.All(offset => lot.Contains(new Cell(anchor.X + offset.X, anchor.Z + offset.Z)));

    public static string TooSmall(string language, string building, int needed, int owned)
        => PlayerText.Local(language,
            $"{building} занимает {Cells(needed,true)}, а участок — {Cells(owned,false)}. Постройте меньший объект или расширьте участок.",
            $"{building} needs {needed} cells, and this lot has {owned}. Build a smaller object or enlarge the lot.");
    public static string Cells(int count,bool accusative)
    {
        int tail=count%10,hundred=count%100;
        string word=tail==1&&hundred!=11?accusative?"клетку":"клетка":tail is >=2 and <=4&&(hundred<12||hundred>14)?"клетки":"клеток";
        return count+" "+word;
    }
}

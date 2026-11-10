namespace Simulation.Core;

/// <summary>
/// Покупка целого промышленного участка. Поле опускается в старых профилях и сохранениях.
/// Обычный старт остаётся на правилах I6-C7.5, пока нет субсидий I6-C8C.
/// </summary>
public sealed record IndustrialParcelRules(int Version = 1)
{
    public const int Cells = 6;
    public const long CellPriceMinor = 10_000;
    public const long PriceMinor = 60_000;

    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("Invalid industrial parcel rules");
    }

    public static bool Applies(EconomicRules? rules) => rules?.IndustrialParcels?.Version == 1;
}

/// <summary>Локальная сетка участка: первая клетка у подъезда, глубина внутрь территории.</summary>
public readonly record struct ParcelFrame(string Facing, Cell Anchor, int AcrossX, int AcrossZ, int DepthX, int DepthZ)
{
    public int RoadX => -DepthX;
    public int RoadZ => -DepthZ;

    public Cell Map(int localX, int localZ) => new(
        checked(Anchor.X + localX * AcrossX + localZ * DepthX),
        checked(Anchor.Z + localX * AcrossZ + localZ * DepthZ));
}

public static class IndustrialParcels
{
    public static long QuotedMinor(EconomicRules? rules, bool shopSite, bool assignedShop, long cellPrice, bool owned)
    {
        if (owned) return 0;
        if (IndustrialParcelRules.Applies(rules) && !shopSite && !assignedShop)
            return checked(cellPrice * IndustrialParcelRules.Cells);
        return cellPrice;
    }

    /// <summary>Смещение каталога. Нулевой подъезд оставляет исторический рост +X +Z.</summary>
    public static Cell Place(Cell anchor, int roadX, int roadZ, int localX, int localZ)
    {
        if (roadX == 0 && roadZ == 0) return new(checked(anchor.X + localX), checked(anchor.Z + localZ));
        int depthX = -roadX, depthZ = -roadZ;
        int acrossX = Math.Abs(depthZ), acrossZ = Math.Abs(depthX);
        return new(checked(anchor.X + localX * acrossX + localZ * depthX), checked(anchor.Z + localX * acrossZ + localZ * depthZ));
    }

    public static ParcelFrame? TryFrame(IndustrialLot lot)
    {
        var cells = lot.Cells.Distinct().ToArray();
        if (cells.Length != IndustrialParcelRules.Cells) return null;
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minZ = cells.Min(c => c.Z), maxZ = cells.Max(c => c.Z);
        int spanX = maxX - minX, spanZ = maxZ - minZ;
        if (cells.Length != (spanX + 1) * (spanZ + 1)) return null;
        ParcelFrame? frame = (spanX, spanZ, lot.Entry) switch
        {
            (1, 2, var entry) when entry.Z == minZ - 1 && entry.X >= minX && entry.X <= maxX => new("north", new(minX, minZ), 1, 0, 0, 1),
            (1, 2, var entry) when entry.Z == maxZ + 1 && entry.X >= minX && entry.X <= maxX => new("south", new(minX, maxZ), 1, 0, 0, -1),
            (2, 1, var entry) when entry.X == minX - 1 && entry.Z >= minZ && entry.Z <= maxZ => new("west", new(minX, minZ), 0, 1, 1, 0),
            (2, 1, var entry) when entry.X == maxX + 1 && entry.Z >= minZ && entry.Z <= maxZ => new("east", new(maxX, minZ), 0, 1, -1, 0),
            _ => null
        };
        if (frame is not { } known) return null;
        var mapped = new HashSet<Cell>();
        for (int depth = 0; depth < 3; depth++)
            for (int across = 0; across < 2; across++)
                mapped.Add(known.Map(across, depth));
        return mapped.SetEquals(cells) ? known : null;
    }

    public static Cell[] MapFootprint(ParcelFrame frame, IEnumerable<Cell> local) =>
        local.Select(c => frame.Map(c.X, c.Z)).OrderBy(c => c.X).ThenBy(c => c.Z).ToArray();

    public static (int X, int Z) FacingOf(ScenarioDefinition scenario, Cell position)
    {
        if (!IndustrialParcelRules.Applies(scenario.Economy) || scenario.Layout is not { } layout) return (0, 0);
        var lot = layout.IndustrialLots.FirstOrDefault(l => l.Cells.Contains(position));
        if (lot is null || TryFrame(lot) is not { } frame || frame.Anchor != position) return (0, 0);
        return (frame.RoadX, frame.RoadZ);
    }

    /// <summary>Подъезд связан с точкой появления или, если её нет, с городской грунтовой сетью.</summary>
    public static bool ReachesNetwork(MapLayout layout, Cell entry)
    {
        var roads = layout.RoadCorridorCells.ToHashSet();
        if (!roads.Contains(entry)) return false;
        var goals = layout.SpawnCells.Length > 0 ? layout.SpawnCells.ToHashSet() : layout.InitialCityDirtCells.ToHashSet();
        if (goals.Count == 0) return false;
        if (goals.Contains(entry)) return true;
        var seen = new HashSet<Cell> { entry };
        var queue = new Queue<Cell>();
        queue.Enqueue(entry);
        while (queue.TryDequeue(out var cell))
            foreach (var next in Neighbors(cell))
                if (roads.Contains(next) && seen.Add(next))
                {
                    if (goals.Contains(next)) return true;
                    queue.Enqueue(next);
                }
        return false;
    }

    /// <summary>Восемь участков 2×3. origin — северо-западный угол района, не дороги.</summary>
    public static IndustrialLot[] District(Cell origin, string roads)
    {
        int ox = origin.X, oz = origin.Z;
        if (roads is "north" or "south")
        {
            var lots = new IndustrialLot[8];
            for (int i = 0; i < 4; i++)
            {
                int x = ox + 2 * i;
                lots[i] = Rectangle($"parcel-{ox}-{oz}-n{i}", x, oz, 2, 3, new(x, oz - 1));
                lots[4 + i] = Rectangle($"parcel-{ox}-{oz}-s{i}", x, oz + 3, 2, 3, new(x, oz + 6));
            }
            return lots;
        }
        if (roads is "west" or "east")
        {
            var lots = new IndustrialLot[8];
            for (int i = 0; i < 4; i++)
            {
                int z = oz + 2 * i;
                lots[i] = Rectangle($"parcel-{ox}-{oz}-w{i}", ox, z, 3, 2, new(ox - 1, z));
                lots[4 + i] = Rectangle($"parcel-{ox}-{oz}-e{i}", ox + 3, z, 3, 2, new(ox + 6, z));
            }
            return lots;
        }
        throw new ArgumentException("Unknown industrial district orientation");
    }

    public static Cell[] DistrictRoads(Cell origin, string roads)
    {
        int ox = origin.X, oz = origin.Z;
        var cells = new List<Cell>();
        if (roads is "north" or "south")
        {
            for (int x = ox; x < ox + 8; x++) { cells.Add(new(x, oz - 1)); cells.Add(new(x, oz + 6)); }
            for (int z = oz - 1; z <= oz + 6; z++) cells.Add(new(ox - 1, z));
            return cells.Distinct().ToArray();
        }
        if (roads is "west" or "east")
        {
            for (int z = oz; z < oz + 8; z++) { cells.Add(new(ox - 1, z)); cells.Add(new(ox + 6, z)); }
            for (int x = ox - 1; x <= ox + 6; x++) cells.Add(new(x, oz - 1));
            return cells.Distinct().ToArray();
        }
        throw new ArgumentException("Unknown industrial district orientation");
    }

    private static IndustrialLot Rectangle(string id, int x, int z, int width, int depth, Cell entry)
    {
        var cells = new Cell[width * depth];
        int n = 0;
        for (int dz = 0; dz < depth; dz++)
            for (int dx = 0; dx < width; dx++)
                cells[n++] = new(x + dx, z + dz);
        return new(id, cells, entry);
    }

    private static IEnumerable<Cell> Neighbors(Cell cell) =>
        [new(cell.X - 1, cell.Z), new(cell.X + 1, cell.Z), new(cell.X, cell.Z - 1), new(cell.X, cell.Z + 1)];
}

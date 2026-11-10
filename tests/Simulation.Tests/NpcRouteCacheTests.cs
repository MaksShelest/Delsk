using System.Collections.Immutable;
using Agents.Algorithmic;
using Simulation.Core;
using Xunit;

namespace Simulation.Tests;

public class NpcRouteCacheTests
{
    private static ControllerMap Map(params Cell[] blocked) => new(5, 4,
        ImmutableArray.Create(blocked),
        ImmutableArray<ResourceNodeDefinition>.Empty,
        ImmutableArray<ItemDefinition>.Empty,
        ImmutableArray<RecipeDefinition>.Empty,
        1, 1, 1);

    private static int Walk(ControllerMap map, Cell from, Cell to)
    {
        if (from == to) return 0;
        if (to.X < 0 || to.Z < 0 || to.X >= map.Width || to.Z >= map.Height || map.BlockedCells.Contains(to)) return -1;
        var blocked = map.BlockedCells.ToHashSet();
        var visited = new HashSet<Cell> { from };
        var queue = new Queue<(Cell Cell, int Distance)>();
        queue.Enqueue((from, 0));
        while (queue.TryDequeue(out var entry))
        foreach (var next in new[] { new Cell(entry.Cell.X + 1, entry.Cell.Z), new(entry.Cell.X, entry.Cell.Z + 1),
            new(entry.Cell.X - 1, entry.Cell.Z), new(entry.Cell.X, entry.Cell.Z - 1) })
        {
            if (next.X < 0 || next.Z < 0 || next.X >= map.Width || next.Z >= map.Height || blocked.Contains(next) || !visited.Add(next)) continue;
            if (next == to) return entry.Distance + 1;
            queue.Enqueue((next, entry.Distance + 1));
        }
        return -1;
    }

    [Fact]
    public void RepeatedRouteSearchKeepsTheSameLength()
    {
        var map = Map(new Cell(2, 1), new Cell(2, 2));
        var origin = new Cell(0, 0);
        foreach (var target in new[] { new Cell(4, 3), new Cell(2, 1), new Cell(0, 0), new Cell(9, 9), new Cell(4, 0) })
        {
            int expected = Walk(map, origin, target);
            Assert.Equal(expected, AlgorithmicController.Distance(map, origin, target));
            Assert.Equal(expected, AlgorithmicController.Distance(map, origin, target));
        }
        var other = Map(new Cell(1, 0), new Cell(1, 1), new Cell(1, 2), new Cell(1, 3));
        Assert.Equal(-1, AlgorithmicController.Distance(other, new Cell(0, 0), new Cell(4, 0)));
        Assert.Equal(2, AlgorithmicController.Distance(other, new Cell(0, 0), new Cell(0, 2)));
        Assert.Equal(2, AlgorithmicController.Distance(other, new Cell(0, 0), new Cell(0, 2)));
    }
}

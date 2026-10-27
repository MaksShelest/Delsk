using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    public ImmutableArray<Cell> BlockedCells() => [.. _config.BlockedCells.Concat(_buildings.Values.Where(b=>!Demolished(b.Id)).SelectMany(BuildingWalls))
        .Distinct().OrderBy(c => c.Z).ThenBy(c => c.X)];
    private static IEnumerable<Cell> BuildingWalls(BuildingState b) => (b.Type.Walls ?? [])
        .Select(c => new Cell(checked(b.Position.X+c.X),checked(b.Position.Z+c.Z)));
    private void ApplyBuildingObstacles() => _map.SetBuildingWalls(_buildings.Values.Where(b=>!Demolished(b.Id)).SelectMany(BuildingWalls));
    private void CheckBuildingAccess(Cell position, BuildingTypeDefinition type)
    {
        var walls=(type.Walls??[]).Select(c=>new Cell(checked(position.X+c.X),checked(position.Z+c.Z))).ToHashSet();
        if(walls.Count==0)return;
        if (_actors.Values.Any(a=>walls.Contains(a.Position) || a.Job is { Kind:"Move" } j && j.Path.Skip(j.PathIndex).Any(walls.Contains)) ||
            _equipment.Values.Any(e=>walls.Contains(e.Position) || e.Motion is {} m && m.Path.Skip(m.Index).Any(walls.Contains)) ||
            _traffic.Values.Any(t=>t.Path.Skip(t.Index).Any(walls.Contains)) ||
            _logistics.Values.Any(l=>l.Trip is {} t && (t.Path.Any(walls.Contains) || t.Freight is {} f && f.ToSource.Concat(f.ToDestination).Concat(f.ToHome).Concat(f.ToGoodsSource).Concat(f.SourceToHome).Any(walls.Contains))) ||
            _economy.Inventories.Values.Any(i=>walls.Contains(i.Position))) throw new RuleViolation("RouteWouldBeBlocked");
        var anchors=_actors.Values.Select(a=>a.Position).Concat(_resources.Keys.Select(k=>k.Position))
            .Concat(_plots.Values.Where(p=>!p.Definition.IsCorridor).Select(p=>p.Definition.Position).Where(c=>!walls.Contains(c)))
            .Concat(_offers.Values.Select(o=>o.Definition.Position)).Concat(_buildings.Values.Where(b=>!Demolished(b.Id)).Select(b=>b.Position)).Distinct().ToArray();
        var previous=_buildings.Values.Where(b=>!Demolished(b.Id)).SelectMany(BuildingWalls).ToArray();
        _map.SetBuildingWalls(previous.Concat(walls));
        try
        {
            foreach(var anchor in anchors) _map.FindPath(position,anchor);
        }
        catch(RuleViolation){throw new RuleViolation("AccessWouldBeBlocked");}
        finally{_map.SetBuildingWalls(previous);}
    }
}

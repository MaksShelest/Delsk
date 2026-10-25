using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Simulation.Core;

/// <summary>Resolved initial configuration only. The live roster is Participants, never a second npcCount.</summary>
public sealed record NewGameProvenance(string Version, long HumanActorId, string Difficulty, string SourceMapId,
    string EconomyProfileSha256, string AssignmentVersion, long BasePopulation, long? BaseMaximumPopulation,
    long BaseTreasuryMinor, long? BaseDemandBudgetMinor);
public sealed record ControllerAssignment(long ActorId, string PreferredInput, string Difficulty);

/// <summary>Read-only use of exactly the runtime grid/walls, without constructing a world.</summary>
public sealed class ScenarioNavigation
{
    private readonly GridMap _map;
    public ScenarioNavigation(ScenarioDefinition definition)
    {
        _map=new(definition);
        var types=(definition.BuildingTypes??[]).ToDictionary(t=>t.Id,StringComparer.Ordinal);
        _map.SetBuildingWalls((definition.Buildings??[]).SelectMany(b=>(types[b.TypeId].Walls??[])
            .Select(c=>new Cell(checked(b.Position.X+c.X),checked(b.Position.Z+c.Z)))));
    }
    public bool Walkable(Cell cell)=>_map.Walkable(cell);
    public Cell[] FindPath(Cell start,Cell end)=>_map.FindPath(start,end);
    public ImmutableDictionary<Cell,int> DistancesFrom(Cell start)=>_map.DistancesFrom(start).ToImmutableDictionary();
}

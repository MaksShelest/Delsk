using System.Collections.Immutable;
namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private sealed record MovementNavigation(ImmutableArray<Cell> Corridor,ImmutableArray<Cell> Public,ImmutableArray<Cell> Restricted);
    private MovementNavigation? _controllerBatchNavigation;
    private MovementNavigation MovementNavigationSnapshot()
    {
        var layout=_config.Layout!;var plots=_plots.Values.Select(p=>p.Definition.Position).ToHashSet();
        var civic=layout.CivicObjects.SelectMany(s=>s.Footprint).ToHashSet();
        var available=layout.RoadCorridorCells.Where(c=>_map.Walkable(c)&&!plots.Contains(c)&&!civic.Contains(c)).ToArray();
        var legal=available.ToHashSet();var turns=layout.TurnCells.ToHashSet();var paved=_roads.Keys.ToHashSet();
        return new([..layout.RoadCorridorCells],[..available],[..layout.RoadCenterCells.Where(c=>legal.Contains(c)&&!turns.Contains(c)&&RoadNeighbors(c).Count(paved.Contains)<3)]);
    }
    private WorldView? _controllerBatchView;
    private CityEconomy? _controllerBatchEconomy;
    private ImmutableArray<MarketPricePoint>? _controllerBatchPrices,_controllerBatchHistory;
    private ImmutableArray<ResourceSiteView>? _controllerBatchResources;
    /// <summary>Freeze one common read-only barrier, retaining the same per-actor allowlists.</summary>
    public ControllerObservation[] ObserveControllers(IEnumerable<(ParticipantId Actor,bool EconomicPlanning)> requests)
    {
        if(_stepping||_controllerBatchView is not null)throw new InvalidOperationException("Observation batch requires an idle barrier");
        var plan=requests.ToArray();
        try
        {
            _controllerBatchView=Inspect();_controllerBatchEconomy=PublicEconomy();
            if(UsesAcceleratedMovement&&Roads is not null)_controllerBatchNavigation=MovementNavigationSnapshot();
            _controllerBatchPrices=PublicCapitalPrices(false);_controllerBatchHistory=PublicCapitalPrices(true);_controllerBatchResources=ResourceSites();
            return plan.Select(r=>ObserveController(r.Actor,r.EconomicPlanning)).ToArray();
        }
        finally
        {_controllerBatchNavigation=null;_controllerBatchView=null;_controllerBatchEconomy=null;_controllerBatchPrices=null;_controllerBatchHistory=null;_controllerBatchResources=null;}
    }
}

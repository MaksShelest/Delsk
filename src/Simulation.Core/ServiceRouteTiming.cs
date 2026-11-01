namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private Cell ServiceStartHeading(Cell source,long vehicle,Cell? heading)
    {
        var v=_vehicles[vehicle];return heading??(source==VehicleCargo(v).Position?new Cell(v.FacingX,v.FacingZ):new Cell(0,0));
    }
    private static Cell EndHeading(Cell[] path,Cell initial)=>path.Length<2?initial:new(path[^1].X-path[^2].X,path[^1].Z-path[^2].Z);
    private (Cell[] Approach,Cell[] Loaded,Cell[] Empty,long Travel,long Road) OrientedServicePlan(OwnedVehicle v,Cell source,Cell dest,long trips)
    {
        var model=Model(v.ModelId);var at=VehicleCargo(v).Position;var facing=new Cell(v.FacingX,v.FacingZ);
        var approach=ServicePath(at,source,v.Id,facing);facing=EndHeading(approach,facing);
        long travel=PathTicks(approach,model),road=PathRoadUnits(approach,model.Id),cycle=0;
        Cell[]? firstLoaded=null,firstEmpty=null;
        var seen=new Dictionary<Cell,(long Cycle,long Travel,long Road)>();
        // A repeated facing at the same source repeats the deterministic round trip. At most four states.
        while(cycle<trips-1)
        {
            if(seen.TryGetValue(facing,out var previous))
            {
                long count=(trips-1-cycle)/(cycle-previous.Cycle);
                if(count>0){travel=checked(travel+count*(travel-previous.Travel));road=checked(road+count*(road-previous.Road));cycle=checked(cycle+count*(cycle-previous.Cycle));if(cycle==trips-1)break;}
            }
            else seen.Add(facing,(cycle,travel,road));
            var loaded=ServicePath(source,dest,v.Id,facing);facing=EndHeading(loaded,facing);
            var empty=ServicePath(dest,source,v.Id,facing);facing=EndHeading(empty,facing);
            firstLoaded??=loaded;firstEmpty??=empty;
            travel=checked(travel+PathTicks(loaded,model)+PathTicks(empty,model));road=checked(road+PathRoadUnits(loaded,model.Id)+PathRoadUnits(empty,model.Id));++cycle;
        }
        var last=ServicePath(source,dest,v.Id,facing);travel=checked(travel+PathTicks(last,model));road=checked(road+PathRoadUnits(last,model.Id));
        return(approach,firstLoaded??last,firstEmpty??[dest],travel,road);
    }
}

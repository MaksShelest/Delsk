using Simulation.Core;
namespace Agents.Algorithmic;
public sealed partial class AlgorithmicController
{
    internal static long TravelTicks(ControllerObservation o,Cell source,Cell target)
    {
        if(o.Map.MovementSpeedDivisor==0)return (long)Distance(o.Map,source,target)*o.Map.MoveTicksPerCell;
        return MovementTiming.TryPersonalTicks(o,source,target)??long.MaxValue/1000;
    }
    private static long FreightTicks(ControllerObservation o,LogisticsView? system,Cell source,Cell target)
    {
        if(o.Map.MovementSpeedDivisor==0)return (long)Distance(o.Map,source,target)*FreightCellEstimate(o);
        var vehicle=system is null?null:o.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Id==system.VehicleId);
        return MovementTiming.TryFreightTicks(o,source,target,vehicle?.Model,vehicle?.Vehicle.Id??0)??long.MaxValue/1000;
    }
}

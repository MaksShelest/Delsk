using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static int FleetCrewMinimum(ControllerObservation o,LogisticsView l) => l.VehicleId!=0?1:l.Level==2?o.LogisticsRules.Upgrade!.MinimumCrew:o.LogisticsRules.MinimumCrew;
    private static long FleetCargoCapacity(ControllerObservation o,LogisticsView l) => l.VehicleId!=0?o.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==l.VehicleId).Model.CapacityVolume:l.Level==2?o.LogisticsRules.Upgrade!.CargoCapacity:o.LogisticsRules.CargoCapacity;
    private static int FreightCellEstimate(ControllerObservation o) => o.Fleet is null?o.LogisticsRules.MoveTicksPerCell:10;
    private Candidate? AcquireFleetLogistics(ControllerObservation o,EnterpriseView enterprise,Cell home)
    {
        var f=o.Fleet!;
        var free=f.Vehicles.Where(v=>v.State!="ServiceReserved"&&!v.Model.MaterialsOnly&&v.Vehicle.Driver==0&&v.Vehicle.Installation is null&&v.Logistics.Value==0&&Inventory(o,v.Vehicle.CargoInventory).OccupiedVolume==0)
            .OrderBy(v=>Distance(o.Map,o.Participant.Position,v.Position)).ThenBy(v=>v.Vehicle.Id).FirstOrDefault();
        if(free is not null)
        {
            var steps=new List<PlanStep>();Move(steps,o.Participant.Position,free.Position);
            steps.Add(new(new(ActionKind.BoardVehicle,VehicleId:free.Vehicle.Id)));Move(steps,free.Position,home);
            steps.Add(new(new(ActionKind.LeaveVehicle,VehicleId:free.Vehicle.Id)));steps.Add(new(new(ActionKind.AssignVehicle,VehicleId:free.Vehicle.Id,Enterprise:enterprise.Id)));
            return new("BuyLogistics","AssignPurchasedVehicle",240000*_state.Settings.ExpansionWeight/100,steps.ToArray());
        }
        if(f.OwnRequests.Length>0)return null;
        var listing=f.Stock.Where(v=>!f.Models.Single(m=>m.Id==v.ModelId).MaterialsOnly && f.Models.Single(m=>m.Id==v.ModelId).PriceMinor+_state.Settings.CashReserveMinor<=o.Account.AvailableMinor)
            .OrderBy(v=>f.Models.Single(m=>m.Id==v.ModelId).PriceMinor).ThenBy(v=>v.Id).FirstOrDefault();
        if(listing is null)return null;
        var buy=new List<PlanStep>();Move(buy,o.Participant.Position,f.Dealer);buy.Add(new(new(ActionKind.BuyVehicle,VehicleId:listing.Id)));
        return new("BuyLogistics","FiniteDealerPurchase",240000*_state.Settings.ExpansionWeight/100,buy.ToArray());
    }
}

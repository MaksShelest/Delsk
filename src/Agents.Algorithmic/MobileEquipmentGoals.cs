using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static bool MobileFleet(ControllerObservation o) => o.Fleet?.Models.Any(m=>m.EquipmentTypeId is not null)==true;

    private IEnumerable<Candidate> MobileEquipmentTransport(ControllerObservation o)
    {
        if (!MobileFleet(o)) yield break;
        foreach (var machine in o.Equipment.Where(e=>e.Deployed==false))
        {
            var type=o.EquipmentTypes.Single(t=>t.Id==machine.TypeId);
            var site=o.Resources.Where(r=>r.ItemId==type.ItemId&&!r.Public&&(r.Owner==Actor||r.Owner is null&&r.Tenant is null))
                .OrderBy(r=>r.Owner==Actor?0:1).ThenBy(r=>Distance(o.Map,machine.Position,r.Position)).FirstOrDefault();
            if (site is null) continue;
            var steps=new List<PlanStep>();
            var vehicle=o.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==machine.Id.Value);
            if (site.Owner!=Actor)
            {
                if(o.Account.AvailableMinor<site.PurchasePriceMinor+_state.Settings.CashReserveMinor)continue;
                if(vehicle.Vehicle.Driver!=0)steps.Add(new(new(ActionKind.LeaveVehicle,VehicleId:vehicle.Vehicle.Id)));
                Move(steps,o.Participant.Position,site.Position);
                steps.Add(new(new(ActionKind.BuyResource,Target:site.Position)));
            }
            else
            {
                if(vehicle.Vehicle.Driver==0)
                { Move(steps,o.Participant.Position,machine.Position);steps.Add(new(new(ActionKind.BoardVehicle,VehicleId:machine.Id.Value))); }
                Move(steps,machine.Position,site.Position);
                steps.Add(new(new(ActionKind.PlaceEquipment,Equipment:machine.Id)));
            }
            yield return new("TransportExtraction","PhysicalCityToOwnedSite",170000,steps.ToArray());
        }
    }
}

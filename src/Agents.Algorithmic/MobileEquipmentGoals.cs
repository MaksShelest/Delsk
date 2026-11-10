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
            var site=o.Resources.Where(r=>type.Accepts(r.ItemId)&&MachineSite(o,r))
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
                steps.Add(new(new(ActionKind.PlaceEquipment,Equipment:machine.Id,ItemId:site.ItemId)));
            }
            yield return new("TransportExtraction","PhysicalCityToOwnedSite",170000,steps.ToArray());
        }
    }

    private IEnumerable<Candidate> MobileEquipmentOperation(ControllerObservation o)
    {
        if (o.Fleet?.Models.Any(m => m.EquipmentTypeId is not null) != true || o.Fleet.Models.Any(m => m.EquipmentLevel > 1)) yield break;
        foreach (var machine in o.Equipment.Where(e => e.Deployed == true && e.Mode == EquipmentMode.Off && e.CycleId == 0 && e.PersonalJobId == 0))
        {
            var type = o.EquipmentTypes.Single(t => t.Id == machine.TypeId);
            var level = type.Levels.Single(l => l.Level == machine.Level);
            string item = machine.ResourceItem ?? type.ItemId;
            var steps = new List<PlanStep>();
            if (o.Participant.Position != machine.Position) Move(steps, o.Participant.Position, machine.Position);
            steps.Add(new(new(ActionKind.StartHarvesting, Equipment: machine.Id, ItemId: item, Quantity: level.CycleQuantity)));
            yield return new("StartExtraction", type.Id + ":" + item, 72000, steps.ToArray());
        }
        foreach (var machine in o.Equipment.Where(e => e.Level == 1 && e.Mode == EquipmentMode.Off && e.CycleId == 0 && e.PersonalJobId == 0 && e.Motion is null))
        {
            var type = o.EquipmentTypes.Single(t => t.Id == machine.TypeId);
            var next = type.Levels.SingleOrDefault(l => l.Level == 2);
            if (next is null || o.Account.AvailableMinor < checked(next.PriceMinor + _state.Settings.CashReserveMinor)) continue;
            var steps = new List<PlanStep>();
            if (o.Participant.Position != machine.Position) Move(steps, o.Participant.Position, machine.Position);
            steps.Add(new(new(ActionKind.UpgradeEquipment, Equipment: machine.Id)));
            yield return new("UpgradeExtraction", type.Id, 64000, steps.ToArray());
        }
    }
}

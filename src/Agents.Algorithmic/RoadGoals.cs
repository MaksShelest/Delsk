using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> RoadCandidates(ControllerObservation o)
    {
        var roads=o.Roads;var fleet=o.Fleet;if(roads is null||fleet is null||HasObligations(o))yield break;
        var order=roads.Orders.Where(r=>r.PaidTick==0&&(r.Assignee==0||r.Assignee==Actor.Value)&&
            (roads.Cells.FirstOrDefault(c=>c.Cell==r.Cell)?.Level??0)==r.Level-1).OrderByDescending(r=>r.Assignee==Actor.Value).ThenBy(r=>r.Id).FirstOrDefault();
        if(order is null)yield break;
        var paver=fleet.Vehicles.Where(v=>v.Model.MaterialsOnly&&v.Vehicle.Installation is null).OrderBy(v=>v.Vehicle.Id).FirstOrDefault();
        if(paver is null)
        {
            var listing=fleet.Stock.FirstOrDefault(v=>v.ModelId=="trakt_d1");
            if(listing is null||fleet.OwnRequests.Length>0)yield break;
            var model=fleet.Models.Single(m=>m.Id==listing.ModelId);
            if(o.Account.AvailableMinor<model.PriceMinor+_state.Settings.CashReserveMinor+10000)yield break;
            var buy=new List<PlanStep>();Move(buy,o.Participant.Position,fleet.Dealer);buy.Add(new(new(ActionKind.BuyVehicle,VehicleId:listing.Id)));
            yield return new("BuildRoad","AcquirePaver",146000,buy.ToArray());yield break;
        }
        var work=roads.OwnWorks.FirstOrDefault(w=>w.VehicleId==paver.Vehicle.Id);
        if(work?.Batch is not null||work is {Enabled:true,Reason:not ("NoRoadMaterials" or "NoMoney" or "PreviousRoadStageRequired")})yield break;
        var rule=roads.Rules.Stages[order.Level-1];
        long normative=rule.PriceMinor+rule.Materials.Sum(m=>m.Quantity*BasePrice(o,m.ItemId))+2L*rule.Ticks*roads.Rules.WageMinor;
        if(order.Reward<=normative||o.Account.AvailableMinor<rule.PriceMinor+2L*rule.Ticks*roads.Rules.WageMinor)yield break;
        var steps=new List<PlanStep>();Move(steps,o.Participant.Position,paver.Position);
        if(work is {Enabled:true})
        {
            steps.Add(new(new(ActionKind.SetRoadWork,VehicleId:paver.Vehicle.Id,Mode:work.Mode,Enabled:false,EquipmentLevel:work.Level,Stops:work.Route)));
            yield return new("BuildRoad","PauseForMaterials",170000,steps.ToArray());yield break;
        }
        if(paver.Vehicle.Driver!=0)
        {steps.Add(new(new(ActionKind.LeaveVehicle)));yield return new("BuildRoad","ReleasePersonalDriver",170000,steps.ToArray());yield break;}
        if(order.Assignee==0)
        {yield return new("BuildRoad","FundedCityTask",170000,[new(new(ActionKind.AcceptRoadOrder,OfferId:order.Id))]);yield break;}
        var cargo=Inventory(o,paver.Vehicle.CargoInventory);var carrier=Carrier(o);
        var missing=rule.Materials.FirstOrDefault(m=>Free(cargo,m.ItemId)<m.Quantity);
        if(missing is not null)
        {
            long quantity=Math.Min(missing.Quantity-Free(cargo,missing.ItemId),Math.Min(FreeVolume(carrier),FreeVolume(cargo))/Volume(o,missing.ItemId));
            if(quantity<=0)yield break;
            long held=Math.Min(quantity,Free(carrier,missing.ItemId));var fetch=held>0?new List<PlanStep>():Fetch(o with{Inventories=[..o.Inventories.Where(i=>i.Id!=cargo.Id)]},missing.ItemId,quantity,out _);
            if(fetch is null)
            {
                var stock=o.ProductionContent?.Imports.FirstOrDefault(i=>i.ItemId==missing.ItemId&&i.Remaining>=quantity);
                if(stock is null)yield break;
                var price=o.ProductionContent!.Rules.Imports.Single(i=>i.ItemId==missing.ItemId).UnitPriceMinor;
                if(order.Reward<=normative+quantity*(price-BasePrice(o,missing.ItemId))||o.Account.AvailableMinor<price*quantity+_state.Settings.CashReserveMinor)yield break;
                var import=new List<PlanStep>();Move(import,o.Participant.Position,o.ProductionContent.Terminal);import.Add(new(new(ActionKind.BuyImport,ItemId:missing.ItemId,Quantity:quantity)));
                yield return new("BuildRoad","FiniteRoadInput",169000,import.ToArray());yield break;
            }
            if(held>0)quantity=held;
            var last=fetch.LastOrDefault(s=>s.Action.Kind==ActionKind.MoveTo)?.Action.Target??o.Participant.Position;Move(fetch,last,paver.Position);
            fetch.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:cargo.Id,ItemId:missing.ItemId,Quantity:quantity)));
            yield return new("BuildRoad","StageRoadMaterial:"+missing.ItemId,170000,fetch.ToArray());yield break;
        }
        steps.Add(new(new(ActionKind.SetRoadWork,VehicleId:paver.Vehicle.Id,Mode:EquipmentMode.Automatic,Enabled:true,EquipmentLevel:order.Level,Stops:[order.Cell])));
        yield return new("BuildRoad","AutomaticCityRoute",171000,steps.ToArray());
    }
}

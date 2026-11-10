using Simulation.Core;
namespace Agents.Algorithmic;
public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> EconomicExtraction(ControllerObservation o)
    {
        if(!UsesEconomicPlanning||o.Enterprises.IsEmpty||_state.Settings.ExpansionWeight==0||HasObligations(o)||o.Unemployed<2)yield break;
        foreach(var type in o.EquipmentTypes.Where(t=>t.CompatibleItems().Any(item=>o.Enterprises.SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Uses(item)))))
        {
            if(o.Equipment.Any(e=>e.TypeId==type.Id))continue;
            var sold=type.Levels.Where(l=>o.Fleet?.Models.Any(m=>m.EquipmentTypeId==type.Id&&m.EquipmentLevel==l.Level)==true).OrderBy(l=>l.PriceMinor).ToArray();
            if(MobileFleet(o)&&sold.Length==0)continue;
            var level=sold.Length>0?sold[0]:type.Levels.OrderBy(l=>l.PriceMinor).First();
            string item=type.CompatibleItems().First(candidate=>o.Enterprises.SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Uses(candidate)));
            foreach(var site in o.Resources.Where(r=>r.ItemId==item&&r.Stock>r.Reserved&&MachineSite(o,r)))
            {
                long land=site.Owner==Actor?0:site.PurchasePriceMinor;long cost=checked(land+level.PriceMinor+level.MinimumCrew*level.WageMinor*_state.Settings.WageReviewTicks+_state.Settings.CashReserveMinor);
                if(o.Account.AvailableMinor<cost||!Reachable(o,site.Position))continue;
                var demand=BestOffer(o,item,level.CycleQuantity,site.Position);if(demand is null||level.CycleQuantity*demand.UnitPriceMinor<=level.CrewCapacity*level.WageMinor*level.CycleTicks)continue;
                if(MobileFleet(o))
                {
                    if(o.Fleet!.OwnRequests.Length>0)continue;
                    var model=o.Fleet.Models.Single(m=>m.EquipmentTypeId==type.Id&&m.EquipmentLevel==level.Level);
                    var listing=o.Fleet.Stock.FirstOrDefault(v=>v.ModelId==model.Id);
                    if(listing is null)continue;
                    var buy=new List<PlanStep>();Move(buy,o.Participant.Position,o.Fleet.Dealer);
                    buy.Add(new(new(ActionKind.BuyVehicle,VehicleId:listing.Id)));
                    yield return new("BuyExtraction","FiniteCityDealer:"+type.Id,71000,buy.ToArray());break;
                }
                var steps=new List<PlanStep>();Move(steps,o.Participant.Position,site.Position);
                steps.Add(new(new(site.Owner==Actor?ActionKind.BuyEquipment:ActionKind.BuyResource,Target:site.Position,EquipmentTypeId:site.Owner==Actor?type.Id:"",EquipmentLevel:level.Level)));
                yield return new("BuyExtraction","PaidOwnedSite:"+site.Position,71000,steps.ToArray());break;
            }
        }
    }
}

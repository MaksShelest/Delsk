using Simulation.Core;
namespace Agents.Algorithmic;
public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> EconomicExtraction(ControllerObservation o)
    {
        if(!UsesEconomicPlanning||o.Enterprises.IsEmpty||_state.Settings.ExpansionWeight==0||HasObligations(o)||o.Unemployed<2)yield break;
        foreach(var type in o.EquipmentTypes.Where(t=>o.Enterprises.SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Uses(t.ItemId))))
        {
            if(o.Equipment.Any(e=>e.TypeId==type.Id))continue;var level=type.Levels.OrderBy(l=>l.PriceMinor).First();
            foreach(var site in o.Resources.Where(r=>r.ItemId==type.ItemId&&!r.Public&&r.Stock>r.Reserved&&(r.Owner is null||r.Owner==Actor)))
            {
                long land=site.Owner==Actor?0:site.PurchasePriceMinor;long cost=checked(land+level.PriceMinor+level.MinimumCrew*level.WageMinor*_state.Settings.WageReviewTicks+_state.Settings.CashReserveMinor);
                if(o.Account.AvailableMinor<cost||!Reachable(o,site.Position))continue;
                var demand=BestOffer(o,type.ItemId,level.CycleQuantity,site.Position);if(demand is null||level.CycleQuantity*demand.UnitPriceMinor<=level.CrewCapacity*level.WageMinor*level.CycleTicks)continue;
                var steps=new List<PlanStep>();Move(steps,o.Participant.Position,site.Position);
                steps.Add(new(new(site.Owner==Actor?ActionKind.BuyEquipment:ActionKind.BuyResource,Target:site.Position,EquipmentTypeId:site.Owner==Actor?type.Id:"",EquipmentLevel:level.Level)));
                yield return new("BuyExtraction","PaidOwnedSite:"+site.Position,71000,steps.ToArray());break;
            }
        }
    }
}

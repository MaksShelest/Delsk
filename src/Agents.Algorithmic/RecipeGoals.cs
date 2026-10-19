using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> RecipeCandidates(ControllerObservation o)
    {
        if(HasObligations(o))yield break;
        foreach(var e in o.Enterprises.Where(e=>e.BatchId==0 && e.ReasonCode!="NoOutputSpace"))
        {
            var b=o.Buildings.SingleOrDefault(b=>b.Id.Value==e.Id.Value);if(b is null)continue;
            var current=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);
            var offer=BestOffer(o,current.OutputItem,current.OutputQuantity,Inventory(o,e.OutputId).Position);if(offer is null)continue;
            var group=o.EmploymentGroups.Single(g=>g.Id.Value==e.Id.Value);
            long Net(RecipeDefinition r)=>checked(r.OutputQuantity*offer.UnitPriceMinor-RecipeInputValue(o,r)-r.EnergyPerBatchMinor-
                e.StaffCapacity*group.WageMinor*((r.FullStaffTicks+e.WorkRateMultiplier-1)/e.WorkRateMultiplier));
            var best=current;
            foreach(string id in o.BuildingTypes.Single(t=>t.Id==b.TypeId).Levels.Where(l=>l.Level<=b.Level).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal))
            {
                var r=o.Map.Recipes.Single(r=>r.Id==id);if(r.OutputItem!=current.OutputItem || Net(r)<=0)continue;
                if((Int128)Net(r)*best.FullStaffTicks>(Int128)Net(best)*r.FullStaffTicks)best=r;
            }
            if(best.Id==current.Id)continue;
            var steps=new List<PlanStep>();
            if(e.Enabled)steps.Add(new(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:false)));
            steps.Add(new(new(ActionKind.SetRecipe,Enterprise:e.Id,RecipeId:best.Id)));
            if(e.Enabled)steps.Add(new(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true)));
            yield return new("SelectRecipe",best.Id,135000*_state.Settings.ProductionWeight/100,steps.ToArray());
        }
    }
}

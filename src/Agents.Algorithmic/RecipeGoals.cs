using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> RecipeCandidates(ControllerObservation o)
    {
        if(HasObligations(o))yield break;
        if(o.ProductionContent is {} content)
        {
            foreach(var parent in o.Enterprises)
            foreach(var line in ProductionWorkplaces(parent))
            {
                var unit=line.Unit;if(unit.BatchId!=0||content.Projects.Any(p=>p.Workshop==line.Workshop)||o.Development!.Preparing.Contains(parent.Id.Value))continue;
                var b=o.Buildings.Single(b=>b.Id.Value==parent.Id.Value);var type=o.BuildingTypes.Single(t=>t.Id==b.TypeId);
                int tech=o.Development.Experience.Single(x=>x.Industry==b.TypeId).TechnologyLevel,version=content.Technologies.SingleOrDefault(t=>t.Workshop==line.Workshop)?.Version??1;
                var input=Inventory(o,parent.InputId);var output=Inventory(o,parent.OutputId);var group=o.EmploymentGroups.Single(g=>g.Id==line.Group);
                long Net(RecipeDefinition r)
                {
                    var offer=BestOffer(o,r.OutputItem,r.OutputQuantity,b.Position);if(offer is null)return long.MinValue/1000000;
                    return checked(r.OutputQuantity*offer.UnitPriceMinor-RecipeInputValue(o,r)-r.EnergyPerBatchMinor-5*group.WageMinor*r.FullStaffTicks);
                }
                var current=o.Map.Recipes.Single(r=>r.Id==unit.RecipeId);var best=current;
                foreach(var id in type.Levels.Where(l=>l.Level<=tech).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal))
                {
                    var r=o.Map.Recipes.Single(r=>r.Id==id);if(content.Rules.Versions.Single(v=>v.RecipeId==id).Version>version||Net(r)<=0||
                        r.Ingredients.Sum(i=>i.Quantity*Volume(o,i.ItemId))>input.Capacity||r.OutputQuantity*Volume(o,r.OutputItem)>output.Capacity)continue;
                    if(!r.Ingredients.All(m=>o.Inventories.Any(i=>Free(i,m.ItemId)>=m.Quantity)||o.Map.ResourceNodes.Any(n=>n.ItemId==m.ItemId&&AvailableResource(o,n))||content.Imports.Any(i=>i.ItemId==m.ItemId&&i.Remaining>=m.Quantity)))continue;
                    if((Int128)Net(r)*best.FullStaffTicks>(Int128)Net(best)*r.FullStaffTicks)best=r;
                }
                if(best.Id==current.Id)continue;var plan=new List<PlanStep>();if(unit.Enabled)plan.Add(new(new(ActionKind.SetProductionEnabled,Enterprise:parent.Id,WorkshopId:line.Workshop,Enabled:false)));
                plan.Add(new(new(ActionKind.SetRecipe,Enterprise:parent.Id,WorkshopId:line.Workshop,RecipeId:best.Id)));plan.Add(new(new(ActionKind.SetProductionEnabled,Enterprise:parent.Id,WorkshopId:line.Workshop,Enabled:true)));
                yield return new("SelectRecipe",best.Id,135000*_state.Settings.ProductionWeight/100,plan.ToArray());
            }
            yield break;
        }
        foreach(var e in o.Enterprises.Where(e=>e.BatchId==0 && e.ReasonCode!="NoOutputSpace"))
        {
            var b=o.Buildings.SingleOrDefault(b=>b.Id.Value==e.Id.Value);if(b is null)continue;
            var current=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);
            var offer=BestOffer(o,current.OutputItem,current.OutputQuantity,Inventory(o,e.OutputId).Position);if(offer is null)continue;
            var group=o.EmploymentGroups.Single(g=>g.Id.Value==e.Id.Value);
            long Duration(RecipeDefinition r)=>checked((r.FullStaffTicks+e.WorkRateMultiplier-1)/e.WorkRateMultiplier);
            long Net(RecipeDefinition r)=>checked(r.OutputQuantity*offer.UnitPriceMinor-RecipeInputValue(o,r)-r.EnergyPerBatchMinor-
                e.StaffCapacity*group.WageMinor*Duration(r));
            var best=current;
            foreach(string id in o.BuildingTypes.Single(t=>t.Id==b.TypeId).Levels.Where(l=>l.Level<=(o.Development?.Experience.Single(x=>x.Industry==b.TypeId).TechnologyLevel??b.Level)).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal))
            {
                var r=o.Map.Recipes.Single(r=>r.Id==id);if(r.OutputItem!=current.OutputItem || Net(r)<=0)continue;
                if((Int128)Net(r)*Duration(best)>(Int128)Net(best)*Duration(r))best=r;
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

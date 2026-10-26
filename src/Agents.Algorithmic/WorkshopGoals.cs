using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static IEnumerable<(EnterpriseView Unit,EmploymentGroupId Group,long Workshop)> ProductionWorkplaces(EnterpriseView e)
    {
        if(e.Workshops.IsDefault){yield return(e,new(e.Id.Value),0);yield break;}
        foreach(var w in e.Workshops)
            yield return(e with{RecipeId=w.RecipeId,Enabled=w.Enabled,Staff=w.Staff,ReasonCode=w.ReasonCode,BatchId=w.BatchId,WorkDone=w.WorkDone,
                WorkRequired=w.WorkRequired,CompletedBatches=w.CompletedBatches,EnergyPaidMinor=w.ServicePaidMinor},w.GroupId,w.Id);
    }
    private IEnumerable<Candidate> WorkshopCandidates(ControllerObservation o)
    {
        var settings=_state.Settings;bool recovery=o.Account.AvailableMinor<settings.CashReserveMinor;var carrier=Carrier(o);
        foreach(var parent in o.Enterprises.Where(e=>!e.Workshops.IsDefault))
        {
            var input=Inventory(o,parent.InputId);var output=Inventory(o,parent.OutputId);
            foreach(var line in ProductionWorkplaces(parent).Where(l=>l.Workshop!=parent.Id.Value))
            {
                var e=line.Unit;var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);
                if(e.ReasonCode=="PreparingDemolition"||o.Development?.Preparing.Contains(parent.Id.Value)==true||o.Development?.Projects.Any(p=>p.BuildingId==parent.Id.Value)==true)continue;
                if(recovery&&e.Enabled)yield return One("PreserveCash","Workshop:"+line.Workshop,210000,new(ActionKind.SetProductionEnabled,Enterprise:parent.Id,WorkshopId:line.Workshop,Enabled:false));
                if(!recovery&&!e.Enabled)yield return One("ResumeProduction","Workshop:"+line.Workshop,111000*settings.ProductionWeight/100,new(ActionKind.SetProductionEnabled,Enterprise:parent.Id,WorkshopId:line.Workshop,Enabled:true));
                if(!recovery&&e.Staff==0&&e.Enabled)
                {
                    var donor=parent.Workshops.Where(w=>w.Staff>=2).OrderByDescending(w=>w.Staff).ThenBy(w=>w.Id).FirstOrDefault();
                    if(donor is not null)yield return One("AssignWorkshopWorkers","ExistingStaff:"+line.Workshop,115000,new(ActionKind.AssignWorkshopWorkers,Enterprise:parent.Id,WorkshopId:line.Workshop,Group:donor.GroupId,Quantity:1));
                }
                foreach(var ingredient in r.Ingredients.Where(i=>!recovery&&Free(input,i.ItemId)<i.Quantity))
                {
                    long q=Math.Min(Math.Max(ingredient.Quantity,settings.BatchQuantity/2),Math.Min(FreeVolume(carrier)/Volume(o,ingredient.ItemId),InputSpace(o,input,r,ingredient.ItemId)));
                    var steps=Acquire(o,ingredient.ItemId,q);if(steps is null||q<ingredient.Quantity)continue;
                    var at=AcquisitionPosition(o,ingredient.ItemId,q);Move(steps,at,input.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:input.Id,ItemId:ingredient.ItemId,Quantity:q)));
                    yield return new("SupplyProduction","Workshop:"+line.Workshop+":"+ingredient.ItemId,81000*settings.ProductionWeight/100,steps.ToArray());
                }
                long free=Free(output,r.OutputItem),take=Math.Min(free,FreeVolume(carrier)/Volume(o,r.OutputItem));var offer=BestOffer(o,r.OutputItem,take,output.Position);
                if(take>0&&offer is not null)
                {
                    take=Math.Min(take,offer.RemainingQuantity);var steps=new List<PlanStep>();Move(steps,o.Participant.Position,output.Position);
                    steps.Add(new(new(ActionKind.Transfer,Source:output.Id,Destination:carrier.Id,ItemId:r.OutputItem,Quantity:take)));Move(steps,output.Position,offer.Position);
                    steps.Add(new(new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:r.OutputItem,Quantity:take)));
                    yield return new("SellOutput","Workshop:"+line.Workshop,95000+Rate(o,offer,take,output.Position)*settings.CashWeight,steps.ToArray());
                }
            }
            var b=o.Buildings.SingleOrDefault(b=>b.Id.Value==parent.Id.Value);var type=b is null?null:o.BuildingTypes.Single(t=>t.Id==b.TypeId);
            if(o.Development?.Preparing.Contains(parent.Id.Value)==true||o.Development?.Projects.Any(p=>p.BuildingId==parent.Id.Value)==true||recovery||HasObligations(o)||type?.WorkshopOutfit is not {} outfit||parent.Workshops.Length+parent.Installations.Length>=type.Levels.Single(l=>l.Level==b!.Level).WorkshopSlots)continue;
            // Buy capacity only behind a busy, supplied, profitable line and a funded payroll reserve.
            var recipe=o.Map.Recipes.Single(r=>r.Id==parent.RecipeId);var demand=BestOffer(o,recipe.OutputItem,recipe.OutputQuantity,input.Position);
            long cost=checked(outfit.PriceMinor+5L*settings.BaseWageMinor*settings.WageReviewTicks);
            if(parent.CompletedBatches==0||demand is null||o.Account.AvailableMinor<cost+settings.CashReserveMinor||outfit.Materials.Any(m=>Free(input,m.ItemId)<m.Quantity)||
                recipe.OutputQuantity*demand.UnitPriceMinor<=RecipeInputValue(o,recipe)+5L*settings.BaseWageMinor*recipe.FullStaffTicks+2*recipe.FullStaffTicks)continue;
            var plan=new List<PlanStep>();Move(plan,o.Participant.Position,input.Position);plan.Add(new(new(ActionKind.OutfitWorkshop,Enterprise:parent.Id)));
            yield return new("OutfitWorkshop","PaidReadyPlace",70000*settings.ExpansionWeight/100,plan.ToArray());
        }
    }
}

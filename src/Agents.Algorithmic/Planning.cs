using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static bool ExpansionGoal(string goal) => goal is "BuildProduction" or "BuyExtraction" or "BuildShop" or "BuyLogistics";
    private static bool ObligationGoal(string goal) => goal is "DeliverContract" or "CollectPurchase" or "ConfigureContract" or
        "BuyInputs" or "SupplyProduction" or "ResumeProduction" or "AdjustWage" or "ClearCarrier";
    private bool HasObligations(ControllerObservation o) => o.OwnDeliveryContracts.Any(c=>c.Supplier==Actor && c.ClosedTick is null && c.UnprovidedQuantity>0) ||
        o.OwnMarketAllocations.Any(c=>c.Buyer==Actor && c.RemainingQuantity>0) || o.OwnSupplyRequests.Length>0;
    private bool PlanRelevant(ControllerObservation o)
    {
        if (_state.NextStep>=_state.Plan.Length) return true;
        var next=_state.Plan[_state.NextStep].Action;
        if(next.Kind==ActionKind.Transfer && (!o.Inventories.Any(i=>i.Id==next.Source && Free(i,next.ItemId)>=next.Quantity) ||
            !o.Inventories.Any(i=>i.Id==next.Destination && FreeVolume(i)>=checked(next.Quantity*Volume(o,next.ItemId)))))return false;
        foreach(var step in _state.Plan.Skip(_state.NextStep))
        {
            var a=step.Action;
            if(a.Kind==ActionKind.DeliverContract && !o.OwnDeliveryContracts.Any(c=>c.Id==a.ContractId && c.Supplier==Actor && c.UnprovidedQuantity>=a.Quantity && c.DeadlineTick>o.Tick))return false;
            if(a.Kind==ActionKind.CollectPurchase && !o.OwnMarketAllocations.Any(c=>c.Id==a.PurchaseId && c.Buyer==Actor && c.RemainingQuantity>=a.Quantity && c.DeadlineTick>o.Tick))return false;
            if(a.Kind is ActionKind.AcceptSupport or ActionKind.RejectSupport or ActionKind.SignSupport && !o.Support.Proposals.Any(p=>p.Id==a.SupportProposalId && p.Election==o.Elections.NextElectionId &&
                p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources))return false;
            if(a.Kind==ActionKind.Transfer && a.SupportProposalId>0 && !o.Support.Proposals.Any(p=>p.Id==a.SupportProposalId && p.Status==SupportProposalStatus.AcceptedAwaitingResources && p.Election==o.Elections.NextElectionId))return false;
            if(a.Kind==ActionKind.SellAvailableToCapital && !o.CapitalOffers.Any(x=>x.Id==a.OfferId && x.RemainingQuantity>0 && (x.ExpiresAtTick is null || x.ExpiresAtTick>o.Tick)))return false;
        }
        return true;
    }
    private static long PlanTicks(ControllerObservation o,IEnumerable<PlanStep> steps)
    {
        Cell position=o.Participant.Position;long ticks=0;
        foreach(var step in steps)
        {
            ticks=checked(ticks+1);var a=step.Action;
            if(a.Kind==ActionKind.MoveTo){int d=Distance(o.Map,position,a.Target);if(d<0)return long.MaxValue;ticks=checked(ticks+(long)d*o.Map.MoveTicksPerCell);position=a.Target;}
            if(a.Kind==ActionKind.Harvest){
                var machine=o.Equipment.FirstOrDefault(m=>m.Id==a.Equipment);
                ticks=checked(ticks+(machine is null?a.Quantity*o.Map.HarvestTicksPerUnit:
                    ((a.Quantity-1)/o.EquipmentTypes.Single(t=>t.Id==machine.TypeId).Levels.Single(l=>l.Level==machine.Level).CycleQuantity+1)*
                    o.EquipmentTypes.Single(t=>t.Id==machine.TypeId).Levels.Single(l=>l.Level==machine.Level).CycleTicks));
            }
        }
        return ticks;
    }
    private List<PlanStep>? Fetch(ControllerObservation o,string item,long quantity,out Cell end)
    {
        end=o.Participant.Position;var carrier=Carrier(o);
        if(quantity<=0)return [];
        var source=o.Inventories.Where(i=>i.Id!=carrier.Id && Free(i,item)>=quantity && Reachable(o,i.Position))
            .OrderBy(i=>Distance(o.Map,o.Participant.Position,i.Position)).ThenBy(i=>i.Id.Value).FirstOrDefault();
        if(source is not null){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,source.Position);
            steps.Add(new(new(ActionKind.Transfer,Source:source.Id,Destination:carrier.Id,ItemId:item,Quantity:quantity)));end=source.Position;return steps;}
        var acquired=Acquire(o,item,quantity);
        if(acquired is not null)end=AcquisitionPosition(o,item,quantity);
        return acquired;
    }
    private static long BasePrice(ControllerObservation o,string item)=>o.Map.Items.Single(i=>i.Id==item).BasePriceMinor;
    private long Need(ControllerObservation o,string item)
    {
        long need=o.Enterprises.Where(e=>o.Map.Recipes.Single(r=>r.Id==e.RecipeId).InputItem==item).Sum(e=>{
            var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);return Math.Max(0,Math.Max(r.InputQuantity,_state.Settings.BatchQuantity/2)-Free(Inventory(o,e.InputId),item));});
        need=checked(need+o.OwnDeliveryContracts.Where(c=>c.Supplier==Actor && c.ClosedTick is null && c.ItemId==item).Sum(c=>c.UnprovidedQuantity));
        long incoming=o.OwnMarketAllocations.Where(c=>c.Buyer==Actor && c.ItemId==item).Sum(c=>c.RemainingQuantity)+
            o.OwnMarketBids.Where(b=>o.MarketOffers.Any(m=>m.Id==b.OfferId && m.ItemId==item)).Sum(b=>b.Quantity);
        incoming=checked(incoming+o.OwnDeliveryContracts.Where(c=>c.Buyer==Actor && c.ClosedTick is null && c.ItemId==item).Sum(c=>c.RemainingQuantity));
        incoming=checked(incoming+o.BuyOffers.Where(b=>b.Buyer==Actor && b.ItemId==item && b.Status=="Open").Sum(b=>b.RemainingQuantity));
        return Math.Max(0,checked(need-Free(Carrier(o),item)-incoming));
    }
}

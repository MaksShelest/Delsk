using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> LogisticsCandidates(ControllerObservation o)
    {
        if(!_state.Settings.LogisticsEnabled)yield break;
        foreach(var e in o.Enterprises)
        {
            var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var output=Inventory(o,e.OutputId);var input=Inventory(o,e.InputId);
            var system=o.OwnLogistics.FirstOrDefault(l=>l.EnterpriseId==e.Id);
            var contract=o.OwnDeliveryContracts.Where(c=>c.Supplier==Actor && c.ClosedTick is null && c.UnprovidedQuantity>0 && c.ItemId==r.OutputItem &&
                Distance(o.Map,output.Position,c.Position)>=0 && checked((long)Distance(o.Map,output.Position,c.Position)*o.LogisticsRules.MoveTicksPerCell+4)<=c.DeadlineTick-o.Tick)
                .OrderBy(c=>c.DeadlineTick).ThenBy(c=>c.Id).FirstOrDefault();
            var destination=o.Buildings.Where(b=>b.Kind is BuildingKind.Warehouse or BuildingKind.Shop && FreeVolume(Inventory(o,b.InputId))>=Volume(o,r.OutputItem))
                .OrderBy(b=>Distance(o.Map,output.Position,b.Position)).ThenBy(b=>b.Id.Value).FirstOrDefault();
            if(system is null)
            {
                if(o.Account.AvailableMinor>=checked(o.LogisticsRules.PurchasePriceMinor+_state.Settings.CashReserveMinor) &&
                    (Free(output,r.OutputItem)>=r.OutputQuantity || e.CompletedBatches>0) && (contract is not null || destination is not null))
                {
                    var steps=new List<PlanStep>();Move(steps,o.Participant.Position,output.Position);steps.Add(new(new(ActionKind.BuyLogistics,Enterprise:e.Id)));
                    yield return new("BuyLogistics",e.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),115000*_state.Settings.ExpansionWeight/100,steps.ToArray());
                }
                continue;
            }
            if(contract is not null)
            {
                if(!system.Queue.Any(j=>j.ContractId==contract.Id && j.Enabled))
                    yield return One("ConfigureContract",contract.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),800000,
                        new(ActionKind.QueueLogisticsJob,Logistics:system.Id,ContractId:contract.Id,Enabled:true));
                else if(!system.Enabled)yield return One("ConfigureContract","EnableOutput",790000,new(ActionKind.SetLogisticsEnabled,Logistics:system.Id,Direction:LogisticsDirection.Output,Enabled:true));
            }
            if(contract is not null)continue;
            if(o.Account.AvailableMinor<_state.Settings.CashReserveMinor && contract is null && system.Trip is null)
            {
                if(system.Enabled)yield return One("PauseLogistics","CashReserve",250000,new(ActionKind.SetLogisticsEnabled,Logistics:system.Id,Direction:LogisticsDirection.Output,Enabled:false));
                if(system.InboundEnabled)yield return One("PauseLogistics","InboundCashReserve",250000,new(ActionKind.SetLogisticsEnabled,Logistics:system.Id,Direction:LogisticsDirection.Inbound,Enabled:false));
                continue;
            }
            if(destination is not null && Free(output,r.OutputItem)>0)
            {
                if(!system.Queue.Any(j=>j.OwnDestination==destination.InputId && j.Enabled))
                    yield return One("ConfigureOutput",destination.Id.ToString(),200000,new(ActionKind.QueueLogisticsJob,Logistics:system.Id,Destination:destination.InputId,Enabled:true));
                else if(!system.Enabled)yield return One("ConfigureOutput","Enable",190000,new(ActionKind.SetLogisticsEnabled,Logistics:system.Id,Direction:LogisticsDirection.Output,Enabled:true));
            }
            if(Free(input,r.InputItem)>=_state.Settings.BatchQuantity)continue;
            var source=o.Buildings.Where(b=>b.Kind is BuildingKind.Warehouse or BuildingKind.Shop && Free(Inventory(o,b.InputId),r.InputItem)>0 &&
                Distance(o.Map,input.Position,b.Position)>=0).OrderBy(b=>Distance(o.Map,input.Position,b.Position)).ThenBy(b=>b.Id.Value).FirstOrDefault();
            if(source is null)continue;
            if(!system.Queue.Any(j=>j.InboundSource==source.InputId && j.Enabled))
                yield return One("ConfigureInbound",source.Id.ToString(),210000,new(ActionKind.QueueLogisticsJob,Logistics:system.Id,Source:source.InputId,Enabled:true));
            else if(!system.InboundEnabled)yield return One("ConfigureInbound","Enable",205000,new(ActionKind.SetLogisticsEnabled,Logistics:system.Id,Direction:LogisticsDirection.Inbound,Enabled:true));
        }
    }
}

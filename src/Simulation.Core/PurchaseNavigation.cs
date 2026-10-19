using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record PurchaseGuide(long PurchaseId,Cell Position,ImmutableArray<Cell> Path,string ReasonCode,long RemainingTicks);

public sealed partial class SimulationEngine
{
    public PurchaseGuide NavigatePurchase(ParticipantId actor,long purchase)
    {
        var a=_marketAllocations.SingleOrDefault(a=>a.Id==purchase && a.Buyer==actor);
        if(a is null || !_actors.TryGetValue(actor.Value,out var buyer))return new(purchase,default,[],"NotAvailable",0);
        var position=_economy.Inventories[a.SourceInventory.Value].Position;
        if(a.ClosedTick is not null)return new(purchase,position,[],"PurchaseClosed",0);
        try{return new(purchase,position,[.._map.FindPath(buyer.Position,position)],"Ready",Math.Max(0,a.DeadlineTick-Tick));}
        catch(RuleViolation e){return new(purchase,position,[],e.ReasonCode,Math.Max(0,a.DeadlineTick-Tick));}
    }
}

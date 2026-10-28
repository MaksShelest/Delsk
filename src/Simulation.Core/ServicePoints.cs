using System.Collections.Immutable;
namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private Cell ServiceCapitalTerminal=>_config.Layout!.CivicObjects.Single(s=>s.Kind=="terminal").Entry;
    private ServiceExecutionRules? ServiceExecution=>_config.Economy?.ServiceExecution;
    private sealed record ServiceSite(long Id,long Owner,Cell Position,string Type,bool Source,bool Receive);
    private ServiceSite? ExecutionSite(long id)
    {
        if(id==0)return new(0,0,ServiceCapitalTerminal,"Capital",false,true);
        if(!_economy.Inventories.TryGetValue(id,out var inventory)||IsGround(id))return null;
        if(ServiceBuilding(id) is {} b)return new(id,b.Owner,b.Position,b.Type.Kind.ToString(),true,b.Input==id);
        if(_equipment.Values.SingleOrDefault(e=>e.Storage==id) is {} equipment&&equipment.Motion is null)
            return new(id,equipment.Owner,equipment.Position,"Extraction",true,true);
        if(_importPickups.Values.Any(p=>p.Value==id))return new(id,inventory.Owner,inventory.Position,"ImportPickup",true,false);
        return null;
    }
    private Cell ServiceAccessPoint(long inventory,long vehicle)
    {
        var site=ExecutionSite(inventory)??throw new RuleViolation("ServicePointRequired");
        if(site.Type!="Extraction")return site.Position;
        var at=site.Position;Cell[] neighbors=[new(at.X-1,at.Z),new(at.X+1,at.Z),new(at.X,at.Z-1),new(at.X,at.Z+1)];
        foreach(var stop in neighbors.Where(_map.Walkable).OrderByDescending(PublicRoadCell).ThenBy(c=>c.Z).ThenBy(c=>c.X))
            try{ServicePath(VehicleCargo(_vehicles[vehicle]).Position,stop,vehicle);return stop;}catch(RuleViolation){ }
        throw new RuleViolation("NoPath");
    }
    private long SourceRemaining(ServiceOrder o)=>_serviceRuns.TryGetValue(o.Id,out var r)?checked(o.Quote.Terms.Quantity-r.Loaded-r.Released):o.Quote.Terms.Quantity;
    private void CheckExecutionPoints(long customer,ServiceTerms terms,long except)
    {
        if(terms.Quantity<=0||!_items.ContainsKey(terms.ItemId))throw new RuleViolation("InvalidQuantity");
        var source=ExecutionSite(terms.Source.Value);var destination=ExecutionSite(terms.Destination.Value);
        if(source is null||!source.Source||destination is null||!destination.Receive||source.Position==destination.Position)throw new RuleViolation("ServicePointRequired");
        if(terms.Destination.Value!=0){RequireProductionItem(terms.Destination.Value,terms.ItemId);if(HasOverflow(terms.Destination.Value))throw new RuleViolation("DestinationOverflow");}
        if(terms.PurchaseId!=0)
        {
            var p=_marketAllocations.SingleOrDefault(a=>a.Id==terms.PurchaseId&&a.Buyer.Value==customer&&a.SourceInventory==terms.Source&&a.ItemId==terms.ItemId&&a.ClosedTick is null&&a.DeadlineTick>=Tick);
            if(p is null||p.RemainingQuantity-PurchaseServiceQuantity(p.Id,except)-_transfers.Values.Where(t=>t.Kind=="Pickup"&&t.Contract==p.Id).Sum(t=>t.Remaining)<terms.Quantity)throw new RuleViolation("ServiceCargoUnavailable");
        }
        else
        {
            if(source.Owner!=customer)throw new RuleViolation("ServiceCargoUnavailable");
            var inv=_economy.Inventories[source.Id];long own=except==0?0:inv.Reservations.GetValueOrDefault("service-stock:"+except)?.Quantity??0;
            if(checked(inv.Free(terms.ItemId)+own)<terms.Quantity)throw new RuleViolation("NoStock");
        }
        if(terms.ContractId!=0)
        {
            var d=_deliveryContracts.SingleOrDefault(d=>d.Id==terms.ContractId&&d.Supplier.Value==customer&&(d.DestinationInventory?.Value??0)==destination.Id&&d.ItemId==terms.ItemId&&d.ClosedTick is null&&d.DeadlineTick>=Tick);
            if(d is null||d.UnprovidedQuantity+(except==0?0:terms.Quantity)<terms.Quantity)throw new RuleViolation("ServiceCargoUnavailable");
        }
        else if(destination.Id==0||destination.Owner!=customer)throw new RuleViolation("ServicePointRequired");
    }
    private InventoryId ServiceReturnPoint(long customer,ServiceTerms terms,Cell source,long vehicle)
    {
        if(_economy.Inventories[terms.Source.Value].Owner==customer)return terms.Source;
        foreach(var inv in _economy.Inventories.Values.Where(i=>i.Owner==customer).OrderBy(i=>Math.Abs(i.Position.X-source.X)+Math.Abs(i.Position.Z-source.Z)).ThenBy(i=>i.Id))
        {
            var site=ExecutionSite(inv.Id);if(site?.Receive!=true)continue;
            try{RequireProductionItem(inv.Id,terms.ItemId);ServicePath(source,site.Position,vehicle);return new(inv.Id);}catch(RuleViolation){ }
        }
        throw new RuleViolation("ServiceReturnPointRequired");
    }
    private ImmutableArray<ServicePoint> ExecutionPoints(ParticipantId actor)
    {
        var points=new List<ServicePoint>();
        foreach(var inv in _economy.Inventories.Values.OrderBy(i=>i.Id))
        {
            var p=ExecutionSite(inv.Id);if(p is null)continue;
            bool source=p.Source&&(p.Owner==actor.Value||_marketAllocations.Any(a=>a.Buyer==actor&&a.ClosedTick is null&&a.SourceInventory.Value==p.Id));
            bool receive=p.Receive&&(p.Owner==actor.Value||_deliveryContracts.Any(d=>d.Supplier==actor&&d.ClosedTick is null&&d.DestinationInventory?.Value==p.Id));
            if(source||receive)points.Add(new(new(p.Id),new(p.Owner),p.Position,ServiceBuilding(p.Id)?.Type.Kind??BuildingKind.Warehouse,source,receive){PointType=p.Type});
        }
        if(_deliveryContracts.Any(d=>d.Supplier==actor&&d.ClosedTick is null&&d.Buyer is null))points.Add(new(new(0),new(0),ServiceCapitalTerminal,BuildingKind.Warehouse,false,true){PointType="Capital"});
        return [..points];
    }
    private bool QuoteNeedsReconfirmation(ServiceQuote old,ServiceQuote current)=>current.FeeMinor>old.FeeMinor||current.EtaTicks>old.EtaTicks||current.BufferTicks>old.BufferTicks||current.ReturnInventory!=old.ReturnInventory||current.ReturnPosition!=old.ReturnPosition;
}

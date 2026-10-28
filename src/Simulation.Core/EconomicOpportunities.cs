using System.Collections.Immutable;
namespace Simulation.Core;
public sealed record OverflowOpportunity(InventoryId Inventory,Cell Position,OverflowLot Lot);
public sealed record EconomicOpportunities(ImmutableArray<OverflowOpportunity> Overflow,ImmutableArray<ServiceQuote> ServiceQuotes);
public sealed partial class SimulationEngine
{
    // Pure, bounded previews through the same quote validator exposed to the player.
    // Only own/paid source rights and own or contractually consented destinations are considered.
    private EconomicOpportunities EconomicOpportunitiesOf(ParticipantId actor)
    {
        var overflow=_economy.Inventories.Values.Where(i=>i.Owner==actor.Value).SelectMany(i=>i.Overflow.Values.Select(l=>new OverflowOpportunity(new(i.Id),i.Position,l))).OrderBy(p=>p.Lot.ExpiresTick).ToImmutableArray();
        var quotes=new List<ServiceQuote>();var services=ServicesOf(actor);
        if(ServiceExecution is null||services is null||_actors[actor.Value].Job is not null)return new(overflow,[]);
        var vehicles=services.Catalog.Where(c=>c.Available&&c.Carrier!=actor).OrderBy(c=>c.VehicleId).Take(2).ToArray();
        var sources=services.OwnPoints.Where(p=>p.CanSource).OrderBy(p=>p.Inventory.Value).Take(8);
        var destinations=services.OwnPoints.Where(p=>p.CanReceive).OrderBy(p=>p.Inventory.Value).Take(4).ToArray();int attempts=0;
        foreach(var source in sources)
        foreach(var item in (_economy.Inventories[source.Inventory.Value].Owner==actor.Value?
            _economy.Inventories[source.Inventory.Value].Stock.Keys.AsEnumerable():
            _marketAllocations.Where(a=>a.Buyer==actor&&a.SourceInventory==source.Inventory&&a.ClosedTick is null).Select(a=>a.ItemId).Distinct()).Order(StringComparer.Ordinal).Take(3))
        foreach(var destination in destinations.Where(p=>p.Position!=source.Position))
        foreach(var car in vehicles)
        {
            if(++attempts>32)return new(overflow,[..quotes]);
            var claim=_marketAllocations.FirstOrDefault(a=>a.Buyer==actor&&a.ClosedTick is null&&a.SourceInventory==source.Inventory&&a.ItemId==item);
            long free=_economy.Inventories[source.Inventory.Value].Owner==actor.Value?_economy.Inventories[source.Inventory.Value].Free(item):claim?.RemainingQuantity??0;
            var contract=_deliveryContracts.FirstOrDefault(d=>d.Supplier==actor&&d.ClosedTick is null&&(d.DestinationInventory?.Value??0)==destination.Inventory.Value&&d.ItemId==item);
            long quantity=Math.Min(free,checked(2*(car.CapacityVolume/_items[item].UnitVolume)));if(contract is not null)quantity=Math.Min(quantity,contract.UnprovidedQuantity);
            else if(destination.Inventory.Value!=0)
            {
                var receiving=_economy.Inventories[destination.Inventory.Value];long room=Math.Max(0,receiving.Capacity-receiving.Occupied(_items)-ExpectedServiceVolume(destination.Inventory.Value));
                quantity=Math.Min(quantity,room/_items[item].UnitVolume);
            }
            if(quantity<=0)continue;
            var result=QuoteDeliveryService(actor,new(car.VehicleId,source.Inventory,destination.Inventory,item,quantity,claim?.Id??0,contract?.Id??0));
            if(result.Quote is {} quote)quotes.Add(quote);
        }
        return new(overflow,[..quotes]);
    }
}

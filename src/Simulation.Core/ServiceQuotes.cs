using System.Collections.Immutable;
namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private ServiceRules? Services=>_config.Economy?.Services;
    private readonly HashSet<long> _serviceListings=[];
    private readonly SortedDictionary<long,ServiceOrder> _serviceOrders=[];
    private bool ServiceVehicleBusy(long vehicle)=>_serviceOrders.Values.Any(o=>o.Active&&o.Quote.Terms.VehicleId==vehicle);
    private bool ServicePointBusy(long inventory)=>_serviceOrders.Values.Any(o=>o.Active&&(o.Quote.Terms.Source.Value==inventory||o.Quote.Terms.Destination.Value==inventory));
    private bool ServiceFree(OwnedVehicle v)=>v.Driver==0&&v.Installation is null&&VehicleLogistics(v.Id) is null&&
        !VehicleTransferring(v.CargoInventory.Value)&&!RoadVehicleWorking(v.Id)&&!(_roadWorks.TryGetValue(v.Id,out var w)&&w.Enabled)&&
        !(_traffic.TryGetValue(v.Id,out var t)&&(t.Progress>0||t.Resume is not null||t.Index<t.Path.Length-1))&&VehicleCargo(v).Stock.Count==0;
    private BuildingState? ServiceBuilding(long inventory)=>_buildings.Values.FirstOrDefault(b=>!Demolished(b.Id)&&!PreparingDemolition(b.Id)&&!PreparingDevelopment(b.Id)&&(b.Input==inventory||b.Output==inventory));
    private long PurchaseServiceQuantity(long purchase,long except=0)=>_serviceOrders.Values.Where(o=>o.Active&&o.Id!=except&&o.Quote.Terms.PurchaseId==purchase).Sum(o=>o.Quote.Terms.Quantity);
    private void CheckServicePoints(long customer,ServiceTerms terms,long except=0)
    {
        if(Services is null)throw new RuleViolation("ServiceUnavailable");
        if(terms.Quantity<=0||!_items.ContainsKey(terms.ItemId))throw new RuleViolation("InvalidQuantity");
        var source=ServiceBuilding(terms.Source.Value);var destination=ServiceBuilding(terms.Destination.Value);
        if(source is null||destination is null||source.Position==destination.Position||terms.Source==terms.Destination||destination.Input!=terms.Destination.Value)throw new RuleViolation("ServicePointRequired");
        RequireProductionItem(terms.Destination.Value,terms.ItemId);
        if(HasOverflow(terms.Destination.Value))throw new RuleViolation("DestinationOverflow");
        if(terms.PurchaseId!=0)
        {
            var claim=_marketAllocations.FirstOrDefault(a=>a.Id==terms.PurchaseId&&a.Buyer.Value==customer&&a.SourceInventory==terms.Source&&a.ItemId==terms.ItemId&&a.ClosedTick is null&&a.DeadlineTick>=Tick);
            if(claim is null||claim.RemainingQuantity-PurchaseServiceQuantity(claim.Id,except)-_transfers.Values.Where(t=>t.Kind=="Pickup"&&t.Contract==claim.Id).Sum(t=>t.Remaining)<terms.Quantity)throw new RuleViolation("ServiceCargoUnavailable");
        }
        else
        {
            if(source.Owner!=customer)throw new RuleViolation("ServiceCargoUnavailable");
            var inventory=_economy.Inventories[terms.Source.Value];long held=except==0?0:inventory.Reservations.GetValueOrDefault("service-stock:"+except)?.Quantity??0;
            if(checked(inventory.Free(terms.ItemId)+held)<terms.Quantity)throw new RuleViolation("NoStock");
        }
        if(terms.ContractId!=0)
        {
            var contract=_deliveryContracts.FirstOrDefault(d=>d.Id==terms.ContractId&&d.Supplier.Value==customer&&d.DestinationInventory==terms.Destination&&d.ItemId==terms.ItemId&&d.ClosedTick is null&&d.DeadlineTick>=Tick);
            long own=except==0?0:terms.Quantity;
            if(contract is null||checked(contract.UnprovidedQuantity+own)<terms.Quantity)throw new RuleViolation("ServiceCargoUnavailable");
        }
        else if(destination.Owner!=customer)throw new RuleViolation("ServicePointRequired");
    }
    private Cell[] ServicePath(Cell source,Cell target,long vehicle)
    {
        bool corridor=_config.Layout!.RoadCorridorCells.Contains(source)&&_config.Layout.RoadCorridorCells.Contains(target);
        var path=FindRoadPath(source,target,c=>_map.Walkable(c)&&!WorkBlocks(vehicle,c)&&(!corridor||PublicRoadCell(c))&&
            !_bypasses.Any(b=>b.Closing&&!b.Users.Contains(vehicle)&&b.Path.Skip(1).SkipLast(1).Contains(c)),c=>RoadLevel(c)>0?80/Roads!.Stages[RoadLevel(c)-1].Speed+1:12);
        if(path.Length==0)throw new RuleViolation("NoPath");return path;
    }
    private (long Power,long Staff) ServicePlatform(long inventory,long customer,long carrier)
    {
        var p=_platforms.Values.FirstOrDefault(p=>p.Installation is null&&(_buildings[p.Building].Input==inventory||_buildings[p.Building].Output==inventory));
        if(p is null||p.Group.Owner!=customer&&p.Group.Owner!=carrier)return(0,0);
        var rule=Fleet!.Platforms.Single(x=>x.Level==p.Level);
        if(p.Group.Headcount<rule.Staff||_economy.Accounts[_actors[p.Group.Owner].Account].Available<(rule.Staff==0?rule.ServiceMinor:checked(p.Group.Headcount*p.Group.Wage)))return(0,0);
        return(rule.Throughput,rule.Staff==0?0:p.Group.Headcount);
    }
    private long PathTicks(Cell[] path,VehicleModel model)
    {
        long quanta=0;for(int n=0;n<path.Length-1;n++){int a=RoadLevel(path[n]),b=RoadLevel(path[n+1]);int speed=Math.Min(model.Speed,a>0&&b>0?Roads!.Stages[Math.Min(a,b)-1].Speed:8);quanta=checked(quanta+(800+speed-1)/speed);}
        return checked((quanta+9)/10);
    }
    private long PathRoadUnits(Cell[] path,string model)
    {long value=0;foreach(var c in path.SkipLast(1))value=checked(value+checked(800L*Billing!.LightIncrementMinor*Billing.ClassFactors[model]*10000*RoadLevel(c)));return value;}
    private long ExpectedServiceVolume(long destination,long except=0)
    {
        var inv=_economy.Inventories[destination];long total=inv.Incoming;
        total=checked(total+_deliveryContracts.Where(d=>d.ClosedTick is null&&d.CapacityForecast&&d.DestinationInventory?.Value==destination).Sum(d=>checked(d.RemainingQuantity*_items[d.ItemId].UnitVolume)));
        total=checked(total+_serviceOrders.Values.Where(o=>o.Active&&o.Id!=except&&o.Quote.Terms.Destination.Value==destination&&o.Quote.Terms.ContractId==0).Sum(o=>o.Quote.Volume));
        return total;
    }
    public ServiceQuoteResult QuoteDeliveryService(ParticipantId customer,ServiceTerms terms)
    {
        try{if(!_actors.ContainsKey(customer.Value))return new(null,"NotAvailable");return new(CalculateServiceQuote(customer.Value,terms),"Allowed");}
        catch(RuleViolation ex){return new(null,ex.ReasonCode);}catch(OverflowException){return new(null,"ArithmeticOverflow");}
    }
    private ServiceQuote CalculateServiceQuote(long customer,ServiceTerms terms,long except=0)
    {
        CheckServicePoints(customer,terms,except);
        if(!_vehicles.TryGetValue(terms.VehicleId,out var vehicle)||except==0&&!_serviceListings.Contains(vehicle.Id)||vehicle.Owner.Value==customer||
            Model(vehicle.ModelId).MaterialsOnly||!ServiceFree(vehicle)||_serviceOrders.Values.Any(o=>o.Active&&o.Id!=except&&o.Quote.Terms.VehicleId==vehicle.Id))throw new RuleViolation("ServiceVehicleUnavailable");
        var model=Model(vehicle.ModelId);long unit=_items[terms.ItemId].UnitVolume,per=model.CapacityVolume/unit;if(per==0)throw new RuleViolation("NoCapacity");
        long trips=checked((terms.Quantity-1)/per+1),volume=checked(unit*terms.Quantity);
        var at=VehicleCargo(vehicle).Position;var source=_economy.Inventories[terms.Source.Value].Position;var dest=_economy.Inventories[terms.Destination.Value].Position;
        var approach=ServicePath(at,source,vehicle.Id);var loaded=ServicePath(source,dest,vehicle.Id);var empty=trips>1?ServicePath(dest,source,vehicle.Id):[dest];
        long travel=checked(PathTicks(approach,model)+trips*PathTicks(loaded,model)+(trips-1)*PathTicks(empty,model));
        var a=ServicePlatform(terms.Source.Value,customer,vehicle.Owner.Value);var b=ServicePlatform(terms.Destination.Value,customer,vehicle.Owner.Value);
        long power=VehicleHandlingPower(vehicle),fullVolume=checked(per*unit),last=checked(volume-(trips-1)*fullVolume);
        long HandlingTicks(long v,long rate)=>checked((v-1)/rate+1);
        long load=checked((trips-1)*HandlingTicks(fullVolume,power+a.Power)+HandlingTicks(last,power+a.Power));
        long unload=checked((trips-1)*HandlingTicks(fullVolume,power+b.Power)+HandlingTicks(last,power+b.Power));
        long handling=checked(load+unload),extra=checked(load*a.Staff+unload*b.Staff),eta=checked(travel+handling);
        long road=checked(PathRoadUnits(approach,model.Id)+trips*PathRoadUnits(loaded,model.Id)+(trips-1)*PathRoadUnits(empty,model.Id));
        var rules=Services!;long blocks=checked(((volume-1)/rules.VolumeBlock+1)*((loaded.Length-2)/rules.DistanceBlock+1));
        Int128 baseCost=(Int128)rules.DriverWageMinor*(eta+extra)*RoadAccrualScale+road;
        Int128 fee=(Int128)(rules.FixedFeeMinor+checked(rules.BlockFeeMinor*blocks))*RoadAccrualScale*10000+baseCost*rules.CostMarkupBasisPoints;
        long price=Math.Max(rules.MinimumFeeMinor,checked((long)((fee+RoadAccrualScale*10000-1)/(RoadAccrualScale*10000))));
        long buffer=Math.Max(rules.MinimumBufferTicks,checked((long)(((Int128)eta*rules.BufferBasisPoints+9999)/10000)));
        long rightsDeadline=new[]{terms.PurchaseId==0?long.MaxValue:_marketAllocations.Single(a=>a.Id==terms.PurchaseId).DeadlineTick,terms.ContractId==0?long.MaxValue:_deliveryContracts.Single(d=>d.Id==terms.ContractId).DeadlineTick}.Min();
        if(rightsDeadline<long.MaxValue&&checked(Tick+eta+(except==0?rules.ResponseTicks:0))>=rightsDeadline)throw new RuleViolation("ServiceCargoDeadlineTooSoon");
        long expected=ExpectedServiceVolume(terms.Destination.Value,except);
        if(terms.ContractId!=0)expected=checked(expected-volume); // This service fulfills an already forecast contract, not another arrival.
        var quote=new ServiceQuote(terms,volume,per,trips,loaded.Length-1,travel,handling,extra,eta,buffer,price,road,
            expected,_economy.Inventories[terms.Destination.Value].Capacity,
            _economy.Inventories[terms.Destination.Value].Occupied(_items),at,source,dest,[..approach],[..loaded],[..empty],""){CargoRightsDeadline=rightsDeadline==long.MaxValue?0:rightsDeadline};
        return quote with{Token=ScenarioValidation.Hash(quote)};
    }
    public ServicesState? InspectServices()=>Services is null?null:new([.._serviceListings.Order()],[.._serviceOrders.Values]);
    public ServiceObservation? ServicesOf(ParticipantId actor)=>Services is null?null:new(
        [.._serviceListings.Order().Select(id=>{var v=_vehicles[id];return new ServiceListing(id,v.Owner,v.ModelId,Model(v.ModelId).CapacityVolume,VehicleCargo(v).Position,ServiceFree(v)&&!ServiceVehicleBusy(id));})],
        [.._serviceListings.Where(id=>_vehicles[id].Owner==actor).Order()],
        [.._serviceOrders.Values.Where(o=>o.Customer==actor||o.Carrier==actor)],
        [.._buildings.Values.Where(b=>(b.Owner==actor.Value||_deliveryContracts.Any(d=>d.Supplier==actor&&d.ClosedTick is null&&d.DestinationInventory?.Value==b.Input)||_marketAllocations.Any(a=>a.Buyer==actor&&a.ClosedTick is null&&(a.SourceInventory.Value==b.Input||a.SourceInventory.Value==b.Output)))&&!Demolished(b.Id)&&!PreparingDemolition(b.Id)).SelectMany(b=>new[]{new ServicePoint(new(b.Input),new(b.Owner),b.Position,b.Type.Kind,b.Owner==actor.Value||_marketAllocations.Any(a=>a.Buyer==actor&&a.ClosedTick is null&&a.SourceInventory.Value==b.Input),b.Owner==actor.Value||_deliveryContracts.Any(d=>d.Supplier==actor&&d.ClosedTick is null&&d.DestinationInventory?.Value==b.Input))}.Concat(b.Output is {} id?[new ServicePoint(new(id),new(b.Owner),b.Position,b.Type.Kind,b.Owner==actor.Value||_marketAllocations.Any(a=>a.Buyer==actor&&a.ClosedTick is null&&a.SourceInventory.Value==id),false)]:[]))],Services);
}

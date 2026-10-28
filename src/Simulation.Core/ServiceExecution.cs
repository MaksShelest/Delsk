using System.Collections.Immutable;
namespace Simulation.Core;

internal sealed class ServiceCrewState(long vehicle,EmploymentGroup group)
{
    public long Vehicle {get;}=vehicle;
    public EmploymentGroup Group {get;}=group;
    public bool Enabled {get;set;}
    public ServiceCrew View()=>new(Vehicle,Enabled,Group.View());
}
public sealed partial class SimulationEngine
{
    private readonly SortedDictionary<long,ServiceRun> _serviceRuns=[];
    private readonly SortedDictionary<long,ServiceCrewState> _serviceCrews=[];
    private readonly Dictionary<long,(string Hold,long Cost)> _serviceDriverFunding=[];
    private ServiceOrder? ExecutingService(long vehicle)=>_serviceOrders.Values.SingleOrDefault(o=>o.State=="Accepted"&&o.Quote.Terms.VehicleId==vehicle&&_serviceRuns.ContainsKey(o.Id));
    private ServiceCrewState? ServiceCrewOf(long vehicle)=>_serviceCrews.GetValueOrDefault(vehicle);
    private bool ServiceAutomatic(long vehicle)=>ServiceCrewOf(vehicle)?.Enabled==true;
    private Cell ServiceTarget(ServiceOrder o,ServiceRun r)=>r.Stage is "Returning" or "UnloadingReturn"||r.Stage=="Cancelled"&&r.Returned>0?o.Quote.ReturnPosition??_economy.Inventories[r.ReturnInventory.Value].Position:
        r.Stage is "ToDestination" or "AwaitingUnload" or "Unloading" or "Completed"||r.Stage=="Cancelled"&&r.Delivered>0?o.Quote.DestinationPosition:o.Quote.SourcePosition;
    private ServiceExecutionState? InspectServiceExecution()=>ServiceExecution is null?null:new([.._serviceRuns.Values],[.._serviceCrews.Values.Select(c=>c.View())]);
    private ServiceExecutionObservation? ServiceExecutionOf(ParticipantId actor)=>ServiceExecution is null?null:new(
        [.._serviceRuns.Values.Where(r=>_serviceOrders[r.Order].Customer==actor||_serviceOrders[r.Order].Carrier==actor).Select(r=>{
            var o=_serviceOrders[r.Order];var v=_vehicles[o.Quote.Terms.VehicleId];return new ServiceRunView(r,v.Id,VehicleCargo(v).Position,ServiceTarget(o,r),SourceRemaining(o),_economy.Accounts[o.EscrowAccount].Balance,ServiceAutomatic(v.Id),ServiceCrewOf(v.Id)?.Group.Headcount??0,r.Reason,VehicleCargo(v).Position==ServiceTarget(o,r)&&(_traffic.GetValueOrDefault(v.Id)?.Progress??0)==0);})],
        [.._serviceCrews.Values.Where(c=>c.Group.Owner==actor.Value).Select(c=>c.View())],ServiceExecution);
    private void CreateServiceRun(ServiceOrder o)
    {
        if(ServiceExecution is null)return;
        _serviceRuns.Add(o.Id,new(o.Id,o.Quote.ReturnInventory??throw new InvalidOperationException("Missing return point"),StageStartedTick:Tick,Receipts:[]));
    }
    private bool ServiceBoardAllowed(long actor,long vehicle)=>ExecutingService(vehicle) is {} o&&o.Carrier.Value==actor&&!ServiceAutomatic(vehicle)&&
        _serviceRuns[o.Id].Stage is not ("Loading" or "Unloading" or "UnloadingReturn");
    private bool ServiceHandlingVehicle(long vehicle)=>ExecutingService(vehicle) is {} o&&_serviceRuns[o.Id].Stage is "Loading" or "Unloading" or "UnloadingReturn";
    private bool ServicePersonalReady(ServiceOrder o)=>_vehicles[o.Quote.Terms.VehicleId].Driver==o.Carrier.Value&&_actors[o.Carrier.Value].Job is null;
    private string ServiceCrewReadiness(ServiceCrewState crew)=>crew.Enabled&&ExecutingService(crew.Vehicle) is not null&&_vehicles[crew.Vehicle].Driver==0&&crew.Group.Wage>0?"Ready":"UserPaused";
    private bool ServiceDriverReady(ServiceOrder o,ServiceRun run)
    {
        var v=_vehicles[o.Quote.Terms.VehicleId];
        if(!ServiceAutomatic(v.Id))return ServicePersonalReady(o);
        if(v.Driver!=0||ServiceCrewOf(v.Id) is not {} crew||crew.Group.Headcount<1)return false;
        if(_serviceDriverFunding.ContainsKey(v.Id))return true;
        long cost=checked(crew.Group.Headcount*crew.Group.Wage);if(cost<=0||_economy.Accounts[_actors[o.Carrier.Value].Account].Available<cost)return false;
        string hold=$"service-driver:{v.Id}:{Tick}";var tx=_economy.Begin(Tick);tx.HoldMoney(_actors[o.Carrier.Value].Account,hold,cost);tx.Commit();_serviceDriverFunding.Add(v.Id,(hold,cost));return true;
    }
    private void ServiceExecutionCommand(ActorState actor,CommandEnvelope command)
    {
        if(ServiceExecution is null)throw new RuleViolation("ServiceExecutionUnavailable");var action=command.Action;
        if(action.Kind==ActionKind.SetServiceAutomation)
        {
            var v=OwnVehicle(actor,action.VehicleId);if(Model(v.ModelId).MaterialsOnly)throw new RuleViolation("ServiceVehicleUnavailable");if(v.Driver!=0||action.Enabled&&ServiceHandlingVehicle(v.Id))throw new RuleViolation("VehicleBusy");
            if(!_serviceCrews.TryGetValue(v.Id,out var crew)){crew=new(v.Id,new(_nextId++,actor.Id,0,1,Fleet!.WageMinor,EmploymentRole.DeliveryService));_serviceCrews.Add(v.Id,crew);}
            crew.Enabled=action.Enabled;
            if(!action.Enabled&&ExecutingService(v.Id) is {} active)
            {
                var stopped=_serviceRuns[active.Id];if(stopped.Stage is "Loading" or "Unloading" or "UnloadingReturn")
                    _serviceRuns[active.Id]=StoppedServiceHandling(stopped) with{Reason="HandlingStopped"};
            }
            Emit("ServiceCrewConfigured",actor.Id,v.Id,quantity:action.Enabled?1:0,command:command.CommandId);return;
        }
        if(!_serviceOrders.TryGetValue(action.ServiceId,out var o)||o.State!="Accepted"||!_serviceRuns.TryGetValue(o.Id,out var r))throw new RuleViolation("ServiceOrderUnavailable");
        if(action.Kind==ActionKind.ForceCloseDeliveryService)
        {if(o.Customer.Value!=actor.Id||Tick<=checked(o.DeadlineTick+ServiceExecution.ForceCloseAfterTicks))throw new RuleViolation("ServiceForceCloseTooSoon");RequestServiceCancellation(o,false,"OverdueClosure",command.CommandId);return;}
        if(o.Carrier.Value!=actor.Id||ServiceAutomatic(o.Quote.Terms.VehicleId)||!ServicePersonalReady(o))throw new RuleViolation("ServiceDriverRequired");
        var vehicle=_vehicles[o.Quote.Terms.VehicleId];var position=VehicleCargo(vehicle).Position;
        if(action.Kind==ActionKind.NavigateDeliveryService)
        {
            if(ServiceHandlingVehicle(vehicle.Id))throw new RuleViolation("ServiceHandlingBusy");
            var target=ServiceTarget(o,r);var path=ServicePath(position,target,vehicle.Id);var traffic=Traffic(vehicle.Id);if(position==target&&traffic.Progress==0)return;
            if(path.Length<2&&traffic.Progress>0)path=[position,traffic.Path[traffic.Index+1],position];
            actor.HeadingX=actor.HeadingZ=0;actor.Job=new(_nextId++,"Move",Tick,checked(Tick+path.Length*10L),path,"",0,"",command.CommandId);
            Emit("ServiceNavigationStarted",actor.Id,o.Id,command:command.CommandId);return;
        }
        if(action.Kind==ActionKind.StopDeliveryServiceHandling)
        {
            if(r.Stage is not ("Loading" or "Unloading" or "UnloadingReturn"))throw new RuleViolation("ServiceHandlingUnavailable");
            _serviceRuns[o.Id]=StoppedServiceHandling(r);return;
        }
        RequireActionLocation(actor,action);
        if(action.Kind==ActionKind.PickupDeliveryService)
        {
            if(r.Cancellation!=""||r.Cargo!=0||r.Stage is not ("AwaitingPickup" or "ToSource")||position!=o.Quote.SourcePosition||Traffic(vehicle.Id).Progress!=0)throw new RuleViolation("ServicePickupUnavailable");
            StartServiceLoading(o,r);return;
        }
        if(action.Kind==ActionKind.UnloadDeliveryService)
        {
            if(r.Cargo<=0||r.Stage is not ("ToDestination" or "AwaitingUnload" or "Returning")||position!=ServiceTarget(o,r)||Traffic(vehicle.Id).Progress!=0)throw new RuleViolation("ServiceUnloadUnavailable");
            _serviceRuns[o.Id]=r with{Stage=r.Stage=="Returning"?"UnloadingReturn":"Unloading",WorkRemainder=0,StageStartedTick=Tick,Reason="Handling"};return;
        }
        throw new RuleViolation("UnknownAction");
    }
    private ServiceRun StoppedServiceHandling(ServiceRun r)=>r with{Stage=r.Stage=="UnloadingReturn"?"Returning":r.Cargo>0?r.Stage=="Loading"?"ToDestination":"AwaitingUnload":"AwaitingPickup",WorkRemainder=0,StageStartedTick=Tick,Reason="HandlingStopped"};
    private void StartServiceLoading(ServiceOrder o,ServiceRun r)
    {
        long quantity=Math.Min(SourceRemaining(o),o.Quote.QuantityPerTrip);if(quantity<=0)throw new RuleViolation("ServicePickupUnavailable");
        if(_vehicles[o.Quote.Terms.VehicleId].Driver is >0 and var driver){_actors[driver].HeadingX=_actors[driver].HeadingZ=0;}
        _serviceRuns[o.Id]=r with{Stage="Loading",TripQuantity=quantity,TripNumber=checked(r.TripNumber+1),WorkRemainder=0,StageStartedTick=Tick,Reason="Loading"};
    }
    private bool ServiceTrafficTravel(OwnedVehicle vehicle,Cell target)
    {
        for(int q=0;q<TravelQuantaPerTick;q++)
        {
            var t=Traffic(vehicle.Id);var continuing=t.Progress>0&&!t.Path.IsDefaultOrEmpty&&(t.Resume??t.Path[^1])!=target;
            bool arrived=TrafficTravel(vehicle,continuing?t.Resume??t.Path[^1]:target,1);
            if(!continuing&&arrived)return true;
        }
        return VehicleCargo(vehicle).Position==target&&Traffic(vehicle.Id).Progress==0;
    }
    private void AdvanceServiceMovement()
    {
        if(ServiceExecution is null)return;
        foreach(var o in _serviceOrders.Values.Where(o=>o.State=="Accepted").ToArray())
        {
            var r=_serviceRuns[o.Id];var v=_vehicles[o.Quote.Terms.VehicleId];if(!ServiceAutomatic(v.Id))continue;
            if(Tick<=r.StageStartedTick||r.Stage is "Loading" or "Unloading" or "UnloadingReturn")continue;
            var target=ServiceTarget(o,r);
            if(VehicleCargo(v).Position==target&&Traffic(v.Id).Progress==0)
            {
                if(r.Stage is "AwaitingPickup" or "ToSource")StartServiceLoading(o,r);
                else _serviceRuns[o.Id]=r with{Stage=r.Stage=="Returning"?"UnloadingReturn":"Unloading",WorkRemainder=0,StageStartedTick=Tick,Reason="Handling"};continue;
            }
            if(!ServiceDriverReady(o,r)){_serviceRuns[o.Id]=r with{Reason=ServiceCrewOf(v.Id)?.Group.Headcount>0?"NoMoney":"NoStaff"};continue;}
            var before=Traffic(v.Id);var at=VehicleCargo(v).Position;
            try
            {
                bool arrived=ServiceTrafficTravel(v,target);bool worked=before.Progress!=Traffic(v.Id).Progress||at!=VehicleCargo(v).Position;
                _serviceRuns[o.Id]=r with{LastWorkedTick=worked?Tick:r.LastWorkedTick,Reason=arrived?"Arrived":Traffic(v.Id).Reason};
            }
            catch(RuleViolation ex){_serviceRuns[o.Id]=r with{Reason=ex.ReasonCode};}
        }
    }
    private long ServiceHandlingPower(ServiceOrder o,bool loading,bool returning)
    {
        var v=_vehicles[o.Quote.Terms.VehicleId];long inventory=loading?o.Quote.Terms.Source.Value:returning?_serviceRuns[o.Id].ReturnInventory.Value:o.Quote.Terms.Destination.Value;
        long extra=0;if(inventory!=0&&ServicePlatform(inventory,o.Customer.Value,o.Carrier.Value).Power>0)extra=PlatformPower(inventory);
        return checked(VehicleHandlingPower(v)+extra);
    }
    private void AdvanceServiceHandling()
    {
        if(ServiceExecution is null)return;
        foreach(var o in _serviceOrders.Values.Where(o=>o.State=="Accepted").ToArray())
        {
            var r=_serviceRuns[o.Id];if(r.Stage is not ("Loading" or "Unloading" or "UnloadingReturn")||Tick<=r.StageStartedTick||r.LastWorkedTick==Tick)continue;
            var v=_vehicles[o.Quote.Terms.VehicleId];bool loading=r.Stage=="Loading",returning=r.Stage=="UnloadingReturn";
            if(VehicleCargo(v).Position!=(loading?o.Quote.SourcePosition:ServiceTarget(o,r))||Traffic(v.Id).Progress!=0||!ServiceDriverReady(o,r))
            {_serviceRuns[o.Id]=r with{Reason=ServiceAutomatic(v.Id)?"NoStaff":"ServiceDriverRequired"};continue;}
            if(!returning&&o.Quote.Terms.ContractId!=0&&_deliveryContracts.Single(d=>d.Id==o.Quote.Terms.ContractId).ClosedTick is not null)
            {RequestServiceCancellation(o,false,"GoodsContractClosed");continue;}
            long remaining=loading?checked(r.TripQuantity-r.Cargo):r.Cargo;
            var work=VolumeWork.Advance(r.WorkRemainder,ServiceHandlingPower(o,loading,returning),_items[o.Quote.Terms.ItemId].UnitVolume,remaining);
            if(work.Units>0)
            {
                if(loading)LoadServiceUnits(o,r,work.Units);
                else if(returning)ReturnServiceUnits(o,r,work.Units);
                else DeliverServiceUnits(o,r,work.Units);
                r=_serviceRuns[o.Id];
            }
            r=r with{WorkRemainder=work.Remainder,LastWorkedTick=Tick,Reason=r.Stage};_serviceRuns[o.Id]=r;
            if(work.Units==remaining)
            {
                if(loading)_serviceRuns[o.Id]=r with{Stage="ToDestination",WorkRemainder=0,StageStartedTick=Tick,Reason="DriveToRecipient"};
                else if(r.Cargo==0)
                {
                    if(r.Cancellation!=""||r.Delivered==o.Quote.Terms.Quantity)CloseExecutedService(o,r.Delivered==o.Quote.Terms.Quantity?"Completed":"Cancelled",r.Cancellation==""?"Delivered":"CancelledAfterCargoSafe");
                    else _serviceRuns[o.Id]=r with{Stage="ToSource",WorkRemainder=0,StageStartedTick=Tick,Reason="DriveToSource"};
                }
            }
        }
        PayServiceDriverWork();
    }
    private void PayServiceDriverWork()
    {
        foreach(var pair in _serviceDriverFunding)
        {
            var o=ExecutingService(pair.Key)??_serviceOrders.Values.Last(o=>o.Quote.Terms.VehicleId==pair.Key&&_serviceRuns.GetValueOrDefault(o.Id)?.LastWorkedTick==Tick);
            var tx=_economy.Begin(Tick);if(_serviceRuns[o.Id].LastWorkedTick==Tick)tx.Pay(_actors[o.Carrier.Value].Account,_wages,pair.Value.Cost,"ServiceDriverWages:"+o.Id,pair.Value.Hold);else tx.ReleaseMoney(_actors[o.Carrier.Value].Account,pair.Value.Hold);tx.Commit();
        }
        _serviceDriverFunding.Clear();
    }
}

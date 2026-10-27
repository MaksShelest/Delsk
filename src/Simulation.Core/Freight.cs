using System.Collections.Immutable;

namespace Simulation.Core;

/// <summary>Physical handling and movement share the existing system, crew, cargo and job queue.</summary>
public sealed record FreightState(InventoryId GoodsSource, InventoryId Destination,
    ImmutableArray<Cell> ToSource, ImmutableArray<Cell> ToDestination, ImmutableArray<Cell> ToHome,
    ImmutableArray<Cell> ToGoodsSource, ImmutableArray<Cell> SourceToHome,
    long Loaded = 0, long Received = 0, long Returned = 0, long Cancelled = 0,
    long WorkRemainder = 0, long LastWorkedTick = 0, long StageStartedTick = 0)
{
    public long Cargo => checked(Loaded-Received-Returned);
}

public sealed partial class SimulationEngine
{
    internal static long MaximumFreightTicks(ScenarioDefinition scenario,long capacity,int level)
    {
        long rate=checked((scenario.Economy?.Handling??new()).WorkerVolumePerTick*level);
        long handling=checked((capacity-1)/rate+1);
        return checked(4L*(scenario.MapWidth*scenario.MapHeight-1L)*(scenario.Logistics??new()).MoveTicksPerCell+3*handling);
    }
    private long MinimumFreightHandling(LogisticsState l) => l.VehicleId==0 ? checked(Handling.WorkerVolumePerTick*l.Level) : VehicleHandlingPower(_vehicles[l.VehicleId]);
    private long FreightRemainingSource(LogisticsTripView t) => checked(t.Quantity-t.Freight!.Loaded-t.Freight.Cancelled);
    private long FreightCost(LogisticsState l,LogisticsPlanData p,bool committed=false)
    {
        var source=_economy.Inventories[p.GoodsSource];var home=_economy.Inventories[l.Source].Position;
        var destination=p.Index>=0?_deliveryContracts[p.Index].Position:_economy.Inventories[p.OwnDestination].Position;
        long approach=FreightTravelTicks(l,_map.FindPath(l.VehicleId==0?home:_economy.Inventories[l.Cargo].Position,source.Position)),delivery=FreightTravelTicks(l,_map.FindPath(source.Position,destination));
        long returning=FreightTravelTicks(l,_map.FindPath(destination,home));
        long travel=checked(approach+delivery+(p.Index>=0?Math.Max(returning,delivery+FreightTravelTicks(l,_map.FindPath(source.Position,home))):returning));
        long volume=checked(p.Quantity*_items[p.ItemId].UnitVolume);
        long handling=checked((volume-1)/checked(MinimumFreightHandling(l))+1);
        return checked(l.CrewCapacity*(committed?l.Group.Wage:l.Group.PendingWage??l.Group.Wage)*checked(travel+(p.Index>=0?3:2)*handling));
    }
    private void PrepareFreight(LogisticsState l)
    {
        string reason=LogisticsVacancyReason(l);if(reason!="Ready"){l.Reason=reason;return;}
        if(l.Group.Headcount<l.MinimumCrew){l.Reason="NoStaff";return;}
        try
        {
            var p=LogisticsPlan(l);if(Storage is not null&&p.OwnDestination!=0&&HasOverflow(p.OwnDestination))throw new RuleViolation("DestinationOverflow");var source=_economy.Inventories[p.GoodsSource];var home=_economy.Inventories[l.Source].Position;
            var destination=p.Index>=0?_deliveryContracts[p.Index].Position:_economy.Inventories[p.OwnDestination].Position;
            long id=_nextId,next=checked(id+1),budget=FreightCost(l,p,true);
            var f=new FreightState(new(p.GoodsSource),new(p.OwnDestination),[.._map.FindPath(l.VehicleId==0?home:_economy.Inventories[l.Cargo].Position,source.Position)],
                [.._map.FindPath(source.Position,destination)],[.._map.FindPath(destination,home)],
                [.._map.FindPath(destination,source.Position)],[.._map.FindPath(source.Position,home)],StageStartedTick:Tick);
            long handling=checked((p.Quantity*_items[p.ItemId].UnitVolume-1)/checked(MinimumFreightHandling(l)*l.MinimumCrew)+1);
            long estimatedReceipt=checked(Tick+FreightTravelTicks(l,f.ToSource)+handling+FreightTravelTicks(l,f.ToDestination)+handling);
            if(p.Index>=0 && estimatedReceipt>_deliveryContracts[p.Index].DeadlineTick)throw new RuleViolation("ArrivalAfterDeadline");
            var t=new LogisticsTripView(id,p.Index>=0?_deliveryContracts[p.Index].Id:0,p.ItemId,p.Quantity,
                f.ToSource.Length==1?"Loading":"FetchingEmpty",Tick,checked(Tick+FreightTravelTicks(l,f.ToSource)+handling),estimatedReceipt,
                checked(estimatedReceipt+FreightTravelTicks(l,f.ToHome)),f.ToSource[0],f.ToSource,0,0,l.Group.Wage,budget,0,
                $"logistics-stock:{id}",$"logistics-return:{id}",$"logistics-wage:{id}",new(p.OwnDestination),new(p.InboundSource),p.JobKey) {Freight=f};
            var tx=_economy.Begin(Tick);tx.ReserveStock(p.GoodsSource,t.StockHold,p.ItemId,p.Quantity);
            if(p.OwnDestination!=0)tx.ReserveCapacity(p.OwnDestination,t.DestinationHold,p.ItemId,p.Quantity);
            tx.HoldMoney(_actors[l.Owner].Account,t.WageHold,budget);tx.Commit();
            if(p.Index>=0){var d=_deliveryContracts[p.Index];_deliveryContracts[p.Index]=d with {PreparedQuantity=checked(d.PreparedQuantity+p.Quantity)};}
            _nextId=next;l.DispatchCount=checked(l.DispatchCount+1);
            int job=l.Queue.FindIndex(j=>j.Key==p.JobKey);l.Queue[job]=l.Queue[job] with {LastServed=l.DispatchCount};
            l.Trip=t;l.Reason=t.State;
            if(l.Group.PendingWage is not null){l.Group.PayEffectiveTick=checked(t.EndTick+1);l.Group.AttractionEffectiveTick=Math.Max(l.Group.AttractionEffectiveTick??0,t.EndTick+1);Emit("LogisticsWageDeferred",l.Owner,l.Id,money:l.Group.PendingWage.Value);}
            Emit("LogisticsJobDispatched",l.Owner,id,reason:p.JobKey);Emit("LogisticsTripPrepared",l.Owner,id,p.ItemId,p.Quantity,budget);
        }
        catch(RuleViolation ex){l.Reason=ex.ReasonCode;}
    }
    private void FreightPath(LogisticsState l,ImmutableArray<Cell> path,string state)
    {
        var t=l.Trip!;l.Trip=t with {State=state,Path=path,PathIndex=0,SegmentTicks=0,Position=path[0],
            Freight=t.Freight! with {StageStartedTick=Tick,WorkRemainder=0}};l.Reason=state;
    }
    private void AdvanceFreightMovement(LogisticsState l)
    {
        if(l.Trip is not {Freight:{} f} t || t.State is not ("FetchingEmpty" or "Outbound" or "ReturningInput" or "ReturningEmpty" or "ReturningCargo"))return;
        if(Tick<=f.StageStartedTick)return;
        if(l.VehicleId!=0 && l.Group.Headcount==0){l.Reason="NoStaff";return;}
        if(Roads is not null&&l.VehicleId!=0){AdvanceRoadFreight(l);return;}
        int index=t.PathIndex,segment=t.SegmentTicks;
        if(t.Path.Length>1){if(++segment==FreightMoveTicks(l,t.Path[index],t.Path[index+1])){segment=0;++index;}}
        string state=t.State;
        if(index==t.Path.Length-1)state=t.State switch {"FetchingEmpty"=>"Loading","Outbound" or "ReturningInput"=>"Unloading","ReturningCargo"=>"UnloadingReturn",_=>"Finishing"};
        l.Trip=t with {PathIndex=index,SegmentTicks=segment,Position=t.Path[index],State=state,
            Freight=f with {LastWorkedTick=Tick,StageStartedTick=state==t.State?f.StageStartedTick:Tick}};
        _economy.Inventories[l.Cargo].Position=t.Path[index];l.Reason=state;
        if(state=="Loading" && FreightRemainingSource(l.Trip)==0)
        {FreightPath(l,f.SourceToHome,"ReturningEmpty");if(f.SourceToHome.Length==1)l.Trip=l.Trip! with {State="Finishing"};}
    }
    private void AdvanceFreightHandling(LogisticsState l)
    {
        if(l.Trip is not {Freight:{} f} t || t.State is not ("Loading" or "Unloading" or "UnloadingReturn") || f.LastWorkedTick==Tick || Tick<=f.StageStartedTick)return;
        if(l.Group.Headcount==0){l.Reason="NoStaff";return;}
        if(Roads is not null&&l.VehicleId!=0)
        {if(!EnsureRoadFreightFunds(l))return;t=l.Trip!;f=t.Freight!;}
        bool loading=t.State=="Loading",returning=t.State=="UnloadingReturn";
        long remaining=loading?FreightRemainingSource(t):f.Cargo;
        if(!loading && t.ContractId!=0 && !returning && _deliveryContracts.Single(d=>d.Id==t.ContractId).ClosedTick is not null)
        {
            long handling=checked((f.Cargo*_items[t.ItemId].UnitVolume-1)/(MinimumFreightHandling(l))+1);
            long end=checked(Tick+FreightTravelTicks(l,f.ToGoodsSource)+FreightTravelTicks(l,f.SourceToHome)+handling);
            l.Trip=t with {EndTick=end,ArrivalTick=Tick};FreightPath(l,f.ToGoodsSource,"ReturningCargo");
            if(l.Group.PendingWage is not null){l.Group.PayEffectiveTick=checked(end+1);l.Group.AttractionEffectiveTick=Math.Max(end+1,l.Group.AttractionEffectiveTick??0);}
            Emit("LogisticsDeliveryRejected",l.Owner,t.Id,t.ItemId,f.Cargo);return;
        }
        if(remaining<=0)throw new InvalidOperationException("Empty freight handling stage");
        var work=VolumeWork.Advance(f.WorkRemainder,FreightHandlingPower(l,loading,returning,f),_items[t.ItemId].UnitVolume,remaining);
        if(work.Units>0)
        {
            var tx=_economy.Begin(Tick);
            if(loading)
            {
                tx.ConsumeReserved(f.GoodsSource.Value,t.StockHold,work.Units);tx.AddStock(l.Cargo,t.ItemId,work.Units);
                tx.ReserveAdditionalStock(l.Cargo,t.CargoHold,t.ItemId,work.Units);
                tx.ReserveAdditionalCapacity(f.GoodsSource.Value,t.ReturnHold,t.ItemId,work.Units);tx.Commit();
                if(t.ContractId!=0){int i=_deliveryContracts.FindIndex(d=>d.Id==t.ContractId);var d=_deliveryContracts[i];
                    _deliveryContracts[i]=d with {PreparedQuantity=checked(d.PreparedQuantity-work.Units),InTransitQuantity=checked(d.InTransitQuantity+work.Units)};}
                f=f with {Loaded=checked(f.Loaded+work.Units)};Emit("LogisticsCargoLoaded",l.Owner,t.Id,t.ItemId,work.Units);
            }
            else if(t.ContractId!=0 && !returning)
            {
                ReceiveDelivery(_deliveryContracts.FindIndex(d=>d.Id==t.ContractId),l.Cargo,work.Units,true,l);
                f=f with {Received=checked(f.Received+work.Units)};
            }
            else
            {
                tx.ConsumeReserved(l.Cargo,t.CargoHold,work.Units);
                if(returning)tx.ReceiveReservedCapacity(f.GoodsSource.Value,t.ReturnHold,t.ItemId,work.Units);
                else{tx.ReceiveReservedCapacity(f.Destination.Value,t.DestinationHold,t.ItemId,work.Units);tx.ReleaseCapacityVolume(f.GoodsSource.Value,t.ReturnHold,t.ItemId,work.Units);}
                tx.Commit();f=returning?f with {Returned=checked(f.Returned+work.Units)}:f with {Received=checked(f.Received+work.Units)};
                Emit(returning?"LogisticsCargoReturned":t.InboundSource.Value!=0?"LogisticsInputReceived":_enterprises.Values.Any(e=>e.Input==f.Destination.Value)?"LogisticsProductionReceived":"LogisticsStorageReceived",l.Owner,t.Id,t.ItemId,work.Units);
            }
        }
        if(!loading && !returning && work.Units>0)Emit("FreightUnitsReceived",l.Owner,t.Id,t.ItemId,work.Units);
        f=f with {WorkRemainder=work.Remainder,LastWorkedTick=Tick};l.Trip=t with {Freight=f};
        if(work.Units==remaining)
        {
            if(loading)
            {
                if(t.InboundSource.Value!=0)Emit("LogisticsInputLoaded",l.Owner,t.Id,t.ItemId,f.Loaded);
                l.Trip=l.Trip with {LoadTick=Tick};
                FreightPath(l,f.ToDestination,t.InboundSource.Value!=0 && f.Destination.Value==_enterprises[l.Enterprise].Input?"ReturningInput":"Outbound");
                if(f.ToDestination.Length==1)l.Trip=l.Trip! with {State="Unloading"};
            }
            else
            {
                l.Trip=l.Trip with {ArrivalTick=Tick};
                if(t.ContractId!=0 && !returning)Emit("LogisticsDeliveryAccepted",l.Owner,t.Id,t.ItemId,f.Received);
                FreightPath(l,returning?f.SourceToHome:f.ToHome,"ReturningEmpty");
                if(l.Trip!.Path.Length==1)l.Trip=l.Trip with {State="Finishing",EndTick=Tick};
            }
        }
    }
    private void PayFreightWork(LogisticsState l)
    {
        if(l.Trip is not {Freight:{} f} t)return;
        long wage=f.LastWorkedTick==Tick?checked(l.Group.Headcount*t.CommittedWageMinor):0;
        long paid=checked(t.WagesPaidMinor+wage);var tx=_economy.Begin(Tick);
        if(wage>0)tx.Pay(_actors[l.Owner].Account,_wages,wage,"LogisticsWages",t.WageHold);
        if(t.State=="Finishing" && paid<t.WageBudgetMinor)tx.ReleaseMoney(_actors[l.Owner].Account,t.WageHold);tx.Commit();
        if(wage>0)Emit("LogisticsWagesPaid",l.Owner,t.Id,money:wage);
        if(t.State=="Finishing")
        {
            if(l.Group.PendingWage is not null){l.Group.PayEffectiveTick=checked(Tick+1);l.Group.AttractionEffectiveTick=Math.Max(Tick+1,l.Group.AttractionEffectiveTick??0);}
            l.Trip=null;++l.Completed;l.Reason="Ready";Emit("LogisticsTripCompleted",l.Owner,t.Id,t.ItemId,t.Quantity,paid);
        }
        else l.Trip=t with {WagesPaidMinor=paid};
    }
    private void CancelPreparedFreight(long contract)
    {
        foreach(var l in _logistics.Values.Where(l=>l.Trip?.ContractId==contract).ToArray())
        {
            var t=l.Trip!;var f=t.Freight!;long remaining=FreightRemainingSource(t);
            if(remaining>0){var tx=_economy.Begin(Tick);tx.ReleaseStock(f.GoodsSource.Value,t.StockHold);tx.Commit();f=f with {Cancelled=checked(f.Cancelled+remaining)};}
            l.Trip=t with {Freight=f};
            if(t.State is "Loading" or "FetchingEmpty")
            {
                if(f.Cargo>0){l.Trip=l.Trip with {State="UnloadingReturn",Freight=f with {StageStartedTick=Tick,WorkRemainder=0}};Emit("LogisticsDeliveryRejected",l.Owner,t.Id,t.ItemId,f.Cargo,reason:"PreparedExpiry");}
                else if(t.State=="Loading")
                {FreightPath(l,f.SourceToHome,"ReturningEmpty");if(f.SourceToHome.Length==1)l.Trip=l.Trip! with {State="Finishing"};}
                // An already moving empty truck reaches the source before returning; it is never teleported.
            }
        }
    }
}

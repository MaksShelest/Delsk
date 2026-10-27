namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void MaintainRoadFreightTimes()
    {
        if(Roads is null)return;
        foreach(var l in _logistics.Values.Where(l=>l.VehicleId!=0&&l.Trip is not null))
        {
            var t=l.Trip!;long load=t.State=="FetchingEmpty"?Math.Max(t.LoadTick,Tick):t.LoadTick;
            long arrival=Math.Max(Math.Max(t.ArrivalTick,load),t.State is "FetchingEmpty" or "Loading" or "Outbound" or "ReturningInput"?Tick:t.ArrivalTick);
            l.Trip=t with{LoadTick=load,ArrivalTick=arrival,EndTick=Math.Max(Math.Max(t.EndTick,arrival),Tick+1)};
            if(l.Group.PendingWage is not null){l.Group.PayEffectiveTick=Math.Max(l.Group.PayEffectiveTick??0,l.Trip.EndTick+1);l.Group.AttractionEffectiveTick=Math.Max(l.Group.AttractionEffectiveTick??0,l.Trip.EndTick+1);}
        }
    }
    private bool EnsureRoadFreightFunds(LogisticsState l)
    {
        var t=l.Trip!;long wage=checked(t.CommittedWageMinor*l.Group.Headcount);
        long remaining=t.WageBudgetMinor-t.WagesPaidMinor;
        if(remaining>=wage)return true;
        long extra=checked(Math.Max(wage,40L*t.CommittedWageMinor)-remaining);
        var account=_actors[l.Owner].Account;
        if(_economy.Accounts[account].Available<extra){l.Reason="NoMoney";return false;}
        var tx=_economy.Begin(Tick);if(remaining>0)tx.ReleaseMoney(account,t.WageHold);
        tx.HoldMoney(account,t.WageHold,checked(remaining+extra));tx.Commit();
        l.Trip=t with{WageBudgetMinor=checked(t.WageBudgetMinor+extra)};
        Emit("FreightBudgetExtended",l.Owner,t.Id,money:extra);return true;
    }
    private void AdvanceRoadFreight(LogisticsState l)
    {
        var t=l.Trip!;var f=t.Freight!;
        long load=t.State=="FetchingEmpty"?Math.Max(t.LoadTick,Tick):t.LoadTick;
        long arrival=Math.Max(Math.Max(t.ArrivalTick,load),t.State is "FetchingEmpty" or "Loading" or "Outbound" or "ReturningInput"?Tick:t.ArrivalTick);
        l.Trip=t=t with{LoadTick=load,ArrivalTick=arrival,EndTick=Math.Max(Math.Max(t.EndTick,arrival),Tick+1)};
        if(l.Group.PendingWage is not null){l.Group.PayEffectiveTick=Math.Max(l.Group.PayEffectiveTick??0,t.EndTick+1);l.Group.AttractionEffectiveTick=Math.Max(l.Group.AttractionEffectiveTick??0,t.EndTick+1);}
        if(!EnsureRoadFreightFunds(l))return;t=l.Trip!;
        var vehicle=_vehicles[l.VehicleId];var before=Traffic(vehicle.Id);var beforePosition=VehicleCargo(vehicle).Position;bool arrived;
        try{arrived=TrafficTravel(vehicle,t.Path[^1]);}
        catch(RuleViolation ex){l.Reason=ex.ReasonCode;return;}
        var after=Traffic(vehicle.Id);bool worked=before.Progress!=after.Progress||beforePosition!=VehicleCargo(vehicle).Position;
        string state=arrived?t.State switch{"FetchingEmpty"=>"Loading","Outbound" or "ReturningInput"=>"Unloading","ReturningCargo"=>"UnloadingReturn",_=>"Finishing"}:t.State;
        l.Trip=t with{Position=VehicleCargo(vehicle).Position,PathIndex=arrived?t.Path.Length-1:0,SegmentTicks=0,State=state,
            Freight=f with{LastWorkedTick=worked?Tick:f.LastWorkedTick,StageStartedTick=state==t.State?f.StageStartedTick:Tick}};
        l.Reason=arrived?state:after.Reason;
        if(state=="Loading"&&FreightRemainingSource(l.Trip)==0)
        {FreightPath(l,f.SourceToHome,"ReturningEmpty");if(f.SourceToHome.Length==1)l.Trip=l.Trip! with{State="Finishing"};}
    }
}

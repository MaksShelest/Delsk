namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private static string TransportKey(long source,long destination,string item)=>$"transport:{source}:{destination}:{item}";
    private bool TransportSource(long owner,long inventory) => _economy.Inventories.TryGetValue(inventory,out var i) && i.Owner==owner &&
        (_buildings.Values.Any(b=>b.Input==inventory || b.Output==inventory) || _equipment.Values.Any(e=>e.Storage==inventory));
    private bool TransportDestination(long owner,long inventory,string item) => _buildings.Values.Any(b=>b.Owner==owner && b.Input==inventory &&
        (b.Type.Kind!=BuildingKind.Production || _enterprises[b.Id].Recipe.Uses(item)));
    private void SetTransportRoute(ActorState actor,CommandEnvelope c)
    {
        if(!_logistics.TryGetValue(c.Action.Logistics.Value,out var l)||l.Owner!=actor.Id ||
            !TransportSource(actor.Id,c.Action.Source.Value) ||
            (c.Action.Enabled ? !TransportDestination(actor.Id,c.Action.Destination.Value,c.Action.ItemId) :
                !_buildings.Values.Any(b=>b.Owner==actor.Id && b.Input==c.Action.Destination.Value)))throw new RuleViolation("NotAvailable");
        if(!_items.ContainsKey(c.Action.ItemId)||c.Action.Quantity!=0||c.Action.Source==c.Action.Destination)throw new RuleViolation("InvalidRoute");
        string key=TransportKey(c.Action.Source.Value,c.Action.Destination.Value,c.Action.ItemId);
        int existing=l.Queue.FindIndex(j=>j.Key==key);
        if(existing<0 && l.Queue.Count>=64)throw new RuleViolation("LogisticsQueueFull");
        for(int i=l.Queue.Count-1;i>=0;--i)
            if(l.Queue[i].Source.Value!=0 && l.Queue[i].Key!=key)
            {if(l.Trip?.JobKey==l.Queue[i].Key)l.Queue[i]=l.Queue[i] with {Enabled=false};else l.Queue.RemoveAt(i);}
        existing=l.Queue.FindIndex(j=>j.Key==key);
        var job=new LogisticsJobView(key,0,default,default,c.Action.Enabled,existing>=0?l.Queue[existing].LastServed:0,c.Action.Source,c.Action.Destination,c.Action.ItemId);
        if(existing>=0)l.Queue[existing]=job;else l.Queue.Add(job);
        if(c.Action.Enabled)
        {if(c.Action.Destination.Value==_enterprises[l.Enterprise].Input)l.InboundEnabled=true;else l.Enabled=true;}
        Emit("TransportRouteSet",actor.Id,l.Id,reason:c.Action.Enabled?"Enabled":"UserPaused",command:c.CommandId);
    }
    private LogisticsPlanData TransportPlan(LogisticsState l,LogisticsJobView job)
    {
        if(!TransportSource(l.Owner,job.Source.Value) || !TransportDestination(l.Owner,job.Destination.Value,job.ItemId))throw new RuleViolation("NotAvailable");
        if(_equipment.Values.Any(e=>e.Storage==job.Source.Value && e.Motion is not null))throw new RuleViolation("EquipmentBusy");
        var source=_economy.Inventories[job.Source.Value];var destination=_economy.Inventories[job.Destination.Value];long volume=_items[job.ItemId].UnitVolume;
        long free=checked(destination.Capacity-destination.Occupied(_items)-destination.Incoming);
        long quantity=Math.Min(source.Free(job.ItemId),Math.Min(l.CargoCapacity/volume,free/volume));
        if(quantity<=0)throw new RuleViolation(free<volume?"NoDestinationSpace":"NoStock");
        var path=_map.FindPath(source.Position,destination.Position);
        return new(-1,job.ItemId,path,quantity,Math.Max(1,(path.Length-1L)*Logistics.MoveTicksPerCell),
            InboundSource:destination.Id==_enterprises[l.Enterprise].Input?source.Id:0,
            OwnDestination:destination.Id,GoodsSource:source.Id,JobKey:job.Key);
    }
}

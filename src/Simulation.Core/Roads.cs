using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private RoadRules? Roads=>_config.Economy?.Roads;
    public bool UsesRoadTraffic=>Roads is not null;
    private readonly Dictionary<Cell,RoadCell> _roads=[];
    private readonly Dictionary<long,RoadWorkState> _roadWorks=[];
    private readonly List<RoadBypass> _bypasses=[];
    private readonly List<RoadOrder> _roadOrders=[];
    private readonly Dictionary<long,TrafficVehicle> _traffic=[];
    private DeterministicRandom? _roadRandom;
    private long _nextRoadOrder;
    private void InitializeRoads()
    {
        if(Roads is null)return;
        _roadRandom=new(unchecked(_config.Seed^0xE270ADUL));_nextRoadOrder=1;
        foreach(var c in _config.Layout!.InitialCityDirtCells)
            _roads.Add(c,new(c,1,[new(c,1,new(0),0,0,0)]));
    }
    private int RoadLevel(Cell cell)=>_roads.GetValueOrDefault(cell)?.Level??0;
    private bool PublicRoadCell(Cell cell)=>_config.Layout!.RoadCorridorCells.Contains(cell)&&_map.Walkable(cell)&&
        !_plots.Values.Any(p=>p.Definition.Position==cell)&&!_config.Layout.CivicObjects.Any(s=>s.Footprint.Contains(cell));
    private void SetRoadWork(ActorState actor,CommandEnvelope c)
    {
        RequireIdle(actor);var v=OwnVehicle(actor,c.Action.VehicleId);RequireActionLocation(actor,c.Action);
        if(Roads is null||v.ModelId!="trakt_d1")throw new RuleViolation("PaverRequired");
        if(v.Installation is not null||VehicleTransferring(v.CargoInventory.Value)||VehicleLogistics(v.Id) is not null)throw new RuleViolation("VehicleBusy");
        if(c.Action.Mode is not (EquipmentMode.Personal or EquipmentMode.Automatic or EquipmentMode.Off))throw new RuleViolation("InvalidMode");
        var route=c.Action.Stops??[];int level=c.Action.EquipmentLevel;
        if(c.Action.Enabled&&(route.IsDefaultOrEmpty||route.Length>64||route.Distinct().Count()!=route.Length||
            route.Any(p=>!PublicRoadCell(p))||route.Zip(route.Skip(1)).Any(p=>Manhattan(p.First,p.Second)!=1)||level is <1 or >3))throw new RuleViolation("InvalidRoadRoute");
        if(c.Action.Enabled&&route.Any(p=>RoadLevel(p)!=level&&RoadLevel(p)!=level-1))throw new RuleViolation("PreviousRoadStageRequired");
        if(c.Action.Mode==EquipmentMode.Personal&&v.Driver!=actor.Id||c.Action.Mode==EquipmentMode.Automatic&&v.Driver!=0)throw new RuleViolation("PaverOperatorRequired");
        bool created=false;if(!_roadWorks.TryGetValue(v.Id,out var w)){w=new(v.Id,actor.Id,Roads.WageMinor);created=true;}
        if(w.Batch is not null&&(w.Mode!=c.Action.Mode||!w.Route.SequenceEqual(route)||w.Level!=level))throw new RuleViolation("RoadBatchInProgress");
        if(w.Batch is null){w.Route=route;w.Cursor=0;w.Level=level;CancelTrafficRoute(v.Id);}
        w.Mode=c.Action.Mode;w.Enabled=c.Action.Enabled&&w.Mode!=EquipmentMode.Off;_roadWorks[v.Id]=w;
        w.Reason=w.Enabled?"Ready":"UserPaused";
        if(created)Emit("RoadCrewCreated",actor.Id,v.Id,command:c.CommandId);
        Emit("RoadWorkConfigured",actor.Id,v.Id,quantity:level,reason:w.Mode.ToString(),command:c.CommandId);
    }
    private string RoadReadiness(RoadWorkState w)
    {
        if(!w.Enabled||w.Mode!=EquipmentMode.Automatic)return "UserPaused";
        if(w.Cursor>=w.Route.Length)return "RouteCompleted";
        if(_actors[w.Group.Owner].Account is var a&&_economy.Accounts[a].Available<checked(2*w.Group.Wage))return "NoMoney";
        return "Ready";
    }
    private void AdvanceRoadWorks()
    {
        if(Roads is null)return;
        PublishRoadOrders();
        foreach(var w in _roadWorks.Values.OrderBy(w=>w.Vehicle))
        {
            if(!w.Enabled){w.Reason="UserPaused";continue;}
            if(w.Cursor>=w.Route.Length){w.Enabled=false;w.Reason="RouteCompleted";continue;}
            var v=_vehicles[w.Vehicle];var cargo=VehicleCargo(v);var actor=_actors[v.Owner.Value];
            int staff=w.Mode==EquipmentMode.Personal?(v.Driver==actor.Id?1:0):w.Group.Headcount;
            if(staff==0){w.Reason=w.Mode==EquipmentMode.Personal?"PaverOperatorRequired":"NoStaff";continue;}
            if(w.Batch is null)
            {
                var target=w.Route[w.Cursor];int before=RoadLevel(target);
                if(before==w.Level){w.Cursor++;if(w.Cursor==w.Route.Length){w.Enabled=false;w.Reason="RouteCompleted";}continue;}
                if(before!=w.Level-1){w.Reason="PreviousRoadStageRequired";continue;}
                if(!PublicRoadCell(target)){w.Reason="ProtectedRoadCell";continue;}
                if(cargo.Position!=target)
                {
                    if(w.Mode==EquipmentMode.Personal){w.Reason="MovePaverToWork";continue;}
                    long travelWage=checked(staff*w.Group.Wage);
                    if(_economy.Accounts[actor.Account].Available<travelWage){w.Reason="NoMoney";continue;}
                    var beforeTravel=Traffic(v.Id);var beforePosition=cargo.Position;
                    try
                    {
                        if(TrafficTravel(v,target))w.Reason="Arrived";else w.Reason=Traffic(v.Id).Reason;
                        var after=Traffic(v.Id);
                        if(beforeTravel.Progress!=after.Progress||beforePosition!=cargo.Position)
                        {
                            if(travelWage>0){var travelTx=_economy.Begin(Tick);travelTx.Pay(actor.Account,_wages,travelWage,"RoadWages");travelTx.Commit();}
                            Emit("RoadWorkTravelled",actor.Id,v.Id,quantity:staff,money:travelWage);
                        }
                    }
                    catch(RuleViolation ex){w.Reason=ex.ReasonCode;}
                    continue;
                }
                if(_roadWorks.Values.Any(other=>other!=w&&other.Batch?.Cell==target)){w.Reason="RoadCellBusy";continue;}
                if(Traffic(v.Id).Progress!=0||VehicleTransferring(cargo.Id)){w.Reason="VehicleBusy";continue;}
                var bypass=RoadWorkBypass(target);
                if(bypass.IsDefaultOrEmpty){w.Reason="NoSafeBypass";continue;}
                var rule=Roads.Stages.Single(r=>r.Level==w.Level);
                if(rule.Materials.Any(m=>cargo.Free(m.ItemId)<m.Quantity)){w.Reason="NoRoadMaterials";continue;}
                if(_economy.Accounts[actor.Account].Available<rule.PriceMinor){w.Reason="NoMoney";continue;}
                var consumed=PrepareMaterialFlows(rule.Materials.ToArray());var tx=_economy.Begin(Tick);
                tx.Pay(actor.Account,_treasury,rule.PriceMinor,"RoadConstruction");
                foreach(var m in rule.Materials)tx.RemoveFreeStock(cargo.Id,m.ItemId,m.Quantity);
                tx.Commit();foreach(var x in consumed)_flows[x.Key].ConstructionInput=x.Value;
                EmitMaterialConsumption(actor.Id,v.Id,rule.Materials.ToArray(),"road:"+v.Id+":"+Tick);
                w.Batch=new(target,w.Level,Tick,0,bypass);_bypasses.Add(new(v.Id,target,bypass,false,[]));
                actor.HeadingX=actor.HeadingZ=0;CancelTrafficRoute(v.Id);
                Emit("RoadWorkStarted",actor.Id,v.Id,quantity:w.Level,money:rule.PriceMinor,reason:target.X+","+target.Z);w.Reason="Working";continue;
            }
            if(w.Batch.StartedTick>=Tick)continue;
            long wage=w.Mode==EquipmentMode.Automatic?checked(staff*w.Group.Wage):0;
            if(_economy.Accounts[actor.Account].Available<wage){w.Reason="NoMoney";continue;}
            if(wage>0){var tx=_economy.Begin(Tick);tx.Pay(actor.Account,_wages,wage,"RoadWages");tx.Commit();}
            w.Batch=w.Batch with{Work=checked(w.Batch.Work+staff)};w.Reason="Working";
            Emit("RoadWorkAdvanced",actor.Id,v.Id,quantity:staff,money:wage);
            var batch=w.Batch;var stage=Roads.Stages.Single(r=>r.Level==batch.Level);
            if(batch.Work<2L*stage.Ticks)continue;
            var contributions=_roads.GetValueOrDefault(batch.Cell)?.Contributions??[];
            _roads[batch.Cell]=new(batch.Cell,batch.Level,[..contributions,new(batch.Cell,batch.Level,v.Owner,Tick,v.Id,stage.PriceMinor)]);
            Emit("RoadStageCompleted",actor.Id,v.Id,quantity:batch.Level,reason:batch.Cell.X+","+batch.Cell.Z);
            PayRoadOrder(batch.Cell,batch.Level,actor.Id);w.Batch=null;w.Cursor++;
            if(w.Cursor==w.Route.Length){w.Enabled=false;w.Reason="RouteCompleted";}
            int bypassIndex=_bypasses.FindIndex(b=>b.VehicleId==v.Id&&!b.Closing);
            _bypasses[bypassIndex]=_bypasses[bypassIndex] with{Closing=true};
        }
        // Only actual entrants retain the temporary lane; its permanent road endpoints never close.
        for(int i=0;i<_bypasses.Count;i++)
        {
            var b=_bypasses[i];var inside=b.Path.Skip(1).SkipLast(1).ToHashSet();
            _bypasses[i]=b with{Users=[..b.Users.Where(id=>FindBody(id) is {} body&&(inside.Contains(VehicleCargo(body).Position)||
                _traffic.TryGetValue(id,out var t)&&t.Progress>0&&t.Index<t.Path.Length-1&&inside.Contains(t.Path[t.Index+1])))]};
        }
        _bypasses.RemoveAll(b=>b.Closing&&b.Users.IsEmpty);
    }
    private ImmutableArray<Cell> RoadWorkBypass(Cell cell)
    {
        var neighbors=RoadNeighbors(cell).Where(PublicRoadCell).OrderByDescending(c=>RoadLevel(c)>0).ToArray();
        if(neighbors.Length<2)return [];
        foreach(var a in neighbors)foreach(var b in neighbors.Where(b=>b!=a))
        {
            if(Manhattan(a,b)!=2)continue;
            var path=PublicCorridorPath(a,b,c=>c!=cell&&!_roadWorks.Values.Any(w=>w.Batch?.Cell==c)&&
                (c==a||c==b||!ReservedByOther(0,c)));
            if(path.Length>2&&path.Length<=12)return [..path];
        }
        return [];
    }
    private void PublishRoadOrders()
    {
        if(Tick<_nextRoadOrder)return;_nextRoadOrder=checked(Tick+1200);
        if(_roadOrders.Count(o=>o.PaidTick==0)>=3)return;
        foreach(var c in _config.Layout!.RoadCenterCells.OrderBy(c=>c.Z).ThenBy(c=>c.X))
        {
            int level=RoadLevel(c)+1;if(level>3||!PublicRoadCell(c)||_roadOrders.Any(o=>o.Cell==c&&o.Level==level)||RoadWorkBypass(c).IsDefaultOrEmpty)continue;
            var s=Roads!.Stages.Single(s=>s.Level==level);
            long reward=checked((s.Materials.Sum(m=>checked(m.Quantity*_items[m.ItemId].BasePriceMinor))+2L*s.Ticks*Roads.WageMinor)*120/100);
            if(_economy.Accounts[_treasury].Available<reward)return;
            var o=new RoadOrder(_nextId++,c,level,reward,Tick);_roadOrders.Add(o);
            Emit("RoadOrderPublished",entity:o.Id,quantity:level,money:reward,reason:c.X+","+c.Z);return;
        }
    }
    private void AcceptRoadOrder(ActorState actor,CommandEnvelope c)
    {
        if(Roads is null)throw new RuleViolation("NotAvailable");
        var o=_roadOrders.SingleOrDefault(o=>o.Id==c.Action.OfferId&&o.Assignee==0&&o.PaidTick==0)??throw new RuleViolation("NotAvailable");
        if(!_vehicles.Values.Any(v=>v.Owner.Value==actor.Id&&v.ModelId=="trakt_d1"))throw new RuleViolation("PaverRequired");
        if(RoadLevel(o.Cell)!=o.Level-1)throw new RuleViolation("RoadOrderClosed");
        var tx=_economy.Begin(Tick);tx.HoldMoney(_treasury,"road-order:"+o.Id,o.Reward);tx.Commit();
        _roadOrders[_roadOrders.IndexOf(o)]=o with{Assignee=actor.Id,AcceptedTick=Tick};Emit("RoadOrderAccepted",actor.Id,o.Id,command:c.CommandId);
    }
    private void PayRoadOrder(Cell cell,int level,long builder)
    {
        var o=_roadOrders.SingleOrDefault(o=>o.Cell==cell&&o.Level==level&&o.PaidTick==0);if(o is null)return;
        var tx=_economy.Begin(Tick);
        if(o.Assignee==builder){tx.Pay(_treasury,_actors[builder].Account,o.Reward,"RoadOrderReward","road-order:"+o.Id);Emit("RoadOrderPaid",builder,o.Id,money:o.Reward);}
        else if(o.Assignee!=0)tx.ReleaseMoney(_treasury,"road-order:"+o.Id);
        tx.Commit();_roadOrders[_roadOrders.IndexOf(o)]=o with{PaidTick=Tick};
    }
    public RoadsState? InspectRoads()=>Roads is null?null:new(_roadRandom!.State,_nextRoadOrder,
        [.._roads.Values.OrderBy(c=>c.Cell.Z).ThenBy(c=>c.Cell.X)],[.._roadWorks.Values.OrderBy(w=>w.Vehicle).Select(w=>w.View())],
        [.._bypasses],[.._roadOrders],[.._traffic.Values.OrderBy(t=>t.VehicleId)]);
    public RoadObservation? RoadsOf(ParticipantId actor)=>InspectRoads() is {} r?new(r.Cells,[..r.Works.Where(w=>w.Group.OwnerId==actor)],r.Bypasses,
        r.Orders,[..r.Traffic.Where(t=>FindBody(t.VehicleId)?.Owner==actor)],Roads!):null;
    private long RoadCapital(ParticipantId actor)
    {
        if(Roads is null)return 0;
        long Cost(int level){var stage=Roads.Stages[level-1];return checked(stage.PriceMinor+stage.Materials.Sum(m=>checked(m.Quantity*_items[m.ItemId].BasePriceMinor)));}
        return checked(_roads.Values.SelectMany(c=>c.Contributions).Where(c=>c.Builder==actor).Sum(c=>Cost(c.Level))+
            _roadWorks.Values.Where(w=>w.Group.Owner==actor.Value&&w.Batch is not null).Sum(w=>Cost(w.Batch!.Level)));
    }
    private static int Manhattan(Cell a,Cell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z);
    private static Cell[] RoadNeighbors(Cell c)=>[new(c.X+1,c.Z),new(c.X,c.Z+1),new(c.X-1,c.Z),new(c.X,c.Z-1)];
}

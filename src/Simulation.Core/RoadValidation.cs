using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void RestoreRoads(RoadsState? state)
    {
        _roads.Clear();_roadWorks.Clear();_bypasses.Clear();_roadOrders.Clear();_traffic.Clear();
        if(Roads is null){if(state is not null)throw new CheckpointException("UnexpectedRoads");return;}
        if(state is null)throw new CheckpointException("MissingRoads");
        _roadRandom!.RestoreState(state.RandomState);_nextRoadOrder=state.NextOrderTick;
        foreach(var c in state.Cells)_roads.Add(c.Cell,c);
        foreach(var w in state.Works)
        {
            var saved=new RoadWorkState(w.VehicleId,w.Group.OwnerId.Value,Roads.WageMinor)
            {Mode=w.Mode,Enabled=w.Enabled,Level=w.Level,Route=w.Route,Cursor=w.Cursor,Batch=w.Batch,Reason=w.Reason};
            LoadGroup(saved.Group,w.Group);_roadWorks.Add(w.VehicleId,saved);
        }
        _bypasses.AddRange(state.Bypasses);_roadOrders.AddRange(state.Orders);
        foreach(var t in state.Traffic)_traffic.Add(t.VehicleId,t);
    }
    private void ValidateRoads()
    {
        if(Roads is null)return;
        void Require(bool condition){if(!condition)throw new InvalidOperationException("Invalid road/traffic state");}
        Require(_nextRoadOrder>=0&&_nextRoadOrder<=Tick+1200);
        foreach(var c in _roads.Values)
        {
            Require(PublicRoadCell(c.Cell)&&c.Level is >=1 and <=3&&c.Contributions.Length==c.Level);
            for(int index=0;index<c.Level;index++)
            {
                var contribution=c.Contributions[index];Require(contribution.Cell==c.Cell&&contribution.Level==index+1&&contribution.Tick>=0&&contribution.Tick<=Tick);
                if(contribution.Builder.Value==0)Require(index==0&&contribution.Tick==0&&contribution.VehicleId==0&&contribution.PriceMinor==0&&_config.Layout!.InitialCityDirtCells.Contains(c.Cell));
                else Require(_vehicles.TryGetValue(contribution.VehicleId,out var vehicle)&&vehicle.Owner==contribution.Builder&&vehicle.ModelId=="trakt_d1"&&contribution.PriceMinor==Roads.Stages[index].PriceMinor&&
                    _events.Any(e=>e.Type=="RoadStageCompleted"&&e.ActorId==contribution.Builder&&e.EntityId==contribution.VehicleId&&e.Tick==contribution.Tick&&e.Quantity==contribution.Level&&e.ReasonCode==c.Cell.X+","+c.Cell.Z));
            }
        }
        Require(_config.Layout!.InitialCityDirtCells.All(_roads.ContainsKey));
        foreach(var w in _roadWorks.Values)
        {
            Require(_vehicles.TryGetValue(w.Vehicle,out var v)&&v.ModelId=="trakt_d1"&&v.Owner.Value==w.Group.Owner&&w.Group.Id==w.Vehicle&&w.Group.Role==EmploymentRole.RoadWorks&&w.Group.Capacity==2&&
                w.Cursor>=0&&w.Cursor<=w.Route.Length&&w.Level is >=1 and <=3&&w.Route.Length<=64&&w.Route.All(PublicRoadCell));
            if(w.Batch is {} batch)Require(w.Cursor<w.Route.Length&&batch.Cell==w.Route[w.Cursor]&&batch.Level==w.Level&&RoadLevel(batch.Cell)==batch.Level-1&&batch.StartedTick>=1&&batch.StartedTick<=Tick&&batch.Work>=0&&batch.Work<2L*Roads.Stages[batch.Level-1].Ticks&&
                VehicleCargo(v!).Position==batch.Cell&&batch.Bypass.Length>2&&batch.Bypass.All(PublicRoadCell)&&!batch.Bypass.Contains(batch.Cell)&&_bypasses.Count(b=>b.VehicleId==v!.Id&&!b.Closing&&b.WorkCell==batch.Cell&&b.Path.SequenceEqual(batch.Bypass))==1);
        }
        foreach(var b in _bypasses)Require(_vehicles.ContainsKey(b.VehicleId)&&b.Path.Length>2&&b.Path.All(PublicRoadCell)&&!b.Path.Contains(b.WorkCell)&&b.Path.Zip(b.Path.Skip(1)).All(p=>Manhattan(p.First,p.Second)==1)&&
            (b.Closing||_roadWorks.GetValueOrDefault(b.VehicleId)?.Batch?.Cell==b.WorkCell));
        var reserved=new HashSet<Cell>();
        foreach(var t in _traffic.Values)
        {
            Require(_vehicles.ContainsKey(t.VehicleId)&&t.Progress is >=0 and <800&&t.Index>=0&&(t.Path.Length==0?t.Index==0&&t.Progress==0:t.Index<t.Path.Length)&&t.WaitingSince>=0&&t.WaitingSince<=Tick&&t.StoppedSince>=0&&t.StoppedSince<=Tick&&
                t.ExitQuanta>=0&&t.Priority>=0&&t.AdvancedTick>=0&&t.AdvancedTick<=Tick+1&&t.AdvancedQuanta is >=0 and <=10&&t.Path.Skip(t.Index).All(_map.Walkable)&&t.Path.Zip(t.Path.Skip(1)).All(p=>Manhattan(p.First,p.Second)==1));
            if(t.Path.Length>0)Require(VehicleCargo(_vehicles[t.VehicleId]).Position==t.Path[t.Index]);
            if(t.Progress>0)Require(t.Index<t.Path.Length-1&&reserved.Add(t.Path[t.Index+1])&&!ReservedByOther(t.VehicleId,t.Path[t.Index+1]));
            if(t.Resume is {} r)Require(_map.Walkable(r)&&t.YieldTo>=0);
        }
        Require(_roadOrders.Select(o=>o.Id).Distinct().Count()==_roadOrders.Count&&_roadOrders.Select(o=>(o.Cell,o.Level)).Distinct().Count()==_roadOrders.Count);
        foreach(var o in _roadOrders)Require(o.Id>=1000&&o.Id<_nextId&&PublicRoadCell(o.Cell)&&o.Level is >=1 and <=3&&o.Reward>0&&o.CreatedTick>=1&&o.CreatedTick<=Tick&&o.AcceptedTick>=0&&o.AcceptedTick<=Tick&&o.PaidTick>=0&&o.PaidTick<=Tick&&
            (o.Assignee==0||_actors.ContainsKey(o.Assignee)&&o.AcceptedTick>=o.CreatedTick)&&
            _economy.Accounts[_treasury].Holds.GetValueOrDefault("road-order:"+o.Id)==(o.Assignee!=0&&o.PaidTick==0?o.Reward:0));
    }
    private void AddExpectedRoadHolds(Dictionary<(long Account,string Hold),long> money)
    {foreach(var o in _roadOrders.Where(o=>o.Assignee!=0&&o.PaidTick==0))money.Add((_treasury,"road-order:"+o.Id),o.Reward);}
}

public static partial class ScenarioValidation
{
    private static void ValidateRoadRules(ScenarioDefinition s)
    {
        if(s.Economy?.Roads is not {} r)return;
        if(s.Layout is null||s.Economy.Fleet is null||r.Stages.IsDefault||r.Stages.Length!=3||!r.Stages.Select(x=>x.Level).SequenceEqual(new[]{1,2,3})||
            !r.Stages.Select(x=>x.Speed).SequenceEqual(new[]{20,40,60})||r.Stages.Any(x=>x.PriceMinor<=0||x.Ticks<=0||x.Ticks>10000||x.Materials.IsDefaultOrEmpty||x.Materials.Any(m=>m.Quantity<=0||!s.Items.Any(i=>i.Id==m.ItemId)))||
            r.WageMinor<=0||r.ExitHoldTicks!=1||r.WarningTicks!=30||r.ReleaseTicks!=60)throw new ArgumentException("Invalid road rules");
    }
}

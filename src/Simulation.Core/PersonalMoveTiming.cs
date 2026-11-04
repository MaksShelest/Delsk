namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private long TimedMoveForecast(ActorState actor,PersonalJob job)
    {
        long total=job.TrafficWaitTime;for(int n=0;n<job.Path.Length-1;n++)total=checked(total+(job.TimedEdges![n]>0?job.TimedEdges[n]:PersonalEdgeTimeUnits(actor,job.Path[n],job.Path[n+1])));
        return checked(job.StartTick+Movement.WholeTicks(total));
    }
    private void RefreshTimedMoveEstimates()
    {
        if(!UsesAcceleratedMovement)return;
        foreach(var actor in _actors.Values)
            if(actor.Job is {Kind:"Move",TimedEdges:not null} job)job.CompletionTick=TimedMoveForecast(actor,job);
    }
    private long PersonalEdgeAtEntry(ActorState actor,Cell a,Cell b,long entryTick)
    {
        if(Fleet is null||Roads is null)return PersonalEdgeTimeUnits(actor,a,b);
        bool WasRoad(Cell c)=>_roads.TryGetValue(c,out var road)&&road.Contributions.Any(part=>part.Tick<entryTick);
        return Movement.EdgeTime(Movement.RunningSpeed(WasRoad(a)&&WasRoad(b)));
    }
    private bool ValidPersonalMoveTiming(ActorState actor,PersonalJob job)
    {
        if(job.TimedEdges is not {} times)
            return job.SegmentTicks<(UsesAcceleratedMovement?PersonalEdgeTimeUnits(actor,job.Path[job.PathIndex],job.Path[job.PathIndex+1]):PersonalMoveTicks(actor,job.Path[job.PathIndex],job.Path[job.PathIndex+1]))&&
                job.CompletionTick==checked(job.StartTick+PersonalPathTicks(actor,job.Path))&&
                (UsesAcceleratedMovement?checked((Tick-job.StartTick)*MotionTimeScale):Tick-job.StartTick)==
                checked(job.Path.Take(job.PathIndex+1).Zip(job.Path.Skip(1).Take(job.PathIndex)).Sum(p=>UsesAcceleratedMovement?PersonalEdgeTimeUnits(actor,p.First,p.Second):(long)PersonalMoveTicks(actor,p.First,p.Second))+job.SegmentTicks);
        if(job.TrafficWaitTime<0||!UsesFreeVehicleControl&&job.TrafficWaitTime!=0||job.TrafficWaitTime>checked((Tick-job.StartTick)*MotionTimeScale))return false;
        if(!UsesAcceleratedMovement||times.Length!=job.Path.Length-1||times.Any(t=>t<0))return false;
        long completed=0;
        for(int n=0;n<times.Length;n++)
        {
            if(n>job.PathIndex&&times[n]!=0||n<job.PathIndex&&times[n]==0)return false;
            if(times[n]>0&&times[n]!=PersonalEdgeAtEntry(actor,job.Path[n],job.Path[n+1],checked(job.StartTick+completed/MotionTimeScale+1)))return false;
            if(n<job.PathIndex)completed=checked(completed+times[n]);
        }
        if(times[job.PathIndex]==0?job.SegmentTicks!=0:job.SegmentTicks>=times[job.PathIndex])return false;
        return checked((Tick-job.StartTick)*MotionTimeScale)==checked(completed+job.SegmentTicks+job.TrafficWaitTime)&&job.CompletionTick==TimedMoveForecast(actor,job);
    }
}

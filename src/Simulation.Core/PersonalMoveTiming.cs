namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private long TimedMoveForecast(ActorState actor,PersonalJob job)
    {
        long total=0;for(int n=0;n<job.Path.Length-1;n++)total=checked(total+(job.TimedEdges![n]>0?job.TimedEdges[n]:PersonalEdgeTimeUnits(actor,job.Path[n],job.Path[n+1])));
        return checked(job.StartTick+MovementTiming.WholeTicks(total));
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
        return MovementTiming.EdgeTimeUnits(WasRoad(a)&&WasRoad(b)?20:16,32);
    }
    private bool ValidPersonalMoveTiming(ActorState actor,PersonalJob job)
    {
        if(job.TimedEdges is not {} times)
            return job.SegmentTicks<(UsesAcceleratedMovement?PersonalEdgeTimeUnits(actor,job.Path[job.PathIndex],job.Path[job.PathIndex+1]):PersonalMoveTicks(actor,job.Path[job.PathIndex],job.Path[job.PathIndex+1]))&&
                job.CompletionTick==checked(job.StartTick+PersonalPathTicks(actor,job.Path))&&
                (UsesAcceleratedMovement?checked((Tick-job.StartTick)*MovementTiming.TimeUnitsPerTick):Tick-job.StartTick)==
                checked(job.Path.Take(job.PathIndex+1).Zip(job.Path.Skip(1).Take(job.PathIndex)).Sum(p=>UsesAcceleratedMovement?PersonalEdgeTimeUnits(actor,p.First,p.Second):(long)PersonalMoveTicks(actor,p.First,p.Second))+job.SegmentTicks);
        if(!UsesAcceleratedMovement||times.Length!=job.Path.Length-1||times.Any(t=>t<0))return false;
        long completed=0;
        for(int n=0;n<times.Length;n++)
        {
            if(n>job.PathIndex&&times[n]!=0||n<job.PathIndex&&times[n]==0)return false;
            if(times[n]>0&&times[n]!=PersonalEdgeAtEntry(actor,job.Path[n],job.Path[n+1],checked(job.StartTick+completed/MovementTiming.TimeUnitsPerTick+1)))return false;
            if(n<job.PathIndex)completed=checked(completed+times[n]);
        }
        if(times[job.PathIndex]==0?job.SegmentTicks!=0:job.SegmentTicks>=times[job.PathIndex])return false;
        return checked((Tick-job.StartTick)*MovementTiming.TimeUnitsPerTick)==checked(completed+job.SegmentTicks)&&job.CompletionTick==TimedMoveForecast(actor,job);
    }
}

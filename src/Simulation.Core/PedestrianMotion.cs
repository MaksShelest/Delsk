namespace Simulation.Core;

/// <summary>
/// Read-only pose of one pedestrian Move job for the current tick.
/// Milli 1000 is one cell. The sample never writes TimedEdges and is not part of the world hash.
/// </summary>
public readonly record struct PedestrianMotion(
    bool Active,
    int Ax, int Az, int Bx, int Bz, int Cx, int Cz,
    bool Crosses, bool Corner,
    int StartMilli, int EndMilli);

public sealed partial class SimulationEngine
{
    /// <summary>
    /// Where a walking participant is allowed to be drawn during this tick.
    /// The end of the sample is the authority pose. The start is the same budget run backwards,
    /// and only when that rewind plays forward to the same cell. A blocked next cell is not entered.
    /// </summary>
    public PedestrianMotion PedestrianMotionOf(long actorId)
    {
        if(!_actors.TryGetValue(actorId,out var actor)||DrivenVehicle(actor.Id) is not null)return default;
        if(actor.Job is not {Kind:"Move"} job||job.StartTick>=Tick||job.PathIndex>=job.Path.Length-1)return default;
        int index=job.PathIndex,segment=job.SegmentTicks;
        var ahead=job.Path[index+1];
        bool blocked=!_map.Walkable(ahead)||UsesFreeVehicleControl&&PedestrianTrafficBlocked(ahead,actor.Id);
        int startIndex=index,startSegment=segment;
        if(!blocked)
        {
            long budget=UsesAcceleratedMovement?MotionTimeScale:1L,left=budget;
            int guard=0;
            while(left>0&&guard++<8&&(startIndex>0||startSegment>0))
            {
                if(startSegment==0)
                {
                    if(startIndex==0)break;
                    startIndex--;
                    long previous=PresentationEdge(actor,job,startIndex);
                    if(previous<=0||previous>int.MaxValue)return default;
                    startSegment=(int)previous;
                }
                long required=PresentationEdge(actor,job,startIndex);
                if(required<=0||required>int.MaxValue)return default;
                long spent=Math.Min(left,startSegment);
                startSegment-=(int)spent;left-=spent;
            }
            if(index-startIndex>1){startIndex=index;startSegment=segment;}
            else
            {
                var end=Play(actor,job,startIndex,startSegment,budget);
                if(end.Index!=index||end.Segment!=segment){startIndex=index;startSegment=segment;}
            }
        }
        bool crosses=startIndex!=index;
        var origin=job.Path[startIndex];
        var middle=job.Path[startIndex+1];
        long originEdge=PresentationEdge(actor,job,startIndex);
        long currentEdge=PresentationEdge(actor,job,index);
        if(originEdge<=0||currentEdge<=0)return default;
        int startMilli=Milli(startSegment,originEdge),endMilli=Milli(segment,currentEdge);
        int cx=middle.X,cz=middle.Z;bool corner=false;
        if(crosses){cx=ahead.X;cz=ahead.Z;corner=true;}
        else if(index+2<job.Path.Length)
        {
            var next=job.Path[index+2];
            if(_map.Walkable(next)&&!(UsesFreeVehicleControl&&PedestrianTrafficBlocked(next,actor.Id)))
            {cx=next.X;cz=next.Z;corner=true;}
        }
        return new(true,origin.X,origin.Z,middle.X,middle.Z,cx,cz,crosses,corner,startMilli,endMilli);
    }

    private long PresentationEdge(ActorState actor,PersonalJob job,int index)
    {
        if(job.TimedEdges is {} timing&&(uint)index<(uint)timing.Length&&timing[index]>0)return timing[index];
        var from=job.Path[index];var to=job.Path[index+1];
        return UsesAcceleratedMovement?PersonalEdgeTimeUnits(actor,from,to):PersonalMoveTicks(actor,from,to);
    }

    private (int Index,int Segment) Play(ActorState actor,PersonalJob job,int index,int segment,long budget)
    {
        long left=budget;int guard=0;
        while(index<job.Path.Length-1&&left>0&&guard++<8)
        {
            var next=job.Path[index+1];
            if(!_map.Walkable(next)||UsesFreeVehicleControl&&PedestrianTrafficBlocked(next,actor.Id))return(index,segment);
            long required=PresentationEdge(actor,job,index);
            if(required<=0||required>int.MaxValue)return(index,segment);
            long room=required-segment;if(room<0)return(index,segment);
            long spent=Math.Min(left,room);left-=spent;segment+=(int)spent;
            if(segment<required)return(index,segment);
            segment=0;index++;
        }
        return(index,segment);
    }

    private static int Milli(long segment,long required)
    {
        if(required<=0||segment<=0)return 0;
        long milli=segment*1000L/required;
        return milli>=1000?1000:(int)milli;
    }
}

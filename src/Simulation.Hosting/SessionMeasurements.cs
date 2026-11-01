using System.Diagnostics;
namespace Simulation.Hosting;

/// <summary>Opt-in host measurements; absent from world state, controller inputs and checkpoints.</summary>
public sealed class SessionMeasurements
{
    public sealed record Metric(long Calls,double Milliseconds,long AllocatedBytes);
    private readonly Dictionary<string,Metric> _metrics=[];
    public readonly record struct Mark(long Timestamp,long Allocated);
    public Mark Start()=>new(Stopwatch.GetTimestamp(),GC.GetTotalAllocatedBytes(false));
    public void End(string phase,Mark mark)
    {
        var previous=_metrics.GetValueOrDefault(phase)??new(0,0,0);
        _metrics[phase]=new(previous.Calls+1,previous.Milliseconds+Stopwatch.GetElapsedTime(mark.Timestamp).TotalMilliseconds,
            previous.AllocatedBytes+Math.Max(0,GC.GetTotalAllocatedBytes(false)-mark.Allocated));
    }
    public IReadOnlyDictionary<string,Metric> Capture()=>new Dictionary<string,Metric>(_metrics);
}

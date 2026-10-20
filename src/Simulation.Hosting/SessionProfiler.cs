using System.Diagnostics;
using System.Text;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record SessionProfile(long MeasuredTicks, double AverageTickMs, double MedianTickMs, double P95TickMs,
    double MaximumTickMs, int SaveBytes, double EncodeMs, double DecodeMs, long WorkingSetBytes,
    bool CheckpointHashMatches, string Scope)
{
    public StepTiming? AverageStages {get;init;}
    public StepTiming? P95Stages {get;init;}
    public double CpuMs {get;init;}
    public long AllocatedBytes {get;init;}
    public long PeakWorkingSetBytes {get;init;}
    public long ManagedMemoryBytes {get;init;}
    public long InitialCapitalMinor {get;init;}
    public long FinalCapitalMinor {get;init;}
}
public sealed record StepTiming(double ControllersMs,double CoreMs,double SamplingMs);

public static class SessionProfiler
{
    public static SessionProfile Run(GameSession session, long finalTick, Func<ControllerCheckpoint,IController> restore)
    {
        if (session.Paused || finalTick <= session.Engine.Tick || finalTick > session.Engine.Configuration().MaxTicks)
            throw new ArgumentException("Invalid profiling horizon");
        var samples = new List<double>();
        var stages = new List<StepTiming>();
        long initialCapital=session.Engine.PublicEconomy().CapitalMinor;
        long allocated=GC.GetTotalAllocatedBytes(false);
        double cpuStart=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
        session.StepMeasured += stages.Add;
        try
        {
        while (session.Engine.Tick < finalTick)
        {
            if(session.Engine.WaitingForBallots)
            {if(!session.PumpVoting())throw new InvalidOperationException("ProfileWaitingForBallots");continue;}
            long start = Stopwatch.GetTimestamp(); session.Step();
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        // Match the ordinary runner at a horizon that lands exactly on a voting barrier.
        if(session.Engine.WaitingForBallots && !session.PumpVoting())throw new InvalidOperationException("ProfileWaitingForBallots");
        session.AdvanceTo(finalTick);
        }
        finally { session.StepMeasured -= stages.Add; }
        double cpu=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds-cpuStart;
        allocated=GC.GetTotalAllocatedBytes(false)-allocated;
        long beforeSave = Stopwatch.GetTimestamp(); string save = CheckpointStore.Encode(session);
        double encode = Stopwatch.GetElapsedTime(beforeSave).TotalMilliseconds;
        long beforeLoad = Stopwatch.GetTimestamp(); var loaded = CheckpointStore.Decode(save);
        loaded.RestoreControllers(restore); double decode = Stopwatch.GetElapsedTime(beforeLoad).TotalMilliseconds;
        bool exact = loaded.Engine.ComputeStateHash() == session.Engine.ComputeStateHash() &&
            loaded.ComputeControllerStateHash() == session.ComputeControllerStateHash();
        if (!exact) throw new InvalidOperationException("Profile checkpoint changed world/controller state");
        samples.Sort();
        return new(samples.Count, samples.Average(), samples[samples.Count/2], samples[(int)Math.Ceiling(samples.Count*.95)-1],
            samples[^1], Encoding.UTF8.GetByteCount(save), encode, decode, Process.GetCurrentProcess().WorkingSet64, exact,
            "Hosted headless Step including controllers, invariants and sample/hash; includes JIT; encode/decode measured separately, not disk I/O; process memory includes restored session; no rendering/device claim")
        {
            AverageStages = new(stages.Average(s=>s.ControllersMs),stages.Average(s=>s.CoreMs),stages.Average(s=>s.SamplingMs)),
            P95Stages = new(P95(stages.Select(s=>s.ControllersMs)),P95(stages.Select(s=>s.CoreMs)),P95(stages.Select(s=>s.SamplingMs))),
            CpuMs=cpu, AllocatedBytes=allocated, PeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
            ManagedMemoryBytes=GC.GetTotalMemory(false),InitialCapitalMinor=initialCapital,FinalCapitalMinor=session.Engine.PublicEconomy().CapitalMinor
        };
    }
    private static double P95(IEnumerable<double> data)
    {var a=data.Order().ToArray();return a[(int)Math.Ceiling(a.Length*.95)-1];}
}

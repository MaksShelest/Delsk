using System.Diagnostics;
using System.Text;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record SessionProfile(long MeasuredTicks, double AverageTickMs, double MedianTickMs, double P95TickMs,
    double MaximumTickMs, int SaveBytes, double EncodeMs, double DecodeMs, long WorkingSetBytes,
    bool CheckpointHashMatches, string Scope);

public static class SessionProfiler
{
    public static SessionProfile Run(GameSession session, long finalTick, Func<ControllerCheckpoint,IController> restore)
    {
        if (session.Paused || finalTick <= session.Engine.Tick || finalTick > session.Engine.Configuration().MaxTicks)
            throw new ArgumentException("Invalid profiling horizon");
        var samples = new List<double>();
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
            "Hosted headless Step including controllers, invariants and sample/hash; includes JIT; save/load measured separately; no rendering or device claim");
    }
}

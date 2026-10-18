using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Presentation only: committed tick plus the Core travel quantum. Never grants goods.</summary>
public static class HarvestProgress
{
    public static double Percent(OwnHarvest? job, long tick, int quantum) => job is null ? 0 :
        Math.Clamp(100 * ((double)(tick - job.StartTick) + quantum / (double)SimulationEngine.TravelQuantaPerTick) /
            (job.CompletionTick - job.StartTick), 0, 100);
}

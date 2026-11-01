using System.Text.Json;
using Simulation.Core;
namespace Simulation.Hosting;

internal sealed record LaborHistoryArchive(EmploymentView[][] Groups,int[] References)
{
    internal static LaborHistoryArchive Create(IEnumerable<LaborSample> samples)
    {
        var groups=new List<EmploymentView[]>();var ids=new List<int>();var index=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var sample in samples)
        {
            string key=JsonSerializer.Serialize(sample.EmploymentGroups,SimulationJson.Options);
            if(!index.TryGetValue(key,out int id)){id=groups.Count;groups.Add(sample.EmploymentGroups);index.Add(key,id);}ids.Add(id);
        }
        return new(groups.ToArray(),ids.ToArray());
    }
    internal LaborSample[] Restore(TickSample[] ticks)
    {
        if(Groups is null||References is null||References.Length!=ticks.Length||Groups.Any(g=>g is null)||References.Any(i=>i<0||i>=Groups.Length)||References.Distinct().Count()!=Groups.Length)throw new CheckpointException("InvalidLaborHistoryArchive");
        return References.Select((id,i)=>new LaborSample(ticks[i].Tick,Groups[id])).ToArray();
    }
}

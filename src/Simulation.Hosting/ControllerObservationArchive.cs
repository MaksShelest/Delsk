using System.Reflection;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

// Public DTO fields only. A field is decoded once and shared by immutable historical views.
// This bounds memory by actual distinct data instead of repeatedly expanding a shared large map.
internal record ObservationField(int Property,string Json);
internal record ArchivedControllerTurn(ControllerTurn Turn,int[] Fields);
internal record ControllerObservationArchive(int Version,string[] Properties,ObservationField[] Values,ArchivedControllerTurn[] Turns);
internal sealed class ControllerObservationPool
{
    private static readonly PropertyInfo[] Properties=typeof(ControllerObservation).GetProperties(BindingFlags.Instance|BindingFlags.Public)
        .Where(p=>p.CanRead&&p.SetMethod?.IsPublic==true).OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray();
    private readonly Dictionary<(int,string),int> _index=[];
    private readonly List<ObservationField> _values=[];
    private readonly List<object?> _objects=[];
    private readonly List<ArchivedControllerTurn> _turns=[];
    internal ControllerTurn Add(ControllerTurn turn)
    {
        var observation=turn.Observation with{};var fields=new int[Properties.Length];
        for(int p=0;p<Properties.Length;p++)
        {
            var property=Properties[p];object? value=property.GetValue(observation);string json=JsonSerializer.Serialize(value,property.PropertyType,SimulationJson.Options);
            if(!_index.TryGetValue((p,json),out int id))
            {id=_values.Count;_index.Add((p,json),id);_values.Add(new(p,json));_objects.Add(value);}
            fields[p]=id;property.SetValue(observation,_objects[id]);
        }
        var full=turn with{Observation=observation};_turns.Add(new(full with{Observation=null!},fields));return full;
    }
    internal ControllerObservationArchive Capture()=>new(1,Properties.Select(p=>p.Name).ToArray(),_values.ToArray(),_turns.ToArray());
    internal static (ControllerObservationPool Pool,ControllerTurn[] Turns) Restore(ControllerObservationArchive saved,long tick,int actors)
    {
        if(saved.Version!=1||saved.Properties is null||!saved.Properties.SequenceEqual(Properties.Select(p=>p.Name))||saved.Values is null||saved.Turns is null||
            saved.Turns.LongLength>checked((tick+1)*Math.Max(1,actors)*16)||saved.Values.Length>checked(saved.Turns.Length*Properties.Length))throw new CheckpointException("InvalidObservationArchive");
        var pool=new ControllerObservationPool();
        foreach(var field in saved.Values)
        {
            if(field is null||field.Property<0||field.Property>=Properties.Length||field.Json is null||!pool._index.TryAdd((field.Property,field.Json),pool._values.Count))throw new CheckpointException("InvalidObservationField");
            var property=Properties[field.Property];object? value=JsonSerializer.Deserialize(field.Json,property.PropertyType,SimulationJson.Options);
            if(value is null&&property.Name is nameof(ControllerObservation.Map) or nameof(ControllerObservation.Participant) or nameof(ControllerObservation.Account))throw new CheckpointException("MissingObservationField");
            pool._values.Add(field);pool._objects.Add(value);
        }
        var turns=new List<ControllerTurn>();var used=new HashSet<int>();
        foreach(var item in saved.Turns)
        {
            if(item?.Turn is null||item.Turn.Observation is not null||item.Fields is null||item.Fields.Length!=Properties.Length)throw new CheckpointException("InvalidArchivedTurn");
            var observation=JsonSerializer.Deserialize<ControllerObservation>("{}",SimulationJson.Options)!;
            for(int p=0;p<Properties.Length;p++)
            {
                int id=item.Fields[p];if(id<0||id>=pool._values.Count||pool._values[id].Property!=p)throw new CheckpointException("InvalidObservationReference");
                Properties[p].SetValue(observation,pool._objects[id]);used.Add(id);
            }
            pool._turns.Add(item);turns.Add(item.Turn with{Observation=observation});
        }
        if(used.Count!=pool._values.Count)throw new CheckpointException("UnusedObservationField");
        return(pool,turns.ToArray());
    }
}

using System.Reflection;
using System.Text.Json;
using Simulation.Core;
namespace Simulation.Hosting;

internal record ObservationField(int Property,string Json);
internal record ArchivedControllerTurn(ControllerTurn Turn,int[] Fields);
internal record ControllerObservationArchive(int Version,string[] Properties,ObservationField[]? Values,ArchivedControllerTurn[] Turns,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ObservationNode[]? Nodes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[][]? Shapes=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long[]? SampleTicks=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int[]? SampleValues=null);
internal sealed class ControllerObservationPool
{
    private static readonly PropertyInfo[] DeclarationOrder = typeof(ControllerObservation).GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.CanRead && p.SetMethod?.IsPublic == true).ToArray();
    private static readonly PropertyInfo[] Properties = DeclarationOrder.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    private static readonly (int Index, string JsonName, bool OmitNull)[] ExportFields = DeclarationOrder.Select(field => (
        Array.IndexOf(Properties, field),
        SimulationJson.Options.PropertyNamingPolicy?.ConvertName(field.Name) ?? field.Name,
        field.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()?.Condition ==
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)).ToArray();
    private const int CachedFieldLimit = 256 * 1024;
    private readonly (string? Json, int Node)[] _recentFields = new (string?, int)[Properties.Length];
    private ObservationGraph _graph=new();
    private readonly List<ArchivedControllerTurn> _turns=[];
    private readonly Dictionary<long,ControllerTurn> _last=[];
    // A bounded decode cache is for explicitly requested historical views, never a full expanded history.
    private readonly Dictionary<(int,int),(object? Value,int Bytes)> _decoded=[];
    private readonly Queue<(int,int)> _decodeOrder=[];
    private int _decodeBytes;
    private readonly List<long> _sampleTicks=[];
    private readonly List<int> _sampleValues=[];
    internal int Count=>_turns.Count;
    internal ControllerTurn? Last(long actor)=>_last.GetValueOrDefault(actor);
    internal ControllerTurn Add(ControllerTurn turn)
    {
        var fields = new int[Properties.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            var property = Properties[i];
            string json = JsonSerializer.Serialize(property.GetValue(turn.Observation), property.PropertyType, SimulationJson.Options);
            var recent = _recentFields[i];
            if (string.Equals(recent.Json, json, StringComparison.Ordinal)) fields[i] = recent.Node;
            else
            {
                fields[i] = _graph.AddJson(json);
                // The last exact representation is enough to reuse common fields across a barrier.
                _recentFields[i] = json.Length <= CachedFieldLimit ? (json, fields[i]) : default;
            }
        }
        var metadata=turn with{Observation=null!};_turns.Add(new(metadata,fields));_last[turn.Actor.Value]=metadata;return turn;
    }
    private object? Read(int property,int node)
    {
        var key=(property,node);if(_decoded.TryGetValue(key,out var cached))return cached.Value;
        byte[] bytes=_graph.Bytes(node);object? value=JsonSerializer.Deserialize(bytes,Properties[property].PropertyType,SimulationJson.Options);
        if(bytes.Length<=16*1024*1024)
        {
            while(_decodeBytes+bytes.Length>16*1024*1024&&_decodeOrder.TryDequeue(out var old))if(_decoded.Remove(old,out var item))_decodeBytes-=item.Bytes;
            _decoded.Add(key,(value,bytes.Length));_decodeOrder.Enqueue(key);_decodeBytes+=bytes.Length;
        }
        return value;
    }
    internal IEnumerable<ControllerTurn> Enumerate(int start=0)
    {
        foreach(var item in _turns.Skip(start))
        {
            var observation=JsonSerializer.Deserialize<ControllerObservation>("{}",SimulationJson.Options)!;
            for(int p=0;p<Properties.Length;p++)Properties[p].SetValue(observation,Read(p,item.Fields[p]));
            yield return item.Turn with{Observation=observation};
        }
    }
    internal IEnumerable<ControllerTurn> Metadata()=>_turns.Select(t=>t.Turn);
    internal void WriteLines(Stream output)
    {
        byte[] newline=System.Text.Encoding.UTF8.GetBytes(Environment.NewLine);
        foreach(var item in _turns)
        {
            using(var writer=new Utf8JsonWriter(output,new JsonWriterOptions{Encoder=SimulationJson.Options.Encoder}))
            using(var metadata=JsonDocument.Parse(JsonSerializer.Serialize(item.Turn,SimulationJson.Options)))
            {
                writer.WriteStartObject();
                foreach(var property in metadata.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if(property.Name!="observation"){property.Value.WriteTo(writer);continue;}
                    writer.WriteStartObject();
                    // Preserve the original DTO declaration/serializer order, not the archive's sorted index order.
                    foreach (var field in ExportFields)
                    {
                        int root = item.Fields[field.Index];
                        if (field.OmitNull && _graph.Nodes[root].Json == "null") continue;
                        writer.WritePropertyName(field.JsonName);
                        _graph.Write(writer, root);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();writer.Flush();
            }
            output.Write(newline);
        }
    }
    internal object Statistics()=>new {Storage="structural-json-dag-v2",Nodes=_graph.Nodes.Count,Shapes=_graph.Shapes.Count,ScalarCharacters=_graph.ScalarCharacters,Turns=_turns.Count,DecodeCacheBytes=_decodeBytes};
    internal ControllerObservationArchive Capture(ControllerSample[] samples)
    {
        if(samples.Length<_sampleTicks.Count)throw new InvalidOperationException("Historical sample prefix shortened");
        for(int i=_sampleTicks.Count;i<samples.Length;i++)
        {_sampleTicks.Add(samples[i].Tick);_sampleValues.Add(_graph.AddJson(JsonSerializer.Serialize(samples[i] with{Tick=0},SimulationJson.Options)));}
        return new(2,Properties.Select(p=>p.Name).ToArray(),null,_turns.ToArray(),_graph.Nodes.ToArray(),_graph.Shapes.ToArray(),_sampleTicks.ToArray(),_sampleValues.ToArray());
    }
    /// <summary>
    /// Старый архив называет поля и хранит их индексы. Совпадение всего списка больше не требуется:
    /// отсутствует только ProvincialProgram, и тогда у каждого хода есть явная JSON-ссылка на null.
    /// </summary>
    private static int[] BindObservationProperties(string[] saved)
    {
        if(saved.Length==0||saved.Any(string.IsNullOrEmpty))throw new CheckpointException("InvalidObservationArchive");
        var current=new Dictionary<string,int>(StringComparer.Ordinal);
        for(int i=0;i<Properties.Length;i++)current.Add(Properties[i].Name,i);
        var seen=new HashSet<string>(StringComparer.Ordinal);var map=new int[saved.Length];
        for(int i=0;i<saved.Length;i++)
        {
            if(!seen.Add(saved[i])||!current.TryGetValue(saved[i],out int index))throw new CheckpointException("InvalidObservationArchive");
            map[i]=index;
        }
        var present=map.ToHashSet();
        for(int i=0;i<Properties.Length;i++)
            if(!present.Contains(i)&&Properties[i].Name!=nameof(ControllerObservation.ProvincialProgram))throw new CheckpointException("InvalidObservationArchive");
        return map;
    }
    internal static (ControllerObservationPool Pool,ControllerTurn[] Turns,ControllerSample[]? Samples) Restore(ControllerObservationArchive saved,long tick,int actors)
    {
        if(saved.Version is not(1 or 2)||saved.Properties is null||saved.Turns is null||
            saved.Turns.LongLength>checked((tick+1)*Math.Max(1,actors)*16))throw new CheckpointException("InvalidObservationArchive");
        int[] savedToCurrent=BindObservationProperties(saved.Properties);
        var pool=new ControllerObservationPool();int[]? translated=null;
        if(saved.Version==1)
        {
            if(saved.Values is null||saved.Nodes is not null||saved.Shapes is not null||saved.SampleTicks is not null||saved.SampleValues is not null||saved.Values.LongLength>(long)saved.Turns.Length*saved.Properties.Length)throw new CheckpointException("InvalidObservationArchive");
            translated=new int[saved.Values.Length];var distinct=new HashSet<(int,string)>();
            for(int i=0;i<saved.Values.Length;i++)
            {
                var field=saved.Values[i];if(field is null||field.Property<0||field.Property>=saved.Properties.Length||field.Json is null||!distinct.Add((field.Property,field.Json)))throw new CheckpointException("InvalidObservationField");
                // Old readers normalised DTO values on restore; preserve that exact interpretation.
                var property=Properties[savedToCurrent[field.Property]];object? value=JsonSerializer.Deserialize(field.Json,property.PropertyType,SimulationJson.Options);
                if(value is null&&property.Name is nameof(ControllerObservation.Map) or nameof(ControllerObservation.Participant) or nameof(ControllerObservation.Account))throw new CheckpointException("MissingObservationField");
                translated[i]=pool._graph.AddJson(JsonSerializer.Serialize(value,property.PropertyType,SimulationJson.Options));
            }
        }
        else
        {
            if(saved.Values is not null||saved.Nodes is null||saved.Shapes is null)throw new CheckpointException("InvalidObservationGraph");
            pool._graph=ObservationGraph.Restore(saved.Nodes,saved.Shapes);
        }
        var identities=new List<ControllerTurn>();var used=new HashSet<int>();int tickIndex=Array.FindIndex(Properties,p=>p.Name==nameof(ControllerObservation.Tick));int actorIndex=Array.FindIndex(Properties,p=>p.Name==nameof(ControllerObservation.Participant));
        var present=savedToCurrent.ToHashSet();
        foreach(var item in saved.Turns)
        {
            if(item?.Turn is null||item.Turn.Observation is not null||item.Fields is null||item.Fields.Length!=saved.Properties.Length)throw new CheckpointException("InvalidArchivedTurn");
            int[] fields=new int[Properties.Length];int absent=-1;
            for(int s=0;s<saved.Properties.Length;s++)
            {
                int id=item.Fields[s];int current=savedToCurrent[s];
                if(translated is not null)
                {if(id<0||id>=translated.Length||saved.Values![id].Property!=s)throw new CheckpointException("InvalidObservationReference");used.Add(id);id=translated[id];}
                var property=Properties[current];pool._graph.Validate(id,property.PropertyType);fields[current]=id;
                if(property.Name is nameof(ControllerObservation.Map) or nameof(ControllerObservation.Participant) or nameof(ControllerObservation.Account)&&pool._graph.Nodes[id].Json=="null")throw new CheckpointException("MissingObservationField");
            }
            for(int p=0;p<Properties.Length;p++)
            {
                if(present.Contains(p))continue;
                absent=absent<0?pool._graph.AddJson("null"):absent;
                pool._graph.Validate(absent,Properties[p].PropertyType);fields[p]=absent;
            }
            var restored=new ArchivedControllerTurn(item.Turn,fields);pool._turns.Add(restored);pool._last[item.Turn.Actor.Value]=item.Turn;
            var identity=JsonSerializer.Deserialize<ControllerObservation>("{}",SimulationJson.Options)!;
            Properties[tickIndex].SetValue(identity,pool.Read(tickIndex,fields[tickIndex]));Properties[actorIndex].SetValue(identity,pool.Read(actorIndex,fields[actorIndex]));identities.Add(item.Turn with{Observation=identity});
        }
        if(translated is not null&&used.Count!=translated.Length)throw new CheckpointException("UnusedObservationField");
        ControllerSample[]? samples=null;
        if(saved.SampleTicks is not null||saved.SampleValues is not null)
        {
            if(saved.SampleTicks is null||saved.SampleValues is null||saved.SampleTicks.Length!=saved.SampleValues.Length||saved.SampleTicks.LongLength>checked(tick*Math.Max(1,actors)))throw new CheckpointException("InvalidArchivedControllerSamples");
            var values=new Dictionary<int,ControllerSample>();samples=new ControllerSample[saved.SampleValues.Length];
            for(int n=0;n<samples.Length;n++)
            {
                int id=saved.SampleValues[n];pool._graph.Validate(id,typeof(ControllerSample));
                if(!values.TryGetValue(id,out var template)){template=JsonSerializer.Deserialize<ControllerSample>(pool._graph.Bytes(id),SimulationJson.Options)!;if(template.Tick!=0)throw new CheckpointException("InvalidControllerSampleTemplate");values.Add(id,template);}
                samples[n]=template with{Tick=saved.SampleTicks[n]};pool._sampleTicks.Add(saved.SampleTicks[n]);pool._sampleValues.Add(id);
            }
        }
        pool._graph.CheckReachability(pool._turns.SelectMany(t=>t.Fields).Concat(pool._sampleValues));return(pool,identities.ToArray(),samples);
    }
}

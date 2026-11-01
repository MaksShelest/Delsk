using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
namespace Simulation.Hosting;

// Lossless JSON DAG. Arrays form persistent trees of at most32 children: appending
// a price/order does not copy every older prefix into every historical observation.
internal sealed record ObservationNode(int Kind,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Json=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] int Shape=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int[]? Children=null);
internal sealed class ObservationGraph
{
    internal readonly List<ObservationNode> Nodes=[];
    internal readonly List<string[]> Shapes=[];
    private readonly List<int> _depths=[];
    private readonly Dictionary<string,int> _atoms=new(StringComparer.Ordinal),_branches=new(StringComparer.Ordinal),_shapes=new(StringComparer.Ordinal);
    private readonly HashSet<(int,Type)> _validated=[];
    private readonly JsonSerializerOptions _metadata=new(Simulation.Core.SimulationJson.Options){TypeInfoResolver=new DefaultJsonTypeInfoResolver()};
    internal long ScalarCharacters {get;private set;}
    internal int AddJson(string json)
    {using var document=JsonDocument.Parse(json);return Add(document.RootElement);}
    private int Add(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Array)
        {
            var ids=value.EnumerateArray().Select(Add).ToArray();
            if(ids.Length<=32)return Branch(2,0,ids);
            int[] parts=ids.Chunk(32).Select(c=>Branch(2,0,c)).ToArray();
            while(parts.Length>32)parts=parts.Chunk(32).Select(c=>Branch(3,0,c)).ToArray();
            return Branch(3,0,parts);
        }
        if(value.ValueKind==JsonValueKind.Object)
        {
            var properties=value.EnumerateObject().ToArray();string[] names=properties.Select(p=>p.Name).ToArray();
            string key=JsonSerializer.Serialize(names);
            if(!_shapes.TryGetValue(key,out int shape)){shape=Shapes.Count;Shapes.Add(names);_shapes.Add(key,shape);}
            return Branch(1,shape,properties.Select(p=>Add(p.Value)).ToArray());
        }
        string atom=value.GetRawText();
        if(_atoms.TryGetValue(atom,out int known))return known;
        int id=Nodes.Count;Nodes.Add(new(0,atom));_depths.Add(0);_atoms.Add(atom,id);ScalarCharacters+=atom.Length;return id;
    }
    private int Branch(int kind,int shape,int[] children)
    {
        string key=kind+":"+shape+":"+string.Join(',',children);
        if(_branches.TryGetValue(key,out int id))return id;
        int depth=1+children.Select(c=>_depths[c]).DefaultIfEmpty(0).Max();if(depth>128)throw new Simulation.Core.CheckpointException("ObservationGraphDepth");
        id=Nodes.Count;Nodes.Add(new(kind,null,shape,children));_depths.Add(depth);_branches.Add(key,id);return id;
    }
    internal static ObservationGraph Restore(ObservationNode[] nodes,string[][] shapes)
    {
        if(nodes.Length>8_000_000||shapes.Length>100_000)throw new Simulation.Core.CheckpointException("ObservationGraphTooLarge");
        var graph=new ObservationGraph();
        foreach(var names in shapes)
        {
            if(names is null||names.Distinct(StringComparer.Ordinal).Count()!=names.Length||names.Any(n=>n is null))throw new Simulation.Core.CheckpointException("InvalidObservationShape");
            string key=JsonSerializer.Serialize(names);
            if(!graph._shapes.TryAdd(key,graph.Shapes.Count))throw new Simulation.Core.CheckpointException("DuplicateObservationShape");
            graph.Shapes.Add(names);
        }
        foreach(var node in nodes)
        {
            if(node is null||node.Kind is <0 or >3)throw new Simulation.Core.CheckpointException("InvalidObservationNode");
            if(node.Kind==0)
            {
                if(node.Json is null||node.Children is not null||node.Shape!=0)throw new Simulation.Core.CheckpointException("InvalidObservationAtom");
                using var json=JsonDocument.Parse(node.Json);
                if(json.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object||!graph._atoms.TryAdd(node.Json,graph.Nodes.Count))throw new Simulation.Core.CheckpointException("InvalidObservationAtom");
                graph.ScalarCharacters+=node.Json.Length;
            }
            else
            {
                if(node.Json is not null||node.Children is null||node.Children.Any(id=>id<0||id>=graph.Nodes.Count)||node.Kind!=1&&node.Children.Length>32||
                    node.Kind==1&&(node.Shape<0||node.Shape>=shapes.Length||node.Children.Length!=shapes[node.Shape].Length)||node.Kind!=1&&node.Shape!=0||
                    node.Kind==3&&node.Children.Any(id=>graph.Nodes[id].Kind is not(2 or 3)))throw new Simulation.Core.CheckpointException("InvalidObservationBranch");
                string key=node.Kind+":"+node.Shape+":"+string.Join(',',node.Children);
                if(!graph._branches.TryAdd(key,graph.Nodes.Count))throw new Simulation.Core.CheckpointException("DuplicateObservationBranch");
            }
            int depth=node.Kind==0?0:1+node.Children!.Select(c=>graph._depths[c]).DefaultIfEmpty(0).Max();if(depth>128)throw new Simulation.Core.CheckpointException("ObservationGraphDepth");
            graph.Nodes.Add(node);graph._depths.Add(depth);
        }
        return graph;
    }
    internal void Validate(int id,Type type,int depth=0)
    {
        if(id<0||id>=Nodes.Count||depth>128)throw new Simulation.Core.CheckpointException("InvalidObservationReference");
        if(!_validated.Add((id,type)))return;
        var node=Nodes[id];
        if(node.Kind==0){_ = JsonSerializer.Deserialize(node.Json!,type,Simulation.Core.SimulationJson.Options);return;}
        type=Nullable.GetUnderlyingType(type)??type;var metadata=_metadata.GetTypeInfo(type);
        if(node.Kind is 2 or 3)
        {
            if(metadata.Kind!=JsonTypeInfoKind.Enumerable)throw new Simulation.Core.CheckpointException("ObservationCollectionTypeMismatch");
            if(node.Kind==3){foreach(int child in node.Children!)Validate(child,type,depth+1);return;}
            var element=type.IsArray?type.GetElementType()!:type.GetInterfaces().Prepend(type).First(t=>t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(IEnumerable<>)).GetGenericArguments()[0];
            foreach(int child in node.Children!)Validate(child,element,depth+1);return;
        }
        if(metadata.Kind==JsonTypeInfoKind.Dictionary)
        {
            var dictionary=type.GetInterfaces().Prepend(type).First(t=>t.IsGenericType&&(t.GetGenericTypeDefinition()==typeof(IDictionary<,>)||t.GetGenericTypeDefinition()==typeof(IReadOnlyDictionary<,>)));
            var args=dictionary.GetGenericArguments();
            foreach(int child in node.Children!)Validate(child,args[1],depth+1);return;
        }
        if(metadata.Kind!=JsonTypeInfoKind.Object)throw new Simulation.Core.CheckpointException("ObservationObjectTypeMismatch");
        var properties=metadata.Properties.ToDictionary(p=>p.Name,StringComparer.Ordinal);var names=Shapes[node.Shape];
        for(int n=0;n<names.Length;n++)
        {
            if(!properties.TryGetValue(names[n],out var property))throw new Simulation.Core.CheckpointException("UnknownObservationProperty");
            Validate(node.Children![n],property.PropertyType,depth+1);
        }
        if(metadata.Properties.Any(p=>p.IsRequired&&!names.Contains(p.Name)))throw new Simulation.Core.CheckpointException("MissingObservationProperty");
    }
    internal void CheckReachability(IEnumerable<int> roots)
    {
        var used=new HashSet<int>();var pending=new Stack<int>(roots);
        while(pending.TryPop(out int id))
        {if(id<0||id>=Nodes.Count)throw new Simulation.Core.CheckpointException("InvalidObservationReference");if(!used.Add(id))continue;foreach(int child in Nodes[id].Children??[])pending.Push(child);}
        if(used.Count!=Nodes.Count)throw new Simulation.Core.CheckpointException("UnusedObservationNode");
    }
    internal void Write(Utf8JsonWriter writer,int id)
    {
        var node=Nodes[id];
        if(node.Kind==0){writer.WriteRawValue(node.Json!,true);return;}
        if(node.Kind==1)
        {writer.WriteStartObject();for(int n=0;n<node.Children!.Length;n++){writer.WritePropertyName(Shapes[node.Shape][n]);Write(writer,node.Children[n]);}writer.WriteEndObject();return;}
        writer.WriteStartArray();Elements(writer,id);writer.WriteEndArray();
    }
    private void Elements(Utf8JsonWriter writer,int id)
    {var n=Nodes[id];foreach(int child in n.Children!)if(n.Kind==3)Elements(writer,child);else Write(writer,child);}
    internal byte[] Bytes(int id)
    {using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream)){Write(writer,id);}return stream.ToArray();}
}

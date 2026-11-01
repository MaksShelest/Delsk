using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class H2StorageTests
{
    [Fact]public void BoundedHistoryTailPreservesCompleteObservationsAfterLoadWithoutChangingSession()
    {
        var s=Frozen();string before=CheckpointStore.Encode(s);
        var expected=s.ControllerTurns.TakeLast(3).ToArray();
        var copy=CheckpointStore.Decode(before);Restore(copy);string copyBefore=CheckpointStore.Encode(copy);
        Assert.Equal(JsonSerializer.Serialize(expected,SimulationJson.Options),JsonSerializer.Serialize(copy.ControllerTurnsTail(3),SimulationJson.Options));
        Assert.All(copy.ControllerTurnsTail(3),t=>Assert.NotNull(t.Observation));
        Assert.Empty(copy.ControllerTurnsTail(0));Assert.Equal(s.ControllerTurnCount,copy.ControllerTurnsTail(int.MaxValue).Count());
        Assert.Throws<ArgumentOutOfRangeException>(()=>copy.ControllerTurnsTail(-1));
        Assert.Equal(copyBefore,CheckpointStore.Encode(copy));
    }
    internal static GameSession Frozen()
    {
        using var data=typeof(H2StorageTests).Assembly.GetManifestResourceStream("Fixture.h1-session3-g2-smoke.json.gz")!;
        using var gzip=new GZipStream(data,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
        var session=CheckpointStore.Decode(reader.ReadToEnd());Restore(session);return session;
    }
    internal static void Restore(GameSession s)=>s.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
    internal static JsonObject Payload(string save)
    {var envelope=JsonNode.Parse(save)!;using var input=new MemoryStream(Convert.FromBase64String(envelope["payloadJson"]!.GetValue<string>()));using var zip=new GZipStream(input,CompressionMode.Decompress);using var reader=new StreamReader(zip);return JsonNode.Parse(reader.ReadToEnd())!.AsObject();}
    internal static string Wrap(JsonObject payload)
    {
        using var compressed=new MemoryStream();using(var zip=new GZipStream(compressed,CompressionLevel.Fastest,true))zip.Write(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        string data=Convert.ToBase64String(compressed.ToArray());return JsonSerializer.Serialize(new{formatVersion=2,payloadJson=data,sha256=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)))},SimulationJson.Options);
    }
    [Fact]public void ActualH1Session3ConvertsStorageAndKeepsGoldenWorldReplayAndEveryObservation()
    {
        var s=Frozen();Assert.Equal(30,s.Engine.Tick);string before=JsonSerializer.Serialize(s.ControllerTurns,SimulationJson.Options);
        string saved=CheckpointStore.Encode(s);var payload=Payload(saved);Assert.Equal(4,payload["schemaVersion"]!.GetValue<int>());
        Assert.Equal(2,payload["controllers"]!["observationArchive"]!["version"]!.GetValue<int>());
        Assert.Empty(payload["controllers"]!["samples"]!.AsArray());Assert.NotNull(payload["laborArchive"]);
        var copy=CheckpointStore.Decode(saved);Restore(copy);Assert.Equal(before,JsonSerializer.Serialize(copy.ControllerTurns,SimulationJson.Options));
        for(int n=0;n<30;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());}
        Assert.Equal("848deb54a20c56e790a07f1b41af207cd7f4b10ac58a7702a3fb33c8c2b954f6",s.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(s.Engine.Configuration(),s.CommandTape,60).Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(s.ControllerTurns,SimulationJson.Options),JsonSerializer.Serialize(copy.ControllerTurns,SimulationJson.Options));
        using var a=new MemoryStream();using var b=new MemoryStream();s.WriteControllerTurns(a);copy.WriteControllerTurns(b);Assert.Equal(a.ToArray(),b.ToArray());
        Assert.Equal(JsonSerializer.Serialize(s.ControllerSamples,SimulationJson.Options),JsonSerializer.Serialize(copy.ControllerSamples,SimulationJson.Options));
        Assert.Equal(JsonSerializer.Serialize(s.LaborSamples,SimulationJson.Options),JsonSerializer.Serialize(copy.LaborSamples,SimulationJson.Options));
        Assert.Equal(64*1024*1024,CheckpointStore.MaximumBytes);Assert.Equal(512*1024*1024,CheckpointStore.MaximumDecodedBytes);
    }
    [Fact]public void BatchReadEqualsIndividualAllowlistAndDoesNotLeakCacheAcrossFailedBarrier()
    {
        var s=Frozen();var engine=s.Engine;var ids=engine.Configuration().Participants.Select(p=>new ParticipantId(p.Id)).ToArray();string hash=engine.ComputeStateHash();
        var expected=ids.Select(id=>JsonSerializer.Serialize(engine.ObserveController(id,id.Value!=1),SimulationJson.Options)).ToArray();
        var batch=engine.ObserveControllers(ids.Select(id=>(id,id.Value!=1)));
        Assert.Equal(expected,batch.Select(o=>JsonSerializer.Serialize(o,SimulationJson.Options)));Assert.Equal(hash,engine.ComputeStateHash());
        for(int n=0;n<ids.Length;n++)Assert.All(batch[n].Inventories,i=>Assert.Contains(engine.Inspect().Inventories,x=>x.Id==i.Id&&x.OwnerId==ids[n]));
        Assert.Throws<ArgumentException>(()=>engine.ObserveControllers(new[]{(new ParticipantId(999),true)}));
        Assert.Equal(expected[0],JsonSerializer.Serialize(engine.ObserveController(ids[0],false),SimulationJson.Options));Assert.Equal(hash,engine.ComputeStateHash());
        Assert.Equal(1,engine.Configuration().MoveTicksPerCell);Assert.Equal(4,engine.ObserveController(ids[1],true).Map.MoveTicksPerCell);
    }
    [Theory][InlineData("forward-reference")][InlineData("sample-reference")][InlineData("unknown-field")][InlineData("actor")]
    public void CompactArchiveRejectsTamperedTypesReferencesAndIdentity(string fault)
    {
        var payload=Payload(CheckpointStore.Encode(Frozen()));var archive=payload["controllers"]!["observationArchive"]!;var nodes=archive["nodes"]!.AsArray();
        if(fault=="forward-reference")nodes.First(n=>n!["children"] is JsonArray a&&a.Count>0)!["children"]![0]=nodes.Count;
        if(fault=="sample-reference")archive["sampleValues"]![0]=nodes.Count;
        if(fault=="unknown-field")archive["shapes"]!.AsArray().First(n=>n is JsonArray a&&a.Count>0)![0]="foreignPrivateInventory";
        if(fault=="actor")archive["turns"]![0]!["turn"]!["actor"]!["value"]=999;
        Assert.Throws<CheckpointException>(()=>CheckpointStore.Decode(Wrap(payload)));
    }
}

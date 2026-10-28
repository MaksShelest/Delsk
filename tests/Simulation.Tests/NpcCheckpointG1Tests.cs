using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcCheckpointG1Tests
{
    private static GameSession Recording()
    {
        var p=NpcEconomyG1Tests.Profile();var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("large_districts"),p,new(10),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));s.AttachControllers(d.Participants.Skip(1).Select(a=>new AlgorithmicController(AlgorithmicController.WithEconomy(new(a.Id,a.PublicPreferenceItem,Difficulty:NpcDifficulty.Medium),p.Npc),AlgorithmicController.RuntimeSeed(d.Seed,a.Id),d.Participants.Select(a=>a.Id))));s.AdvanceTo(20);return s;
    }
    private static JsonObject Payload(string encoded)
    {
        var envelope=JsonNode.Parse(encoded)!;using var input=new MemoryStream(Convert.FromBase64String((string)envelope["payloadJson"]!));using var gzip=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();gzip.CopyTo(output);return JsonNode.Parse(output.ToArray())!.AsObject();
    }
    private static string Envelope(JsonObject payload)
    {
        using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionLevel.Fastest,true))gzip.Write(Encoding.UTF8.GetBytes(payload.ToJsonString()));string data=Convert.ToBase64String(output.ToArray());return new JsonObject{["formatVersion"]=2,["payloadJson"]=data,["sha256"]=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)))}.ToJsonString();
    }
    [Fact]public void LargeRosterArchiveRetainsEveryHistoricalViewAndContinuesExactly()
    {
        var s=Recording();string turns=JsonSerializer.Serialize(s.ControllerTurns,SimulationJson.Options);string checkpoint=CheckpointStore.Encode(s);var payload=Payload(checkpoint);
        Assert.Equal(3,(int)payload["schemaVersion"]!);Assert.Empty(payload["controllers"]!["turns"]!.AsArray());Assert.NotNull(payload["controllers"]!["observationArchive"]);
        Assert.True(Encoding.UTF8.GetByteCount(payload.ToJsonString())<Encoding.UTF8.GetByteCount(turns),"Shared map/catalog fields must reduce the decoded payload too");
        var copy=CheckpointStore.Decode(checkpoint);copy.RestoreControllers(AlgorithmicController.Restore);Assert.Equal(turns,JsonSerializer.Serialize(copy.ControllerTurns,SimulationJson.Options));
        for(int i=0;i<5;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());}
        Assert.Equal(JsonSerializer.Serialize(s.ControllerTurns,SimulationJson.Options),JsonSerializer.Serialize(copy.ControllerTurns,SimulationJson.Options));
        Assert.Equal(turns,JsonSerializer.Serialize(s.ControllerTurns.Take(JsonSerializer.Deserialize<ControllerTurn[]>(turns,SimulationJson.Options)!.Length),SimulationJson.Options));
    }
    [Theory][InlineData(false)][InlineData(true)]public void InvalidArchiveReferenceOrPropertyLayoutIsRejected(bool layout)
    {
        var payload=Payload(CheckpointStore.Encode(Recording()));var archive=payload["controllers"]!["observationArchive"]!;
        if(layout)archive["properties"]![0]="PrivateWorld";else archive["turns"]![0]!["fields"]![0]=-1;
        Assert.Throws<CheckpointException>(()=>CheckpointStore.Decode(Envelope(payload)));
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class H3ExportTests
{
    [Fact]public void DirectGzipPreservesEveryLegacyExportByteAndFullPrivateHistory()
    {
        var session=H2StorageTests.Frozen();string world=session.Engine.ComputeStateHash(),controllers=session.ComputeControllerStateHash();
        string root=Path.Combine(Path.GetTempPath(),"delsk-h3-export-"+Guid.NewGuid().ToString("N"));
        try
        {
            string raw=Path.Combine(root,"raw"),archived=Path.Combine(root,"archived");
            RunArtifacts.Export(session,session.Engine.Configuration(),raw,"headless","h3-export-equivalence",0);
            RunArtifacts.Export(session,session.Engine.Configuration(),archived,"headless","h3-export-equivalence",0,compressControllerTurns:true);
            foreach(string file in Directory.GetFiles(raw))
            {
                string name=Path.GetFileName(file);byte[] actual;
                if(name=="decisions.jsonl")
                {using var source=File.OpenRead(Path.Combine(archived,name+".gz"));using var zip=new GZipStream(source,CompressionMode.Decompress);using var output=new MemoryStream();zip.CopyTo(output);actual=output.ToArray();}
                else actual=File.ReadAllBytes(Path.Combine(archived,name));
                Assert.Equal(File.ReadAllBytes(file),actual);
            }
            byte[] history=File.ReadAllBytes(Path.Combine(raw,"decisions.jsonl"));
            var metadata=JsonNode.Parse(File.ReadAllText(Path.Combine(archived,"decisions-storage.json")))!;
            Assert.Equal(history.LongLength,metadata["uncompressedBytes"]!.GetValue<long>());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(history)),metadata["sha256"]!.GetValue<string>());
            Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(archived,"export-complete.json")))!["complete"]!.GetValue<bool>());
            Assert.False(File.Exists(Path.Combine(archived,"decisions.jsonl")));
            var prior=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            Assert.Throws<ArgumentException>(()=>RunArtifacts.Export(session,session.Engine.Configuration(),raw,"headless","reused-raw",0,compressControllerTurns:true));
            Assert.Throws<ArgumentException>(()=>RunArtifacts.Export(session,session.Engine.Configuration(),archived,"headless","reused-archive",0));
            Assert.Equal(prior.Count,Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length);
            foreach(var file in prior)Assert.Equal(file.Value,File.ReadAllBytes(file.Key));
            Assert.Equal(world,session.Engine.ComputeStateHash());Assert.Equal(controllers,session.ComputeControllerStateHash());
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

using System.Reflection;
using System.Text.Json.Nodes;
using Simulation.Core;
using Xunit;

namespace Simulation.Tests;

public class ReleaseBoundaryTests
{
#if !DELSK_DEVELOPER
    [Fact]
    public void OrdinaryCoreAndHostingDoNotContainInterventionEntryPoints()
    {
        Assert.False(SimulationEngine.IsDeveloperBuild);
        Assert.DoesNotContain(typeof(SimulationEngine).GetMethods(BindingFlags.Public|BindingFlags.Instance),m=>m.Name=="DeveloperModify");
        Assert.DoesNotContain(typeof(Simulation.Hosting.GameSession).GetMethods(BindingFlags.Public|BindingFlags.Instance),m=>m.Name=="DeveloperModify");
        Assert.DoesNotContain(typeof(Simulation.Hosting.GameSession).Assembly.GetReferencedAssemblies(),a=>a.Name=="Simulation.Developer");
    }
    [Fact]
    public void OrdinaryCoreRejectsDeveloperPackagesEvenWithNoEdits()
    {
        var s=new SimulationEngine(StoppedElectionTests.Definition());var json=JsonNode.Parse(s.CreateCheckpointJson())!;json["developerPackage"]=true;
        Assert.Equal("DeveloperSessionUnsupported",Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString())).ReasonCode);
    }
#endif
}

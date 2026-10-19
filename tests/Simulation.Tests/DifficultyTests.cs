using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class DifficultyTests
{
    [Theory] [InlineData(NpcDifficulty.Easy)][InlineData(NpcDifficulty.Medium)][InlineData(NpcDifficulty.Hard)]
    public void PersistedModulesAndPlansRestoreAtEveryDifficulty(NpcDifficulty difficulty)
    {
        var d=ProductionRecipeTests.LandScenario();var s=Fixtures.Session(d);
        var policy=new AlgorithmicController(new(1,"wood",Difficulty:difficulty,ExpansionWeight:0,TradeEnabled:false,PoliticsEnabled:false,LogisticsEnabled:false),42,[1,2]);
        Assert.NotNull(policy.Profile);Assert.Equal(difficulty,policy.Profile.Difficulty);Assert.Equal(1,policy.Profile.ExperienceSchemaVersion);
        Assert.Equal(difficulty>=NpcDifficulty.Medium,policy.Profile.Modules.Any(m=>m.Id=="economics"));
        Assert.Equal(difficulty==NpcDifficulty.Hard,policy.Profile.Modules.Any(m=>m.Id=="planning"));
        s.AttachControllers([policy]);s.AdvanceTo(40);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));loaded.RestoreControllers(AlgorithmicController.Restore);
        while(s.Engine.Tick<150){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());}
        Assert.Equal(s.CommandTape,loaded.CommandTape);
    }
    [Fact]
    public void UnknownModuleVersionsCannotSilentlyChangeSavedPolicy()
    {
        var policy=new AlgorithmicController(new(1,"wood",Difficulty:NpcDifficulty.Hard),42,[1,2]);var saved=policy.Capture();
        var state=JsonNode.Parse(saved.StateJson)!;state["profile"]!["modules"]![0]!["version"]=99;
        Assert.Equal("UnknownPolicyModuleVersion",Assert.Throws<CheckpointException>(()=>AlgorithmicController.Restore(saved with {StateJson=state.ToJsonString()})).ReasonCode);
        Assert.Throws<ArgumentException>(()=>new AlgorithmicController(new(1,"wood",Difficulty:(NpcDifficulty)999),42,[1,2]));
    }
    [Fact]
    public void CompatibilityModeRetainsCheckpoint2AndCannotBecomeModularByChangingItsLabel()
    {
        var legacy=new AlgorithmicController(new(1,"wood"),42,[1,2]);Assert.Null(legacy.Profile);Assert.Equal(2,legacy.Capture().Version);
        Assert.Equal(AlgorithmicController.PolicyKind,legacy.Capture().Kind);
        Assert.Throws<CheckpointException>(()=>AlgorithmicController.Restore(legacy.Capture() with {Kind=AlgorithmicController.ModularPolicyKind,Version=3}));
    }
    [Fact]
    public void EnabledBlocksProduceAnExplainableDifferentRecipeDecision()
    {
        var d=ProductionRecipeTests.LandScenario();d=d with {Buildings=d.Buildings!.Select(b=>b.Id==1?b with {Level=2}:b).ToArray()};var s=Fixtures.Session(d);var o=s.Engine.ObserveController(new(1));
        ControllerDecision Decide(NpcDifficulty difficulty)=>new AlgorithmicController(new(1,"wood",Difficulty:difficulty,ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false),42,[1,2]).Decide(o,[]);
        Assert.DoesNotContain(Decide(NpcDifficulty.Easy).Actions,a=>a.Kind==ActionKind.SetRecipe);
        Assert.Contains(Decide(NpcDifficulty.Medium).Actions,a=>a.Kind==ActionKind.SetRecipe);
        Assert.StartsWith("Hard:",Decide(NpcDifficulty.Hard).Reason);
    }

}

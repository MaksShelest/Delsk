using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class EconomyProfileTests
{
    internal static string Text(string resource)
    {
        using var input = typeof(EconomyProfileTests).Assembly.GetManifestResourceStream("Fixture." + resource + ".json")!;
        using var reader = new StreamReader(input); return reader.ReadToEnd();
    }
    internal static ScenarioDefinition Map() => JsonSerializer.Deserialize<ScenarioDefinition>(Text("baseline-economy"),SimulationJson.Options)!;
    internal static EconomyProfile Profile() => EconomyProfile.Read(Text("economy"));

    [Fact]
    public void OneProfileChangesBothMapsWithoutMovingResourceOrCapitalCells()
    {
        var profile = Profile(); profile = profile with { World = profile.World with { StartingCashMinor = 34567,
            Rules = profile.World.Rules with { PlayerWageStepMinor = 7 }, HarvestTicksPerUnit = 6 } };
        foreach (string name in new[] { "baseline-economy", "presentation-map-two" })
        {
            var map = JsonSerializer.Deserialize<ScenarioDefinition>(Text(name),SimulationJson.Options)!;
            var changed = profile.Apply(map);
            Assert.All(changed.Participants,p=>Assert.Equal(34567,p.CashMinor));
            Assert.Equal(map.ResourceNodes.Select(n=>(n.ItemId,n.Position)),changed.ResourceNodes.Select(n=>(n.ItemId,n.Position))); Assert.Contains(changed.ResourceNodes,n=>n.Rules is {Public:true}); Assert.Equal(map.CapitalDemand!.Terminal,changed.CapitalDemand!.Terminal);
            Assert.Equal(6,changed.HarvestTicksPerUnit); Assert.Equal(7,changed.Economy!.PlayerWageStepMinor);
        }
    }
    [Fact]
    public void ReservationUsesTheConfiguredFeeAndCanBeFree()
    {
        foreach (int basisPoints in new[] { 0, 500, 2000 })
        {
            var map=Map() with { Economy = new(ReservationFeeBasisPoints:basisPoints) };
            var s=Fixtures.Session(map);var plot=map.Plots!.First(p=>!p.IsCorridor && !p.IsShopSite);
            LandContentFixtures.Move(s,plot.Position);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
            Assert.Equal("Accepted",LandContentFixtures.Reason(s,PlayerDesk.ReservePlot(plot.Id)));
            long fee=(long)(((Int128)plot.PriceMinor*basisPoints+9999)/10000);
            Assert.Equal(cash-fee,s.Engine.Observe(new(1)).Account.BalanceMinor);
            Assert.Equal(fee,PlayContext.ReservationFee(plot.PriceMinor,map.Economy));
        }
    }
    [Fact]
    public void NpcWageIncrementIsConfigurableWithoutOverwritingIdentityOrStrategy()
    {
        var original = new ControllerSettings(2,"grain");
        var changed=AlgorithmicController.WithEconomy(original,JsonSerializer.SerializeToElement(new {baseWageMinor=12,maximumWageMinor=80,wageRaiseMinor=7}));
        Assert.Equal(7,changed.WageRaiseMinor);Assert.Equal(12,changed.BaseWageMinor);Assert.Equal(original.ActorId,changed.ActorId);Assert.Equal("grain",changed.PreferredInput);
        Assert.Throws<ArgumentException>(()=>AlgorithmicController.WithEconomy(original,JsonSerializer.SerializeToElement(new {actorId=3})));
        Assert.Throws<ArgumentException>(()=>AlgorithmicController.WithEconomy(original,JsonSerializer.SerializeToElement(new {wageRaiseMinor=-1})));
    }
    [Fact]
    public void WageAttractionExponentChangesAllocation()
    {
        LaborCandidate[] candidates=[new(1,10,1),new(2,10,2)];
        var linear=LaborAllocation.Cubic(8,candidates,new(42),1);
        var cubic=LaborAllocation.Cubic(8,candidates,new(42),3);
        Assert.True(cubic[2]>linear[2]);Assert.Equal(8,cubic.Values.Sum());
    }
    [Fact]
    public void ChangedProfileCannotRewriteAStartedSavedRun()
    {
        var p=Profile();var original=Fixtures.Session(p.Apply(Map()));
        original.Step();string save=CheckpointStore.Encode(original);var changed=p with {World=p.World with {StartingCashMinor=1}};
        var fresh=Fixtures.Session(changed.Apply(Map()));var loaded=CheckpointStore.Decode(save);
        Assert.NotEqual(fresh.Engine.ConfigHash,loaded.Engine.ConfigHash);
        Assert.Equal(original.Engine.ConfigHash,loaded.Engine.ConfigHash);
        for(int i=0;i<20;i++){original.Step();loaded.Step();Assert.Equal(original.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("version")]
    public void InvalidProfilesFailExplicitly(string kind)
    {
        var json=JsonNode.Parse(Text("economy"))!;
        if(kind=="unknown")json["world"]!["labro"]=new JsonObject();
        if(kind=="negative")json["world"]!["rules"]!["playerWageStepMinor"]=-1;
        if(kind=="version")json["version"]=99;
        string text=kind=="duplicate"?Text("economy").Replace("\"version\": 1,","\"version\": 1, \"version\": 1,",StringComparison.Ordinal):json.ToJsonString();
        Assert.ThrowsAny<Exception>(()=>EconomyProfile.Read(text));
    }
    [Fact]
    public void ProfileMeasuresRealTicksAndCheckpointWithoutChangingDeterminism()
    {
        var measured=Fixtures.Session();var ordinary=Fixtures.Session();
        var profile=SessionProfiler.Run(measured,10,AlgorithmicController.Restore);
        ordinary.AdvanceTo(10);
        Assert.Equal(ordinary.Engine.ComputeStateHash(),measured.Engine.ComputeStateHash());
        Assert.Equal(10,profile.MeasuredTicks);Assert.True(profile.CheckpointHashMatches);Assert.True(profile.SaveBytes>0);Assert.True(profile.MaximumTickMs>=profile.MedianTickMs);
    }
    [Fact]
    public void CompressedEnvelopeRetainsHistoryAndReadsUncompressedCurrentSchema()
    {
        var s=Fixtures.Session();s.AdvanceTo(40);
        string raw=s.CreateSessionJson();string compressed=CheckpointStore.Encode(s);
        Assert.True(compressed.Length<raw.Length);
        Assert.Equal(2,JsonNode.Parse(compressed)!["formatVersion"]!.GetValue<int>());
        var loaded=CheckpointStore.Decode(compressed);Assert.Equal(s.CommandTape,loaded.CommandTape);Assert.Equal(s.Engine.EventsSince(0),loaded.Engine.EventsSince(0));
        string digest=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        string legacy=JsonSerializer.Serialize(new CheckpointEnvelope(1,raw,digest),SimulationJson.Options);
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(legacy).Engine.ComputeStateHash());
    }
}

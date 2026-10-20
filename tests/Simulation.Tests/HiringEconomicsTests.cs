using System.Collections.Immutable;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class HiringEconomicsTests
{
    private static ControllerObservation Observation()
    {
        var d=ProductionRecipeTests.LandScenario(capacity:80);var s=Fixtures.Session(d);var o=s.Engine.ObserveController(new(1));var e=o.Enterprises.Single();
        return o with {Enterprises=[e with {Enabled=true,Staff=0}],Population=2,Unemployed=2,
            EmploymentGroups=[o.EmploymentGroups[0] with {Headcount=0,WageMinor=10,AttractionWageMinor=10,Ready=true}],
            LaborMarket=[new(new(1),new(1),EmploymentRole.Production,0,2,10),new(new(9),new(2),EmploymentRole.Production,2,2,30)],
            Inventories=o.Inventories.Select(i=>i.Id==e.InputId?i with {Stock=[new("wood",20,0),new("input-b",10,0)],OccupiedVolume=40}:i).ToImmutableArray(),
            Economics=new(new(StepTicks:1,SalaryChangeDelayTicks:2,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0),new(),3)};
    }
    private static ControllerSettings Settings=>new(1,"wood",Difficulty:NpcDifficulty.Medium,BaseWageMinor:10,MaximumWageMinor:50,ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false);
    private static HiringEstimate Estimate(ControllerObservation o,long wage)=>HiringEconomics.Evaluate(o,o.Enterprises.Single(),o.EmploymentGroups.Single(),wage,Settings);
    [Fact]
    public void ProfitableHiringPaysTheWholeTeamAndValuesOwnRawMaterials()
    {
        var o=Observation();var old=Estimate(o,10);var next=Estimate(o,31);
        Assert.True(next.ExpectedStaff>old.ExpectedStaff);Assert.True(next.ProfitMinor>old.ProfitMinor);Assert.True(next.InputMinor>0);Assert.True(next.PayrollMinor>0);Assert.True(next.EnergyMinor>0);Assert.True(next.DelayTicks>0);
        Assert.Equal(next.RevenueMinor-next.InputMinor-next.EnergyMinor-next.PayrollMinor-next.TransportMinor,next.ProfitMinor);
        var policy=new AlgorithmicController(Settings,42,[1,2]);var decision=policy.Decide(o,[]);
        Assert.Contains(decision.Actions,a=>a.Kind==ActionKind.MoveTo || a.Kind==ActionKind.SetWage);Assert.Contains("Hiring net=",decision.Reason);
        Assert.True(Estimate(o,50).ProfitMinor<next.ProfitMinor);
    }
    [Theory] [InlineData("raw")][InlineData("output")][InlineData("demand")][InlineData("cash")][InlineData("competitor")]
    public void NeitherStarvationNorAnExpensiveCompetitorJustifiesAWageRace(string condition)
    {
        var o=Observation();var e=o.Enterprises.Single();
        if(condition=="raw")o=o with {Inventories=o.Inventories.Select(i=>i.Id==e.InputId?i with {Stock=[],OccupiedVolume=0}:i).ToImmutableArray()};
        if(condition=="output")o=o with {Inventories=o.Inventories.Select(i=>i.Id==e.OutputId?i with {OccupiedVolume=i.Capacity}:i).ToImmutableArray()};
        if(condition=="demand")o=o with {CapitalOffers=[]};
        if(condition=="cash")o=o with {Account=o.Account with {BalanceMinor=0}};
        if(condition=="competitor")o=o with {LaborMarket=o.LaborMarket.Select(g=>g.OwnerId.Value==2?g with {WageMinor=10000}:g).ToImmutableArray()};
        var policy=new AlgorithmicController(Settings,42,[1,2]);Assert.DoesNotContain(policy.ModelOptions(o),p=>p.Actions.Any(a=>a.Kind==ActionKind.SetWage));
    }
    [Fact]
    public void QuotaAndActualMobilityLimitPaybackAndTransportIsCharged()
    {
        var o=Observation();var profitable=Estimate(o,31);o=o with {CapitalOffers=o.CapitalOffers.Select(a=>a with {RemainingQuantity=1}).ToImmutableArray()};
        var limited=Estimate(o,31);Assert.InRange(limited.Batches,0,1);Assert.True(limited.ProfitMinor<profitable.ProfitMinor);
        o=o with {Economics=new(new(StepTicks:10,SalaryChangeDelayTicks:100,MobilityNumerator:1,MobilityDenominator:10),new(),3)};
        Assert.Equal(0,Estimate(o,31).Batches);
        o=Observation();o=o with {CapitalOffers=[..o.CapitalOffers,new(2,"wood",10,500,new(4,0),null,10)]};Assert.True(Estimate(o,31).TransportMinor>0);
    }
    [Fact]
    public void PendingWageAndCooldownDoNotResubmitAndSaveRestoresTheEconomicPlan()
    {
        var o=Observation();var controller=new AlgorithmicController(Settings,42,[1,2]);var first=controller.Decide(o,[]);controller.CommandSubmitted("hire");
        var restored=AlgorithmicController.Restore(controller.Capture());Assert.Equal(controller.Capture().StateJson,restored.Capture().StateJson);
        Assert.Empty(restored.Decide(o,[]).Actions);
        o=o with {EmploymentGroups=[o.EmploymentGroups[0] with {PendingWageMinor=31}]};
        Assert.DoesNotContain(new AlgorithmicController(Settings,42,[1,2]).ModelOptions(o),p=>p.Actions.Any(a=>a.Kind==ActionKind.SetWage));
    }
    [Fact]
    public void PoliticalExposureIsSeparateFromBusinessProfitAndReservedOncePerElection()
    {
        var o=Observation();var election=o.Elections.NextElectionId!.Value;
        var proposal=new SupportProposal(1,new(1),new(2),election,new(1),new(2),[new("wood",2)],[],0,100,SupportProposalStatus.Signed);
        o=o with {Support=o.Support with {Proposals=[proposal],Commitments=[new(1,new(2),new(1),election,new(1),new(2),proposal.Compensation,1,PoliticalCommitmentStatus.Active)]}};
        var controller=new AlgorithmicController(Settings with {PoliticalBudgetMinor=1500},42,[1,2]);
        Assert.Equal(500,controller.PoliticalBudgetRemaining(o)); // signed proposal + commitment are one exposure
        var old=Estimate(o,31);var withoutPolitics=Estimate(o with {Support=o.Support with {Proposals=[],Commitments=[]}},31);
        Assert.Equal(old.ProfitMinor,withoutPolitics.ProfitMinor);
        var restored=AlgorithmicController.Restore(controller.Capture());Assert.Equal(500,restored.PoliticalBudgetRemaining(o));
    }

}

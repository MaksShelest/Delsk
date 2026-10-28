using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcProductionG1Tests
{
    [Fact]public void SecondEnterpriseKeepsBothRealWorkforcesAndProductionRunning()
    {
        var d=ServiceF2Tests.Definition();d=d with{CapitalDemand=d.CapitalDemand! with{PeriodTicks=60,PriceScaleBasisPoints=20000},Buildings=[..d.Buildings!.Where(b=>b.Id!=10).Select(b=>b.Id==9?b with{TypeId="woodworking"}:b)]};var natural=NpcEconomyG1Tests.Profile().World.Resources!.Select(r=>r.ItemId).ToHashSet();d=d with{ResourceNodes=[..d.ResourceNodes.Where(r=>natural.Contains(r.ItemId))],Layout=d.Layout! with{ResourceDistricts=[..d.Layout.ResourceDistricts.Where(r=>natural.Contains(r.Item))]}};var s=new GameSession(new(d));
        foreach(var b in s.Engine.Observe(new(1)).OwnBuildings){StorageC1Tests.Harvest(s,"wood",20);StorageC1Tests.Put(s,b.InputId,"wood",20);}
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Observe(new(1)).OwnEnterprises.All(e=>e.CompletedBatches>0&&e.Staff>0));
        Assert.Equal(2,s.Engine.Observe(new(1)).OwnEnterprises.Length);Assert.Equal(s.Engine.Inspect().Population,s.Engine.Inspect().Unemployed+s.Engine.Inspect().EmploymentGroups.Sum(g=>g.Headcount));
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]public void UnacceptedBuyOfferDoesNotBlockPhysicalSelfSupplyAndHiring()
    {
        var d=ServiceF2Tests.Definition();d=d with{Buildings=[..d.Buildings!,new(11,1,new(30,3),"shop")],Plots=[..d.Plots!.Select(p=>p.Id==90?p with{OwnerId=1}:p)]};var natural=NpcEconomyG1Tests.Profile().World.Resources!.Select(r=>r.ItemId).ToHashSet();
        d=d with{ResourceNodes=[..d.ResourceNodes.Where(r=>natural.Contains(r.ItemId))],Layout=d.Layout! with{ResourceDistricts=[..d.Layout.ResourceDistricts.Where(r=>natural.Contains(r.Item))]}};
        var s=new GameSession(new(d));var e=s.Engine.Observe(new(1)).OwnEnterprises.Single();
        ServiceF2Tests.Step(s,new(ActionKind.CreateBuyOffer,Destination:e.InputId,ItemId:"wood",Quantity:20,UnitPriceMinor:d.Items.Single(i=>i.Id=="wood").BasePriceMinor),1);
        ServiceF2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true),1);Assert.Contains(s.Engine.Observe(new(1)).BuyOffers,b=>b.Status=="Open");
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches>0,900);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.Harvest);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.Transfer&&r.Command.Action.Destination==e.InputId);Assert.True(s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff>0);
    }
    [Fact]public void SmallBatchesReachExchangeAndSellDespiteShortLivedOffers()
    {
        var d=ServiceF2Tests.Definition();d=d with{CapitalDemand=d.CapitalDemand! with{PeriodTicks=20}};var natural=NpcEconomyG1Tests.Profile().World.Resources!.Select(r=>r.ItemId).ToHashSet();
        d=d with{ResourceNodes=[..d.ResourceNodes.Where(r=>natural.Contains(r.ItemId))],Layout=d.Layout! with{ResourceDistricts=[..d.Layout.ResourceDistricts.Where(r=>natural.Contains(r.Item))]}};var s=new GameSession(new(d));var e=s.Engine.Observe(new(1)).OwnEnterprises.Single();StorageC1Tests.Harvest(s,"wood",20);StorageC1Tests.Put(s,e.InputId,"wood",20);
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Inspect().Settlements.Any(r=>r.ItemId=="plank"&&r.PaidMinor>0)&&s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches>=2,1800);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.Transfer&&r.Command.Action.Source==e.OutputId);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SellAvailableToCapital&&r.Command.Action.ItemId=="plank");NpcEconomyG1Tests.SameCheckpoint(c);
    }
    [Fact]public void ZeroWageWorkplaceIsFundedAndHiredThroughTheNormalLocalCommand()
    {
        var s=new GameSession(new(ServiceF2Tests.Definition()));var e=s.Engine.Observe(new(1)).OwnEnterprises.Single();StorageC1Tests.Harvest(s,"wood",20);StorageC1Tests.Put(s,e.InputId,"wood",20);
        ServiceF2Tests.Step(s,new(ActionKind.SetWage,Group:new(e.Id.Value),WageMinor:0),1);s.AdvanceTo(s.Engine.Tick+25);ServiceF2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true),1);
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches>0);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SetWage&&r.Command.Action.WageMinor>0);Assert.True(s.Engine.Inspect().Accounts.Single(a=>a.Kind=="WagesSink").BalanceMinor>0);
    }
    [Fact]public void FiniteConstructionImportsAreBoughtOnlyForAFundedProject()
    {
        var d=ServiceF2Tests.Definition();var s=new GameSession(new(d));
        var c=NpcEconomyG1Tests.Controller(2,"wood");NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.EventsSince(0).Any(e=>e.Type=="ImportPurchased"&&e.ActorId?.Value==2));
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="ImportPurchase");NpcEconomyG1Tests.SameCheckpoint(c);
    }
}

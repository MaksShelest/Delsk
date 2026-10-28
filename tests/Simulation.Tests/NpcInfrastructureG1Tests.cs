using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcInfrastructureG1Tests
{
    [Fact]public void FundedIndustryQuestIsAcceptedAndCompletedByActualGoodsReceipt()
    {
        var d=DevelopmentD2Tests.Definition();d=d with{Delivery=new(ContractTtlTicks:4500),Economy=d.Economy! with{Development=d.Economy.Development! with{PublicProjectOffers=[220]}}};var s=new GameSession(new(d));
        StorageC1Tests.Harvest(s,"plank",10);DevelopmentD2Tests.Step(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:220,Quantity:10));s.AdvanceTo(s.Engine.Tick+6);
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.DevelopmentOf(new(1))!.Quests.Any(q=>q.Status=="Completed"));
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.AcceptIndustryQuest);Assert.Equal(10,s.Engine.Inspect().DeliveryContracts.Single().DeliveredQuantity);Assert.True(DevelopmentD2Tests.Xp(s).QuestXp>0);
        long xp=DevelopmentD2Tests.Xp(s).Total;s.AdvanceTo(s.Engine.Tick+5);Assert.Equal(xp,DevelopmentD2Tests.Xp(s).Total);NpcEconomyG1Tests.SameCheckpoint(c);
    }
    [Fact]public void EarnedExperienceLeadsToPaidLandAndPhysicalEnterpriseUpgrade()
    {
        // Accelerated XP is explicit fixture data; the NPC receives no runtime grants.
        var s=ExpansionD2Tests.Initial();var factory=ExpansionD2Tests.Factory(s);StorageC1Tests.Harvest(s,"wood",3);StorageC1Tests.Put(s,factory.InputId,"wood",3);
        ExpansionD2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:true));s.AdvanceTo(s.Engine.Tick+5);ExpansionD2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:false));
        var next=s.Engine.Configuration().BuildingTypes!.Single(t=>t.Id==factory.TypeId).Levels.Single(l=>l.Level==2);ExpansionD2Tests.Materials(s,next.Materials,factory.InputId);
        long before=s.Engine.Observe(new(1)).Account.BalanceMinor;var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>ExpansionD2Tests.Factory(s).Level==2);
        Assert.True(s.Engine.Observe(new(1)).Account.BalanceMinor<before);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.BuyExpansionLand);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.UpgradeBuilding);
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]public void OwnedPaverExecutesFundedRoadOrderAndReceivesOnlyActualCompletionReward()
    {
        var d=RoadE2Tests.Prepared();var resources=NpcEconomyG1Tests.Profile().World.Resources!.Select(r=>r.ItemId).Concat(new[]{"gravel","asphalt","concrete"}).ToHashSet();
        d=d with{ResourceNodes=[..d.ResourceNodes.Where(r=>resources.Contains(r.ItemId))],Layout=d.Layout! with{ResourceDistricts=[..d.Layout.ResourceDistricts.Where(r=>resources.Contains(r.Item))]}};
        var s=new GameSession(new(d));var paver=RoadE2Tests.Paver(s);var order=s.Engine.InspectRoads()!.Orders.Single();StorageC1Tests.Move(s,order.Cell);
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.InspectRoads()!.Orders.Single(o=>o.Id==order.Id).PaidTick>0);
        Assert.Single(s.Engine.Ledger(),l=>l.Reason=="RoadOrderReward");Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.AcceptRoadOrder);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SetRoadWork&&r.Command.Action.Mode==EquipmentMode.Automatic);
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class DevelopmentD2Tests
{
    internal static ScenarioDefinition Definition(long output=2)
    {
        var d=WorkshopD1Tests.Definition(ticks:1);
        var recipes=d.Recipes.Select(r=>r.Id=="d1-plank"?r with{OutputQuantity=output}:r).ToArray();
        var products=recipes.Select(r=>new IndustryProduct(r.Id,r.OutputItem,r.OutputQuantity,r.OutputItem=="bread"?2:1,1)).ToImmutableArray();
        return d with{Id="development-d2-fixture",Recipes=recipes,BuildingTypes=d.BuildingTypes!.Select(t=>t.Kind==BuildingKind.Production?t with{Levels=t.Levels.Select(l=>l with{OutputCapacity=Math.Max(80,output)}).ToArray()}:t).ToArray(),
            Economy=d.Economy! with{Development=new([new("d1-prepared",products)])},CapitalOffers=[new(220,"plank",1000,100,new(15,3))]};
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)=>DemolitionC2Tests.Step(s,a,actor,accepted);
    internal static IndustryExperience Xp(GameSession s,long actor=1)=>s.Engine.DevelopmentOf(new(actor))!.Experience.Single(e=>e.Industry=="d1-prepared");
    [Theory][InlineData(599,1)][InlineData(600,2)][InlineData(2999,2)][InlineData(3000,3)][InlineData(11999,3)][InlineData(12000,4)][InlineData(39999,4)][InlineData(40000,5)]
    public void TechnologyDependsOnActualExperienceWithoutAnotherProductionCount(long quantity,int level)
    {
        var s=new GameSession(new(Definition(quantity)));WorkshopD1Tests.Supply(s,2,0);Assert.Equal(0,Xp(s).Total);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));
        Assert.Equal(quantity,Xp(s).ProductionXp);Assert.Equal(level,Xp(s).TechnologyLevel);Assert.Equal(1,WorkshopD1Tests.Lines(s).Single().CompletedBatches);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(Definition(quantity),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        var state=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;state["development"]!["grants"]!.AsArray().Add(state["development"]!["grants"]![0]!.DeepClone());Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(state.ToJsonString(SimulationJson.Options)));
    }
    private static DeliveryContractView Contract(GameSession s,long quantity=10,int offer=220)
    {Step(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:offer,Quantity:quantity,OfferWindow:s.Engine.Observe(new(1)).CapitalOffers.Single(o=>o.Id==offer).Window));s.AdvanceTo(s.Engine.Tick+6);return s.Engine.Inspect().DeliveryContracts.Last();}
    private static void Accept(GameSession s,DeliveryContractView contract,int batches=5)=>Step(s,new(ActionKind.AcceptIndustryQuest,ContractId:contract.Id,BuildingTypeId:"d1-prepared",Quantity:batches));
    [Theory][InlineData(5,50)][InlineData(15,100)][InlineData(30,200)][InlineData(60,500)]
    public void QuestUsesFundedRealDemandAndAwardsExactlyOnceAfterPhysicalReceipt(int batches,long reward)
    {
        long quantity=batches*2;var d=Definition();d=d with{Economy=d.Economy! with{Development=d.Economy.Development! with{PublicProjectOffers=[220]}},Delivery=new(ContractTtlTicks:4500)};
        var s=new GameSession(new(d));var contract=Contract(s,quantity);Accept(s,contract,batches);Assert.Equal(0,Xp(s).Total);
        StorageC1Tests.Harvest(s,"plank",quantity);StorageC1Tests.Move(s,contract.Position);Step(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:quantity-1));StorageC1Tests.Idle(s);Assert.Equal(0,Xp(s).Total);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));var action=new GameAction(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:1);Step(s,action);Step(copy,action);StorageC1Tests.Idle(s);StorageC1Tests.Idle(copy);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(reward,Xp(s).QuestXp);Assert.Equal(0,Xp(s).ProductionXp);s.AdvanceTo(s.Engine.Tick+10);Assert.Equal(reward,Xp(s).Total);Step(s,new(ActionKind.AcceptIndustryQuest,ContractId:contract.Id,BuildingTypeId:"d1-prepared",Quantity:batches),accepted:false);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact] public void BoughtGoodsCompleteAQuestWithoutInventingProductionExperience()
    {
        var s=new GameSession(new(Definition()));StorageC1Tests.Harvest(s,"plank",10,2);var shop=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);StorageC1Tests.Put(s,shop.InputId,"plank",10,2);
        Step(s,new(ActionKind.CreateSellOffer,Building:shop.Id,ItemId:"plank",Quantity:10,UnitPriceMinor:100),2);var offer=s.Engine.Inspect().MarketOffers.Single();Step(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:10,MaximumPriceMinor:100));s.AdvanceTo(s.Engine.Tick+6);
        var purchase=s.Engine.Observe(new(1)).OwnMarketAllocations.Single();StorageC1Tests.Move(s,shop.Position);Step(s,new(ActionKind.CollectPurchase,PurchaseId:purchase.Id,Quantity:10));StorageC1Tests.Idle(s);Assert.Equal(0,Xp(s).Total);
        var contract=Contract(s);Accept(s,contract);StorageC1Tests.Move(s,contract.Position);Step(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10));StorageC1Tests.Idle(s);Assert.Equal(50,Xp(s).QuestXp);Assert.Equal(0,Xp(s).ProductionXp);Assert.Equal(0,Xp(s,2).Total);
    }
    [Fact] public void TwoSlotsCancellationExpiryAndPublicationCannotAwardExperience()
    {
        var d=Definition();d=d with{CapitalOffers=[new(220,"plank",10,100,new(15,3)),new(221,"plank",10,100,new(15,3)),new(222,"plank",10,100,new(15,3))],Delivery=new(ContractTtlTicks:100)};var s=new GameSession(new(d));
        var a=Contract(s,offer:220);var b=Contract(s,offer:221);var c=Contract(s,offer:222);Accept(s,a);Accept(s,b);Step(s,new(ActionKind.AcceptIndustryQuest,ContractId:c.Id,BuildingTypeId:"d1-prepared",Quantity:5),accepted:false);Assert.Equal(0,Xp(s).Total);
        Step(s,new(ActionKind.AbandonIndustryQuest,OfferId:s.Engine.DevelopmentOf(new(1))!.Quests.First().Id));Accept(s,c);s.AdvanceTo(c.DeadlineTick+1);Assert.All(s.Engine.DevelopmentOf(new(1))!.Quests,q=>Assert.Contains(q.Status,new[]{"Abandoned","Expired"}));Assert.Equal(0,Xp(s).Total);
        Assert.Null(s.Engine.DevelopmentOf(new(999)));Assert.All(s.Engine.ObserveController(new(2)).Development!.Quests,q=>Assert.Equal(2,q.Owner.Value));Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type.StartsWith("Industry",StringComparison.Ordinal));
    }
    [Fact] public void ProductionAndQuestRewardsStackAndSurviveDemolition()
    {
        var s=new GameSession(new(Definition(10)));var contract=Contract(s,50);Accept(s,contract);WorkshopD1Tests.Supply(s,10,0);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));s.AdvanceTo(s.Engine.Tick+5);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));
        Assert.Equal(50,Xp(s).ProductionXp);var e=WorkshopD1Tests.Enterprise(s);Step(s,new(ActionKind.Transfer,Source:e.OutputId,Destination:StorageC1Tests.Carrier(s),ItemId:"plank",Quantity:50));StorageC1Tests.Idle(s);StorageC1Tests.Move(s,contract.Position);Step(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:50));StorageC1Tests.Idle(s);Assert.Equal(100,Xp(s).Total);
        StorageC1Tests.Move(s,new(0,0));Step(s,new(ActionKind.PrepareDemolition,Building:new(8)));var q=s.Engine.QuoteDemolition(new(1),8)!;Step(s,new(ActionKind.DemolishBuilding,Building:new(8),DemolitionQuote:q.Token));Assert.Equal(100,Xp(s).Total);CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
    [Fact] public void TradingQuestsAloneUnlockTheSecondTechnologyWithoutAnyProduction()
    {
        var s=new GameSession(new(Definition()));StorageC1Tests.Harvest(s,"plank",120);StorageC1Tests.Move(s,new(15,3));
        for(int n=0;n<12;n++)
        {var c=Contract(s);Accept(s,c);Step(s,new(ActionKind.DeliverContract,ContractId:c.Id,Quantity:10));StorageC1Tests.Idle(s);}
        Assert.Equal(0,Xp(s).ProductionXp);Assert.Equal(600,Xp(s).QuestXp);Assert.Equal(2,Xp(s).TechnologyLevel);Assert.Equal(0,WorkshopD1Tests.Lines(s).Single().CompletedBatches);
        Assert.Single(s.Engine.DevelopmentOf(new(1))!.Quests.Where(q=>q.Kind=="FirstDelivery"&&q.Status=="Completed"));CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
}

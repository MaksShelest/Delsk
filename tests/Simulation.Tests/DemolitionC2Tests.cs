using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class DemolitionC2Tests
{
    internal static ScenarioDefinition Definition(bool initial=false)
    {
        var d=StorageC1Tests.Definition();var recipe=new RecipeDefinition("c2-slow","wood",1,"plank",1,10,3,1,0);
        var type=new BuildingTypeDefinition("c2-factory",BuildingKind.Production,[new(0,0),new(1,0)],
            [new(1,recipe.Id,10000,[new("wood",15),new("steel",1)],300,300,3,20),new(2,recipe.Id,25000,[new("wood",15),new("steel",2)],400,400,3,20)]);
        return d with{Id="demolition-c2-fixture",Recipes=[..d.Recipes,recipe],BuildingTypes=[..d.BuildingTypes!,type],Plots=[..d.Plots!,new(6,new(1,0),10000,OwnerId:1)],Buildings=initial?[..d.Buildings!,new(6,1,new(0,0),type.Id,2,2,false)]:d.Buildings};
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true){string id=s.Send(new(actor),a);s.Step();var result=s.Engine.Result(id)!;Assert.True(result.Status==(accepted?ResultStatus.Accepted:ResultStatus.Rejected),a.Kind+": "+result.ReasonCode);}
    internal static long Factory(GameSession s)=>s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="c2-factory").Id.Value;
    internal static GameSession Built(bool demand=false)
    {
        var d=Definition();if(demand)d=d with{CapitalDemand=new(new(15,3),PeriodTicks:100,OrderTtlTicks:100,BootstrapBaseBudgetMinor:1000000)};var s=new GameSession(new(d));StorageC1Tests.Harvest(s,"wood",30);StorageC1Tests.Harvest(s,"steel",3);StorageC1Tests.Move(s,new(0,0));Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"c2-factory"));
        var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="c2-factory");StorageC1Tests.Put(s,b.InputId,"wood",15);StorageC1Tests.Put(s,b.InputId,"steel",2);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id));return s;
    }
    internal static void Demolish(GameSession s,long id)
    {Step(s,new(ActionKind.PrepareDemolition,Building:new(id)));var q=s.Engine.QuoteDemolition(new(1),id)!;Assert.Empty(q.Blockers);Step(s,new(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:q.Token));}
    [Fact]public void BalanceFeeAndActualAggregatedInvestmentsRoundOnce()
    {
        var rules=new DemolitionRules();var balance=MapDesignTests.Balance().Rules;Assert.Equal(balance.DemolitionRecoveryBp,rules.RecoveryBasisPoints);Assert.Equal(balance.DemolitionCellMinor,rules.CellPriceMinor);Assert.Equal(balance.DemolitionExtraCellLevelMinor,rules.ExtraLevelPriceMinor);
        var s=Built();var q=s.Engine.QuoteDemolition(new(1),Factory(s))!;Assert.Equal(9000,q.PriceMinor);Assert.Equal(30,q.Invested.Single(m=>m.ItemId=="wood").Quantity);Assert.Equal(18,q.Recovery.Single(m=>m.ItemId=="wood").Quantity);Assert.Equal(1,q.Recovery.Single(m=>m.ItemId=="steel").Quantity);
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Demolish(s,Factory(s));Assert.Equal(cash-9000,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings,b=>b.TypeId=="c2-factory");Assert.All(s.Engine.Observe(new(1)).Plots.Where(p=>p.Id.Value is 1 or 6),p=>Assert.Equal(new ParticipantId(1),p.OwnerId));
        Assert.Equal(18,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood").ConstructionRecovered);Assert.Equal(1,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").ConstructionRecovered);
    }
    [Fact]public void InitialFreeAssetDoesNotInventPaidMaterialsAndWorkersStayInPopulation()
    {var s=new GameSession(new(Definition(true)));var q=s.Engine.QuoteDemolition(new(1),6)!;Assert.Empty(q.Invested);long pop=s.Engine.Inspect().Population;Demolish(s,6);Assert.Equal(pop,s.Engine.Inspect().Population);Assert.Equal(pop,s.Engine.Inspect().Unemployed);Assert.Equal(0,s.Engine.PublicEconomy().Participants.Single(p=>p.Actor.Value==1).Employed);}
    [Fact]public void StocksArePreservedCompletelyAndExcessUsesC1WithExactReplay()
    {
        var s=Built();long id=Factory(s);var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==id);StorageC1Tests.Harvest(s,"grain",200);StorageC1Tests.Put(s,b.InputId,"grain",200);StorageC1Tests.Harvest(s,"plank",100);StorageC1Tests.Put(s,b.OutputId!.Value,"plank",100);
        Demolish(s,id);var grounds=s.Engine.StorageOf(new(1)).Ground.Where(g=>g.PlotId is 1 or 6).Select(g=>StorageC1Tests.Inventory(s,new(g.InventoryId))).ToArray();Assert.Equal(140,grounds.Sum(i=>i.OccupiedVolume));Assert.Equal(200,grounds.Sum(i=>i.Stock.Where(x=>x.ItemId=="grain").Sum(x=>x.Quantity)+(i.Overflow??[]).Where(l=>l.ItemId=="grain").Sum(l=>l.Quantity)));Assert.Equal(100,grounds.Sum(i=>i.Stock.Where(x=>x.ItemId=="plank").Sum(x=>x.Quantity)+(i.Overflow??[]).Where(l=>l.ItemId=="plank").Sum(l=>l.Quantity)));
        Assert.All(grounds.SelectMany(i=>i.Overflow??[]),l=>Assert.Equal(1200,l.ExpiresTick-l.CreatedTick));var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();restored.Step();Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(Definition(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_C2_EVIDENCE") is {} output){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"demolition-c2-fixture.json"),JsonSerializer.Serialize(Definition(),new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true})+"\n");CheckpointStore.Save(s,Path.Combine(output,"demolished.save.json"));}
    }
    [Fact]public void PreparationAndConfirmationAreSeparateAndStaleQuoteCannotSpend()
    {
        var s=Built();long id=Factory(s);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;var quote=s.Engine.QuoteDemolition(new(1),id)!;
        Step(s,new(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:quote.Token),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,new(ActionKind.PrepareDemolition,Building:new(id)));Step(s,new(ActionKind.CancelDemolitionPreparation,Building:new(id)));Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,new(ActionKind.PrepareDemolition,Building:new(id)));Step(s,new(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:"stale"),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.NotNull(s.Engine.QuoteDemolition(new(1),id));Assert.Null(s.Engine.QuoteDemolition(new(2),id));
    }
    [Fact]public void DuplicateDemolitionCannotReturnMaterialsOrChargeTwice()
    {var s=Built();long id=Factory(s);Step(s,new(ActionKind.PrepareDemolition,Building:new(id)));var q=s.Engine.QuoteDemolition(new(1),id)!;var action=new GameAction(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:q.Token);Step(s,action);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,action,accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Single(s.Engine.DemolitionOf(new(1))!.Receipts);}
    [Fact]public void ExistingBatchFinishesButNextOneCannotStartDuringPreparation()
    {
        var s=new GameSession(new(Definition(true)));var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==6);StorageC1Tests.Harvest(s,"wood",5);StorageC1Tests.Put(s,b.InputId,"wood",5);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(6),Enabled:true));Assert.NotEqual(0,s.Engine.Observe(new(1)).OwnEnterprises.Single().BatchId);
        Step(s,new(ActionKind.PrepareDemolition,Building:new(6)));Assert.Contains("ProductionBatch",s.Engine.QuoteDemolition(new(1),6)!.Blockers);var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+40);restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(1,s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);Assert.Empty(s.Engine.QuoteDemolition(new(1),6)!.Blockers);Demolish(s,6);
    }
    [Theory][InlineData(false)][InlineData(true)]public void StartedConstructionCancellationKeepsPaymentAndReturnsOnlyCommittedMaterials(bool upgrading)
    {
        var s=new GameSession(new(StorageC1Tests.Definition()));var pile=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single().InventoryId);StorageC1Tests.Harvest(s,"plank",10);StorageC1Tests.Put(s,pile,"plank",10);Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"));
        if(upgrading){s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);StorageC1Tests.Harvest(s,"plank",20);StorageC1Tests.Put(s,pile,"plank",20);StorageC1Tests.Harvest(s,"steel",4);StorageC1Tests.Put(s,pile,"steel",4);StorageC1Tests.Harvest(s,"grain",196);StorageC1Tests.Put(s,pile,"grain",176);Step(s,new(ActionKind.UpgradeBuilding,Building:s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).Id));Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:pile,ItemId:"grain",Quantity:20));StorageC1Tests.Idle(s);}
        var job=s.Engine.StorageOf(new(1)).Construction.Single();var q=s.Engine.QuoteDemolition(new(1),job.Id,true)!;long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.CancelConstruction,OfferId:job.Id,DemolitionQuote:q.Token));Assert.Equal(cash-q.PriceMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Empty(s.Engine.StorageOf(new(1)).Construction);Assert.Equal(upgrading?12:6,StorageC1Tests.Inventory(s,pile).Stock.Where(x=>x.ItemId=="plank").Sum(x=>x.Quantity)+(StorageC1Tests.Inventory(s,pile).Overflow??[]).Where(l=>l.ItemId=="plank").Sum(l=>l.Quantity));if(upgrading)foreach(var lot in StorageC1Tests.Inventory(s,pile).Overflow!)Assert.Equal(lot.Quantity,s.Engine.EventsSince(0).Single(e=>e.Type=="OverflowCreated"&&e.EntityId==lot.Id).Quantity);var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletesTick+1);restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        Assert.Equal(upgrading?1:0,s.Engine.Observe(new(1)).OwnBuildings.Count(b=>b.Kind==BuildingKind.Warehouse));
    }
    [Fact]public void IncomingContractMustFinishAndExistingOverflowDeadlineCannotBeExtended()
    {
        var s=StorageC1Tests.Overflowing(incoming:20,delivered:10);long id=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).Id.Value;var input=StorageC1Tests.Warehouse(s,2);var old=StorageC1Tests.Inventory(s,input).Overflow!.First();
        Step(s,new(ActionKind.PrepareDemolition,Building:new(id)),2);var q=s.Engine.QuoteDemolition(new(2),id)!;Assert.Contains("DeliveryCommitment",q.Blockers);Step(s,new(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:q.Token),2,false);
        Step(s,new(ActionKind.DeliverContract,ContractId:s.Engine.Inspect().DeliveryContracts.Single().Id,Quantity:10));StorageC1Tests.Idle(s);q=s.Engine.QuoteDemolition(new(2),id)!;Assert.Empty(q.Blockers);Step(s,new(ActionKind.DemolishBuilding,Building:new(id),DemolitionQuote:q.Token),2);
        Assert.Equal(old.ExpiresTick,StorageC1Tests.Inventory(s,input).Overflow!.Single(l=>l.Id==old.Id).ExpiresTick);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(old.ExpiresTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.True(s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost>0);
    }
    [Fact]public void CustomerGoodsAndHistoricalMarketReferencesSurviveDemolition()
    {
        var s=Built();var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="c2-factory");StorageC1Tests.Harvest(s,"plank",1);StorageC1Tests.Put(s,b.OutputId!.Value,"plank",1);Step(s,new(ActionKind.CreateSellOffer,Building:s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop).Id,Source:b.OutputId.Value,ItemId:"plank",Quantity:1,UnitPriceMinor:10));var offer=s.Engine.Observe(new(1)).MarketOffers.Single();Step(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:1,MaximumPriceMinor:10),2);s.AdvanceTo(s.Engine.Tick+6);var allocation=s.Engine.Inspect().MarketAllocations.Single();
        Step(s,new(ActionKind.PrepareDemolition,Building:b.Id));Assert.Contains("CustomerPickup",s.Engine.QuoteDemolition(new(1),b.Id.Value)!.Blockers);StorageC1Tests.Move(s,b.Position,2);Step(s,new(ActionKind.CollectPurchase,PurchaseId:allocation.Id,Quantity:1),2);StorageC1Tests.Idle(s,2);Demolish(s,b.Id.Value);Assert.Equal(1,StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s,2)).Stock.Single(x=>x.ItemId=="plank").Quantity);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]public void RemovableTruckAndItsValueSurviveWithoutMaterialDuplication()
    {
        using var stream=typeof(DemolitionC2Tests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;var d=JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;d=d with{Economy=(d.Economy??new()) with{Storage=new(70,1200,[600,120])}};var s=new GameSession(new(d));ScriptedScenarios.LogisticsCycle(s,d);var truck=s.Engine.Inspect().Logistics.Single();long equipment=s.Engine.CapitalOf(new(1)).EquipmentMinor;Step(s,new(ActionKind.PrepareDemolition,Building:new(1)));string foreign=s.Send(new(2),new(ActionKind.SetLogisticsEnabled,Logistics:truck.Id,Enabled:true));s.Step();Assert.Equal("NotAvailable",s.Engine.Result(foreign)!.ReasonCode);Demolish(s,1);var kept=s.Engine.Inspect().Logistics.Single();Assert.Equal(truck.Id,kept.Id);Assert.Equal(truck.Level,kept.Level);Assert.Equal(equipment,s.Engine.CapitalOf(new(1)).EquipmentMinor);Assert.Empty(s.Engine.DemolitionOf(new(1))!.Receipts.Single().Recovery);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
        Step(s,new(ActionKind.Build,Target:new(2,0),BuildingTypeId:"sawmill"));var replacement=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Production);StorageC1Tests.Harvest(s,"wood",2);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop).InputId;StorageC1Tests.Put(s,source,"wood",2);Step(s,new(ActionKind.SetTransportRoute,Logistics:kept.Id,Source:source,Destination:replacement.InputId,ItemId:"wood",Enabled:true));s.AdvanceTo(s.Engine.Tick+120);Assert.Equal(2,StorageC1Tests.Inventory(s,replacement.InputId).Stock.Single(i=>i.ItemId=="wood").Quantity);Assert.Equal(truck.Id,s.Engine.Inspect().Logistics.Single().Id);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]public void NoMoneyAndRepeatedPreparationDoNotPartiallyMutateStock()
    {
        var d=Definition(true);d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=0}:p).ToArray()};var s=new GameSession(new(d));Step(s,new(ActionKind.PrepareDemolition,Building:new(6)));Step(s,new(ActionKind.PrepareDemolition,Building:new(6)));var q=s.Engine.QuoteDemolition(new(1),6)!;Step(s,new(ActionKind.DemolishBuilding,Building:new(6),DemolitionQuote:q.Token),accepted:false);Assert.NotNull(s.Engine.QuoteDemolition(new(1),6));Assert.Empty(s.Engine.DemolitionOf(new(1))!.Receipts);Assert.Equal(0,s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]public void CurrentC1CheckpointRestoresWithItsExactHashWithoutDemolitionState()
    {
        var s=StorageC1Tests.Overflowing();var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["schemaVersion"]=19;var restored=SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options));Assert.Equal(s.Engine.ComputeStateHash(),restored.ComputeStateHash());
    }

    [Fact]public void LegacyProductionCanBeDemolishedWithoutChangingItsConstructionRules()
    {
        var d=Definition(true);d=d with{Economy=d.Economy! with{Storage=null},BuildingTypes=d.BuildingTypes!.Select(t=>t with{Levels=t.Levels.Select(l=>l with{ConstructionTicks=0}).ToArray()}).ToArray()};var s=new GameSession(new(d));Assert.False(s.Engine.HasGroundStorage);Assert.Empty(s.Engine.StorageOf(new(1)).Ground);Demolish(s,6);Assert.True(s.Engine.HasGroundStorage);Assert.NotNull(PlayerDesk.Read(s.Engine,new(1)).Storage);Assert.Null(s.Engine.Configuration().Economy!.Storage);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }

    [Fact]public void RecoveredMaterialsAreNotCountedAsFreshProductionOrHarvest()
    {var s=Built(true);long id=Factory(s);Demolish(s,id);s.AdvanceTo((s.Engine.Tick/100+1)*100);var flow=s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood");Assert.Equal(30,flow.Harvested);Assert.Equal(0,flow.Produced);Assert.Equal(18,flow.ConstructionRecovered);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());}
    [Fact]public void ARealStockChangeInvalidatesThePreviousConfirmation()
    {var s=Built();var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="c2-factory");StorageC1Tests.Harvest(s,"grain",3);StorageC1Tests.Put(s,b.InputId,"grain",3);Step(s,new(ActionKind.PrepareDemolition,Building:b.Id));var quote=s.Engine.QuoteDemolition(new(1),b.Id.Value)!;Step(s,new(ActionKind.Transfer,Source:b.InputId,Destination:StorageC1Tests.Carrier(s),ItemId:"grain",Quantity:1));StorageC1Tests.Idle(s);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.DemolishBuilding,Building:b.Id,DemolitionQuote:quote.Token),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);}

    [Fact]public void PreparedShopCannotCreateNewDefaultSourcePromises()
    {var s=new GameSession(new(StorageC1Tests.Definition()));var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);StorageC1Tests.Harvest(s,"plank",1);StorageC1Tests.Put(s,shop.InputId,"plank",1);Step(s,new(ActionKind.PrepareDemolition,Building:shop.Id));Step(s,new(ActionKind.CreateSellOffer,Building:shop.Id,ItemId:"plank",Quantity:1,UnitPriceMinor:10),accepted:false);Assert.Empty(s.Engine.Inspect().MarketOffers);}

    [Fact]public void RetiredBuildingStateIsValidatedAndPartOfTheCanonicalHash()
    {var s=Built();long id=Factory(s);Demolish(s,id);var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["buildings"]!.AsArray().Single(b=>b!["id"]!["value"]!.GetValue<long>()==id)!["level"]=1;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));}

}

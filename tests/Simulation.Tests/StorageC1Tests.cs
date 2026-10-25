using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class StorageC1Tests
{
    internal static EconomyProfile Profile()=>VNextStorageProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Definition(bool warehouse=false,int grace=1200)
    {
        var w=Profile().World;
        return new("storage-c1-fixture",42,16,4,[],w.Items,w.Recipes,[new(1,1000000,new(0,0),2000),new(2,1000000,new(0,2),2000)],[],[],
            new[]{"wood","grain","plank","steel","beam","concrete"}.Select((item,n)=>new ResourceNodeDefinition(item,new(n+3,0))).ToArray(),
            MaxTicks:10000,Population:4,Plots:[new(1,new(0,0),10000,OwnerId:1),new(2,new(0,2),10000,OwnerId:2),new(3,new(1,2),10000,OwnerId:1,ShopParticipantId:1),new(4,new(2,2),10000,OwnerId:2,ShopParticipantId:2),new(5,new(1,3),10000)],
            BuildingTypes:w.BuildingTypes,Buildings:warehouse?[new(1,1,new(0,0),"warehouse"),new(2,2,new(0,2),"warehouse"),new(3,1,new(1,2),"shop"),new(4,2,new(2,2),"shop")]:[new(2,2,new(0,2),"warehouse"),new(3,1,new(1,2),"shop"),new(4,2,new(2,2),"shop")],
            Economy:w.Rules with{Storage=new(70,grace,grace==1200?[600,120]:[3,1])},Labor:new(GrowthPerPeriod:0),Delivery:new(ContractTtlTicks:4000));
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)
    {string id=s.Send(new(actor),a);s.Step();Assert.Equal(accepted?ResultStatus.Accepted:ResultStatus.Rejected,s.Engine.Result(id)!.Status);}
    internal static void Idle(GameSession s,long actor=1){int bound=5000;while(s.Engine.Observe(new(actor)).Participant.Busy&&bound-->0)s.Step();Assert.True(bound>0);}
    internal static void Move(GameSession s,Cell c,long actor=1){Step(s,new(ActionKind.MoveTo,Target:c),actor);Idle(s,actor);}
    internal static InventoryId Carrier(GameSession s,long actor=1)=>s.Engine.Observe(new(actor)).Participant.InventoryId;
    internal static InventoryId Warehouse(GameSession s,long actor=1)=>s.Engine.Observe(new(actor)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;
    internal static void Harvest(GameSession s,string item,long q,long actor=1)
    {var target=s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId==item).Position;Move(s,target,actor);Step(s,new(ActionKind.Harvest,ItemId:item,Quantity:q),actor);Idle(s,actor);}
    internal static void Put(GameSession s,InventoryId target,string item,long q,long actor=1)
    {Move(s,s.Engine.Inspect().Inventories.Single(i=>i.Id==target).Position,actor);Step(s,new(ActionKind.Transfer,Source:Carrier(s,actor),Destination:target,ItemId:item,Quantity:q),actor);Idle(s,actor);}
    internal static InventoryView Inventory(GameSession s,InventoryId id)=>s.Engine.Inspect().Inventories.Single(i=>i.Id==id);
    [Fact]public void ProfileMatchesSourceAndDoesNotAlterA2()
    {var p=Profile();Assert.Equal(new long[]{200,300,400},p.World.BuildingTypes.Single(t=>t.Id=="warehouse").Levels.Select(l=>l.InputCapacity));Assert.Equal(new long[]{60,120,180},p.World.BuildingTypes.Single(t=>t.Id=="warehouse").Levels.Select(l=>l.ConstructionTicks));Assert.NotNull(EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).World.Rules.Storage);Assert.Null(MapDesignTests.Economy().World.Rules.Storage);}
    [Fact]public void MixedManualPileUsesTotalVolumeAndIsNeitherMarketNorAutomation()
    {
        var s=new GameSession(new(Definition()));var pile=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single().InventoryId);
        Harvest(s,"wood",30);Put(s,pile,"wood",30);Harvest(s,"grain",10);Put(s,pile,"grain",10);Assert.Equal(70,Inventory(s,pile).OccupiedVolume);
        Harvest(s,"grain",1);Move(s,new(0,0));Step(s,new(ActionKind.Transfer,Source:Carrier(s),Destination:pile,ItemId:"grain",Quantity:1),accepted:false);
        Assert.Null(Inventory(s,pile).Overflow);Step(s,new(ActionKind.CreateBuyOffer,Destination:pile,ItemId:"wood",Quantity:1,UnitPriceMinor:100),accepted:false);
        Step(s,new(ActionKind.CreateSellOffer,Source:pile,ItemId:"wood",Quantity:1,UnitPriceMinor:100),accepted:false);
        Step(s,new(ActionKind.Transfer,Source:pile,Destination:Carrier(s,2),ItemId:"wood",Quantity:1),actor:2,accepted:false);
        Step(s,new(ActionKind.Transfer,Source:pile,Destination:Carrier(s),ItemId:"wood",Quantity:1));Idle(s);Assert.Equal(68,Inventory(s,pile).OccupiedVolume);
    }
    [Fact]public void WarehouseBuildAndBothUpgradesPayRealMaterialsAndWaitExactTicks()
    {
        var d=Definition();var s=new GameSession(new(d));var pile=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single().InventoryId);Harvest(s,"plank",10);Put(s,pile,"plank",10);
        Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"));var job=s.Engine.StorageOf(new(1)).Construction.Single();Assert.Equal(60,job.CompletesTick-job.StartedTick);Assert.Equal(990000,s.Engine.Observe(new(1)).Account.BalanceMinor);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletesTick-1);Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings,b=>b.Kind==BuildingKind.Warehouse);s.Step();copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(pile,Warehouse(s));Assert.Equal(200,Inventory(s,pile).Capacity);
        foreach(var next in d.BuildingTypes!.Single(t=>t.Id=="warehouse").Levels.Skip(1))
        {
            foreach(var m in next.Materials){Harvest(s,m.ItemId,m.Quantity);Put(s,pile,m.ItemId,m.Quantity);}
            var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id));var work=s.Engine.StorageOf(new(1)).Construction.Single();long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
            Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
            var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(work.CompletesTick-1);Assert.Equal(next.InputCapacity-100,Inventory(s,pile).Capacity);s.Step();restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(next.InputCapacity,Inventory(s,pile).Capacity);
        }
        Assert.Equal(905000,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    internal static GameSession Overflowing(int grace=1200,long incoming=10,long delivered=0,bool demand=false,long? handling=null,long? unitVolume=null,Func<ScenarioDefinition,ScenarioDefinition>? customize=null)
    {
        var d=Definition(true,grace) with{Items=Definition().Items.Select(i=>i.Id=="plank"&&unitVolume is {} v?i with{UnitVolume=v}:i).ToArray(),CapitalDemand=demand?new(PeriodTicks:100,OrderTtlTicks:100,Terminal:new(15,3),BootstrapBaseBudgetMinor:1000000):null,Economy=Profile().World.Rules with{Storage=new(70,grace,grace==1200?[600,120]:[3,1]),Handling=handling is {} rate?new(rate,rate):null}};if(customize is not null)d=customize(d);var s=new GameSession(new(d));var target=Warehouse(s,2);Harvest(s,"grain",200,2);Put(s,target,"grain",200,2);Harvest(s,"plank",incoming);
        Step(s,new(ActionKind.CreateBuyOffer,Destination:target,ItemId:"plank",Quantity:incoming,UnitPriceMinor:10),2);var offer=s.Engine.Observe(new(2)).BuyOffers.Single();Step(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer.Id,Quantity:incoming));s.AdvanceTo(s.Engine.Tick+6);var contract=s.Engine.Inspect().DeliveryContracts.Single();Assert.True(contract.CapacityForecast);Move(s,new(0,2));Step(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:delivered==0?incoming:delivered));Idle(s);
        return s;
    }
    [Fact]public void AcceptedDeliveryUnloadsAndPaysWithoutExceedingNormalCapacity()
    {
        var s=Overflowing();var target=Warehouse(s,2);var i=Inventory(s,target);Assert.Equal(200,i.OccupiedVolume);Assert.Equal(10,i.Overflow!.Sum(l=>l.Quantity));Assert.Equal("Fulfilled",s.Engine.Inspect().DeliveryContracts.Single().Status);
        Assert.Equal(100,s.Engine.Inspect().DeliveryContracts.Single().PaidMinor);Assert.All(i.Overflow!,l=>Assert.Equal(1200,l.ExpiresTick-l.CreatedTick));
        Step(s,new(ActionKind.CreateBuyOffer,Destination:target,ItemId:"plank",Quantity:1,UnitPriceMinor:10),2,false);
        Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type.StartsWith("Overflow",StringComparison.Ordinal));Assert.NotEmpty(s.Engine.StorageEventsOf(new(2),0));Assert.Empty(s.Engine.StorageEventsOf(new(1),0));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+3);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
    }
    [Fact]public void FreeingNormalRoomPromotesOldestRiskWithoutLoss()
    {
        var s=Overflowing();var target=Warehouse(s,2);Move(s,new(0,2),2);Step(s,new(ActionKind.Transfer,Source:target,Destination:Carrier(s,2),ItemId:"grain",Quantity:10),2);Idle(s,2);
        Assert.True((Inventory(s,target).Overflow??[]).Length==0);Assert.Equal(0,s.Engine.Inspect().Flows.Sum(f=>f.Lost));Assert.Equal(200,Inventory(s,target).OccupiedVolume);
    }
    [Fact]public void ManualPartialRecoveryAndExpiryDestroyOnlyRemainingOverflow()
    {
        var s=Overflowing(incoming:10);var target=Warehouse(s,2);var first=Inventory(s,target).Overflow!.First();Move(s,new(0,2),2);Step(s,new(ActionKind.TakeOverflow,Source:target,OfferId:first.Id,ItemId:first.ItemId,Quantity:3),2);Idle(s,2);
        Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type=="TransferStarted"&&e.ReasonCode=="Overflow");
        long expiration=Inventory(s,target).Overflow!.Max(l=>l.ExpiresTick);s.AdvanceTo(expiration-1);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(7,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost);Assert.Equal(200,Inventory(s,target).OccupiedVolume);Assert.Null(Inventory(s,target).Overflow);
        Assert.Equal(3,Inventory(s,Carrier(s,2)).Stock.Single(x=>x.ItemId=="plank").Quantity);Assert.Contains(s.Engine.StorageEventsOf(new(2),0),e=>e.Type=="OverflowWarning"&&e.ReasonCode=="600");Assert.Contains(s.Engine.StorageEventsOf(new(2),0),e=>e.Type=="OverflowWarning"&&e.ReasonCode=="120");
    }
    [Fact]public void PausedSessionDoesNotAgeOverflow()
    {var s=Overflowing();var hash=s.Engine.ComputeStateHash();long tick=s.Engine.Tick;s.SetPaused(true);for(int i=0;i<1300;i++)s.Step();Assert.Equal(tick,s.Engine.Tick);Assert.Equal(hash,s.Engine.ComputeStateHash());}
    [Fact]public void NewArrivalCannotExtendAnOlderLotAndCannotBeRepledged()
    {
        var s=Overflowing(incoming:20,delivered:10);var target=Warehouse(s,2);long oldExpiry=Inventory(s,target).Overflow!.Max(l=>l.ExpiresTick);
        Step(s,new(ActionKind.CreateSellOffer,Source:target,ItemId:"plank",Quantity:1,UnitPriceMinor:100),2,false);
        s.AdvanceTo(s.Engine.Tick+10);var contract=s.Engine.Inspect().DeliveryContracts.Single();Step(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10));Idle(s);
        Assert.Contains(Inventory(s,target).Overflow!,l=>l.ExpiresTick>oldExpiry);s.AdvanceTo(oldExpiry);
        Assert.Equal(10,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost);Assert.Equal(10,Inventory(s,target).Overflow!.Sum(l=>l.Quantity));
    }
    [Fact]public void ProductionStillStopsAtNormalCapacity()
    {
        var d=Fixtures.Cycle(outputCapacity:1) with{Economy=new(Storage:new(70,1200,[600,120]))};var s=new GameSession(new(d));Fixtures.Supply(s,4);Fixtures.Command(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));s.AdvanceTo(s.Engine.Tick+10);
        Assert.Equal(1,s.Engine.Inspect().Enterprises.Single().CompletedBatches);Assert.Equal("NoOutputSpace",s.Engine.Inspect().Enterprises.Single().ReasonCode);Assert.All(s.Engine.Inspect().Inventories,i=>Assert.Null(i.Overflow));
    }
    [Fact]public void FailedConstructionDoesNotSpendOrCreateWork()
    {
        var s=new GameSession(new(Definition()));long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Empty(s.Engine.StorageOf(new(1)).Construction);
    }
    [Fact]public void InvalidOverflowAndConstructionSnapshotsAreRejected()
    {
        var s=Overflowing();var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;var inv=root["inventories"]!.AsArray().First(i=>i!["overflow"] is not null)!;
        inv["overflow"]![0]!["expiresTick"]=s.Engine.Tick+1;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));
        root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["schemaVersion"]=18;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));
        var legacy=new GameSession(new(Fixtures.Cycle()));root=JsonNode.Parse(legacy.Engine.CreateCheckpointJson())!;root["schemaVersion"]=18;Assert.Equal(legacy.Engine.ComputeStateHash(),SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)).ComputeStateHash());
    }
    [Fact]public void CompletePreparedScenarioHasExactReplayAndOptionalExport()
    {
        var d=Definition();var s=new GameSession(new(d));ScriptedScenarios.StorageC1(s,d);Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_C1_EVIDENCE") is {} output){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"storage-c1-fixture.json"),JsonSerializer.Serialize(d,new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true})+"\n");}
    }

    [Fact]public void IncomingForecastProtectsNormalCapacityAndStockReservations()
    {
        var store=new EconomyStore(new Dictionary<string,ItemDefinition>{["wood"]=new("wood",2,500)});
        var setup=store.Begin(0);setup.CreateInventory(1,1,new(0,0),10);setup.AddStock(1,"wood",2);setup.ReserveStock(1,"sale","wood",2);setup.ReserveCapacity(1,"promised","wood",2);setup.Commit();
        var tx=store.Begin(1);var receipt=tx.ReceiveForecast(1,"wood",3,new(1000,1,"wood",3,"delivery:1001",1,1201,[]));tx.Commit();
        Assert.Equal((1L,2L),receipt);Assert.Equal(4,store.Inventories[1].Incoming);Assert.Equal(2,store.Inventories[1].Reserved("wood"));
        tx=store.Begin(2);tx.ReceiveReservedCapacity(1,"promised","wood",2);tx.WithdrawOverflow(1,1000,2);tx.Commit();store.Validate();
        Assert.Equal(5,store.Inventories[1].Count("wood"));Assert.Equal(2,store.Inventories[1].Reserved("wood"));
    }
    [Fact]public void OverflowLossCrossesDemandBoundaryWithoutInflatingOutput()
    {
        var s=Overflowing(demand:true);long expires=Inventory(s,Warehouse(s,2)).Overflow!.Max(l=>l.ExpiresTick);s.AdvanceTo((expires/100+1)*100);
        Assert.Equal(10,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
    }
    [Fact]public void PendingManualRescueSurvivesLoadAndReleasesItsHoldOnExpiry()
    {
        var s=Overflowing(incoming:10,handling:1,unitVolume:4);var target=Warehouse(s,2);Move(s,new(0,2),2);long expiry=Inventory(s,target).Overflow!.First().ExpiresTick;
        s.AdvanceTo(expiry-2);var lot=Inventory(s,target).Overflow!.First();Step(s,new(ActionKind.TakeOverflow,Source:target,OfferId:lot.Id,ItemId:lot.ItemId,Quantity:lot.Quantity),2);
        Assert.NotEmpty(s.Engine.TransfersOf(new(2)));var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(expiry+100);restored.AdvanceTo(s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Empty(s.Engine.TransfersOf(new(2)));Assert.Equal(0,Inventory(s,Carrier(s,2)).ReservedCapacity);
        Assert.Equal(10,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost+Inventory(s,Carrier(s,2)).Stock.Where(i=>i.ItemId=="plank").Sum(i=>i.Quantity));
    }
    [Fact]public void GroundPurchaseAndFailedConstructionPreserveOwnershipAndMoney()
    {
        var s=new GameSession(new(Definition()));Move(s,new(1,3));long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.BuyPlot,Plot:new(5)));
        Assert.Equal(cash-10000,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(70,Inventory(s,new(s.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==5).InventoryId)).Capacity);
        Assert.Empty(s.Engine.StorageOf(new(2)).Ground);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]public void ConstructionInvestmentIsNotLostAndMissingWorkIsRejected()
    {
        var s=new GameSession(new(Definition()));var pile=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single().InventoryId);Harvest(s,"plank",10);Put(s,pile,"plank",10);
        long assets=s.Engine.CapitalOf(new(1)).TotalMinor;Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"));Assert.Equal(assets,s.Engine.CapitalOf(new(1)).TotalMinor);
        var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["storage"]!["construction"]=new JsonArray();Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));
        s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);Assert.Equal(assets,s.Engine.CapitalOf(new(1)).TotalMinor);
    }

    [Fact]public void AcceptedAutomaticFreightUnloadsAndGetsPaidWithFullReceiver()
    {
        using var stream=typeof(StorageC1Tests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
        d=d with{Economy=(d.Economy??new()) with{Storage=new(70,1200,[600,120])},BuildingTypes=d.BuildingTypes!.Select(t=>t.Kind==BuildingKind.Shop?t with{Levels=t.Levels.Select(l=>l with{InputCapacity=5}).ToArray()}:t).ToArray()};
        var s=new GameSession(new(d));ScriptedScenarios.LogisticsCycle(s,d);var c=s.Engine.Inspect().DeliveryContracts.Single();Assert.Equal(10,c.DeliveredQuantity);Assert.Equal(30000,c.PaidMinor);
        var receiver=Inventory(s,c.DestinationInventory!.Value);Assert.Equal(5,receiver.OccupiedVolume);Assert.Equal(5,receiver.Overflow!.Sum(l=>l.Quantity));Assert.Null(s.Engine.Inspect().Logistics.Single().Trip);
        var mid=new GameSession(new(d));mid.ConfigurePlayback(s.CommandTape,s.Engine.Tick,"storage-freight-test");mid.AdvanceTo(69);var restored=CheckpointStore.Decode(CheckpointStore.Encode(mid));mid.AdvanceTo(s.Engine.Tick);restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(mid.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
    }

    [Fact]public void CapacityFreedDuringManualRescuePromotesGoodsAndReleasesBackpackHold()
    {
        // Prepared short upgrade isolates simultaneous normal-room recovery and partial volume work.
        var s=Overflowing(handling:1,unitVolume:4,customize:d=>d with{BuildingTypes=d.BuildingTypes!.Select(t=>t.Kind==BuildingKind.Warehouse?t with{Levels=t.Levels.Select(l=>l.Level==2?l with{Materials=[],PriceMinor=0,ConstructionTicks=2}:l).ToArray()}:t).ToArray()});
        var target=Warehouse(s,2);var building=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);Move(s,new(0,2),2);
        Step(s,new(ActionKind.UpgradeBuilding,Building:building.Id),2);var lot=Inventory(s,target).Overflow!.First();Step(s,new(ActionKind.TakeOverflow,Source:target,OfferId:lot.Id,ItemId:lot.ItemId,Quantity:1),2);
        Assert.NotEmpty(s.Engine.TransfersOf(new(2)));s.Step();Assert.Empty(s.Engine.TransfersOf(new(2)));Assert.Null(Inventory(s,target).Overflow);Assert.Equal(0,Inventory(s,Carrier(s,2)).ReservedCapacity);Assert.Equal(240,Inventory(s,target).OccupiedVolume);
        Assert.Equal(0,s.Engine.Inspect().Flows.Sum(f=>f.Lost));Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }

}

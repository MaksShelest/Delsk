using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class I6C2MapsTests
{
    internal static EconomyProfile Economy(string key)=>key is PlayMaps.CompactValley or PlayMaps.Second?VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(MobileEquipmentI4Tests.Profile())):MobileEquipmentI4Tests.Profile();
    internal static OrdinaryGameResolution Resolve(string key,int npcs=3)
    {
        var profile=Economy(key);
        string file=key==PlayMaps.Second?"woodland_region_i6c4.json":"compact_valley_i6c21.json";
        var template=MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text(file)),profile);
        return OrdinaryNewGame.Resolve(template,profile,new(npcs),vnextBalance:MapDesignTests.Balance());
    }
    internal static GameSession Session(string key,bool funded=false)
    {
        var d=Resolve(key).Scenario;
        if(funded)d=d with{Participants=d.Participants.Select(p=>p with{CashMinor=10000000}).ToArray()};
        return new(new(d));
    }
    private static ParticipantId Human=>new(1);
    private static void Move(GameSession s,Cell at)=>FleetE1Tests.Move(s,at);
    private static void Step(GameSession s,GameAction a,bool accepted=true)=>FleetE1Tests.Step(s,a,accepted);
    [Theory][InlineData(PlayMaps.CompactValley,32,25,15,11)][InlineData(PlayMaps.Second,18,15,6,4)]
    public void NewSnapshotMatchesSourceAndCurrentEconomyWithoutAnAppendedYard(string key,int width,int height,int sites,int capacity)
    {
        var d=Resolve(key).Scenario;var l=d.Layout!;
        var snapshot=JsonSerializer.Deserialize<ScenarioDefinition>(OrdinaryNewGameTests.Text(PlayMaps.NewGameResource(key).Replace("Play.","")),SimulationJson.Options)!;
        var actual=OrdinaryNewGame.Resolve(snapshot,Economy(key),new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        Assert.Equal(JsonSerializer.Serialize(d,SimulationJson.Options),JsonSerializer.Serialize(actual,SimulationJson.Options));
        Assert.Equal((width,height),(d.MapWidth,d.MapHeight));Assert.Equal(sites,l.IndustrialLots.Length);Assert.Equal(capacity,l.MaxParticipants);
        Assert.Equal(34,d.Items.Length);Assert.Equal(69,d.Recipes.Length);Assert.Equal(6,d.BuildingTypes!.Count(t=>t.Kind==BuildingKind.Production));
        Assert.Equal(8,d.ResourceNodes.Select(n=>n.ItemId).Distinct().Count());Assert.Equal(key==PlayMaps.Second?2:16,l.CivicObjects.Single(c=>c.Kind=="dealership").EquipmentParking!.Length);
        if(key==PlayMaps.Second){Assert.Equal(PlayMaps.WoodlandI6C4ScenarioId,d.Id);Assert.Equal(46,l.InitialCityDirtCells.Length);Assert.Equal(2,d.Economy!.Fleet!.EquipmentDealerVersion);}
        Assert.All(d.ResourceNodes.GroupBy(r=>r.ItemId),g=>{Assert.Contains(g,r=>r.Rules!.Public);Assert.Contains(g,r=>!r.Rules!.Public);});
        Assert.All(d.Participants,a=>Assert.Equal(20000,a.CashMinor));Assert.All(d.Plots!,p=>Assert.Null(p.OwnerId));
        Assert.Equal(3,Resolve(key).Controllers.Length);Assert.NotEqual(PlayMaps.CurrentSecondScenarioId,d.Id);
        Assert.Throws<ArgumentException>(()=>Resolve(key,capacity));
        var maximum=Resolve(key,capacity-1).Scenario;Assert.Equal(capacity,maximum.Participants.Length);
        var w=Economy(key).World;
        Assert.Equal(JsonSerializer.Serialize(w.Rules.Fleet),JsonSerializer.Serialize(d.Economy!.Fleet));Assert.Equal(w.PlotPriceMinor,d.Plots!.First(p=>!p.IsShopSite).PriceMinor);
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void FullIndustrialOccupancyKeepsEveryResourceCivicEntryMarketAndSpawnReachable(string key)
    {
        var d=Resolve(key).Scenario;var l=d.Layout!;
        var full=new GridMap(d.MapWidth,d.MapHeight,d.BlockedCells.Concat(l.IndustrialLots.SelectMany(p=>p.Cells)));
        var targets=l.IndustrialLots.Select(p=>p.Entry).Concat(l.MarketStalls.Select(p=>p.Cell))
            .Concat(l.CivicObjects.Select(c=>c.Entry)).Concat(d.ResourceNodes.Select(r=>r.Position)).ToArray();
        foreach(var spawn in l.SpawnCells){var paths=full.DistancesFrom(spawn);Assert.All(targets,t=>Assert.True(paths.ContainsKey(t),$"{key}: {spawn} -> {t}"));}
        var streets=l.InitialCityDirtCells.ToHashSet();
        var roadGrid=new GridMap(d.MapWidth,d.MapHeight,Enumerable.Range(0,d.MapHeight).SelectMany(z=>Enumerable.Range(0,d.MapWidth).Select(x=>new Cell(x,z))).Where(c=>!streets.Contains(c)));
        Assert.Equal(streets.Count,roadGrid.DistancesFrom(streets.First()).Count);
        foreach(var type in d.BuildingTypes!.Where(t=>t.Kind==BuildingKind.Production))
            foreach(var lot in l.IndustrialLots)
            {var anchor=new Cell(lot.Cells.Min(c=>c.X),lot.Cells.Min(c=>c.Z));Assert.All(type.Footprint,c=>Assert.Contains(new Cell(anchor.X+c.X,anchor.Z+c.Z),lot.Cells));}
        var available=d.ResourceNodes.Select(r=>r.ItemId).Concat(d.Economy!.ProductionContent!.Imports.Select(i=>i.ItemId)).ToHashSet();
        bool changed;do{changed=false;foreach(var recipe in d.Recipes)if(recipe.Ingredients.All(i=>available.Contains(i.ItemId)))changed|=available.Add(recipe.OutputItem);}while(changed);
        Assert.All(d.Items,i=>Assert.Contains(i.Id,available));
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void ThreeIndependentOrdinaryNpcsHarvestTradeAndRestoreWithoutPreparedAssets(string key)
    {
        var r=Resolve(key);var d=r.Scenario;var p=Economy(key);
        var config=OrdinaryNewGameTests.Config().ForOrdinary(d,1,r.Controllers);
        var policies=config.Npcs.Select(n=>(IController)new AlgorithmicController(AlgorithmicController.WithEconomy(n,p.Npc),AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(a=>a.Id))).ToArray();
        var s=new GameSession(new(d));s.AttachControllers(policies);s.AdvanceTo(240);
        Assert.Equal(3,s.ControllerStates.Length);
        foreach(var actor in d.Participants.Skip(1))
        {
            Assert.Contains(s.CommandTape,c=>c.AuthenticatedActor.Value==actor.Id&&c.Command.Action.Kind==ActionKind.Harvest);
            Assert.Contains(s.CommandTape,c=>c.AuthenticatedActor.Value==actor.Id&&c.Command.Action.Kind==ActionKind.SellAvailableToCapital);
        }
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.RestoreControllers(AlgorithmicController.Restore);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());s.AdvanceTo(245);copy.AdvanceTo(245);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());s.Engine.ValidateInvariants();
        Evidence(s,key+"-ordinary-npcs");
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void EveryStockedVehicleCanPhysicallyLeaveItsRealDeliveryBay(string key)
    {
        var d=Resolve(key).Scenario;d=d with{Participants=d.Participants.Select(p=>p with{CashMinor=10000000}).ToArray()};
        var exit=d.Layout!.InitialCityDirtCells.OrderBy(c=>Math.Abs(c.X-11)+Math.Abs(c.Z-5)).First(c=>!d.Participants.Any(p=>p.Position==c));
        var shown=new HashSet<string>(StringComparer.Ordinal){"sputnik20","muravey70","eger70","trakt_d1","extractor_extract_wood_1","extractor_extract_grain_1"};
        var classes=new HashSet<string>(StringComparer.Ordinal){"sputnik20","muravey70","extractor_field_1","extractor_forest_1"};
        foreach(var model in d.Economy!.Fleet!.Models.Where(m=>d.Economy.Fleet.EquipmentDealerVersion==2?classes.Contains(m.Id):!d.Layout!.Assignment.Contains("compact-exhibition-1",StringComparison.Ordinal)||shown.Contains(m.Id)))
        {
            var s=new GameSession(new(d));var v=model.EquipmentTypeId is null?FleetE1Tests.Buy(s,model.Id):d.Economy.Fleet.EquipmentDealerVersion==2?EquipmentClassesI6C22Tests.BuyModel(s,model.Id):MobileEquipmentI4Tests.Buy(s,d.EquipmentTypes!.Single(t=>t.Id==model.EquipmentTypeId).ItemId,model.EquipmentLevel);
            // Одна дорога от города до ресурсного ответвления длиннее прежних широких улиц. Прибытие на ту же клетку обязательно.
            FleetE1Tests.Board(s,v);Step(s,new(ActionKind.MoveTo,Target:exit));int bound=800;
            while(s.Engine.Observe(Human).Participant.Busy&&bound-->0)s.Step();
            Assert.True(bound>0,$"{key}: trapped {model.Id} at {StorageC1Tests.Inventory(s,v.CargoInventory).Position}");
            Assert.Equal(exit,StorageC1Tests.Inventory(s,v.CargoInventory).Position);s.Engine.ValidateInvariants();
        }
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void PublicHarvestInitialSaleAndLegalLandAndMachinePurchaseUseRealMap(string key)
    {
        var s=Session(key);var d=s.Engine.Configuration();var node=d.ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public);
        Move(s,node.Position);Step(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:5));StorageC1Tests.Idle(s);
        Assert.Equal(5,FleetE1Tests.Stock(s,StorageC1Tests.Carrier(s),"wood"));Move(s,d.CapitalDemand!.Terminal);
        int wait=80;while(!s.Engine.Observe(Human).CapitalOffers.Any(o=>o.ItemId=="wood"&&o.RemainingQuantity>0&&o.CreatedTick<s.Engine.Tick)&&wait-->0)s.Step();
        Assert.True(wait>0);var offer=s.Engine.Observe(Human).CapitalOffers.First(o=>o.ItemId=="wood"&&o.RemainingQuantity>0&&o.CreatedTick<s.Engine.Tick);
        Step(s,new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:"wood",Quantity:Math.Min(5,offer.RemainingQuantity)));StorageC1Tests.Idle(s);
        Assert.Contains(s.Engine.Ledger(),e=>e.Reason=="CapitalSettlement");
        var plot=d.Plots!.First(p=>!p.IsShopSite);Move(s,plot.Position);Step(s,new(ActionKind.BuyPlot,Plot:new(plot.Id)));
        Assert.Equal(Human,s.Engine.Observe(Human).Plots.Single(p=>p.Id.Value==plot.Id).OwnerId);
        // Earn the difference from the unchanged 200-coin start before buying both land and a machine.
        var machineSession=Session(key);
        machineSession.AttachControllers(d.Participants.Select(p=>(IController)new SelfBallotController(new(p.Id))));
        int earningBudget=80;
        while(machineSession.Engine.Observe(Human).Account.AvailableMinor<34000&&earningBudget-->0)
        {
            long held=FleetE1Tests.Stock(machineSession,StorageC1Tests.Carrier(machineSession),"wood");
            if(held==0){Move(machineSession,node.Position);Step(machineSession,new(ActionKind.Harvest,ItemId:"wood",Quantity:10));StorageC1Tests.Idle(machineSession);held=10;}
            Move(machineSession,d.CapitalDemand.Terminal);
            var order=machineSession.Engine.Observe(Human).CapitalOffers.FirstOrDefault(o=>o.ItemId=="wood"&&o.RemainingQuantity>0&&o.CreatedTick<machineSession.Engine.Tick);
            if(order is null){machineSession.AdvanceTo(machineSession.Engine.Tick+60);continue;}
            Step(machineSession,new(ActionKind.SellAvailableToCapital,OfferId:order.Id,ItemId:"wood",Quantity:Math.Min(held,order.RemainingQuantity)));StorageC1Tests.Idle(machineSession);
        }
        Assert.True(earningBudget>0,"bounded manual income must finance actual resource land and machine prices");
        var resource=d.ResourceNodes.First(r=>r.ItemId=="wood"&&!r.Rules!.Public&&r.Position.X>0&&r.Position.Z>0);
        Move(machineSession,resource.Position);Step(machineSession,new(ActionKind.BuyResource,Target:resource.Position));
        var v=d.Economy!.Fleet!.EquipmentDealerVersion==2?EquipmentClassesI6C22Tests.Buy(machineSession,"wood"):MobileEquipmentI4Tests.Buy(machineSession);FleetE1Tests.Board(machineSession,v);
        Step(machineSession,new(ActionKind.MoveTo,Target:resource.Position));StorageC1Tests.Idle(machineSession);
        Step(machineSession,new(ActionKind.PlaceEquipment,Equipment:new(v.Id)));
        Step(machineSession,new(ActionKind.StartHarvesting,Equipment:new(v.Id),ItemId:"wood",Quantity:2));StorageC1Tests.Idle(machineSession);
        Assert.True(FleetE1Tests.Stock(machineSession,v.CargoInventory,"wood")>=2);Evidence(machineSession,key+"-extraction");
    }
    [Theory][InlineData(PlayMaps.CompactValley,false)][InlineData(PlayMaps.Second,false)][InlineData(PlayMaps.Second,true)]
    public void ConcreteCarPurchaseKeepsCompetitionHoldsDistanceOwnershipAndSinglePayment(string key,bool entrance)
    {
        var initial=Session(key,true).Engine.Configuration();var listing=new SimulationEngine(initial).FleetOf(Human)!.Stock.First(v=>v.ModelId=="sputnik20");
        Cell at=entrance?initial.Layout!.CivicObjects.Single(c=>c.Kind=="dealership").Entry:listing.Position;
        var d=initial with{Participants=initial.Participants.Select(p=>p.Id<=2?p with{Position=at}:p).ToArray()};
        var s=new GameSession(new(d));var action=new GameAction(ActionKind.BuyVehicle,VehicleId:listing.Id);
        Assert.True(s.Engine.AccessFor(Human,action).Allowed);Assert.False(s.Engine.AccessFor(new(3),action).Allowed);
        foreach(string language in new[]{"ru","en"})
        {var choice=ContextActions.Read(s.Engine,Human,d,language).First(c=>c.Command==action);Assert.Contains("Спутник-20",choice.Label);Assert.Contains(PlayerText.Money(d.Economy!.Fleet!.Models.Single(m=>m.Id==listing.ModelId).PriceMinor),choice.Label);}
        Step(s,action);Step(s,action,false);FleetE1Tests.Step(s,action,actor:2);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+6);copy.AdvanceTo(s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Single(s.Engine.InspectFleet()!.Purchases);
        Assert.Single(s.Engine.InspectFleet()!.Vehicles);Assert.Single(s.Engine.Ledger(),l=>l.Reason=="VehiclePurchase");
        Assert.False(s.Engine.AccessFor(Human,action).Allowed);Assert.False(s.Engine.AccessFor(new(2),action).Allowed);
        Assert.DoesNotContain(ContextActions.Read(s.Engine,Human,d,"en"),c=>c.Command==action);
        Step(s,action,false);Assert.Single(s.Engine.Ledger(),l=>l.Reason=="VehiclePurchase");
    }
    [Theory][InlineData(PlayMaps.CompactValley)][InlineData(PlayMaps.Second)]
    public void InitialFactoryOnRealFullLotHiresConsumesProducesAndSells(string key)
    {
        // Prepared factory/cash isolates the map's first production chain; no balance or staffing override.
        var d=Resolve(key).Scenario;var lot=d.Layout!.IndustrialLots.First();
        var anchor=new Cell(lot.Cells.Min(c=>c.X),lot.Cells.Min(c=>c.Z));
        d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=1000000}:p).ToArray(),
            Plots=d.Plots!.Select(p=>lot.Cells.Contains(p.Position)?p with{OwnerId=1}:p).ToArray(),Buildings=[new(8,1,anchor,"woodworking")]};
        var s=new GameSession(new(d));var node=d.ResourceNodes.First(r=>r.ItemId=="wood"&&r.Rules!.Public);
        Move(s,node.Position);Step(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:4));StorageC1Tests.Idle(s);
        var factory=Assert.Single(s.Engine.Observe(Human).OwnBuildings);Move(s,anchor);
        Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:factory.InputId,ItemId:"wood",Quantity:4));StorageC1Tests.Idle(s);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));
        int bound=300;while(s.Engine.Observe(Human).OwnEnterprises.Single().CompletedBatches==0&&bound-->0)s.Step();Assert.True(bound>0);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="Wages");Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="Energy");
        var recipe=d.Recipes.Single(r=>r.Id==s.Engine.Observe(Human).OwnEnterprises.Single().RecipeId);
        Step(s,new(ActionKind.Transfer,Source:factory.OutputId!.Value,Destination:StorageC1Tests.Carrier(s),ItemId:recipe.OutputItem,Quantity:1));StorageC1Tests.Idle(s);
        Move(s,d.CapitalDemand!.Terminal);int wait=120;
        while(!s.Engine.Observe(Human).CapitalOffers.Any(o=>o.ItemId==recipe.OutputItem&&o.RemainingQuantity>0&&o.CreatedTick<s.Engine.Tick)&&wait-->0)s.Step();Assert.True(wait>0);
        var offer=s.Engine.Observe(Human).CapitalOffers.First(o=>o.ItemId==recipe.OutputItem&&o.RemainingQuantity>0&&o.CreatedTick<s.Engine.Tick);
        Step(s,new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:recipe.OutputItem,Quantity:1));StorageC1Tests.Idle(s);
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="CapitalSettlement");
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Evidence(s,key+"-first-production");
    }
    [Fact]public void PreviousWoodlandSnapshotStaysOnI4AndIsNotTheNewStart()
    {
        var i4=MobileEquipmentI4Tests.Profile();
        var design=MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text("woodland_region.json")),i4);
        var fromDesign=OrdinaryNewGame.Resolve(design,i4,new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        var snapshot=JsonSerializer.Deserialize<ScenarioDefinition>(OrdinaryNewGameTests.Text("woodland-region-i6c2.json"),SimulationJson.Options)!;
        var fromSnapshot=OrdinaryNewGame.Resolve(snapshot,i4,new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        Assert.Equal(JsonSerializer.Serialize(fromDesign,SimulationJson.Options),JsonSerializer.Serialize(fromSnapshot,SimulationJson.Options));
        Assert.Equal(PlayMaps.WoodlandScenarioId,fromSnapshot.Id);Assert.Equal(16,fromSnapshot.Layout!.CivicObjects.Single(c=>c.Kind=="dealership").EquipmentParking!.Length);
        Assert.Equal(1,fromSnapshot.Economy!.Fleet!.EquipmentDealerVersion);Assert.NotEqual(PlayMaps.WoodlandI6C4ScenarioId,fromSnapshot.Id);
    }
    [Fact]public void OldValleyAndHistoricalSecondKeepGeometryRoutesIdsAndOwnershipOnLoad()
    {
        foreach(var d in new[]{MobileEquipmentI4Tests.Profile().Apply(MapDesignTests.Template("compact_valley")),OrdinaryNewGameTests.Map(PlayMaps.Second)})
        {
            var s=new GameSession(new(d));var target=d.ResourceNodes.First(r=>r.Rules?.Public!=false).Position;
            Step(s,new(ActionKind.MoveTo,Target:target));var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
            Assert.Equal(d.Id,copy.Engine.Configuration().Id);Assert.Equal(JsonSerializer.Serialize(d),JsonSerializer.Serialize(copy.Engine.Configuration()));
            Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());s.AdvanceTo(s.Engine.Tick+5);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
            Assert.NotEqual(PlayMaps.CompactValleyScenarioId,d.Id);Assert.NotEqual(PlayMaps.WoodlandScenarioId,d.Id);
        }
    }
    [Fact]public void LegacyValleyKeepsOwnedLandVehicleIdAndAnActiveTransportRoute()
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("compact_valley"),MobileEquipmentI4Tests.Profile(),new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=1000000}:p).ToArray()};
        var s=new GameSession(new(d));var plot=d.Plots!.First(p=>!p.IsShopSite);Move(s,plot.Position);Step(s,new(ActionKind.BuyPlot,Plot:new(plot.Id)));
        var car=FleetE1Tests.Buy(s,"sputnik20");FleetE1Tests.Board(s,car);Step(s,new(ActionKind.MoveTo,Target:new(11,3)));
        Assert.True(s.Engine.Observe(Human).Participant.Busy);var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        var own=Assert.Single(restored.Engine.FleetOf(Human)!.Vehicles);Assert.Equal(car.Id,own.Vehicle.Id);Assert.Equal(car.CargoInventory,own.Vehicle.CargoInventory);
        Assert.Equal(Human,restored.Engine.Observe(Human).Plots.Single(p=>p.Id.Value==plot.Id).OwnerId);
        s.AdvanceTo(s.Engine.Tick+5);restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        Assert.Equal("compact_valley",restored.Engine.Configuration().Id);Assert.Equal(d.MapHeight,restored.Engine.Configuration().MapHeight);
    }
    private static void Evidence(GameSession s,string name)
    {
        if(Environment.GetEnvironmentVariable("DELSK_I6C2_EVIDENCE") is not {} folder)return;
        Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(new{tick=s.Engine.Tick,hash=s.Engine.ComputeStateHash(),participants=s.Engine.Configuration().Participants.Select(p=>p.Id),commands=s.CommandTape.GroupBy(c=>new{actor=c.AuthenticatedActor.Value,kind=c.Command.Action.Kind}).Select(g=>new{g.Key.actor,g.Key.kind,count=g.Count()})},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));
    }
}

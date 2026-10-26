using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ProductionD3Tests
{
    internal static EconomyProfile Profile()=>VNextProductionProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    // Prepared sources and accelerated XP: exercises execution and local upgrades, not natural progression.
    internal static ScenarioDefinition Prepared(string industry="woodworking")
    {
        var p=Profile();var w=p.World;var basis=w.BuildingTypes.Single(t=>t.Id==industry).Levels[0].RecipeId;
        var r=w.Recipes.Single(r=>r.Id==basis);var seed=r with{Id="prepared-xp",Inputs=[new("wood",1)],OutputQuantity=1,FullStaffTicks=1,EnergyPerBatchMinor=2};
        var industries=w.Rules.Development!.Industries.Select(i=>i.Id!=industry?i:i with{Products=[..i.Products.Select(pr=>pr.ItemId==r.OutputItem?pr with{XpPerUnit=40000}:pr),new(seed.Id,seed.OutputItem,1,40000,1)]}).ToImmutableArray();
        Cell[] cells=[new(4,4),new(5,4),new(4,5),new(5,5),new(4,6),new(5,6)];
        return new("production-d3-prepared",42,40,10,[],w.Items,[..w.Recipes,seed],
            [new(1,10000000,new(4,4),2000),new(2,10000000,new(35,8),2000)],[],[],w.Items.Select((i,n)=>new ResourceNodeDefinition(i.Id,new(n,0))).ToArray(),
            Population:10,MaxTicks:20000,Plots:cells.Select((c,n)=>new PlotDefinition(50+n,c,10000,OwnerId:1)).ToArray(),
            BuildingTypes:w.BuildingTypes.Select(t=>t.Id!=industry?t:t with{Levels=t.Levels.Select(l=>l with{RecipeId=seed.Id,AdditionalRecipeIds=[..l.UnlockedRecipes]}).ToArray()}).ToArray(),
            Buildings:[new(8,1,new(4,4),industry,Staff:5)],EquipmentTypes:w.EquipmentTypes,
            Labor:new(StepTicks:10000,GrowthPeriodTicks:10000,GrowthPerPeriod:0),Elections:new(PeriodTicks:20000),
            CapitalDemand:w.CapitalDemand with{Terminal=new(37,8),BootstrapBaseBudgetMinor=10000000,PeriodTicks=10000},Economy:w.Rules with{
                Development=w.Rules.Development with{Industries=industries},ProductionContent=w.Rules.ProductionContent! with{Versions=[..w.Rules.ProductionContent!.Versions,new(seed.Id,seed.Id,1,1)]}});
    }
    internal static void Step(GameSession s,GameAction a,bool accepted=true)=>StorageC1Tests.Step(s,a,accepted:accepted);
    internal static BuildingView Factory(GameSession s)=>s.Engine.Observe(new(1)).OwnBuildings.Single();
    internal static void Supply(GameSession s,IEnumerable<MaterialCost> materials)
    {foreach(var m in materials){StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,Factory(s).InputId,m.ItemId,m.Quantity);}}
    internal static void Unlock(GameSession s)
    {Supply(s,[new("wood",1)]);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));Assert.Equal(5,s.Engine.DevelopmentOf(new(1))!.Experience.Single(i=>i.Industry==Factory(s).TypeId).TechnologyLevel);}
    [Fact] public void FullProfileIsStrictAndHasAllTechnologiesAndFiniteSupplies()
    {
        var p=Profile();Assert.Equal(34,p.World.Items.Length);Assert.Equal(69,p.World.Recipes.Length);Assert.Equal(6,p.World.BuildingTypes.Count(t=>t.Kind==BuildingKind.Production));Assert.Equal(8,p.World.EquipmentTypes.Length);
        var read=EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p)));Assert.Equal(p.Id,read.Id);
        foreach(var r in MapDesignTests.Balance().Recipes)
            Assert.Contains(p.World.BuildingTypes.Single(t=>t.Id==r.Industry).Levels.Single(l=>l.Level==r.UnlockLevel).UnlockedRecipes,id=>id==r.Id);
        Assert.Equal(8,p.World.Rules.ProductionContent!.Imports.Length);Assert.Equal(3125,p.World.Rules.ProductionContent.Imports.Single(i=>i.ItemId=="steel").UnitPriceMinor);
        Assert.Null(MapDesignTests.Economy().World.Rules.ProductionContent);
    }
    public static IEnumerable<object[]> Recipes()=>MapDesignTests.Balance().Recipes.Select(r=>new object[]{r.Id,r.Industry});
    [Theory][MemberData(nameof(Recipes))]
    public void EveryBaseRecipeActuallyConsumesPaysProducesAndRestores(string recipe,string industry)
    {
        var d=Prepared(industry);var s=new GameSession(new(d));Unlock(s);var r=d.Recipes.Single(r=>r.Id==recipe);Supply(s,r.Ingredients);
        Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:recipe));long before=s.Engine.Inspect().Flows.Single(f=>f.ItemId==r.OutputItem).Produced;
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.AdvanceTo(s.Engine.Tick+r.FullStaffTicks+1);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(before+r.OutputQuantity,s.Engine.Inspect().Flows.Single(f=>f.ItemId==r.OutputItem).Produced);
        foreach(var m in r.Ingredients)Assert.True(s.Engine.Inspect().Flows.Single(f=>f.ItemId==m.ItemId).RecipeInput>=m.Quantity);
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="Wages");Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="Energy");
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        Evidence(s,recipe);
    }
    internal static void Evidence(GameSession s,string name)
    {
        if(Environment.GetEnvironmentVariable("DELSK_D3_EVIDENCE") is not {} folder)return;
        Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(folder,name+".commands.jsonl"),string.Join("\n",s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(folder,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(folder,name+".save.json"));
    }
    [Fact] public void ImportIsFinitePaidPhysicalCappedAndSavedAtRefreshBoundary()
    {
        var d=Prepared();d=d with{CapitalDemand=d.CapitalDemand! with{PeriodTicks=1200}};var s=new GameSession(new(d));StorageC1Tests.Move(s,d.CapitalDemand!.Terminal);
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.BuyImport,ItemId:"steel",Quantity:12));
        Assert.Equal(cash-12*3125,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(0,StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s)).Stock.Where(i=>i.ItemId=="steel").Sum(i=>i.Quantity));
        Step(s,new(ActionKind.BuyImport,ItemId:"steel",Quantity:1),false);var pickup=s.Engine.ProductionContentOf(new(1))!.Pickup!.Value;
        Step(s,new(ActionKind.Transfer,Source:pickup,Destination:StorageC1Tests.Carrier(s),ItemId:"steel",Quantity:3));StorageC1Tests.Idle(s);
        Assert.Equal(3,StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s)).Stock.Single(i=>i.ItemId=="steel").Quantity);Assert.Empty(s.Engine.ProductionContentOf(new(2))!.Technologies);Assert.Null(s.Engine.ProductionContentOf(new(2))!.Pickup);
        s.AdvanceTo(1199);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(12,s.Engine.ProductionContentOf(new(1))!.Imports.Single(i=>i.ItemId=="steel").Remaining);s.AdvanceTo(3600);Assert.Equal(24,s.Engine.ProductionContentOf(new(1))!.Imports.Single(i=>i.ItemId=="steel").Remaining);
        Assert.Equal(12,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").Imported);Assert.Equal(0,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").Produced);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());Evidence(s,"import");
    }
    [Theory][InlineData(2)][InlineData(3)]
    public void ModernizationIsLocalPaidTimedAndOldVersionsRemain(int target)
    {
        var d=Prepared();var s=new GameSession(new(d));Unlock(s);
        for(int v=2;v<=target;v++)
        {
            var cost=d.Economy!.ProductionContent!.Modernizations.Single(m=>m.Version==v);Supply(s,cost.Materials);StorageC1Tests.Move(s,Factory(s).Position);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
            Step(s,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:v));Assert.Equal(cash-cost.PriceMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);
            var project=s.Engine.ProductionContentOf(new(1))!.Projects.Single();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Step(s,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:v),false);Step(copy,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:v),false);
            s.AdvanceTo(project.EndTick-1);Assert.Equal(v-1,s.Engine.ProductionContentOf(new(1))!.Technologies.SingleOrDefault()?.Version??1);s.Step();copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        }
        string id="saw_planks_v"+target;Assert.Contains(d.Recipes,r=>r.Id==id);var recipe=d.Recipes.Single(r=>r.Id==id);Supply(s,recipe.Ingredients);
        Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:id));Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));var saved=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Step(s,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:target),false);Step(saved,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:target),false);s.AdvanceTo(s.Engine.Tick+recipe.FullStaffTicks+1);saved.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),saved.Engine.ComputeStateHash());
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:"saw_planks"));
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        Assert.Contains(s.Engine.QuoteDemolition(new(1),8)!.Invested,m=>m.ItemId=="steel");Evidence(s,"modernization-"+target);
    }
    [Fact] public void VersionNeedsBothTechnologyAndLocalEquipmentAndCannotInventCapacity()
    {
        var s=new GameSession(new(Prepared()));Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:"saw_planks_v2"),false);Unlock(s);
        Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:"saw_planks_v2"),false);
        StorageC1Tests.Harvest(s,"grain",1);StorageC1Tests.Move(s,Factory(s).Position);Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:Factory(s).InputId,ItemId:"grain",Quantity:1),false);
    }
    internal static ScenarioDefinition CompactOrdinary()
    {
        var w=Profile().World;
        var map=new ScenarioDefinition("d3-compact-ordinary",47,10,8,[],w.Items,w.Recipes,
            [new(1,20000,new(1,3),20),new(2,20000,new(7,3),20),new(3,20000,new(8,3),20),new(4,20000,new(9,3),20)],[],[],
            w.Resources!.Select((r,n)=>new ResourceNodeDefinition(r.ItemId,new(n,0),r.Public)).ToArray(),Population:80,MaxTicks:12000,
            Plots:new[]{new Cell(3,4),new Cell(4,4),new Cell(3,5),new Cell(4,5),new Cell(3,6),new Cell(4,6)}.Select((c,n)=>new PlotDefinition(50+n,c,10000)).ToArray(),CapitalDemand:w.CapitalDemand with{Terminal=new(1,3)});
        // Election dialogs are disabled only in this economic path fixture; no economic value is changed.
        return Profile().Apply(map) with{Elections=w.Elections with{PauseForBallots=false}};
    }
    [Fact] public void NpcBuysImportThroughOrdinaryValidationAndSavesTheSamePlan()
    {
        var s=new GameSession(new(CompactOrdinary()));var npc=new AlgorithmicController(new(1,"wood",PoliticsEnabled:false,CashReserveMinor:0),17,[1,2,3,4]);
        var observation=s.Engine.ObserveController(new(1));var decision=npc.Decide(observation,[]);
        Assert.Equal("BuyImport",decision.Goal);Assert.Equal(ActionKind.BuyImport,decision.Actions.Single().Kind);Step(s,decision.Actions.Single());
        Assert.NotNull(s.Engine.ProductionContentOf(new(1))!.Pickup);Assert.Null(s.Engine.ObserveController(new(2)).ProductionContent!.Pickup);
        var clone=AlgorithmicController.Restore(npc.Capture());Assert.Equal(JsonSerializer.Serialize(npc.Capture(),SimulationJson.Options),JsonSerializer.Serialize(clone.Capture(),SimulationJson.Options));
    }
    [Fact] public void NpcActuallyPaysForBothLocalVersionsWithoutLlmOrPrivilegedCommands()
    {
        var d=Prepared();d=d with{Buildings=[d.Buildings![0] with{Level=5}]};var s=new GameSession(new(d));Unlock(s);
        foreach(var version in new[]{2,3})
        {
            Supply(s,d.Economy!.ProductionContent!.Modernizations.Single(m=>m.Version==version).Materials);StorageC1Tests.Move(s,Factory(s).Position);
            var npc=new AlgorithmicController(new(1,"wood",PoliticsEnabled:false,CashReserveMinor:0,CashWeight:0),17,[1,2]);
            var decision=npc.Decide(s.Engine.ObserveController(new(1)),[]);Assert.Equal("ModernizeWorkshop",decision.Goal);
            Assert.Equal(ActionKind.ModernizeWorkshop,decision.Actions.Single().Kind);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
            Step(s,decision.Actions.Single());Assert.True(s.Engine.Observe(new(1)).Account.BalanceMinor<cash);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
            s.AdvanceTo(s.Engine.ProductionContentOf(new(1))!.Projects.Single().EndTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(version,s.Engine.ProductionContentOf(new(1))!.Technologies.Single().Version);
        }
        Evidence(s,"npc-modernization");
    }
    [Fact] public void PaidModernizationDoesNotEquipOtherWorkshopsAndDemolitionWaitsForIt()
    {
        var d=Prepared();d=d with{Buildings=[d.Buildings![0] with{Level=2}]};var s=new GameSession(new(d));Unlock(s);
        var outfit=d.BuildingTypes!.Single(t=>t.Id=="woodworking").WorkshopOutfit!;Supply(s,outfit.Materials);StorageC1Tests.Move(s,Factory(s).Position);Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(8)));s.AdvanceTo(s.Engine.Observe(new(1)).OwnEnterprises.Single().Installations.Single().EndTick);
        var second=s.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Single(w=>w.Id!=8);Assert.Equal(0,second.Staff);
        var cost=d.Economy!.ProductionContent!.Modernizations.Single(m=>m.Version==2);Supply(s,cost.Materials);StorageC1Tests.Move(s,Factory(s).Position);Step(s,new(ActionKind.ModernizeWorkshop,Enterprise:new(8),Quantity:2));
        Step(s,new(ActionKind.PrepareDemolition,Building:new(8)));Assert.Contains("ConstructionInProgress",s.Engine.QuoteDemolition(new(1),8)!.Blockers);Step(s,new(ActionKind.CancelDemolitionPreparation,Building:new(8)));
        s.AdvanceTo(s.Engine.ProductionContentOf(new(1))!.Projects.Single().EndTick);Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),WorkshopId:second.Id,RecipeId:"saw_planks_v2"),false);
        Assert.Single(s.Engine.ProductionContentOf(new(1))!.Technologies);Step(s,new(ActionKind.PrepareDemolition,Building:new(8)));var quote=s.Engine.QuoteDemolition(new(1),8)!;Assert.Empty(quote.Blockers);Step(s,new(ActionKind.DemolishBuilding,Building:new(8),DemolitionQuote:quote.Token));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(3,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").ConstructionRecovered);Evidence(s,"modernized-demolition");
    }
    [Fact] public void ImportsRejectInsufficientCashAndDuplicateCommandsCannotChargeTwice()
    {
        var d=CompactOrdinary();var s=new GameSession(new(d));Step(s,new(ActionKind.BuyImport,ItemId:"steel",Quantity:12),false);Assert.Null(s.Engine.ProductionContentOf(new(1))!.Pickup);
        var id=s.Send(new(1),new(ActionKind.BuyImport,ItemId:"steel",Quantity:3));s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);var command=s.CommandTape.Single(t=>t.Command.CommandId==id).Command;
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Assert.Equal(ResultStatus.Accepted,s.Engine.Enqueue(new(1),command)!.Status);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(3,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").Imported);Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type=="ImportPurchased"||e.Type=="CommandApplied"&&e.ReasonCode=="BuyImport");
        var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["productionContent"]!["imports"]![0]!["remaining"]=999999;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));
    }
    [Fact] public void NativeFixturesAreExplicitlyPreparedAndUseRealCoreCommands()
    {
        if(Environment.GetEnvironmentVariable("DELSK_D3_EVIDENCE") is null)return;
        var s=new GameSession(new(Prepared()));Unlock(s);Supply(s,s.Engine.Configuration().Economy!.ProductionContent!.Modernizations.Single(m=>m.Version==2).Materials);StorageC1Tests.Move(s,Factory(s).Position);Evidence(s,"native-modernize");
        Evidence(new GameSession(new(CompactOrdinary())),"native-import");
    }
}

public class ProductionD3StartTests
{
        [Fact] public void OrdinaryStartEarnsAndBuildsWithRealImportAndNoPreparedMoneyMaterialsOrExperience()
    {
        // Compact test geography only. All costs, stocks, staff, recipes and work durations are D3.
        var d=ProductionD3Tests.CompactOrdinary();var s=new GameSession(new(d));Assert.Empty(s.Engine.Observe(new(1)).OwnBuildings);Assert.Empty(StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s)).Stock);
        void Wood(long q){long stock=s.Engine.ResourceSites().Single(r=>r.ItemId=="wood").Stock;if(stock<q)s.AdvanceTo(s.Engine.Tick+(q-stock)*12+1);StorageC1Tests.Harvest(s,"wood",q);}
        int bound=200;while(s.Engine.Observe(new(1)).Account.AvailableMinor<50000&&bound-->0)
        {
            var pack=StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s));long held=pack.Stock.Where(i=>i.ItemId=="wood").Sum(i=>i.Quantity-i.Reserved);
            if(held==0){Wood(Math.Min(10,Math.Max(1,s.Engine.ResourceSites().Single(r=>r.Position==new Cell(0,0)).Stock)));held=StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s)).Stock.Single(i=>i.ItemId=="wood").Quantity;}
            StorageC1Tests.Move(s,d.CapitalDemand!.Terminal);var order=s.Engine.Observe(new(1)).CapitalOffers.Where(o=>o.ItemId=="wood"&&o.RemainingQuantity>0&&o.CreatedTick<s.Engine.Tick).OrderByDescending(o=>o.UnitPriceMinor).FirstOrDefault();
            if(order is null){s.AdvanceTo(s.Engine.Tick+60);continue;}
            ProductionD3Tests.Step(s,new(ActionKind.SellAvailableToCapital,OfferId:order.Id,ItemId:"wood",Quantity:Math.Min(held,order.RemainingQuantity)));s.AdvanceTo(s.Engine.Tick+6);
        }
        Assert.True(bound>0);StorageC1Tests.Move(s,new(3,4));ProductionD3Tests.Step(s,new(ActionKind.BuyPlot,Plot:new(50)));
        var ground=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==50).InventoryId);
        var existing=StorageC1Tests.Inventory(s,StorageC1Tests.Carrier(s)).Stock.FirstOrDefault(i=>i.ItemId=="wood"&&i.Quantity>0);long amount=Math.Min(30,existing?.Quantity??0);if(amount>0)StorageC1Tests.Put(s,ground,"wood",amount);
        while(amount<30){long quantity=Math.Min(10,30-amount);Wood(quantity);StorageC1Tests.Put(s,ground,"wood",quantity);amount+=quantity;}
        StorageC1Tests.Move(s,d.CapitalDemand!.Terminal);ProductionD3Tests.Step(s,new(ActionKind.BuyImport,ItemId:"steel",Quantity:3));var pickup=s.Engine.ProductionContentOf(new(1))!.Pickup!.Value;
        ProductionD3Tests.Step(s,new(ActionKind.Transfer,Source:pickup,Destination:StorageC1Tests.Carrier(s),ItemId:"steel",Quantity:3));StorageC1Tests.Idle(s);StorageC1Tests.Put(s,ground,"steel",3);
        ProductionD3Tests.Step(s,new(ActionKind.Build,BuildingTypeId:"woodworking",Target:new(3,4)));var project=s.Engine.StorageOf(new(1)).Construction.Single();Assert.Equal(60,project.CompletesTick-project.StartedTick);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(project.CompletesTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        var factory=s.Engine.Observe(new(1)).OwnBuildings.Single();Assert.Equal(0,s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff);Assert.Equal(0,s.Engine.DevelopmentOf(new(1))!.Experience.Single(i=>i.Industry=="woodworking").Total);
        Wood(2);StorageC1Tests.Put(s,factory.InputId,"wood",2);ProductionD3Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:true));
        int limit=300;while(s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches==0&&limit-->0)s.Step();Assert.True(limit>0);Assert.True(s.Engine.DevelopmentOf(new(1))!.Experience.Single(i=>i.Industry=="woodworking").ProductionXp>0);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());ProductionD3Tests.Evidence(s,"ordinary-start");
    }
}

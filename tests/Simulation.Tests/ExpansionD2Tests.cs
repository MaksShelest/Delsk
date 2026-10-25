using System.Collections.Immutable;
using System.Text;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ExpansionD2Tests
{
    // Accelerated research fixture only: isolates physical projects. Normal XP values are verified separately.
    internal static ScenarioDefinition Definition(bool fastExperience=true)
    {
        var p=VNextDevelopmentProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());var d=StorageC1Tests.Definition();
        var recipe=new RecipeDefinition("fixture-plank","wood",1,"plank",1,1,5,1,2);
        Cell[] cells=[new(8,2),new(9,2),new(8,3),new(9,3),new(8,4),new(9,4)];
        return d with{Id="development-physical-prepared",MapHeight=6,MaxTicks=10000,Population=10,Recipes=[..p.World.Recipes,recipe],
            Participants=d.Participants.Select(a=>a.Id==1?a with{CashMinor=10000000}:a).ToArray(),
            Plots=[..d.Plots!,..cells.Select((c,n)=>new PlotDefinition(50+n,c,10000,OwnerId:n==0?1:null))],
            BuildingTypes=p.World.BuildingTypes.Select(t=>t.Id=="woodworking"?t with{Levels=t.Levels.Select(l=>l with{RecipeId=recipe.Id,AdditionalRecipeIds=[]}).ToArray()}:t).ToArray(),
            ResourceNodes=[..d.ResourceNodes,..new[]{"glass","motor","window","conveyor"}.Select((item,n)=>new ResourceNodeDefinition(item,new(9+n,0)))],
            Economy=p.World.Rules with{Development=p.World.Rules.Development! with{Industries=[..p.World.Rules.Development!.Industries.Select(i=>i.Id=="woodworking"?i with{Products=[new(recipe.Id,recipe.OutputItem,1,fastExperience?40000:1,1)]}:i)]}},
            Labor=new(StepTicks:1,MobilityNumerator:1,MobilityDenominator:1,GrowthPeriodTicks:10000,GrowthPerPeriod:0)};
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)=>DevelopmentD2Tests.Step(s,a,actor,accepted);
    internal static BuildingView Factory(GameSession s)=>s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.TypeId=="woodworking");
    internal static void Materials(GameSession s,IEnumerable<MaterialCost> materials,InventoryId inventory)
    {foreach(var m in materials){StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,inventory,m.ItemId,m.Quantity);}}
    internal static GameSession Initial(bool complete=true,bool fastExperience=true,ScenarioDefinition? definition=null)
    {
        var s=new GameSession(new(definition??Definition(fastExperience)));var ground=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==50).InventoryId);
        Materials(s,s.Engine.Configuration().BuildingTypes!.Single(t=>t.Id=="woodworking").Levels[0].Materials,ground);
        Step(s,new(ActionKind.Build,BuildingTypeId:"woodworking",Target:new(8,2)));
        if(complete)s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);
        return s;
    }
    internal static void Unlock(GameSession s)
    {
        var b=Factory(s);Materials(s,[new("wood",1)],b.InputId);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true));int limit=100;while(s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches==0&&limit-->0)s.Step();Assert.True(limit>0);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:false));
    }
    internal static void ReadyExpansion(GameSession s)
    {
        var b=Factory(s);var q=s.Engine.QuoteExpansion(new(1),b.Id)!;
        foreach(var land in q.Land.Where(l=>l.Owner is null))Step(s,new(ActionKind.BuyExpansionLand,Building:b.Id,Plot:land.Plot));
        Materials(s,q.Materials,b.InputId);
    }
    [Fact] public void ProfileKeepsExactCostsThresholdsFootprintsAndOnePaidInitialWorkshop()
    {
        var balance=MapDesignTests.Balance();var p=VNextDevelopmentProfile.Create(MapDesignTests.Economy(),balance);
        foreach(var t in p.World.BuildingTypes.Where(t=>t.Kind==BuildingKind.Production))
            foreach(var l in t.Levels){var expected=balance.EnterpriseTiers.Single(e=>e.Level==l.Level);Assert.Equal(expected.Cells,l.Footprint.Length);Assert.Equal(expected.Workshops,l.WorkshopSlots);Assert.Equal(expected.InputCapacity,l.InputCapacity);Assert.Equal(expected.OutputCapacity,l.OutputCapacity);Assert.Equal(l.Level==1?60:expected.Ticks,l.ConstructionTicks);if(l.Level>1)Assert.Equal(expected.PriceMinor,l.PriceMinor);}
        Assert.NotNull(EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).World.Rules.Development);Assert.Null(StorageC1Tests.Profile().World.Rules.Development);
        if(Environment.GetEnvironmentVariable("DELSK_D2_EVIDENCE") is {} folder){Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"economy.d2-development.json"),VNextSupportedProfile.Serialize(p));}
    }
    [Fact] public void TimedInitialBuildUsesGroundStockClaimsContourAndNeverCreatesWorkers()
    {
        var s=Initial(false);var job=s.Engine.StorageOf(new(1)).Construction.Single();Assert.NotNull(job.Development);Assert.Equal(60,job.CompletesTick-job.StartedTick);Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings,b=>b.TypeId=="woodworking");Assert.Equal(6,s.Engine.Observe(new(1)).Plots.Count(p=>p.ExpansionFor==job.Id));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletesTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());var b=Factory(s);Assert.Single(b.Footprint);Assert.Equal(80,StorageC1Tests.Inventory(s,b.InputId).Capacity);Assert.Equal(0,s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff);Assert.Equal(10,s.Engine.Inspect().Unemployed);
    }
    [Fact] public void ExperienceGatesExpansionButNotAnotherPaidFirstLevelFactory()
    {
        var s=Initial();var b=Factory(s);Assert.Contains("IndustryTechnologyRequired",s.Engine.QuoteExpansion(new(1),b.Id)!.Blockers);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2),accepted:false);Unlock(s);Assert.Equal(5,s.Engine.DevelopmentOf(new(1))!.Experience.Single(x=>x.Industry=="woodworking").TechnologyLevel);
        Assert.Equal(1,Factory(s).Level);Assert.Single(s.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops);
    }
    [Fact] public void ExpansionActuallyAddsPaidLandCapacityAndEmptyPlacesAcrossAllTiers()
    {
        var s=Initial();Unlock(s);
        foreach(int level in new[]{2,3,4,5})
        {
            var b=Factory(s);ReadyExpansion(s);long money=s.Engine.Observe(new(1)).Account.BalanceMinor;var quote=s.Engine.QuoteExpansion(new(1),b.Id)!;Assert.Empty(quote.Blockers);
            Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:level));Assert.Equal(money-quote.PriceMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(level-1,Factory(s).Level);
            var job=s.Engine.StorageOf(new(1)).Construction.Single();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletesTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
            b=Factory(s);Assert.Equal(level,b.Level);Assert.Equal(new[]{1,3,4,5,6}[level-1],b.Footprint.Length);Assert.Equal(80+40*(level-1),StorageC1Tests.Inventory(s,b.InputId).Capacity);Assert.Equal(80+40*(level-1),StorageC1Tests.Inventory(s,b.OutputId!.Value).Capacity);Assert.Single(s.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops);
            Assert.All(s.Engine.DevelopmentOf(new(1))!.Buildings.Single().Cells,c=>Assert.Equal(level,c.Level));
        }
        Assert.Equal(50000,s.Engine.Ledger().Where(l=>l.Reason=="PlotPurchase").Sum(l=>l.AmountMinor));Assert.Equal(10,s.Engine.Inspect().Population);Assert.Equal(40000,s.Engine.DevelopmentOf(new(1))!.Experience.Single(x=>x.Industry=="woodworking").Total);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(Definition(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact] public void PendingNewCellsCannotReceiveStockOrBeStolenAndCancellationReleasesHolds()
    {
        var s=Initial();Unlock(s);ReadyExpansion(s);var b=Factory(s);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2));var job=s.Engine.StorageOf(new(1)).Construction.Single();
        var future=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==51).InventoryId);Assert.Equal(70,StorageC1Tests.Inventory(s,future).ReservedCapacity);StorageC1Tests.Move(s,new(9,2));StorageC1Tests.Harvest(s,"wood",1);StorageC1Tests.Move(s,new(9,2));Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:future,ItemId:"wood",Quantity:1),accepted:false);
        StorageC1Tests.Move(s,new(8,2));var quote=s.Engine.QuoteDemolition(new(1),job.Id,true)!;Assert.Equal(13500,quote.PriceMinor);Step(s,new(ActionKind.CancelConstruction,OfferId:job.Id,DemolitionQuote:quote.Token));Assert.Equal(1,Factory(s).Level);Assert.Equal(0,StorageC1Tests.Inventory(s,future).ReservedCapacity);Assert.Empty(s.Engine.StorageOf(new(1)).Construction);
        Assert.Equal(12,StorageC1Tests.Inventory(s,b.InputId).Stock.Single(i=>i.ItemId=="plank").Quantity);Assert.Equal(3,StorageC1Tests.Inventory(s,b.InputId).Stock.Single(i=>i.ItemId=="steel").Quantity);Assert.All(s.Engine.Observe(new(1)).Plots.Where(p=>p.Position is {X:8 or 9,Z:2 or 3 or 4}),p=>Assert.Null(p.ExpansionFor));CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
    [Fact] public void CancellationOfInitialProjectKeepsSunkCostsGoodsAndRemovesFutureRights()
    {
        var s=Initial(false);var job=s.Engine.StorageOf(new(1)).Construction.Single();long money=s.Engine.Observe(new(1)).Account.BalanceMinor;var q=s.Engine.QuoteDemolition(new(1),job.Id,true)!;Step(s,new(ActionKind.CancelConstruction,OfferId:job.Id,DemolitionQuote:q.Token));Assert.Equal(money-3000,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.DoesNotContain(s.Engine.Observe(new(1)).Plots,p=>p.ExpansionFor is not null);Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings,b=>b.TypeId=="woodworking");Assert.Equal(10,s.Engine.Inspect().Unemployed);CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletesTick+1);Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings,b=>b.TypeId=="woodworking");
    }
    [Fact] public void ASecondFactoryUsesSharedTechnologyButPaysItsOwnFirstLevelAndHasNoFreeCrew()
    {
        var d=Definition();Cell[] cells=[new(10,2),new(11,2),new(10,3),new(11,3),new(10,4),new(11,4)];d=d with{Plots=[..d.Plots!,..cells.Select((c,n)=>new PlotDefinition(60+n,c,10000,OwnerId:n==0?1:null))]};var s=Initial(definition:d);Unlock(s);long xp=s.Engine.DevelopmentOf(new(1))!.Experience.Single(e=>e.Industry=="woodworking").Total;
        var ground=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==60).InventoryId);Materials(s,d.BuildingTypes!.Single(t=>t.Id=="woodworking").Levels[0].Materials,ground);long money=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Step(s,new(ActionKind.Build,BuildingTypeId:"woodworking",Target:new(10,2)));Assert.Equal(money-d.BuildingTypes!.Single(t=>t.Id=="woodworking").Levels[0].PriceMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);
        Assert.Equal(2,s.Engine.Observe(new(1)).OwnEnterprises.Length);Assert.All(s.Engine.Observe(new(1)).OwnEnterprises,e=>{Assert.Equal(1,e.Level);Assert.Single(e.Workshops);Assert.Equal(0,e.Workshops.Single().Staff);});Assert.Equal(xp,s.Engine.DevelopmentOf(new(1))!.Experience.Single(e=>e.Industry=="woodworking").Total);CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
    [Fact] public void ExpansionRightsProtectOtherActorsAndDoNotAllowAnotherBuildingInsideTheContour()
    {
        var s=Initial();StorageC1Tests.Move(s,new(9,4),2);Step(s,new(ActionKind.BuyPlot,Plot:new(55)),2,false);Step(s,new(ActionKind.ReservePlot,Plot:new(55)),2,false);
        StorageC1Tests.Move(s,new(9,4));Step(s,new(ActionKind.BuyPlot,Plot:new(55)));Step(s,new(ActionKind.Build,BuildingTypeId:"warehouse",Target:new(9,4)),accepted:false);Assert.Equal(1,s.Engine.Observe(new(1)).OwnBuildings.Count(b=>b.TypeId=="woodworking"));
        StorageC1Tests.Move(s,new(8,2));long money=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.BuyExpansionLand,Building:Factory(s).Id,Plot:new(55)),accepted:false);Assert.Equal(money,s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact] public void ExpandedDemolitionUsesEachCellLevelAndAllActualMaterialsIncludingOutfitOnce()
    {
        var s=Initial();Unlock(s);ReadyExpansion(s);var b=Factory(s);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2));s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);Materials(s,[new("plank",6),new("steel",1)],b.InputId);Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(b.Id.Value)));s.AdvanceTo(s.Engine.Observe(new(1)).OwnEnterprises.Single().Installations.Single().EndTick);
        Assert.Equal(2,s.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops.Length);Assert.All(s.Engine.Observe(new(1)).OwnEnterprises.Single().Workshops,w=>Assert.Equal(0,w.Staff));Step(s,new(ActionKind.PrepareDemolition,Building:b.Id));var q=s.Engine.QuoteDemolition(new(1),b.Id.Value)!;
        Assert.Equal(13500,q.PriceMinor);Assert.Equal(3,q.Cells.Length);Assert.Equal(new[]{new MaterialCost("plank",15),new MaterialCost("steel",5),new MaterialCost("wood",18)},q.Recovery);Step(s,new(ActionKind.DemolishBuilding,Building:b.Id,DemolitionQuote:q.Token));Assert.Equal(40000,s.Engine.DevelopmentOf(new(1))!.Experience.Single(e=>e.Industry=="woodworking").Total);Assert.DoesNotContain(s.Engine.Observe(new(1)).Plots,p=>p.ExpansionFor is not null);CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
    [Fact] public void UnfinishedBatchBlocksExpansionAndARepeatedTargetCannotAdvanceAnotherTier()
    {
        var d=Definition();d=d with{Recipes=d.Recipes.Select(r=>r.Id=="fixture-plank"?r with{FullStaffTicks=10}:r).ToArray()};var s=Initial(definition:d);Unlock(s);ReadyExpansion(s);var b=Factory(s);Materials(s,[new("wood",1)],b.InputId);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true));s.Step();Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:false));Assert.NotEqual(0,s.Engine.Observe(new(1)).OwnEnterprises.Single().BatchId);Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2),accepted:false);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true));s.AdvanceTo(s.Engine.Tick+12);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:false));Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2));s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:2),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(2,Factory(s).Level);
    }
    [Fact] public void PreparationFinishesTheCurrentBatchWithoutStartingAnotherAndSurvivesLoad()
    {
        var d=Definition();d=d with{Recipes=d.Recipes.Select(r=>r.Id=="fixture-plank"?r with{FullStaffTicks=10}:r).ToArray()};var s=Initial(definition:d);Unlock(s);var b=Factory(s);Materials(s,[new("wood",10)],b.InputId);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true));s.Step();long completed=s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches;
        Step(s,new(ActionKind.PrepareEnterpriseExpansion,Building:b.Id));var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+20);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(completed+1,s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);Assert.False(s.Engine.Observe(new(1)).OwnEnterprises.Single().Enabled);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true),accepted:false);Step(s,new(ActionKind.CancelExpansionPreparation,Building:b.Id));Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(b.Id.Value),Enabled:true));
    }
}

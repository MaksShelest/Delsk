using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class WorkshopD1Tests
{
    internal static ScenarioDefinition Definition(int staff=5,long output=80,long ticks=20)
    {
        var d=StorageC1Tests.Definition();
        var recipes=new[]{new RecipeDefinition("d1-plank","wood",2,"plank",2,ticks,5,1,999),new RecipeDefinition("d1-bread","grain",2,"bread",1,ticks,5,1,999)};
        var type=new BuildingTypeDefinition("d1-prepared",BuildingKind.Production,[new(0,0),new(1,0),new(2,0)],
            [new(1,recipes[0].Id,15000,[new("wood",15)],80,output,5,20,AdditionalRecipeIds:[recipes[1].Id],WorkshopSlots:1),
             new(2,recipes[0].Id,45000,[new("plank",20),new("steel",5)],120,output,5,20,AdditionalRecipeIds:[recipes[1].Id],WorkshopSlots:2)],[],new(15000,[new("plank",6),new("steel",1)]));
        return d with{Id="workshops-d1-prepared",Recipes=recipes,BuildingTypes=[..d.BuildingTypes!,type],
            Plots=[..d.Plots!,new(6,new(1,0),10000,OwnerId:1),new(7,new(2,0),10000,OwnerId:1)],Buildings=[..d.Buildings!,new(8,1,new(0,0),type.Id,2,staff)],
            Population=10,Economy=d.Economy! with{Workshops=new()},Labor=new(StepTicks:10000,GrowthPeriodTicks:10000,GrowthPerPeriod:0)};
    }
    internal static void Step(GameSession s,GameAction a,bool accepted=true)=>DemolitionC2Tests.Step(s,a,accepted:accepted);
    internal static EnterpriseView Enterprise(GameSession s)=>s.Engine.Observe(new(1)).OwnEnterprises.Single();
    internal static ImmutableArray<WorkshopView> Lines(GameSession s)=>Enterprise(s).Workshops;
    internal static long Second(GameSession s)=>Lines(s).Single(w=>w.Id!=8).Id;
    internal static GameSession Equipped(ScenarioDefinition? definition=null)
    {
        var s=new GameSession(new(definition??Definition()));var e=Enterprise(s);
        StorageC1Tests.Harvest(s,"plank",6);StorageC1Tests.Put(s,e.InputId,"plank",6);StorageC1Tests.Harvest(s,"steel",1);StorageC1Tests.Put(s,e.InputId,"steel",1);
        Step(s,new(ActionKind.OutfitWorkshop,Enterprise:e.Id));var install=Enterprise(s).Installations.Single();s.AdvanceTo(install.EndTick);
        return s;
    }
    internal static void Supply(GameSession s,long wood=10,long grain=10)
    {
        if(wood>0){StorageC1Tests.Harvest(s,"wood",wood);StorageC1Tests.Put(s,Enterprise(s).InputId,"wood",wood);}
        if(grain>0){StorageC1Tests.Harvest(s,"grain",grain);StorageC1Tests.Put(s,Enterprise(s).InputId,"grain",grain);}
    }
    internal static void StartBoth(GameSession s,bool different=true)
    {
        long second=Second(s);if(different)Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),WorkshopId:second,RecipeId:"d1-bread"));
        Step(s,new(ActionKind.AssignWorkshopWorkers,Enterprise:new(8),Group:new(8),WorkshopId:second,Quantity:2));
        s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:second,Enabled:true));s.Step();
    }
    [Fact] public void ProfileIsDerivedFromBalanceWithoutFreePlacesOrOutfits()
    {
        var b=MapDesignTests.Balance();var p=VNextWorkshopProfile.Create(MapDesignTests.Economy(),b);
        Assert.Equal(b.Rules.StaffRateBp.Select(n=>n/100),Enumerable.Range(0,6).Select(n=>(long)WorkshopRules.Rate(n)));
        Assert.Equal(2,p.World.Rules.Workshops!.ServiceMinorPerFullTick);
        var factories=p.World.BuildingTypes.Where(t=>t.Kind==BuildingKind.Production).ToArray();Assert.Equal(6,factories.Length);
        foreach(var t in factories){Assert.Equal(1,t.Levels.Single().WorkshopSlots);Assert.Equal(5,t.Levels.Single().StaffCapacity);Assert.Equal(60,t.WorkshopOutfit!.Ticks);Assert.Equal(b.AdditionalWorkshopOutfits[t.Id].PriceMinor,t.WorkshopOutfit.PriceMinor);}
        Assert.NotNull(EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).World.Rules.Workshops);
        Assert.Null(StorageC1Tests.Profile().World.Rules.Workshops);
        Assert.Throws<ArgumentException>(()=>VNextWorkshopProfile.Create(MapDesignTests.Economy(),b with{Rules=b.Rules with{StaffPerWorkshop=6}}));
        if(Environment.GetEnvironmentVariable("DELSK_D1_EVIDENCE") is {} folder)
        {Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"economy.d1-workshops.json"),VNextSupportedProfile.Serialize(p));File.WriteAllText(Path.Combine(folder,"workshops-d1-fixture.json"),JsonSerializer.Serialize(Definition(),new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true})+"\n");}
    }
    [Theory][InlineData(0,0)][InlineData(1,25)][InlineData(2,45)][InlineData(3,65)][InlineData(4,85)][InlineData(5,100)]
    public void ExactStaffCurveUsesFixedPointAndNoIdleWages(int staff,long rate)
    {
        var s=new GameSession(new(Definition(staff)));Supply(s,2,0);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));var w=Lines(s).Single();Assert.Equal(rate,w.WorkDone);
        Assert.Equal(cash-staff*20-(2*rate/100),s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(rate==0?0:2000,w.WorkRequired);s.Engine.ValidateInvariants();
    }
    [Fact] public void TwoRecipesRunConcurrentlyAndStoppingOnePreservesOnlyItsBatch()
    {
        var s=Equipped();Supply(s);StartBoth(s);Assert.All(Lines(s),w=>Assert.NotEqual(0,w.BatchId));var initial=Lines(s);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:Second(s),Enabled:false));var paused=Lines(s).Single(w=>w.Id!=8);Assert.Equal(initial.Single(w=>w.Id!=8).WorkDone,paused.WorkDone);
        s.AdvanceTo(s.Engine.Tick+40);Assert.True(Lines(s).Single(w=>w.Id==8).CompletedBatches>0);Assert.Equal(paused.WorkDone,Lines(s).Single(w=>w.Id!=8).WorkDone);
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:Second(s),Enabled:true));s.AdvanceTo(s.Engine.Tick+50);Assert.True(Lines(s).Single(w=>w.Id!=8).CompletedBatches>0);s.Engine.ValidateInvariants();
    }
    [Fact] public void WorkforceMovesAreConservativeAndCannotMintOrDoubleEmploy()
    {
        var s=Equipped();Assert.Equal(5,Lines(s).Sum(w=>w.Staff));Assert.Equal(0,Lines(s).Single(w=>w.Id!=8).Staff);
        long unemployed=s.Engine.Inspect().Unemployed;Step(s,new(ActionKind.AssignWorkshopWorkers,Enterprise:new(8),WorkshopId:Second(s),Group:new(8),Quantity:3));
        Assert.Equal(new[]{2,3},Lines(s).Select(w=>w.Staff));Assert.Equal(unemployed,s.Engine.Inspect().Unemployed);
        Step(s,new(ActionKind.AssignWorkshopWorkers,Enterprise:new(8),WorkshopId:Second(s),Group:new(8),Quantity:3),false);
        Step(s,new(ActionKind.AssignWorkshopWorkers,Enterprise:new(8),WorkshopId:Second(s),Group:new(Second(s)),Quantity:1),false);
        Assert.Equal(5,s.Engine.Inspect().EmploymentGroups.Sum(g=>g.Headcount));s.Engine.ValidateInvariants();
    }
    [Fact] public void MontageIsPaidTimedPersistedAndDuplicateCannotCreateFreeStaff()
    {
        var s=Equipped();var lines=Lines(s);Assert.Equal(2,lines.Length);Assert.Equal(5,lines.Sum(w=>w.Staff));
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(8)),false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(15000,s.Engine.Ledger().Where(l=>l.Reason=="WorkshopOutfit").Sum(l=>l.AmountMinor));
        var started=s.Engine.EventsSince(0).Single(e=>e.Type=="WorkshopOutfitStarted");Assert.Equal(60,s.Engine.EventsSince(0).Single(e=>e.Type=="WorkshopOutfitCompleted").Tick-started.Tick);
        var d=Definition();var r=GameSession.Replay(d,s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),r.Engine.ComputeStateHash());
    }
    [Fact] public void NoMaterialsOrMoneyCannotPartiallyPayOutfit()
    {
        var s=new GameSession(new(Definition()));long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(8)),false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Empty(Enterprise(s).Installations);
        var poor=Definition();poor=poor with{Participants=poor.Participants.Select(p=>p.Id==1?p with{CashMinor=14999}:p).ToArray()};var unpaid=new GameSession(new(poor));StorageC1Tests.Harvest(unpaid,"plank",6);StorageC1Tests.Put(unpaid,Enterprise(unpaid).InputId,"plank",6);StorageC1Tests.Harvest(unpaid,"steel",1);StorageC1Tests.Put(unpaid,Enterprise(unpaid).InputId,"steel",1);Step(unpaid,new(ActionKind.OutfitWorkshop,Enterprise:new(8)),false);Assert.Equal(14999,unpaid.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(7,StorageC1Tests.Inventory(unpaid,Enterprise(unpaid).InputId).Stock.Sum(i=>i.Quantity));Assert.Single(Lines(unpaid));
        var d=Definition();d=d with{Participants=d.Participants.Select(p=>p with{CashMinor=0}).ToArray()};s=new(new(d));Supply(s,2,0);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));Assert.Equal("NoMoney",Lines(s).Single().ReasonCode);Assert.Equal(2,StorageC1Tests.Inventory(s,Enterprise(s).InputId).Stock.Single(i=>i.ItemId=="wood").Quantity);Assert.Equal(0,Lines(s).Single().BatchId);
    }
    [Fact] public void SharedOutputHoldAdmitsOnlyOneFullBatchAndFairnessSurvivesScarceArrivals()
    {
        var s=Equipped(Definition(output:2,ticks:1));Supply(s,12,0);StartBoth(s,false);
        var work=Lines(s).Where(w=>w.BatchId!=0).ToArray();Assert.Single(work);Assert.Equal(2,StorageC1Tests.Inventory(s,Enterprise(s).OutputId).ReservedCapacity);
        var served=new List<long>();
        for(int n=0;n<5;n++)
        {
            while(Lines(s).Sum(w=>w.CompletedBatches)<=n)s.Step();var done=Lines(s).Single(w=>w.CompletedBatches>served.Count(id=>id==w.Id));served.Add(done.Id);
            Step(s,new(ActionKind.Transfer,Source:Enterprise(s).OutputId,Destination:StorageC1Tests.Carrier(s),ItemId:"plank",Quantity:2));StorageC1Tests.Idle(s);
        }
        Assert.Equal(2,served.Distinct().Count());Assert.All(served.Zip(served.Skip(1)),pair=>Assert.NotEqual(pair.First,pair.Second));s.Engine.ValidateInvariants();
    }
    [Fact] public void BatchKeepsWageRecipeAndServiceTermsAcrossReassignmentAndRestore()
    {
        var s=Equipped();Supply(s);StartBoth(s);var before=Lines(s).Single(w=>w.Id==8);
        Step(s,new(ActionKind.SetWage,Group:new(8),WageMinor:99));s.Step();Assert.Equal(20,Lines(s).Single(w=>w.Id==8).BatchWageMinor);
        Assert.Equal(20,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id.Value==8).WageMinor);Assert.Equal(99,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id.Value==8).PendingWageMinor);
        Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),RecipeId:"d1-bread"),false);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+90);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.True(Lines(s).Any(w=>w.CompletedBatches>0));
        Assert.Equal(99,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id.Value==8).WageMinor);
    }
    [Fact] public void DemolitionFinishesAllBatchesAndRecoversActualOutfitOnce()
    {
        var s=Equipped();Supply(s);StartBoth(s);Step(s,new(ActionKind.PrepareDemolition,Building:new(8)));Assert.Contains("ProductionBatch",s.Engine.QuoteDemolition(new(1),8)!.Blockers);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+60);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.All(Lines(s),w=>{Assert.Equal(1,w.CompletedBatches);Assert.Equal(0,w.BatchId);});
        var q=s.Engine.QuoteDemolition(new(1),8)!;Assert.Empty(q.Blockers);Assert.Equal(new[]{new MaterialCost("plank",3)},q.Recovery);
        Step(s,new(ActionKind.DemolishBuilding,Building:new(8),DemolitionQuote:q.Token));Assert.Equal(10,s.Engine.Inspect().Unemployed);Assert.Empty(s.Engine.Observe(new(1)).OwnEnterprises);CheckpointStore.Decode(CheckpointStore.Encode(s));
    }
    [Fact] public void OwnWorkshopStateIsPrivateAndTamperedSnapshotIsRejected()
    {
        var s=Equipped();Supply(s);StartBoth(s);Assert.Empty(s.Engine.ObserveController(new(2)).Enterprises);Assert.DoesNotContain(s.Engine.ObserveController(new(2)).EmploymentGroups,g=>g.OwnerId.Value==1);
        var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;root["workshops"]!["additional"]![0]!["unit"]!["batch"]!["terms"]!["wageMinor"]=0;
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(Definition(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact] public void SaveInsideInstallationRestoresRemainingTimeAndNeverPaysTwice()
    {
        var s=new GameSession(new(Definition()));var e=Enterprise(s);StorageC1Tests.Harvest(s,"plank",6);StorageC1Tests.Put(s,e.InputId,"plank",6);StorageC1Tests.Harvest(s,"steel",1);StorageC1Tests.Put(s,e.InputId,"steel",1);
        string command=s.Send(new(1),new(ActionKind.OutfitWorkshop,Enterprise:new(8)));s.Step();var w=Enterprise(s).Installations.Single();s.AdvanceTo(w.EndTick-1);Assert.Single(Lines(s));
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();restored.Step();Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(2,Lines(s).Length);Assert.Equal(5,Lines(s).Sum(x=>x.Staff));
        var envelope=s.CommandTape.Single(t=>t.Command.CommandId==command).Command;s.Engine.Enqueue(new(1),envelope);Assert.Equal(15000,s.Engine.Ledger().Where(l=>l.Reason=="WorkshopOutfit").Sum(l=>l.AmountMinor));
        var downgraded=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;downgraded["schemaVersion"]=20;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(downgraded.ToJsonString(SimulationJson.Options)));
    }
    [Fact] public void SimultaneousCompletionIsExactlyOnceOnEitherSideOfSaveBoundary()
    {
        var s=Equipped(Definition(staff:4,ticks:1));Supply(s,8,0);StartBoth(s,false);Assert.All(Lines(s),w=>Assert.Equal(45,w.WorkDone));
        for(int n=0;n<7;n++){var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.All(Lines(s),w=>Assert.Equal(2,w.CompletedBatches));Assert.Equal(8,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Produced);
    }
    [Fact] public void SharedWalletCannotPayOrConsumeTheSameBudgetTwice()
    {
        var d=Definition(staff:4,ticks:1);d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{CashMinor=15040}:p).ToArray()};var s=Equipped(d);Supply(s,4,0);StartBoth(s,false);
        Assert.Equal(0,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Single(Lines(s).Where(w=>w.BatchId!=0));Assert.Equal(45,Lines(s).Sum(w=>w.WorkDone));Assert.Equal(2,StorageC1Tests.Inventory(s,Enterprise(s).InputId).Stock.Single(i=>i.ItemId=="wood").Quantity);s.Engine.ValidateInvariants();
    }
    [Fact] public void EmptyEquippedGroupHiresThroughOrdinaryGradualLaborOnly()
    {
        var d=Definition(staff:0);d=d with{Labor=new(GrowthPerPeriod:0)};var s=Equipped(d);Supply(s,10,10);Assert.All(Lines(s),w=>Assert.Equal(0,w.Staff));
        Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),WorkshopId:Second(s),RecipeId:"d1-bread"));s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:Second(s),Enabled:true));s.Step();
        long next=(s.Engine.Tick/10+1)*10;s.AdvanceTo(next);Assert.All(Lines(s),w=>Assert.InRange(w.Staff,1,2));Assert.Equal(10,s.Engine.Inspect().Unemployed+s.Engine.Inspect().EmploymentGroups.Sum(g=>g.Headcount));s.AdvanceTo(next+80);Assert.All(Lines(s),w=>Assert.True(w.CompletedBatches>0));
    }
    [Fact] public void ExistingTruckShipsBothWorkshopProductsUsingSharedFiniteOutput()
    {
        var d=Definition(staff:0,ticks:2);d=d with{Labor=new(GrowthPerPeriod:0),Logistics=new(PurchasePriceMinor:1000,CargoCapacity:10,MinimumCrew:1,CrewCapacity:1)};
        var s=Equipped(d);Supply(s,4,4);Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),WorkshopId:Second(s),RecipeId:"d1-bread"));Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:Second(s),Enabled:true));s.AdvanceTo(s.Engine.Tick+80);
        Step(s,new(ActionKind.BuyLogistics,Enterprise:new(8)));var l=s.Engine.Observe(new(1)).OwnLogistics.Single();var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Step(s,new(ActionKind.SetLogisticsStorage,Logistics:l.Id,Destination:shop.InputId,Enabled:true));s.AdvanceTo(s.Engine.Tick+150);
        var stock=StorageC1Tests.Inventory(s,shop.InputId).Stock;Assert.Equal(4,stock.Single(i=>i.ItemId=="plank").Quantity);Assert.Equal(2,stock.Single(i=>i.ItemId=="bread").Quantity);Assert.Equal(0,StorageC1Tests.Inventory(s,Enterprise(s).OutputId).OccupiedVolume);s.Engine.ValidateInvariants();
    }
    [Fact] public void BaselineUsesWorkshopCommandsAndRestoresItsPlansWithoutWorldAccess()
    {
        var s=Equipped();Supply(s);Step(s,new(ActionKind.SetRecipe,Enterprise:new(8),WorkshopId:Second(s),RecipeId:"d1-bread"));
        var controller=new Agents.Algorithmic.AlgorithmicController(new(1,"wood",HumanActorId:2,ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false),42,[1,2]);
        ImmutableArray<CommandResult> results=[];bool resumed=false,assigned=false;
        for(int n=0;n<20;n++)
        {
            var observation=s.Engine.ObserveController(new(1));var restored=Agents.Algorithmic.AlgorithmicController.Restore(controller.Capture());var a=controller.Decide(observation,results);var b=restored.Decide(observation,results);
            Assert.Equal(JsonSerializer.Serialize(a,SimulationJson.Options),JsonSerializer.Serialize(b,SimulationJson.Options));
            var commands=new List<string>();foreach(var action in a.Actions){resumed|=action.Kind==ActionKind.SetProductionEnabled&&action.WorkshopId==Second(s);assigned|=action.Kind==ActionKind.AssignWorkshopWorkers;string id=s.Send(new(1),action);controller.CommandSubmitted(id);commands.Add(id);}s.Step();results=[..commands.Select(id=>s.Engine.Result(id)!)];Assert.All(results,r=>Assert.Equal(ResultStatus.Accepted,r.Status));
        }
        Assert.True(resumed);Assert.True(assigned);Assert.Equal(5,Lines(s).Sum(w=>w.Staff));s.Engine.ValidateInvariants();
    }
    [Fact] public void FivePaidLinesShareOneOutputAndOneConservedWorkforce()
    {
        var d=Definition();var type=d.BuildingTypes!.Single(t=>t.Id=="d1-prepared");Cell[] cells=[new(0,0),new(1,0),new(2,0),new(0,1),new(1,1),new(2,1)];
        type=type with{Footprint=cells,Levels=Enumerable.Range(1,5).Select(n=>type.Levels[Math.Min(1,n-1)] with{Level=n,WorkshopSlots=n}).ToArray()};
        d=d with{BuildingTypes=d.BuildingTypes!.Select(t=>t.Id==type.Id?type:t).ToArray(),Buildings=d.Buildings!.Select(b=>b.Id==8?b with{Level=5}:b).ToArray(),Plots=[..d.Plots!,new(8,new(0,1),10000,OwnerId:1),new(9,new(1,1),10000,OwnerId:1),new(10,new(2,1),10000,OwnerId:1)]};
        var s=Equipped(d);StorageC1Tests.Harvest(s,"plank",18);StorageC1Tests.Put(s,Enterprise(s).InputId,"plank",18);StorageC1Tests.Harvest(s,"steel",3);StorageC1Tests.Put(s,Enterprise(s).InputId,"steel",3);
        for(int n=0;n<3;n++)Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(8)));s.AdvanceTo(Enterprise(s).Installations.Max(i=>i.EndTick));Assert.Equal(5,Lines(s).Length);Assert.Equal(60000,s.Engine.Ledger().Where(l=>l.Reason=="WorkshopOutfit").Sum(l=>l.AmountMinor));
        foreach(var w in Lines(s).Where(w=>w.Id!=8))Step(s,new(ActionKind.AssignWorkshopWorkers,Enterprise:new(8),WorkshopId:w.Id,Group:new(8),Quantity:1));Supply(s,10,0);
        foreach(var w in Lines(s))s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:new(8),WorkshopId:w.Id,Enabled:true));s.Step();Assert.All(Lines(s),w=>{Assert.Equal(1,w.Staff);Assert.Equal(25,w.WorkDone);});Assert.Equal(10,StorageC1Tests.Inventory(s,Enterprise(s).OutputId).ReservedCapacity);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+79);restored.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.All(Lines(s),w=>Assert.Equal(1,w.CompletedBatches));Assert.Equal(10,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Produced);Step(s,new(ActionKind.OutfitWorkshop,Enterprise:new(8)),false);
    }
}

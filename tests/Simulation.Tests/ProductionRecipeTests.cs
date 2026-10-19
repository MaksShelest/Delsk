using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ProductionRecipeTests
{
    // Synthetic regression components, not the unselected ordinary P48 content.
    internal static ScenarioDefinition Scenario(int components=2,long capacity=12,long output=10)=>Fixtures.Cycle() with {
        Id="multi-input-regression",MaxTicks=2000,Population=2,
        Items=[new("wood",1,500),new("input-b",2,700),new("input-c",1,900),new("plank",1,3000)],
        Recipes=[new("mix",OutputItem:"plank",OutputQuantity:1,FullStaffTicks:6,RequiredStaff:2,MinimumStaff:1,EnergyPerBatchMinor:60,
            Inputs:components==2?[new("wood",2),new("input-b",1)]:[new("wood",2),new("input-b",1),new("input-c",1)])],
        Enterprises=[new(1,1,new(4,0),"mix",capacity,output,2,2,10)],
        ResourceNodes=[new("wood",new(0,0)),new("input-b",new(1,0)),new("input-c",new(2,0))],
        CapitalOffers=[new(1,"plank",20,3000,new(8,0))],HarvestTicksPerUnit=1};
    internal static string Do(GameSession s,GameAction a,long actor=1)=>LandContentFixtures.Reason(s,a,actor);
    internal static EnterpriseView Factory(GameSession s)=>s.Engine.Observe(new(1)).OwnEnterprises.Single();
    internal static void Supply(GameSession s,string item,long quantity)
    {
        var node=s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId==item);LandContentFixtures.Move(s,node.Position);
        Assert.Equal("Accepted",Do(s,new(ActionKind.Harvest,ItemId:item,Quantity:quantity)));Fixtures.UntilIdle(s);LandContentFixtures.Move(s,new(4,0));
        Assert.Equal("Accepted",Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:Factory(s).InputId,ItemId:item,Quantity:quantity)));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();
    }
    internal static ScenarioDefinition LandScenario(long capacity=12)
    {
        var d=Scenario(capacity:capacity);var level=new BuildingLevelDefinition(1,"mix",1,[],capacity,10,2,10);
        return d with {Enterprises=[],BuildingTypes=[new("mixer",BuildingKind.Production,[new(0,0)],[level,level with {Level=2,AdditionalRecipeIds=["alternative"]}]),
                new("store",BuildingKind.Warehouse,[new(0,0)],[new(1,"",1,[],200,0,0,0)])],
            Recipes=[..d.Recipes,new("alternative",OutputItem:"plank",OutputQuantity:2,FullStaffTicks:4,RequiredStaff:2,MinimumStaff:1,EnergyPerBatchMinor:80,
                Inputs:[new("wood",1),new("input-b",1),new("input-c",1)])],
            Plots=[new(1,new(4,0),1,OwnerId:1),new(2,new(6,0),1,OwnerId:1),new(3,new(6,1),1,OwnerId:1)],
            Buildings=[new(1,1,new(4,0),"mixer",Staff:2),new(2,1,new(6,0),"store"),new(3,1,new(6,1),"store")]};
    }
    private static void OneBatch(GameSession s)
    {
        long before=Factory(s).CompletedBatches;Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
        for(int i=0;Factory(s).CompletedBatches==before;i++){Assert.True(i<100);s.Step();}
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:false));
    }
    [Theory] [InlineData(2)] [InlineData(3)]
    public void CompleteIngredientSetIsConsumedAtomicallyWithPinnedWipAndAllValue(int count)
    {
        var d=Scenario(count);var s=new GameSession(new(d));foreach(var i in d.Recipes[0].Ingredients)Supply(s,i.ItemId,i.Quantity);
        Assert.Equal("Accepted",Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true)));
        var e=Factory(s);Assert.Equal("mix",e.BatchRecipeId);Assert.True(d.Recipes[0].Ingredients.SequenceEqual(e.BatchInputs));
        Assert.All(d.Recipes[0].Ingredients,i=>Assert.Equal(0,Fixtures.Count(s,e.InputId,i.ItemId)));
        Assert.Equal(d.Recipes[0].Ingredients.Sum(i=>i.Quantity*d.Items.Single(x=>x.Id==i.ItemId).BasePriceMinor),s.Engine.CapitalOf(new(1)).WorkInProgressMinor);
        OneBatch(s);Assert.Equal(1,Factory(s).CompletedBatches);Assert.Equal(1,Fixtures.Count(s,e.OutputId,"plank"));Assert.Empty(Factory(s).BatchInputs);
        Assert.All(d.Recipes[0].Ingredients,i=>Assert.Equal(i.Quantity,s.Engine.Inspect().Flows.Single(f=>f.ItemId==i.ItemId).RecipeInput));
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact]
    public void MissingIngredientPreservesAllPresentStockMoneyAndNoOutputHold()
    {
        var s=new GameSession(new(Scenario(3)));Supply(s,"wood",2);Supply(s,"input-c",1);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));s.AdvanceTo(s.Engine.Tick+15);
        Assert.Equal("NoInputs",Factory(s).ReasonCode);Assert.Equal(0,Factory(s).BatchId);Assert.Equal(2,Fixtures.Count(s,Factory(s).InputId,"wood"));Assert.Equal(1,Fixtures.Count(s,Factory(s).InputId,"input-c"));
        Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(0,s.Engine.Inspect().Inventories.Single(i=>i.Id==Factory(s).OutputId).ReservedCapacity);
        Assert.All(s.Engine.Inspect().Flows,f=>Assert.Equal(0,f.RecipeInput));
    }
    [Fact]
    public void FullOutputRollsBackEveryIngredientAndWageThenResumesWhenSpaceReturns()
    {
        var s=new GameSession(new(Scenario(output:1)));Supply(s,"wood",4);Supply(s,"input-b",2);OneBatch(s);
        var e=Factory(s);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Do(s,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true));s.AdvanceTo(s.Engine.Tick+8);
        Assert.Equal("NoOutputSpace",Factory(s).ReasonCode);Assert.Equal(2,Fixtures.Count(s,e.InputId,"wood"));Assert.Equal(1,Fixtures.Count(s,e.InputId,"input-b"));Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Do(s,new(ActionKind.Transfer,Source:e.OutputId,Destination:s.Engine.Observe(new(1)).Participant.InventoryId,ItemId:"plank",Quantity:1));
        for(int i=0;Factory(s).CompletedBatches<2;i++){Assert.True(i<100);s.Step();}Assert.Equal(2,Factory(s).CompletedBatches);
    }
    [Theory] [InlineData(2)] [InlineData(3)]
    public void PausedWipRestoresWithoutSecondConsumptionOrWageAndReplays(int count)
    {
        var d=Scenario(count);var s=new GameSession(new(d));foreach(var i in d.Recipes[0].Ingredients)Supply(s,i.ItemId,i.Quantity);
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:false));
        var before=Factory(s);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(s.Engine.Tick+10);copy.AdvanceTo(s.Engine.Tick);
        Assert.Equal(before.WorkDone,Factory(s).WorkDone);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        OneBatch(s);OneBatch(copy);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        Assert.All(d.Recipes[0].Ingredients,i=>Assert.Equal(i.Quantity,s.Engine.Inspect().Flows.Single(f=>f.ItemId==i.ItemId).RecipeInput));
    }
    [Theory] [InlineData("recipeId")] [InlineData("inputs")]
    public void ForgedBatchRecipeOrIngredientReceiptIsRejectedBeforeHash(string field)
    {
        var s=new GameSession(new(Scenario()));Supply(s,"wood",2);Supply(s,"input-b",1);Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
        var root=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;var batch=root["enterprises"]![0]!["batch"]!;
        if(field=="recipeId")batch[field]="unknown";else batch[field]![0]!["quantity"]=99;
        var error=Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(root.ToJsonString(SimulationJson.Options)));Assert.NotEqual("StateHashMismatch",error.ReasonCode);
    }
    [Fact]
    public void BaselineSuppliesEveryComponentWithoutFillingSharedInputWithOnlyOne()
    {
        var d=Scenario(capacity:4);var s=new GameSession(new(d));s.AttachControllers([new AlgorithmicController(new(1,"wood",ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false),42,[1,2])]);s.AdvanceTo(300);
        Assert.True(Factory(s).CompletedBatches>=2);Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.Harvest && c.Command.Action.ItemId=="input-b");
        Assert.DoesNotContain(s.ControllerTurns.SelectMany(t=>t.Feedback),r=>r.ReasonCode=="NoCapacity");
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,300).Engine.ComputeStateHash());
    }
    [Fact]
    public void AmbiguousDuplicateOrNonpositiveIngredientsAndTooSmallSharedInputAreInvalid()
    {
        var d=Scenario();var r=d.Recipes[0];
        foreach(var bad in new[]{r with {InputItem="wood",InputQuantity=2},r with {Inputs=[]},r with {Inputs=[new("wood",1),new("wood",2)]},r with {Inputs=[new("wood",0)]}})
            Assert.Throws<ArgumentException>(()=>new SimulationEngine(d with {Recipes=[bad]}));
        // Legacy enterprises also need enough shared volume for one complete input set.
        Assert.Throws<ArgumentException>(()=>new SimulationEngine(Scenario(capacity:3)));
    }

    [Fact]
    public void UpgradeUnlocksAlternativeAndPreservesPausedBatchUntilItCompletes()
    {
        var d=LandScenario();var s=new GameSession(new(d));
        Assert.Equal("RecipeLocked",Do(s,new(ActionKind.SetRecipe,Enterprise:new(1),RecipeId:"alternative")));
        Supply(s,"wood",2);Supply(s,"input-b",1);Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:false));
        var before=Factory(s);Assert.Equal("ProductionNotSafe",Do(s,new(ActionKind.SetRecipe,Enterprise:new(1),RecipeId:"alternative")));
        Assert.Equal("ProductionNotSafe",Do(s,new(ActionKind.UpgradeBuilding,Building:new(1))));Assert.Equal(before.BatchRecipeId,Factory(s).BatchRecipeId);
        OneBatch(s);Assert.Equal("Accepted",Do(s,new(ActionKind.UpgradeBuilding,Building:new(1))));
        Assert.Equal("Accepted",Do(s,new(ActionKind.SetRecipe,Enterprise:new(1),RecipeId:"alternative")));
        Supply(s,"wood",1);Supply(s,"input-b",1);Supply(s,"input-c",1);OneBatch(s);
        Assert.Equal(3,Fixtures.Count(s,Factory(s).OutputId,"plank"));Assert.Equal(2,Factory(s).CompletedBatches);
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact]
    public void HiringRequiresAllComponentsBeforeOfferingReadyVacancies()
    {
        var d=Scenario() with {Labor=new(StepTicks:1,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0)};
        d=d with {Enterprises=d.Enterprises.Select(e=>e with {Staff=0}).ToArray()};var s=new GameSession(new(d));
        Supply(s,"wood",2);Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));s.AdvanceTo(s.Engine.Tick+8);
        Assert.False(s.Engine.Observe(new(1)).OwnEmploymentGroups.Single().Ready);Assert.Equal(0,Factory(s).Staff);
        Supply(s,"input-b",1);s.AdvanceTo(s.Engine.Tick+20);Assert.Equal(1,Factory(s).CompletedBatches);
    }
    [Fact]
    public void SharedInboundQueueBalancesTwoIngredientsAndRestoresExistingTruck()
    {
        var d=LandScenario(capacity:5) with {Population=4,Labor=new(StepTicks:1,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0)};
        var s=new GameSession(new(d));
        foreach(var (item,q,id) in new[]{("wood",8L,2L),("input-b",4L,3L)})
        {
            var node=d.ResourceNodes.Single(n=>n.ItemId==item);LandContentFixtures.Move(s,node.Position);Do(s,new(ActionKind.Harvest,ItemId:item,Quantity:q));Fixtures.UntilIdle(s);
            var b=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==id);LandContentFixtures.Move(s,b.Position);
            Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:b.InputId,ItemId:item,Quantity:q));
        }
        LandContentFixtures.Move(s,new(4,0));Assert.Equal("Accepted",Do(s,new(ActionKind.BuyLogistics,Enterprise:new(1))));var l=s.Engine.Observe(new(1)).OwnLogistics.Single();
        foreach(long id in new[]{2L,3L})Assert.Equal("Accepted",Do(s,new(ActionKind.QueueLogisticsJob,Logistics:l.Id,Source:s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==id).InputId,Enabled:true)));
        Do(s,new(ActionKind.SetLogisticsEnabled,Logistics:l.Id,Direction:LogisticsDirection.Inbound,Enabled:true));
        Do(s,new(ActionKind.SetLogisticsStorage,Logistics:l.Id,Destination:s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==2).InputId,Enabled:true));
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
        for(int i=0;s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is null;i++){Assert.True(i<40);s.Step();}
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(250);copy.AdvanceTo(250);
        Assert.Equal(4,Factory(s).CompletedBatches);Assert.True(s.Engine.Observe(new(1)).OwnLogistics.Single().CompletedTrips>=8);
        Assert.Equal(4,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="input-b").RecipeInput);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,250).Engine.ComputeStateHash());
    }

    [Fact]
    public void BaselineCanSelectUnlockedRecipeUsingEveryInputCostAndRestoreThePlan()
    {
        var d=LandScenario();d=d with {Buildings=d.Buildings!.Select(b=>b.Id==1?b with {Level=2}:b).ToArray()};var s=new GameSession(new(d));
        var policy=new AlgorithmicController(new(1,"wood",ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false,BaseWageMinor:10),42,[1,2]);
        var observation=s.Engine.ObserveController(new(1));var decision=policy.Decide(observation,[]);var action=Assert.Single(decision.Actions);
        Assert.Equal(ActionKind.SetRecipe,action.Kind);Assert.Equal("alternative",action.RecipeId);
        var copy=AlgorithmicController.Restore(policy.Capture());Assert.Equal(JsonSerializer.Serialize(policy.Capture(),SimulationJson.Options),JsonSerializer.Serialize(copy.Capture(),SimulationJson.Options));
        Assert.Equal("Accepted",Do(s,action));Assert.Equal("alternative",Factory(s).RecipeId);
        var expensive=d with {Items=d.Items.Select(i=>i.Id=="input-c"?i with {BasePriceMinor=10000}:i).ToArray()};
        var other=new GameSession(new(expensive));var candidate=new AlgorithmicController(new(1,"wood",ExpansionWeight:0,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false,BaseWageMinor:10),42,[1,2]);
        Assert.DoesNotContain(candidate.Decide(other.Engine.ObserveController(new(1)),[]).Actions,a=>a.Kind==ActionKind.SetRecipe);
    }
}

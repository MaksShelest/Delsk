using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class FoodChainTests
{
    internal static ScenarioDefinition Definition()=>JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("food-chain"),SimulationJson.Options)!;
    private static GameSession Record(){var d=Definition();var s=Fixtures.Session(d);ScriptedScenarios.FoodChain(s,d);return s;}
    [Fact]
    public void ThreeSpecializationsBuildProduceBuyMissingSugarAndPhysicallySellToFundedDemand()
    {
        var s=Record();var w=s.Engine.Inspect();s.Engine.ValidateInvariants();
        Assert.Equal(2,w.Flows.Single(f=>f.ItemId=="pastry").Produced);Assert.Equal(2,w.Flows.Single(f=>f.ItemId=="pastry").Exported);
        Assert.Equal(2,w.Flows.Single(f=>f.ItemId=="sugar").RecipeInput);Assert.Equal(2,w.Flows.Single(f=>f.ItemId=="cheese").Exported);
        Assert.All(w.MarketAllocations,a=>Assert.Equal("Fulfilled",a.Status));Assert.Contains(w.Enterprises,e=>e.RecipeId=="sugar-2" && e.Level==2);
        Assert.All(new[]{"cheese","sugar","pastry"},item=>Assert.Contains(w.Settlements,r=>r.ItemId==item && r.PaidMinor>0));
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.Build && c.Command.Action.BuildingTypeId=="confectionery");
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
    [Theory] [InlineData("Wip")][InlineData("PaidPickup")][InlineData("Alternative")]
    public void CriticalChainSaveRestoresEveryTickAndExactlyOnceTransfer(string stage)
    {
        var baseline=Record();var s=Fixtures.Session(baseline.Engine.Configuration());s.ConfigurePlayback(baseline.CommandTape,baseline.Engine.Tick,"food-chain");
        bool At()=>stage switch {"Wip"=>s.Engine.Inspect().Enterprises.Any(e=>e.BatchInputs.Length==3),"PaidPickup"=>s.Engine.Inspect().MarketAllocations.Any(a=>a.ItemId=="sugar" && a.RemainingQuantity>0),_=>s.Engine.Inspect().Enterprises.Any(e=>e.RecipeId=="sugar-2" && e.BatchId!=0)};
        while(!At()){Assert.True(s.Engine.Tick<baseline.Engine.Tick);s.Step();}
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.Tick<baseline.Engine.Tick){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(baseline.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Theory] [InlineData("one-v3")][InlineData("two-v3")][InlineData("three-v2")]
    public void VersionedMapsOfferReachableRawSuppliesAndKeepHistoricalGeometry(string name)
    {
        var raw=JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("presentation-map-"+name),SimulationJson.Options)!;
        var d=EconomyProfileTests.Profile().Apply(raw);var s=Fixtures.Session(d);var map=new GridMap(d);
        foreach(string item in new[]{"milk","beet"})
        {
            Assert.Equal(5,d.ResourceNodes.Count(n=>n.ItemId==item));var node=d.ResourceNodes.Single(n=>n.ItemId==item && n.Rules!.Public);
            Assert.NotEmpty(map.FindPath(d.Participants[0].Position,node.Position));LandContentFixtures.Move(s,node.Position);
            Assert.Equal("Accepted",ProductionRecipeTests.Do(s,new(ActionKind.Harvest,ItemId:item,Quantity:1)));Fixtures.UntilIdle(s);
            Assert.Equal(1,Fixtures.Count(s,s.Engine.Observe(new(1)).Participant.InventoryId,item));
            var owned=d.ResourceNodes.First(n=>n.ItemId==item && !n.Rules!.Public);LandContentFixtures.Move(s,owned.Position);
            Assert.Equal("NoResourceRights",ProductionRecipeTests.Do(s,new(ActionKind.Harvest,ItemId:item,Quantity:1)));
            Assert.Equal("Accepted",ProductionRecipeTests.Do(s,new(ActionKind.BuyResource,Target:owned.Position)));
        }
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(d.Id,restored.Engine.Configuration().Id);
        Assert.All(new[]{"milk","beet","cheese","sugar","pastry"},item=>Assert.Contains(s.Engine.Inspect().Flows,f=>f.ItemId==item));
        Assert.DoesNotContain(d.EquipmentTypes!,e=>e.ItemId is "milk" or "beet");
    }
    [Theory] [InlineData("dairy","milk")][InlineData("sugar-factory","beet")]
    public void BaselineRunsNewFactoryWithoutLlmOrSpecialCommandsAndRestoresItsPlans(string type,string input)
    {
        var d=Definition();d=d with {Buildings=[new(1,1,new(4,0),type)],Plots=d.Plots!.Select(p=>p.Id==1?p with {OwnerId=1}:p).ToArray()};
        var s=Fixtures.Session(d);s.AttachControllers([new AlgorithmicController(new(1,input,ExpansionWeight:0,TradeEnabled:false,PoliticsEnabled:false,LogisticsEnabled:false),42,[1,2,3])]);s.AdvanceTo(100);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));loaded.RestoreControllers(AlgorithmicController.Restore);
        while(s.Engine.Tick<350){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());}
        Assert.True(s.Engine.Inspect().Enterprises.Single().CompletedBatches>1);Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.Harvest && c.Command.Action.ItemId==input);
    }
    [Fact]
    public void AlternativeMethodsPayForAllInputsEnergyAndNumericTeamAndLargePlantNeedsDemand()
    {
        var p=EconomyProfileTests.Profile();var w=p.World;
        long Invest(string id){var l=w.BuildingTypes.Single(b=>b.Id==id).Levels[0];return w.PlotPriceMinor+l.PriceMinor+l.Materials.Sum(m=>m.Quantity*w.Items.Single(i=>i.Id==m.ItemId).BasePriceMinor);}
        Assert.InRange((decimal)Invest("confectionery")/Invest("dairy"),2m,3m);
        foreach(string type in new[]{"dairy","sugar-factory","confectionery"})
        {
            var levels=w.BuildingTypes.Single(b=>b.Id==type).Levels;
            long Profit(int level,long price){var l=levels[level-1];var r=w.Recipes.Single(r=>r.Id==(level==1?l.RecipeId:l.AdditionalRecipeIds.Single()));return r.OutputQuantity*price-r.Ingredients.Sum(i=>i.Quantity*w.Items.Single(x=>x.Id==i.ItemId).BasePriceMinor)-r.EnergyPerBatchMinor-l.StaffCapacity*l.WageMinor*((r.FullStaffTicks+l.WorkRateMultiplier-1)/l.WorkRateMultiplier);}
            var recipe=w.Recipes.Single(r=>r.Id==levels[0].RecipeId);long price=w.Items.Single(i=>i.Id==recipe.OutputItem).BasePriceMinor;
            Assert.True(Profit(1,price)>0);Assert.True(Profit(2,price)>Profit(1,price));Assert.True(Profit(1,0)<0);Assert.True(Profit(2,0)<0);
        }
    }
    [Theory] [InlineData("dairy")][InlineData("sugar-factory")]
    public void UpgradeMarginUsesActualPayrollEnergyAndPaidSalesInsteadOfOutputQuantity(string type)
    {
        var w=EconomyProfileTests.Profile().World;var levels=w.BuildingTypes.Single(b=>b.Id==type).Levels;
        var margins=new List<decimal>();
        foreach(int level in new[]{1,2})
        {
            var l=levels[level-1];var r=w.Recipes.Single(r=>r.Id==(level==1?l.RecipeId:l.AdditionalRecipeIds.Single()));
            long price=w.Items.Single(i=>i.Id==r.OutputItem).BasePriceMinor;
            var d=ProductionRecipeTests.Scenario() with {Items=w.Items,Recipes=w.Recipes,Population=l.StaffCapacity,
                Enterprises=[],BuildingTypes=[w.BuildingTypes.Single(b=>b.Id==type)],Plots=[new(1,new(4,0),w.PlotPriceMinor,OwnerId:1)],Buildings=[new(1,1,new(4,0),type,Level:level,Staff:l.StaffCapacity)],
                ResourceNodes=[new(r.Ingredients.Single().ItemId,new(0,0))],CapitalOffers=[new(1,r.OutputItem,4*r.OutputQuantity,price,new(8,0))]};
            var run=Fixtures.Session(d);if(level==2)Assert.Equal("Accepted",ProductionRecipeTests.Do(run,new(ActionKind.SetRecipe,Enterprise:new(1),RecipeId:r.Id)));var ingredient=r.Ingredients.Single();long remaining=4*ingredient.Quantity;
            while(remaining>0){long take=Math.Min(remaining,8);ProductionRecipeTests.Supply(run,ingredient.ItemId,take);remaining-=take;}
            ProductionRecipeTests.Do(run,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
            for(int i=0;ProductionRecipeTests.Factory(run).CompletedBatches<4;i++){Assert.True(i<100);run.Step();}
            ProductionRecipeTests.Do(run,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:false));
            var e=ProductionRecipeTests.Factory(run);var carrier=run.Engine.Observe(new(1)).Participant.InventoryId;
            ProductionRecipeTests.Do(run,new(ActionKind.Transfer,Source:e.OutputId,Destination:carrier,ItemId:r.OutputItem,Quantity:4*r.OutputQuantity));Fixtures.UntilIdle(run);
            LandContentFixtures.Move(run,new(8,0));Assert.Equal("Accepted",ProductionRecipeTests.Do(run,new(ActionKind.SellAvailableToCapital,ItemId:r.OutputItem,Quantity:4*r.OutputQuantity,OfferId:1)));Fixtures.UntilIdle(run);
            long raw=4*ingredient.Quantity*w.Items.Single(i=>i.Id==ingredient.ItemId).BasePriceMinor;
            long wages=run.Engine.EventsSince(0).Where(e=>e.Type=="WagesPaid").Sum(e=>e.AmountMinor);
            long profit=run.Engine.Inspect().Settlements.Sum(x=>x.PaidMinor)-raw-wages-e.EnergyPaidMinor;
            long duration=(r.FullStaffTicks+l.WorkRateMultiplier-1)/l.WorkRateMultiplier;
            Assert.Equal(4*l.StaffCapacity*l.WageMinor*duration,wages);Assert.Equal(4*r.EnergyPerBatchMinor,e.EnergyPaidMinor);Assert.True(profit>0);
            margins.Add((decimal)profit/(4*duration));run.Engine.ValidateInvariants();
        }
        Assert.True(margins[1]>margins[0]);var upgrade=levels[1];
        decimal investment=upgrade.PriceMinor+upgrade.Materials.Sum(m=>m.Quantity*w.Items.Single(i=>i.Id==m.ItemId).BasePriceMinor);
        // Optimistic throughput payback assumes every additional unit is sold; finite quota is separate.
        Assert.InRange(investment/(margins[1]-margins[0]),1m,1000m);
    }

    [Fact]
    public void ThreeBaselineSpecializationsPurchaseInputsAndRestoreWithoutControllerOrderAdvantage()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("baseline-food"),SimulationJson.Options)!;
        var settings=JsonSerializer.Deserialize<AutonomousConfiguration>(EconomyProfileTests.Text("baseline-food-controllers"),SimulationJson.Options)!;
        GameSession Start(bool reverse)
        {
            var s=Fixtures.Session(d);var controllers=settings.Npcs.Select(n=>new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))).ToArray();
            s.AttachControllers(reverse?controllers.Reverse():controllers);return s;
        }
        var a=Start(false);a.AdvanceTo(85);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(a));loaded.RestoreControllers(AlgorithmicController.Restore);
        while(a.Engine.Tick<450){a.Step();loaded.Step();Assert.Equal(a.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(a.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());}
        Assert.All(a.Engine.Inspect().Enterprises,e=>Assert.True(e.CompletedBatches>0));
        Assert.Contains(a.Engine.Inspect().DeliveryContracts,c=>c.ItemId=="sugar" && c.Status=="Fulfilled");
        Assert.True(a.Engine.Inspect().Flows.Single(f=>f.ItemId=="pastry").Exported>0);
        var reversed=Start(true);reversed.AdvanceTo(450);Assert.Equal(a.Engine.ComputeStateHash(),reversed.Engine.ComputeStateHash());Assert.Equal(a.CommandTape,reversed.CommandTape);
        Assert.Equal(a.Engine.ComputeStateHash(),GameSession.Replay(d,a.CommandTape,450).Engine.ComputeStateHash());
    }

}

using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MapRuntimeTests
{
    private static void Idle(GameSession s)
    {int limit=300;while(s.Engine.Inspect().Participants.Single(p=>p.Id.Value==1).Busy && limit-->0)s.Step();Assert.True(limit>0);}
    private static void Do(GameSession s,GameAction action,long actor=1)
    {string id=s.Send(new(actor),action);s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);}
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void ZeroCashPhysicallyHarvestsEveryPublicResourceAndSellsAtTheTerminal(string id)
    {
        var config=MapDesignTests.Resolve(id).Scenario;config=config with{Participants=config.Participants.Select(p=>p.Id==1?p with{CashMinor=0}:p).ToArray(),Elections=null};
        // Scenario fixture changes only initial cash/election horizon. No stock or production is injected.
        var s=new GameSession(new(config));Assert.Equal(0,PlayerDesk.Read(s.Engine,new(1)).AvailableCashMinor);
        foreach(var node in config.ResourceNodes.Where(n=>n.Rules!.Public).GroupBy(n=>n.ItemId).Select(g=>g.First()))
        {
            Do(s,new(ActionKind.MoveTo,Target:node.Position));Idle(s);
            Do(s,new(ActionKind.Harvest,ItemId:node.ItemId,Quantity:1));Idle(s);
            var pack=PlayerDesk.Read(s.Engine,new(1)).Inventories.Single(i=>i.Id==PlayerDesk.Read(s.Engine,new(1)).Participant.InventoryId);Assert.Contains(pack.Stock,x=>x.ItemId==node.ItemId&&x.Quantity==1);
        }
        Do(s,new(ActionKind.MoveTo,Target:config.CapitalDemand!.Terminal));Idle(s);
        // Existing finite spot demand, using its current published price/quota and ordinary sale command.
        foreach(var item in config.ResourceNodes.Select(n=>n.ItemId).Distinct())
        {var offer=s.Engine.Inspect().CapitalOffers.FirstOrDefault(o=>o.ItemId==item&&o.RemainingQuantity>0);Assert.NotNull(offer);{Do(s,new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:item,Quantity:1));Idle(s);}}
        Assert.True(PlayerDesk.Read(s.Engine,new(1)).AvailableCashMinor>0);Assert.All(s.Engine.Inspect().Flows.Where(f=>config.ResourceNodes.Any(n=>n.ItemId==f.ItemId)),f=>Assert.True(f.Harvested>=1&&f.Exported>=1));
        Assert.Equal(s.Engine.Inspect().InitialMoneyMinor+s.Engine.Inspect().ExternalMoneyInMinor-s.Engine.Inspect().ExternalMoneyOutMinor,s.Engine.Inspect().Accounts.Sum(a=>a.BalanceMinor));
    }
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void CheckpointDuringMovingHarvestAndLoadedBackpackReplaysExactGeometry(string id)
    {
        var d=MapDesignTests.Resolve(id).Scenario;var s=new GameSession(new(d));var node=d.ResourceNodes.First(n=>n.Rules!.Public&&n.ItemId=="wood");
        Do(s,new(ActionKind.MoveTo,Target:node.Position));CompareCheckpointToIdle(s);Idle(s);
        Do(s,new(ActionKind.Harvest,ItemId:node.ItemId,Quantity:2));Assert.NotNull(s.Engine.HarvestOf(new(1)));CompareCheckpointToIdle(s);Idle(s);
        Do(s,new(ActionKind.MoveTo,Target:d.CapitalDemand!.Terminal));Assert.Contains(PlayerDesk.Read(s.Engine,new(1)).Inventories.Single(i=>i.Id==PlayerDesk.Read(s.Engine,new(1)).Participant.InventoryId).Stock,x=>x.ItemId=="wood"&&x.Quantity==2);CompareCheckpointToIdle(s);Idle(s);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(d.Layout),JsonSerializer.Serialize(CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.Configuration().Layout));
    }
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void ExistingTruckUsesPhysicalMapPathsAndRestoresLoadedTrip(string id)
    {
        // Prepared legacy production/contract fixture on the exact new civic obstacles and industrial/market cells.
        // It tests the existing truck, not production templates absent from A2 until D.
        var map=MapDesignTests.Resolve(id).Scenario;var layout=map.Layout!;
        var legacy=JsonSerializer.Deserialize<ScenarioDefinition>(OrdinaryNewGameTests.Text("logistics-cycle.json"),SimulationJson.Options)!;
        Cell factory=layout.IndustrialLots[0].Cells[0],buyer=layout.MarketStalls[0].Cell,seller=layout.MarketStalls[1].Cell,wood=map.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        Cell Place(Cell p)=>p==new Cell(2,0)?factory:p==new Cell(5,0)?buyer:p==new Cell(0,3)?seller:wood;
        var config=legacy with{Id=id+"-legacy-truck-fixture",MapWidth=map.MapWidth,MapHeight=map.MapHeight,BlockedCells=map.BlockedCells,MaxTicks=5000,
            Participants=legacy.Participants.Select(p=>p with{Position=Place(p.Position)}).ToArray(),ResourceNodes=[new("wood",wood)],
            Buildings=legacy.Buildings!.Select(b=>b with{Position=Place(b.Position)}).ToArray(),Plots=legacy.Plots!.Select(p=>p with{Position=Place(p.Position)}).ToArray(),Delivery=legacy.Delivery! with{ContractTtlTicks=1000}};
        var s=new GameSession(new(config));Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:20));Idle(s);Do(s,new(ActionKind.MoveTo,Target:factory));Idle(s);
        var f=s.Engine.Observe(new(1)).OwnEnterprises.Single();var shop=s.Engine.Observe(new(2)).OwnBuildings.Single();
        Do(s,new(ActionKind.BuyLogistics,Enterprise:f.Id));Do(s,new(ActionKind.CreateBuyOffer,Destination:shop.InputId,ItemId:"plank",Quantity:10,UnitPriceMinor:3000),2);
        Do(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:s.Engine.Observe(new(2)).BuyOffers.Single().Id,Quantity:10));s.AdvanceTo(s.Engine.Tick+12);
        Do(s,new(ActionKind.Transfer,ItemId:"wood",Quantity:20,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:f.InputId));Idle(s);
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:f.Id,Enabled:true));int bound=300;
        while(s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches<10&&bound-->0)s.Step();Assert.True(bound>0);Do(s,new(ActionKind.SetProductionEnabled,Enterprise:f.Id,Enabled:false));
        var truck=s.Engine.Inspect().Logistics.Single();Do(s,new(ActionKind.SetLogisticsContract,Logistics:truck.Id,ContractId:s.Engine.Inspect().DeliveryContracts.Single().Id,Enabled:true));
        bound=600;var positions=new HashSet<Cell>();GameSession? restored=null;
        while(s.Engine.Inspect().Logistics.Single().CompletedTrips==0&&bound-->0)
        {
            var state=s.Engine.Inspect().Logistics.Single();if(state.Trip is {} trip){positions.Add(trip.Position);Assert.All(trip.Path,c=>Assert.DoesNotContain(c,config.BlockedCells));
                if(restored is null && s.Engine.Inspect().Inventories.Single(i=>i.Id==state.CargoInventory).Stock.Any(x=>x.Quantity>0))restored=CheckpointStore.Decode(CheckpointStore.Encode(s));}
            s.Step();restored?.Step();if(restored is not null)Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        }
        Assert.True(bound>0);Assert.NotNull(restored);Assert.True(positions.Count>10);Assert.Equal("Fulfilled",s.Engine.Inspect().DeliveryContracts.Single().Status);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(config,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    private static void CompareCheckpointToIdle(GameSession s)
    {var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));Idle(s);Idle(loaded);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
}

using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class ServiceF2Tests
{
    internal static EconomyProfile Profile()=>VNextServiceRunProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Definition()=>ServiceF1Tests.Definition() with{Economy=Profile().World.Rules,Market=Profile().World.Market,Delivery=Profile().World.Delivery};
    internal static void Step(GameSession s,GameAction a,long actor=2,bool accepted=true)=>ServiceF1Tests.Step(s,a,actor,accepted);
    internal static ServiceRun Run(GameSession s)=>s.Engine.InspectServices()!.Execution!.Runs.Single();
    internal static (GameSession Session,OwnedVehicle Vehicle,ServiceOrder Order) Prepare(long quantity=30,int? population=null)
    {
        var d=Definition();if(population is {} people)d=d with{Population=people};if(quantity>100)d=d with{Buildings=[..d.Buildings!.Select(b=>b.Id==9?b with{Level=2}:b)]};
        var s=new GameSession(new(d));StorageC1Tests.Harvest(s,"wood",quantity);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;var dest=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;StorageC1Tests.Put(s,source,"wood",quantity);
        var v=FleetE1Tests.Buy(s,"muravey70",2);Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:true));var order=ServiceF1Tests.Request(s,new(v.Id,source,dest,"wood",quantity));Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));
        return(s,v,s.Engine.InspectServices()!.Orders.Single());
    }
    internal static void Until(GameSession s,Func<bool> condition,int limit=2000)
    {int n=0;while(!condition()&&n++<limit)s.Step();Assert.True(condition(),"Delivery stalled: "+Run(s));}
    internal static void Board(GameSession s,OwnedVehicle v)
    {StorageC1Tests.Move(s,s.Engine.FleetOf(new(2))!.Vehicles.Single().Position,2);Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));}
    internal static void Navigate(GameSession s,long id)
    {Step(s,new(ActionKind.NavigateDeliveryService,ServiceId:id));Until(s,()=>!s.Engine.Observe(new(2)).Participant.Busy);}
    internal static void Load(GameSession s,long id)
    {Navigate(s,id);Step(s,new(ActionKind.PickupDeliveryService,ServiceId:id));Until(s,()=>Run(s).Stage=="ToDestination");}
    internal static void Deliver(GameSession s,long id)
    {Navigate(s,id);Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:id));Until(s,()=>Run(s).Cargo==0);}
    internal static void Exact(GameSession s,string name)
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        for(int i=0;i<3;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_F2_EVIDENCE") is not {} output)return;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(output,name+".commands.jsonl"),string.Join('\n',s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(output,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(output,name+".save.json"));
    }
    [Fact]public void NewProfileIsFrozenAndLegacyF1HasNoExecution()
    {
        var p=Profile();Assert.NotNull(p.World.Rules.ServiceExecution);Assert.Null(ServiceF1Tests.Profile().World.Rules.ServiceExecution);
        Assert.Equal(1200,p.World.Market.PickupTtlTicks);Assert.Equal(2400,p.World.Delivery.ContractTtlTicks);
        Assert.Equal(p.Id,EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).Id);
        foreach(string key in new[]{"small_river","large_districts"})
        {var s=new GameSession(new(OrdinaryNewGame.Resolve(MapDesignTests.Template(key),p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario));s.Step();Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());}
    }
    [Fact]public void CheckpointCannotInventLoadedGoodsReceiptsOrReuseOlderSchemaForExecution()
    {
        var(s,v,o)=Prepare();Board(s,v);Load(s,o.Id);string json=s.Engine.CreateCheckpointJson();
        foreach(string field in new[]{"loaded","delivered","paidMinor","refundedMinor"})
        {var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var run=node["services"]!["execution"]!["runs"]![0]!;run[field]=run[field]!.GetValue<long>()+1;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(node.ToJsonString()));}
        var old=System.Text.Json.Nodes.JsonNode.Parse(json)!;old["schemaVersion"]=27;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(old.ToJsonString()));
    }
    [Fact]public void OneRealVehicleLoadsMovesAndPaysOnlyAfterActualReceipt()
    {
        var(s,v,o)=Prepare();Board(s,v);Exact(s,"ready-driver");Load(s,o.Id);Assert.Equal(30,Run(s).Cargo);Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason=="ServicePayment:"+o.Id);
        Step(s,new(ActionKind.NavigateDeliveryService,ServiceId:o.Id));s.Step();Assert.True(s.Engine.Observe(new(2)).Participant.Busy);Exact(s,"in-transit");Until(s,()=>!s.Engine.Observe(new(2)).Participant.Busy);
        Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:o.Id));s.Step();Assert.True(Run(s).Delivered>0);Assert.True(Run(s).Cargo>0);Assert.True(Run(s).PaidMinor>0);Assert.Equal(0,Run(s).LateRefundMinor);
        Until(s,()=>s.Engine.InspectServices()!.Orders.Single().State=="Completed");Assert.Equal(30,Run(s).Delivered);Assert.Equal(o.Quote.FeeMinor,Run(s).PaidMinor);Assert.Equal(30,FleetE1Tests.Stock(s,o.Quote.Terms.Destination,"wood"));Assert.Equal(0,FleetE1Tests.Stock(s,v.CargoInventory,"wood"));Exact(s,"completed");
    }
    [Fact]public void FiveTripsCompleteWithoutDuplicateGoodsOrPayments()
    {
        var(s,v,o)=Prepare(150);Board(s,v);Assert.Equal(5,o.Quote.Trips);
        for(int n=0;n<5;n++){Load(s,o.Id);Assert.InRange(Run(s).Cargo,1,35);Deliver(s,o.Id);}
        Assert.Equal(5,Run(s).TripNumber);Assert.Equal(150,Run(s).Delivered);Assert.Equal(o.Quote.FeeMinor,Run(s).PaidMinor);Assert.Equal(0,Run(s).Cargo);Assert.Equal(150,FleetE1Tests.Stock(s,o.Quote.Terms.Destination,"wood")+(StorageC1Tests.Inventory(s,o.Quote.Terms.Destination).Overflow??[]).Sum(l=>l.ItemId=="wood"?l.Quantity:0));
        Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:o.Id),accepted:false);Exact(s,"five-trips");
    }
    [Fact]public void NumericDriverIsAllocatedFromPopulationAndReceivesActualWages()
    {
        var(s,v,o)=Prepare();Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));Assert.Equal(1,s.Engine.InspectServices()!.Execution!.Crews.Single().Group.Capacity);
        Until(s,()=>s.Engine.InspectServices()!.Orders.Single().State=="Completed");Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="ServiceDriverWages:"+o.Id);Assert.Equal(0,s.Engine.InspectFleet()!.Vehicles.Single().Driver);Assert.Equal(30,Run(s).Delivered);Exact(s,"numeric");
    }
}

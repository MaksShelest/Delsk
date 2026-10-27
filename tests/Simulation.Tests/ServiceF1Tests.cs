using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

public class ServiceF1Tests
{
    internal static EconomyProfile Profile()=>VNextServiceProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Definition(ulong seed=42)
    {var s=FleetE1Tests.FreightDefinition();return s with{Id="f1-prepared",Seed=seed,Economy=Profile().World.Rules,Layout=s.Layout! with{TurnCells=s.Layout.RoadCorridorCells}};}
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)=>BillingE3Tests.Step(s,a,actor,accepted);
    internal static (GameSession Session,OwnedVehicle Vehicle,ServiceTerms Terms) Prepare(string model="muravey70",long quantity=30,ulong seed=42)
    {
        var definition=Definition(seed);if(quantity>100)definition=definition with{Buildings=[..definition.Buildings!.Select(b=>b.Id==9?b with{Level=2}:b)]};
        var s=new GameSession(new(definition));StorageC1Tests.Harvest(s,"wood",quantity);
        var buildings=s.Engine.Observe(new(1)).OwnBuildings;
        var source=buildings.Single(b=>b.Id.Value==9).InputId;var dest=buildings.Single(b=>b.Id.Value==10).InputId;
        StorageC1Tests.Put(s,source,"wood",quantity);var v=FleetE1Tests.Buy(s,model,2);
        Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:true),2);
        return(s,v,new(v.Id,source,dest,"wood",quantity));
    }
    internal static ServiceQuote Quote(GameSession s,ServiceTerms terms)
    {var result=s.Engine.QuoteDeliveryService(new(1),terms);Assert.True(result.Quote is not null,result.Reason);return result.Quote;}
    internal static ServiceOrder Request(GameSession s,ServiceTerms terms)
    {var q=Quote(s,terms);Step(s,new(ActionKind.RequestDeliveryService,VehicleId:terms.VehicleId,Source:terms.Source,Destination:terms.Destination,ItemId:terms.ItemId,Quantity:terms.Quantity,PurchaseId:terms.PurchaseId,ContractId:terms.ContractId,ServiceQuote:q.Token));return s.Engine.InspectServices()!.Orders.Last();}
    internal static void Exact(GameSession s,string name)
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        for(int n=0;n<3;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_F1_EVIDENCE") is not {} output)return;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(output,name+".commands.jsonl"),string.Join('\n',s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(output,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(output,name+".save.json"));
    }
    [Fact]public void ProfileIsOptInFrozenAndBothDesignedMapsLoad()
    {
        var p=Profile();Assert.Null(BillingE3Tests.Profile().World.Rules.Services);Assert.Equal(10,p.World.Rules.Services!.ResponseTicks);
        Assert.Equal(p.Id,EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).Id);Assert.Equal(1000,p.World.Rules.Billing!.GraceTicks);
        foreach(string map in new[]{"small_river","large_districts"}){var s=new GameSession(new(OrdinaryNewGame.Resolve(MapDesignTests.Template(map),p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario));s.Step();Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());}
    }
    [Theory][InlineData("sputnik20",3)][InlineData("muravey70",1)][InlineData("eger70",1)][InlineData("vector200",1)]
    public void RealModelsQuoteVolumeTripsWholeHandlingAndSingleRoundedPrice(string model,long trips)
    {
        var(s,v,t)=Prepare(model);string before=s.Engine.ComputeStateHash();var q=Quote(s,t);Assert.Equal(before,s.Engine.ComputeStateHash());
        Assert.Equal(trips,q.Trips);Assert.Equal(60,q.Volume);Assert.Equal(12,q.HandlingTicks);Assert.Equal(q.Approach.Length-1+trips*(q.Loaded.Length-1)+(trips-1)*(q.EmptyReturn.Length-1),q.Approach.Length-1+trips*q.LoadedDistance+(trips-1)*(q.EmptyReturn.Length-1));
        decimal road=(decimal)q.RoadUnits/SimulationEngine.RoadAccrualScale;
        long expected=Math.Max(500,(long)Math.Ceiling(200m+10m*Math.Ceiling(q.Volume/10m)*Math.Ceiling(q.LoadedDistance/10m)+1.2m*(20m*(q.EtaTicks+q.ExtraStaffTicks)+road)));
        Assert.Equal(expected,q.FeeMinor);Assert.Equal(Math.Max(600,(q.EtaTicks+1)/2),q.BufferTicks);Exact(s,"quote-"+model);
    }
    [Fact]public void TraktAndDrivenVehicleCannotOffer()
    {
        var(s,v,_)=Prepare();Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:false),2);
        StorageC1Tests.Move(s,s.Engine.FleetOf(new(2))!.Vehicles.Single().Position,2);Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),2);
        Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:true),2,false);
        var t=FleetE1Tests.Buy(s,"trakt_d1",1);Step(s,new(ActionKind.SetDeliveryService,VehicleId:t.Id,Enabled:true),1,false);
    }
    [Fact]public void PendingReservesMoneyCargoAndVehicleWithoutMovingAndRejectReleasesAll()
    {
        var(s,v,t)=Prepare();var position=s.Engine.FleetOf(new(2))!.Vehicles.Single().Position;long cash=s.Engine.Observe(new(1)).Account.AvailableMinor;
        var order=Request(s,t);Assert.Equal(cash-order.Quote.FeeMinor,s.Engine.Observe(new(1)).Account.AvailableMinor);
        Assert.Equal(t.Quantity,StorageC1Tests.Inventory(s,t.Source).Stock.Single(i=>i.ItemId=="wood").Reserved);
        Assert.False(s.Engine.ServicesOf(new(2))!.Catalog.Single().Available);Assert.Equal(position,s.Engine.FleetOf(new(2))!.Vehicles.Single().Position);
        Assert.False(s.Paused);Exact(s,"pending");Step(s,new(ActionKind.RejectDeliveryService,ServiceId:order.Id),2);
        Assert.Equal(cash,s.Engine.Observe(new(1)).Account.AvailableMinor);Assert.Equal(0,StorageC1Tests.Inventory(s,t.Source).Stock.Single().Reserved);Assert.True(s.Engine.ServicesOf(new(2))!.Catalog.Single().Available);Exact(s,"rejected");
    }
    [Fact]public void AcceptanceCreatesOneEscrowNoPayoutAndCancellationRefunds()
    {
        var(s,v,t)=Prepare();long customer=s.Engine.Observe(new(1)).Account.BalanceMinor,carrier=s.Engine.Observe(new(2)).Account.BalanceMinor;
        var order=Request(s,t);Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2);order=s.Engine.InspectServices()!.Orders.Single();Assert.Equal("Accepted",order.State);
        Assert.Equal(order.Quote.EstimatedDeadline(order.AcceptedTick),order.DeadlineTick);Assert.Equal(carrier,s.Engine.Observe(new(2)).Account.BalanceMinor);Assert.Equal(customer-order.Quote.FeeMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(order.Quote.FeeMinor,s.Engine.AccountOf(new(1)).EscrowMinor);Assert.Single(s.Engine.Ledger(),l=>l.Reason=="ServiceFunding:"+order.Id);
        Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2,false);Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:false),2);
        Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);Exact(s,"accepted");
        Step(s,new(ActionKind.CancelDeliveryService,ServiceId:order.Id));Assert.Equal(customer,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(carrier,s.Engine.Observe(new(2)).Account.BalanceMinor);Assert.Equal(0,s.Engine.AccountOf(new(1)).EscrowMinor);
        Step(s,new(ActionKind.CancelDeliveryService,ServiceId:order.Id),accepted:false);Exact(s,"refunded");
    }
    [Fact]public void TimeoutIsTenActiveTicksAndLoadedWaitingStateExpiresOnce()
    {
        var(s,_,t)=Prepare();var order=Request(s,t);Assert.Equal(order.CreatedTick+10,order.RespondByTick);s.AdvanceTo(order.RespondByTick-1);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal("Expired",s.Engine.InspectServices()!.Orders.Single().State);
        Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2,false);Assert.Equal(0,s.Engine.Observe(new(1)).Account.ReservedMinor);Exact(s,"expired");
    }
    [Fact]public void InclusiveLastResponseTickAcceptsAndGlobalPauseDoesNotExpire()
    {
        var(s,_,t)=Prepare();var order=Request(s,t);s.AdvanceTo(order.RespondByTick-1);s.SetPaused(true);long tick=s.Engine.Tick;for(int n=0;n<20;n++)s.Step();Assert.Equal(tick,s.Engine.Tick);s.SetPaused(false);
        Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2);Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);Exact(s,"inclusive");
    }
    [Fact]public void CompetingRequestsCreateOnlyOneLeaseAndIdenticalRetryDoesNotDuplicate()
    {
        var(s,v,t)=Prepare();var q=Quote(s,t);var action=new GameAction(ActionKind.RequestDeliveryService,VehicleId:v.Id,Source:t.Source,Destination:t.Destination,ItemId:t.ItemId,Quantity:t.Quantity,ServiceQuote:q.Token);
        string one=s.Send(new(1),action),two=s.Send(new(1),action);s.Step();Assert.Single(s.Engine.InspectServices()!.Orders);Assert.Equal(1,new[]{s.Engine.Result(one)!,s.Engine.Result(two)!}.Count(r=>r.Status==ResultStatus.Accepted));
        var command=s.CommandTape.First(c=>c.Command.CommandId==one).Command;string hash=s.Engine.ComputeStateHash();s.Engine.Enqueue(new(1),command);Assert.Equal(hash,s.Engine.ComputeStateHash());Exact(s,"competition");
    }
    [Fact]public void WrongCargoGroundAndSamePointAreRejectedWithoutReservations()
    {
        var(s,_,t)=Prepare();Assert.Null(s.Engine.QuoteDeliveryService(new(2),t).Quote);Assert.Null(s.Engine.QuoteDeliveryService(new(1),t with{Quantity=31}).Quote);
        Assert.Null(s.Engine.QuoteDeliveryService(new(1),t with{Destination=new InventoryId(s.Engine.StorageOf(new(1)).Ground.First().InventoryId)}).Quote);
        Assert.Null(s.Engine.QuoteDeliveryService(new(1),t with{Destination=t.Source}).Quote);Assert.Equal(0,s.Engine.Observe(new(1)).Account.ReservedMinor);Exact(s,"invalid");
    }
    [Fact]public void OrdersAndEventsStayPrivateAndViewingDoesNotAccept()
    {
        var(s,_,t)=Prepare();var order=Request(s,t);Assert.False(order.CarrierViewed);Step(s,new(ActionKind.ViewDeliveryService,ServiceId:order.Id),2);
        Assert.Equal("Pending",s.Engine.InspectServices()!.Orders.Single().State);Assert.True(s.Engine.InspectServices()!.Orders.Single().CarrierViewed);
        Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type.StartsWith("Service"));Exact(s,"viewed");
    }
    [Fact]public void OldSchema26WithoutServicesKeepsExactState()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));s.Step();var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;json["schemaVersion"]=26;
        Assert.Equal(s.Engine.ComputeStateHash(),SimulationEngine.FromCheckpointJson(json.ToJsonString()).ComputeStateHash());
        var f=new GameSession(new(Definition()));f.Step();json=JsonNode.Parse(f.Engine.CreateCheckpointJson())!;json["schemaVersion"]=26;Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString()));
    }
}

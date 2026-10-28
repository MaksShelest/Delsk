using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.ServiceF2Tests;
namespace Simulation.Tests;
public class ServicePointsF2Tests
{
    internal static (GameSession Session,OwnedVehicle Vehicle,InventoryId Destination) Empty(ScenarioDefinition? definition=null)
    {
        var s=new GameSession(new(definition??Definition()));var v=FleetE1Tests.Buy(s,"muravey70",2);Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:true));return(s,v,s.Engine.Inspect().Buildings.Single(b=>b.Id.Value==10).InputId);
    }
    [Fact]public void ImportPickupIsSourceOnlyAndPaidGoodsMovePhysically()
    {
        var(s,v,dest)=Empty();StorageC1Tests.Move(s,s.Engine.Configuration().CapitalDemand!.Terminal);Step(s,new(ActionKind.BuyImport,ItemId:"steel",Quantity:3),1);
        var source=s.Engine.ProductionContentOf(new(1))!.Pickup!.Value;var point=s.Engine.ServicesOf(new(1))!.OwnPoints.Single(p=>p.Inventory==source);Assert.True(point.CanSource);Assert.False(point.CanReceive);
        var o=ServiceF1Tests.Request(s,new(v.Id,source,dest,"steel",3));Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:o.Id));Board(s,v);Load(s,o.Id);Assert.Equal(0,FleetE1Tests.Stock(s,source,"steel"));Deliver(s,o.Id);Assert.Equal(3,FleetE1Tests.Stock(s,dest,"steel"));Assert.Equal(3,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").Imported);Exact(s,"import-delivered");
    }
    [Theory][InlineData(false)][InlineData(true)]public void StationaryExtractionStorageIsUsableAndCannotMoveWhileCommitted(bool receiver)
    {
        var d=Definition();var wood=d.ResourceNodes.Single(n=>n.ItemId=="wood");d=d with{ResourceNodes=[..d.ResourceNodes.Select(n=>n.ItemId=="wood"?n with{Rules=new(Public:false,InitialOwner:1)}:n),wood with{Position=new(38,0)}],Layout=d.Layout! with{ResourceDistricts=[..d.Layout.ResourceDistricts.Select(r=>r.Item=="wood"?r with{Cells=[wood.Position,new(38,0)],PublicCells=[new(38,0)]}:r)]}};
        var(s,v,dest)=Empty(d);StorageC1Tests.Move(s,wood.Position);Step(s,new(ActionKind.BuyEquipment,Target:wood.Position,EquipmentTypeId:d.EquipmentTypes!.Single(t=>t.ItemId=="wood").Id),1);var machine=s.Engine.Observe(new(1)).OwnEquipment.Single();
        Step(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:5),1);StorageC1Tests.Idle(s);StorageC1Tests.Put(s,receiver?dest:machine.StorageId,"wood",5);var point=s.Engine.ServicesOf(new(1))!.OwnPoints.Single(p=>p.Inventory==machine.StorageId);Assert.Equal("Extraction",point.PointType);
        var target=receiver?machine.StorageId:dest;var o=ServiceF1Tests.Request(s,new(v.Id,receiver?dest:machine.StorageId,target,"wood",5));Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:o.Id));
        StorageC1Tests.Move(s,wood.Position);Step(s,new(ActionKind.SetWage,Group:machine.GroupId,WageMinor:0),1);Step(s,new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic),1);
        Step(s,new(ActionKind.MoveEquipment,Equipment:machine.Id,Target:wood.Position),1,false);Assert.Equal("DeliveryServiceCommitment",s.Engine.Result(s.CommandTape.Last().Command.CommandId)!.ReasonCode);
        Board(s,v);Load(s,o.Id);Deliver(s,o.Id);Assert.Equal(5,FleetE1Tests.Stock(s,target,"wood"));Assert.Equal(wood.Position,s.Engine.Observe(new(1)).OwnEquipment.Single().Position);Exact(s,receiver?"extraction-received":"extraction-delivered");
    }
    [Theory][InlineData("ground")][InlineData("backpack")][InlineData("cargo")][InlineData("capital")]
    public void InvalidUnconsentedServicePointsDoNotBecomePublicEndpoints(string kind)
    {
        var(s,v,o)=Prepare();Step(s,new(ActionKind.CancelDeliveryService,ServiceId:o.Id),1);
        var bad=kind switch{"ground"=>new InventoryId(s.Engine.StorageOf(new(1)).Ground.First().InventoryId),"backpack"=>StorageC1Tests.Carrier(s),"cargo"=>v.CargoInventory,_=>new InventoryId(0)};
        var result=s.Engine.QuoteDeliveryService(new(1),o.Quote.Terms with{Destination=bad});Assert.Null(result.Quote);Assert.Equal("ServicePointRequired",result.Reason);
    }
}

using System.Text;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MobileEquipmentI4Tests
{
    internal static EconomyProfile Profile() => VNextMobileEquipmentProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Prepared(ulong seed=42,long cash=1000000)
    {
        var d=FleetE1Tests.Prepared(seed);var p=Profile();var items=p.World.EquipmentTypes.Select(t=>t.ItemId).ToHashSet();
        var resources=p.World.EquipmentTypes.SelectMany((type,n)=>new[]{
            new ResourceNodeDefinition(type.ItemId,new(2*n+1,11),new(Public:true)),
            new ResourceNodeDefinition(type.ItemId,new(2*n+2,11),new(Public:false,InitialOwner:1)) }).ToArray();
        var road=d.Layout!.InitialCityDirtCells.Concat(Enumerable.Range(2,8).Select(z=>new Cell(39,z)))
            .Concat(Enumerable.Range(0,60).Select(x=>new Cell(x,9))).Distinct().ToArray();
        var layout=d.Layout with { InitialCityDirtCells=road,RoadCenterCells=road,RoadCorridorCells=road,
            ResourceDistricts=p.World.EquipmentTypes.Select(type=>new ResourceDistrict("source-"+type.ItemId,type.ItemId,type.ItemId,resources.Where(r=>r.ItemId==type.ItemId).Select(r=>r.Position).ToArray(),resources.Where(r=>r.ItemId==type.ItemId&&r.Rules!.Public).Select(r=>r.Position).ToArray())).ToArray(),
            CivicObjects=d.Layout.CivicObjects.Select(c=>c.Kind=="dealership"?c with {
                EquipmentParking=Enumerable.Range(0,16).Select(n=>new Cell(22+2*n,8)).ToArray() }:c).ToArray() };
        return d with {MapWidth=60,MapHeight=16,EconomyProfileId=p.Id,Economy=p.World.Rules,Layout=layout,ResourceNodes=resources,
            Participants=d.Participants.Select(a=>a with {CashMinor=cash}).ToArray()};
    }
    internal static OwnedVehicle Buy(GameSession s,string item="wood",int level=1,long actor=1)
    {
        var f=s.Engine.FleetOf(new(actor))!;
        FleetE1Tests.Move(s,f.Dealer,actor);
        var model=f.Models.Single(m=>m.EquipmentTypeId=="extract_"+item&&m.EquipmentLevel==level);
        var listing=s.Engine.FleetOf(new(actor))!.Stock.Single(v=>v.ModelId==model.Id);
        FleetE1Tests.Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id),actor:actor);
        s.AdvanceTo(s.Engine.Tick+5);
        return s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==listing.Id);
    }
    internal static void Exact(GameSession s)
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        s.AdvanceTo(s.Engine.Tick+3);copy.AdvanceTo(s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
    [Fact] public void NewProfileHasFiveDiscountsOnceAndHistoricalProjectionIsByteExact()
    {
        var p=Profile();var read=EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p)));
        Assert.Equal(VNextMobileEquipmentProfile.ProfileId,read.Id);
        Assert.Equal(new long[]{130000,180000,320000,630000,840000},p.World.Rules.Fleet!.Models.Take(5).Select(m=>m.PriceMinor));
        Assert.Equal(new long[]{180000,250000,450000,900000,1200000},FleetE1Tests.Profile().World.Rules.Fleet!.Models.Select(m=>m.PriceMinor));
        Assert.Equal(VNextSupportedProfile.Serialize(p),VNextSupportedProfile.Serialize(Profile()));
        Assert.Throws<ArgumentException>(()=>VNextMobileEquipmentProfile.Create(p,MapDesignTests.Balance()));
    }
    [Theory]
    [InlineData("compact_valley")][InlineData("small_river")][InlineData("large_districts")]
    public void DesignedCityHasSixteenSeparateParkingPlacesWithRealRoadApproaches(string map)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var e=new SimulationEngine(d);var f=e.FleetOf(new(1))!;
        var machines=f.Stock.Where(v=>f.Models.Single(m=>m.Id==v.ModelId).EquipmentTypeId is not null).ToArray();
        Assert.Equal(16,machines.Length);Assert.Equal(16,machines.Select(v=>v.Position).Distinct().Count());
        Assert.All(machines,v=>Assert.Contains(d.Layout!.InitialCityDirtCells,c=>Math.Abs(c.X-v.Position.X)+Math.Abs(c.Z-v.Position.Z)==1));
        Assert.Equal(31,e.ActiveCheckpointSchemaVersion);
        var saved=CheckpointStore.Decode(CheckpointStore.Encode(new GameSession(e)));
        Assert.Equal(e.ComputeStateHash(),saved.Engine.ComputeStateHash());
    }
    [Fact] public void StartingTwoHundredCoinsBuyExactlyOneSmallMachineWithoutTeleportOrFreeCrew()
    {
        var s=new GameSession(new(Prepared(cash:20000)));var v=Buy(s);
        var e=Assert.Single(s.Engine.Observe(new(1)).OwnEquipment);
        Assert.Equal(v.Id,e.Id.Value);Assert.Equal(v.CargoInventory,e.StorageId);Assert.False(e.Deployed);
        Assert.Equal(5000,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(15000,s.Engine.Ledger().Where(l=>l.Reason=="VehiclePurchase").Sum(l=>l.AmountMinor));
        Assert.Equal(0,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id==e.GroupId).Headcount);
        Assert.Contains(e.Position,s.Engine.Configuration().Layout!.CivicObjects.Single(c=>c.Kind=="dealership").EquipmentParking!);Assert.Empty(StorageC1Tests.Inventory(s,e.StorageId).Stock);
        FleetE1Tests.Step(s,new(ActionKind.BuyVehicle,VehicleId:v.Id),false);Exact(s);
    }
    [Theory]
    [InlineData("wood",1)][InlineData("wood",2)][InlineData("grain",1)][InlineData("grain",2)]
    [InlineData("stone",1)][InlineData("stone",2)][InlineData("iron_ore",1)][InlineData("iron_ore",2)]
    [InlineData("copper_ore",1)][InlineData("copper_ore",2)][InlineData("sand",1)][InlineData("sand",2)]
    [InlineData("beet",1)][InlineData("beet",2)][InlineData("milk",1)][InlineData("milk",2)]
    public void EverySpecializationDrivesDeploysWorksUnloadsAndPacksTheSameInstance(string item,int level)
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s,item,level);var id=new EquipmentId(v.Id);
        FleetE1Tests.Board(s,v);
        var target=s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId==item&&!n.Rules!.Public).Position;
        FleetE1Tests.Step(s,new(ActionKind.MoveTo,Target:target));
        Assert.NotEqual(target,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        Exact(s);StorageC1Tests.Idle(s);
        Assert.Equal(target,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        Assert.Equal("Allowed",s.Engine.EquipmentPlacementReason(new(1),id));
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:id));
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Personal));
        long quantity=level==1?2:6;
        FleetE1Tests.Step(s,new(ActionKind.StartHarvesting,Equipment:id,ItemId:item,Quantity:quantity));
        Exact(s);FleetE1Tests.Step(s,new(ActionKind.StopHarvest));
        Assert.True(FleetE1Tests.Stock(s,v.CargoInventory,item)>0);
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Off));
        var before=s.Engine.Observe(new(1)).OwnEquipment.Single();
        FleetE1Tests.Step(s,new(ActionKind.PackEquipment,Equipment:id));
        Assert.Equal(before.StorageId,s.Engine.Observe(new(1)).OwnEquipment.Single().StorageId);
        Assert.Equal(before.CompletedCycles,s.Engine.Observe(new(1)).OwnEquipment.Single().CompletedCycles);
        FleetE1Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));
        FleetE1Tests.Move(s,new(target.X,2));FleetE1Tests.Step(s,new(ActionKind.LeaveVehicle));
        long cargo=FleetE1Tests.Stock(s,v.CargoInventory,item);
        FleetE1Tests.Step(s,new(ActionKind.Transfer,Source:v.CargoInventory,Destination:StorageC1Tests.Carrier(s),ItemId:item,Quantity:cargo));
        StorageC1Tests.Idle(s);Assert.Equal(0,FleetE1Tests.Stock(s,v.CargoInventory,item));
        FleetE1Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));FleetE1Tests.Move(s,target);
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:id));
        FleetE1Tests.Step(s,new(ActionKind.StartHarvesting,Equipment:id,ItemId:item,Quantity:quantity));
        s.AdvanceTo(s.Engine.Tick+3);FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Off));
        Assert.True(FleetE1Tests.Stock(s,v.CargoInventory,item)>0);Exact(s);
    }
    [Theory]
    [InlineData("sputnik20",130000)][InlineData("muravey70",180000)][InlineData("eger70",320000)]
    [InlineData("vector200",630000)][InlineData("trakt_d1",840000)]
    public void EveryDiscountedPriceHoldsAndPaysOnceAcrossLoad(string model,long price)
    {
        var s=new GameSession(new(Prepared()));var listing=s.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId==model);
        long money=s.Engine.Observe(new(1)).Account.BalanceMinor;
        FleetE1Tests.Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id));
        Assert.Equal(money-price,s.Engine.Observe(new(1)).Account.AvailableMinor);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(6);copy.AdvanceTo(6);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(price,s.Engine.Ledger().Where(l=>l.Reason=="VehiclePurchase").Sum(l=>l.AmountMinor));
        Assert.Equal(money-price,s.Engine.Observe(new(1)).Account.BalanceMinor);Exact(s);
    }
}

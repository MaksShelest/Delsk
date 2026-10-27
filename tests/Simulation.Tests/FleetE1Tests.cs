using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class FleetE1Tests
{
    internal static EconomyProfile Profile()=>VNextFleetProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    // Controlled geography and explicit resource sources for all materials. Not natural progression or balance evidence.
    internal static ScenarioDefinition Prepared(ulong seed=42)
    {
        var w=Profile().World;
        Cell[] Rect(int x,int z,int width,int height)=>Enumerable.Range(z,height).SelectMany(b=>Enumerable.Range(x,width).Select(a=>new Cell(a,b))).ToArray();
        var hall=new CivicSite("hall","town_hall",Rect(10,3,3,3),null,null,Rect(10,3,2,2),new(10,5));
        var terminal=new CivicSite("terminal","terminal",Rect(16,3,2,3),null,null,Rect(16,3,2,2),new(16,5));
        var dealer=new CivicSite("dealer","dealership",Rect(22,3,5,5),null,[new(22,6),new(23,6),new(25,6),new(26,6)],Rect(22,3,5,3),new(24,6));
        var roads=Enumerable.Range(0,40).Select(x=>new Cell(x,1)).Concat(new[]{new Cell(4,2),new Cell(4,3)}).ToArray();
        var cells=Rect(4,4,2,3);var resources=w.Items.Select((i,n)=>new ResourceNodeDefinition(i.Id,new(n,0),new())).ToArray();
        var layout=new MapLayout("map-adapter-1","e1-prepared","E1 prepared",new string('a',64),4,roads,roads,roads,
            [new("lot",cells,new(4,3))],resources.Select(n=>new ResourceDistrict("source-"+n.ItemId,n.ItemId,n.ItemId,[n.Position],[n.Position])).ToArray(),
            [hall,terminal,dealer],[new("stall",new(30,3)),new("stall2",new(31,3))],[new(0,1),new(1,1),new(2,1),new(3,1)],[new(0,1)],"existing navigation","prepared");
        return new("fleet-e1-prepared",seed,40,10,layout.CivicObjects.SelectMany(c=>c.Body).ToArray(),w.Items,w.Recipes,
            [new(1,10000000,dealer.Entry,2000),new(2,10000000,dealer.Entry,2000)],[],[],resources,HarvestTicksPerUnit:1,
            Population:10,MaxTicks:20000,Plots:cells.Select((c,n)=>new PlotDefinition(50+n,c,10000,OwnerId:1)).Append(new(90,new(30,3),10000,IsShopSite:true)).Append(new(91,new(31,3),10000,IsShopSite:true)).ToArray(),
            BuildingTypes:w.BuildingTypes,Buildings:[new(8,1,new(4,4),"woodworking")],EquipmentTypes:w.EquipmentTypes,
            Labor:new(StepTicks:1,MobilityNumerator:1,MobilityDenominator:1,GrowthPeriodTicks:100000,GrowthPerPeriod:0),
            Logistics:w.Logistics,Economy:w.Rules,CapitalDemand:w.CapitalDemand with{Terminal=terminal.Entry,PeriodTicks=10000,BootstrapBaseBudgetMinor=10000000},Support:w.Support with{TradingPoint=terminal.Entry},Elections:new(PeriodTicks:20000),Layout:layout);
    }
    internal static void Step(GameSession s,GameAction a,bool accepted=true,long actor=1)=>StorageC1Tests.Step(s,a,actor,accepted);
    internal static void Move(GameSession s,Cell c,long actor=1)=>StorageC1Tests.Move(s,c,actor);
    internal static OwnedVehicle Buy(GameSession s,string model="vector200",long actor=1)
    {
        Move(s,s.Engine.FleetOf(new(actor))!.Dealer,actor);var listing=s.Engine.FleetOf(new(actor))!.Stock.First(v=>v.ModelId==model);
        Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id),actor:actor);s.AdvanceTo(s.Engine.Tick+5);
        return s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==listing.Id);
    }
    internal static void Board(GameSession s,OwnedVehicle v)
    {Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single(x=>x.Vehicle.Id==v.Id).Position);Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));}
    internal static long Stock(GameSession s,InventoryId id,string item)=>StorageC1Tests.Inventory(s,id).Stock.Where(i=>i.ItemId==item).Sum(i=>i.Quantity);
    internal static void RestoreAndReplay(GameSession s,[System.Runtime.CompilerServices.CallerMemberName]string name="")
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        s.AdvanceTo(s.Engine.Tick+3);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_E1_EVIDENCE") is {} folder)
        {
            name=name switch{nameof(SharedLogisticsUsesPurchasedCargoPaysOneDriverAndRestoresEveryTripStage)=>"freight",nameof(BareTruckTransfersByVolumeOverWholeTicksAndLoadedCargoSurvivesMovement)=>"manual-cargo",nameof(PlatformsNeedRealStaffAndAddIndependentPowerWithoutCountingTheDriverTwice)=>"platforms",nameof(HandlingKitCostsMaterialsAndMoneyAndWaitsSixtyTicks)=>"kit",_=>name};
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
            File.WriteAllText(Path.Combine(folder,name+".commands.jsonl"),string.Join("\n",s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
            File.WriteAllText(Path.Combine(folder,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(folder,name+".save.json"));
        }
    }
    [Fact] public void ModelsMatchBalanceAndOldProfilesRemainUnchanged()
    {
        var p=Profile();var read=EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p)));
        Assert.Equal(VNextFleetProfile.ProfileId,read.Id);Assert.Null(ProductionD3Tests.Profile().World.Rules.Fleet);
        Assert.Equal(new long[]{20,70,70,200,70},p.World.Rules.Fleet!.Models.Select(m=>m.CapacityVolume));
        foreach(var id in new[]{"small_river","large_districts"})
        {var map=OrdinaryNewGame.Resolve(MapDesignTests.Template(id),p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario;var e=new SimulationEngine(map);Assert.Equal(6,e.InspectFleet()!.Stock.Length);Assert.Equal(4,e.InspectFleet()!.Stock.Select(v=>v.Position).Distinct().Count());}
    }
    [Fact] public void PurchaseHoldsMoneyThenCreatesExactlyOneRealVehicleAndRestoresPendingWindow()
    {
        var s=new GameSession(new(Prepared()));var listing=s.Engine.FleetOf(new(1))!.Stock.Single(v=>v.ModelId=="vector200");
        long before=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id));
        Assert.Empty(s.Engine.InspectFleet()!.Vehicles);Assert.Equal(before-900000,s.Engine.Observe(new(1)).Account.AvailableMinor);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(6);copy.AdvanceTo(6);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());var v=Assert.Single(s.Engine.InspectFleet()!.Vehicles);
        Assert.Equal(before-900000,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(listing.Position,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        Assert.Equal(200,StorageC1Tests.Inventory(s,v.CargoInventory).Capacity);Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id),false);RestoreAndReplay(s);
    }
    [Fact] public void LastListingCompetitionUsesCommonInclusiveWindowAndBothActorsCanWin()
    {
        var winners=new HashSet<long>();
        for(ulong seed=1;seed<=12;seed++)
        {
            var s=new GameSession(new(Prepared(seed)));var listing=s.Engine.InspectFleet()!.Stock.Single(v=>v.ModelId=="sputnik20");
            Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id));s.AdvanceTo(5);Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id),actor:2);
            var v=Assert.Single(s.Engine.InspectFleet()!.Vehicles);winners.Add(v.Owner.Value);
            Assert.Equal(180000,s.Engine.Ledger().Where(l=>l.Reason=="VehiclePurchase").Sum(l=>l.AmountMinor));
            Assert.All(s.Engine.Inspect().Participants,a=>Assert.Equal(0,s.Engine.Observe(a.Id).Account.ReservedMinor));
            RestoreAndReplay(s);
        }
        Assert.Equal(new long[]{1,2},winners.Order());
    }
    [Fact] public void MovementCarriesOneInventoryAndSaveDuringDrivingContinuesExactly()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s);Board(s,v);var from=s.Engine.Observe(new(1)).Participant.Position;
        Step(s,new(ActionKind.MoveTo,Target:new(from.X,8)));Assert.True(s.Engine.Observe(new(1)).Participant.Busy);RestoreAndReplay(s);
        StorageC1Tests.Idle(s);Assert.Equal(s.Engine.Observe(new(1)).Participant.Position,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        Step(s,new(ActionKind.LeaveVehicle));Move(s,new(35,8));Assert.NotEqual(s.Engine.Observe(new(1)).Participant.Position,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
    }
    [Fact] public void BareTruckTransfersByVolumeOverWholeTicksAndLoadedCargoSurvivesMovement()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s);StorageC1Tests.Harvest(s,"wood",30);Move(s,StorageC1Tests.Inventory(s,v.CargoInventory).Position);Board(s,v);
        Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:v.CargoInventory,ItemId:"wood",Quantity:30));
        Assert.Equal(0,Stock(s,v.CargoInventory,"wood"));s.Step();Assert.Equal(5,Stock(s,v.CargoInventory,"wood"));Assert.False(s.Engine.CanTravel(new(1),1,0));
        Step(s,new(ActionKind.LeaveVehicle),false);RestoreAndReplay(s);StorageC1Tests.Idle(s);Assert.Equal(30,Stock(s,v.CargoInventory,"wood"));
        Move(s,new(4,4));var input=s.Engine.Observe(new(1)).OwnBuildings.Single().InputId;Step(s,new(ActionKind.Transfer,Source:v.CargoInventory,Destination:input,ItemId:"wood",Quantity:30));
        RestoreAndReplay(s);StorageC1Tests.Idle(s);Assert.Equal(30,Stock(s,input,"wood"));Assert.Equal(0,Stock(s,v.CargoInventory,"wood"));
    }
    [Fact] public void TraktOnlyCarriesRoadMaterialsAndCannotBecomeGeneralFreight()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s,"trakt_d1");StorageC1Tests.Harvest(s,"wood",1);Move(s,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:v.CargoInventory,ItemId:"wood",Quantity:1),false);
        Board(s,v);Move(s,new(4,4));Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.AssignVehicle,VehicleId:v.Id,Enterprise:new(8)),false);
        StorageC1Tests.Harvest(s,"gravel",1);Move(s,StorageC1Tests.Inventory(s,v.CargoInventory).Position);Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:v.CargoInventory,ItemId:"gravel",Quantity:1));StorageC1Tests.Idle(s);Assert.Equal(1,Stock(s,v.CargoInventory,"gravel"));
    }
    [Fact] public void HandlingKitCostsMaterialsAndMoneyAndWaitsSixtyTicks()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s);StorageC1Tests.Harvest(s,"steel",6);StorageC1Tests.Harvest(s,"conveyor",1);Move(s,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        long before=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.InstallVehicleKit,VehicleId:v.Id,EquipmentLevel:2));
        var job=s.Engine.InspectFleet()!.Vehicles.Single().Installation!;Assert.Equal(60,job.CompletionTick-job.StartedTick);Assert.Equal(before-60000,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),false);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(job.CompletionTick);copy.AdvanceTo(s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(2,s.Engine.InspectFleet()!.Vehicles.Single().HandlingLevel);Assert.Equal(6,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").ConstructionInput);
        RestoreAndReplay(s);
    }

    internal static ScenarioDefinition FreightDefinition()
    {
        var d=Prepared();Cell[] cells=[new(7,4),new(8,4),new(7,5),new(8,5),new(7,6),new(8,6)];var roads=d.Layout!.RoadCorridorCells.Concat(new[]{new Cell(7,2),new Cell(7,3)}).ToArray();
        return d with{Buildings=[..d.Buildings!,new(9,1,new(7,4),"warehouse"),new(10,1,new(8,4),"warehouse")],Plots=[..d.Plots!,..cells.Select((c,n)=>new PlotDefinition(100+n,c,10000,OwnerId:1))],Layout=d.Layout with{IndustrialLots=[..d.Layout.IndustrialLots,new("lot-2",cells,new(7,3))],RoadCenterCells=roads,RoadCorridorCells=roads,InitialCityDirtCells=roads}};
    }
    [Fact] public void SharedLogisticsUsesPurchasedCargoPaysOneDriverAndRestoresEveryTripStage()
    {
        var d=FreightDefinition();var s=new GameSession(new(d));var v=Buy(s,"muravey70");
        StorageC1Tests.Harvest(s,"wood",30);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;
        StorageC1Tests.Put(s,source,"wood",30);Board(s,v);Move(s,new(4,4));Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.AssignVehicle,VehicleId:v.Id,Enterprise:new(8)));
        var l=Assert.Single(s.Engine.Observe(new(1)).OwnLogistics);Assert.Equal(v.CargoInventory,l.CargoInventory);Assert.Equal(0,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id==l.GroupId).Headcount);
        var target=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;
        Step(s,new(ActionKind.SetTransportRoute,Logistics:l.Id,Source:source,Destination:target,ItemId:"wood",Enabled:true));
        while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is null)s.Step();
        Assert.Equal(1,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id==l.GroupId).Headcount);
        Step(s,new(ActionKind.SetLogisticsEnabled,Logistics:l.Id,Direction:LogisticsDirection.Output,Enabled:false));Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),false);
        var stages=new HashSet<string>();var copies=new List<GameSession>();int bound=200;
        while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is {} trip && bound-->0)
        {
            if(stages.Add(trip.State))copies.Add(CheckpointStore.Decode(CheckpointStore.Encode(s)));
            Assert.Equal(trip.Position,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
            s.Step();foreach(var copy in copies){copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        }
        Assert.True(bound>0);Assert.Contains("Loading",stages);Assert.Contains("Outbound",stages);Assert.Contains("Unloading",stages);Assert.Contains("ReturningEmpty",stages);
        Assert.Equal(30,Stock(s,target,"wood"));Assert.Equal(0,Stock(s,source,"wood"));Assert.Equal(0,Stock(s,v.CargoInventory,"wood"));
        Assert.Equal(new Cell(4,4),StorageC1Tests.Inventory(s,v.CargoInventory).Position);Assert.Equal(1,s.Engine.Observe(new(1)).OwnLogistics.Single().CompletedTrips);
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="LogisticsWages");RestoreAndReplay(s);
    }
    [Fact] public void PlatformsNeedRealStaffAndAddIndependentPowerWithoutCountingTheDriverTwice()
    {
        var s=new GameSession(new(Prepared()));var input=s.Engine.Observe(new(1)).OwnBuildings.Single().InputId;
        StorageC1Tests.Harvest(s,"wood",30);Move(s,new(4,4));Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(8),EquipmentLevel:1));
        var platform=Assert.Single(s.Engine.InspectFleet()!.Platforms);Assert.Equal(10,s.Engine.Inspect().Unemployed+ s.Engine.Inspect().EmploymentGroups.Sum(g=>(long)g.Headcount));s.Step();
        Assert.Equal(1,s.Engine.InspectFleet()!.Platforms.Single().Group.Headcount);
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:input,ItemId:"wood",Quantity:30));
        Assert.Equal(0,Stock(s,input,"wood"));s.Step();Assert.Equal(10,Stock(s,input,"wood"));Assert.Equal(cash-20,s.Engine.Observe(new(1)).Account.BalanceMinor);
        RestoreAndReplay(s);StorageC1Tests.Idle(s);long idleCash=s.Engine.Observe(new(1)).Account.BalanceMinor;s.AdvanceTo(s.Engine.Tick+5);Assert.Equal(idleCash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        foreach(var m in new[]{new MaterialCost("steel",4),new MaterialCost("conveyor",1)}){StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,input,m.ItemId,m.Quantity);}
        Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(8),EquipmentLevel:2));var job=s.Engine.InspectFleet()!.Platforms.Single().Installation!;
        Assert.Equal(120,job.CompletionTick-job.StartedTick);RestoreAndReplay(s);s.AdvanceTo(job.CompletionTick);Assert.Equal(2,s.Engine.InspectFleet()!.Platforms.Single().Level);
    }
    [Fact] public void CheckpointRejectsDuplicateCargoDriversAndInvalidPurchaseHolds()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s);Board(s,v);var saved=JsonNode.Parse(s.Engine.CreateCheckpointJson())!.AsObject();
        var fleet=saved["fleet"]!.AsObject();fleet["vehicles"]!.AsArray()[0]!["driver"]=2;
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(saved.ToJsonString()));
        var old=JsonNode.Parse(s.Engine.CreateCheckpointJson())!.AsObject();old["schemaVersion"]=23;Assert.Equal("UnknownWorldSchema",Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(old.ToJsonString())).ReasonCode);
        Assert.Null(s.Engine.FleetOf(new(2))!.Vehicles.FirstOrDefault());Assert.DoesNotContain("randomState",JsonSerializer.Serialize(s.Engine.ObserveController(new(2)).Fleet,SimulationJson.Options),StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void QuantaUseActualDirtAndOffroadSpeedsAndResumeWithoutDuplicatingTheVehicle()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s);Board(s,v);Move(s,new(4,1));
        s.SetTravelHeading(new(1),0,1);s.SetTravelHeading(new(1),1,0);int before=s.Engine.TravelOf(new(1)).OffsetX;
        for(int i=0;i<5;i++)s.AdvanceTravelQuantum();Assert.Equal(before+125,s.Engine.TravelOf(new(1)).OffsetX);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int i=0;i<5;i++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(before+250,s.Engine.TravelOf(new(1)).OffsetX);s.SetTravelHeading(new(1),0,0);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        Move(s,new(0,2));before=s.Engine.TravelOf(new(1)).OffsetX;s.SetTravelHeading(new(1),0,1);s.SetTravelHeading(new(1),1,0);s.CompleteTravelTick();s.SetTravelHeading(new(1),0,0);Assert.Equal(before+100,s.Engine.TravelOf(new(1)).OffsetX);
        Assert.Equal(s.Engine.TravelOf(new(1)).OffsetX,s.Engine.InspectFleet()!.Vehicles.Single().OffsetX);Step(s,new(ActionKind.LeaveVehicle));
        before=s.Engine.TravelOf(new(1)).OffsetX;s.SetTravelHeading(new(1),1,0);s.CompleteTravelTick();s.SetTravelHeading(new(1),0,0);Assert.Equal(before+200,s.Engine.TravelOf(new(1)).OffsetX);RestoreAndReplay(s);
    }
    [Fact] public void LightKitAndIndependentMannedPlatformAddPowerAndChargeTheirActualInputs()
    {
        var s=new GameSession(new(Prepared()));var v=Buy(s,"muravey70");var input=s.Engine.Observe(new(1)).OwnBuildings.Single().InputId;
        foreach(var m in new[]{new MaterialCost("steel",6),new MaterialCost("conveyor",1),new MaterialCost("wood",30)}){StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,input,m.ItemId,m.Quantity);}
        Board(s,v);Move(s,new(4,4));Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.InstallVehicleKit,VehicleId:v.Id,EquipmentLevel:1,Source:input));
        Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(8),EquipmentLevel:1));Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(8),EquipmentLevel:2));
        var finish=s.Engine.InspectFleet()!.Platforms.Single().Installation!.CompletionTick;s.AdvanceTo(finish+1);
        Assert.Equal(1,s.Engine.InspectFleet()!.Platforms.Single().Group.Headcount);Assert.Equal(1,s.Engine.InspectFleet()!.Vehicles.Single().HandlingLevel);
        Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));Step(s,new(ActionKind.Transfer,Source:input,Destination:v.CargoInventory,ItemId:"wood",Quantity:30));
        Assert.Equal(0,Stock(s,v.CargoInventory,"wood"));s.Step();Assert.Equal(30,Stock(s,v.CargoInventory,"wood"));Assert.Empty(s.Engine.TransfersOf(new(1)));
        Assert.Equal(6,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="steel").ConstructionInput);Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="HandlingWages");RestoreAndReplay(s);
    }
    [Fact] public void OversizedSingleItemAccumulatesRealWholeTickWorkAndCannotFitInASmallCar()
    {
        var d=Prepared();var node=new ResourceNodeDefinition("bulky",new(34,0),new());
        d=d with{Items=[..d.Items,new("bulky",25,100)],ResourceNodes=[..d.ResourceNodes,node],Layout=d.Layout! with{ResourceDistricts=[..d.Layout!.ResourceDistricts,new("bulky","bulky","bulky",[node.Position],[node.Position])]}};
        var s=new GameSession(new(d));var small=Buy(s,"sputnik20");var truck=Buy(s);StorageC1Tests.Harvest(s,"bulky",1);
        Move(s,StorageC1Tests.Inventory(s,small.CargoInventory).Position);Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:small.CargoInventory,ItemId:"bulky",Quantity:1),false);
        Move(s,StorageC1Tests.Inventory(s,truck.CargoInventory).Position);Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:truck.CargoInventory,ItemId:"bulky",Quantity:1));
        s.Step();Assert.Equal(0,Stock(s,truck.CargoInventory,"bulky"));Assert.Equal(10,s.Engine.TransfersOf(new(1)).Single().WorkRemainder);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(0,Stock(s,truck.CargoInventory,"bulky"));s.Step();copy.Step();Assert.Equal(1,Stock(s,truck.CargoInventory,"bulky"));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());RestoreAndReplay(s);
    }
    [Fact] public void PreparedNativeEntryContainsActuallyHarvestedCargoAndARealCheckpoint()
    {
        var s=new GameSession(new(Prepared()));StorageC1Tests.Harvest(s,"wood",30);Move(s,s.Engine.FleetOf(new(1))!.Dealer);
        Assert.Equal(30,Stock(s,StorageC1Tests.Carrier(s),"wood"));Assert.Empty(s.Engine.InspectFleet()!.Vehicles);
        if(Environment.GetEnvironmentVariable("DELSK_E1_EVIDENCE") is {} folder){Directory.CreateDirectory(folder);CheckpointStore.Save(s,Path.Combine(folder,"native-fleet.save.json"));}
    }
    [Theory]
    [InlineData("sputnik20",180000,20,8)]
    [InlineData("muravey70",250000,70,8)]
    [InlineData("eger70",450000,70,8)]
    [InlineData("vector200",900000,200,8)]
    [InlineData("trakt_d1",1200000,70,16)]
    public void EveryModelCanBePurchasedDrivenAndParkedWithItsRealCapacity(string model,long price,long capacity,long roadTicks)
    {
        var s=new GameSession(new(Prepared()));long money=s.Engine.Observe(new(1)).Account.BalanceMinor;var v=Buy(s,model);
        Assert.Equal(money-price,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(capacity,StorageC1Tests.Inventory(s,v.CargoInventory).Capacity);
        Board(s,v);Move(s,new(4,1));Step(s,new(ActionKind.MoveTo,Target:new(6,1)));var actor=s.Engine.Observe(new(1)).Participant;
        Assert.Equal(s.Engine.Tick+roadTicks,actor.ArrivalTick);s.AdvanceTo(actor.ArrivalTick!.Value);Step(s,new(ActionKind.LeaveVehicle));
        Assert.Equal(new Cell(6,1),StorageC1Tests.Inventory(s,v.CargoInventory).Position);Assert.Equal(0,s.Engine.InspectFleet()!.Vehicles.Single().Driver);
    }
    [Fact] public void NpcBuysAnActualListingAndResumesItsVehicleAssignmentPlanAfterRestore()
    {
        var d=FreightDefinition();var s=new GameSession(new(d));var factory=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==8);var recipe=d.Recipes.Single(r=>r.Id==s.Engine.Observe(new(1)).OwnEnterprises.Single().RecipeId);
        foreach(var m in recipe.Ingredients){StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,factory.InputId,m.ItemId,m.Quantity);}
        Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));int budget=200;
        while(s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches==0&&budget-->0)s.Step();Assert.True(budget>0);Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));
        var npc=new Agents.Algorithmic.AlgorithmicController(new(1,"wood",CashReserveMinor:0,CashWeight:0,ProductionWeight:0,ExpansionWeight:1000,PoliticsEnabled:false,TradeEnabled:false,FutureContractsEnabled:false),17,[1,2]);
        ImmutableArray<CommandResult> feedback=[];bool restored=false;budget=1500;var trace=new List<string>();
        while(s.Engine.Observe(new(1)).OwnLogistics.Length==0&&budget-->0)
        {
            var decision=npc.Decide(s.Engine.ObserveController(new(1)),feedback);feedback=[];
            if(decision.Actions.Length>0){string id=s.Send(new(1),decision.Actions.Single());npc.CommandSubmitted(id);s.Step();feedback=[s.Engine.Result(id)!];trace.Add($"{s.Engine.Tick} {decision.Goal} {decision.Actions.Single().Kind} {feedback[0].ReasonCode} {s.Engine.Observe(new(1)).Participant.Position}");if(decision.Actions.Single().Kind==ActionKind.BuyVehicle){Assert.Equal(ResultStatus.Accepted,feedback[0].Status);s.AdvanceTo(s.Engine.Tick+5);}}
            else s.Step();
            if(!restored&&s.Engine.InspectFleet()!.Vehicles.Length>0){npc=Agents.Algorithmic.AlgorithmicController.Restore(npc.Capture());restored=true;}
        }
        Assert.True(budget>0,string.Join("\n",trace.TakeLast(35))+"\n"+JsonSerializer.Serialize(s.Engine.FleetOf(new(1)),SimulationJson.Options));Assert.True(restored);var system=Assert.Single(s.Engine.Observe(new(1)).OwnLogistics);var owned=Assert.Single(s.Engine.InspectFleet()!.Vehicles);
        Assert.Equal(owned.Id,system.VehicleId);Assert.Equal(owned.CargoInventory,system.CargoInventory);Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="VehiclePurchase");RestoreAndReplay(s);
    }
}

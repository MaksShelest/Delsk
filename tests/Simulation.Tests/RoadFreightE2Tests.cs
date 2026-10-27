using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class RoadFreightE2Tests
{
    [Fact]public void NpcResumesItsOrdinaryFundedRoadPlanAndBuildsWithRealWorkers()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var paver=RoadE2Tests.Paver(s);var order=s.Engine.InspectRoads()!.Orders.Single();
        StorageC1Tests.Move(s,order.Cell);
        var npc=new Agents.Algorithmic.AlgorithmicController(new(1,"wood",DecisionIntervalTicks:1,CashReserveMinor:0,CashWeight:0,ProductionWeight:0,ExpansionWeight:0,PoliticsEnabled:false,LogisticsEnabled:false,TradeEnabled:false,FutureContractsEnabled:false),23,[1,2]);
        System.Collections.Immutable.ImmutableArray<CommandResult> feedback=[];bool restored=false;int bound=250;var trace=new List<string>();
        while(s.Engine.InspectRoads()!.Orders.Single(o=>o.Id==order.Id).PaidTick==0&&bound-->0)
        {
            var decision=npc.Decide(s.Engine.ObserveController(new(1)),feedback);feedback=[];
            if(decision.Actions.Length>0)
            {
                var action=decision.Actions.Single();string id=s.Send(new(1),action);npc.CommandSubmitted(id);s.Step();feedback=[s.Engine.Result(id)!];trace.Add(action.Kind+":"+feedback[0].ReasonCode);
                if(!restored&&action.Kind==ActionKind.SetRoadWork){npc=Agents.Algorithmic.AlgorithmicController.Restore(npc.Capture());restored=true;}
            }
            else s.Step();
        }
        Assert.True(bound>0,string.Join("\n",trace));Assert.True(restored);Assert.Equal(2,s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==order.Cell).Level);
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="RoadWages");Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="RoadOrderReward");RoadE2Tests.Exact(s);
    }
    [Fact]public void ExistingExtractionMachineUsesSameBypassAndContinuesMiningAfterLoad()
    {
        var d=RoadE2Tests.Prepared();var source=d.ResourceNodes.Single(r=>r.ItemId=="wood") with{Position=new(0,3),Rules=new(Public:false,InitialOwner:2,Capacity:2,RegenerationQuantity:0)};var target=new Cell(20,3);
        d=d with{ResourceNodes=[..d.ResourceNodes.Select(r=>r.ItemId=="wood"?source:r),source with{Position=target,Rules=new(Public:false,InitialOwner:2)},source with{Position=new(21,3),Rules=new(Public:true)}],
            Layout=d.Layout! with{ResourceDistricts=[..d.Layout!.ResourceDistricts.Select(r=>r.Item=="wood"?r with{Cells=[source.Position,target,new(21,3)],PublicCells=[new(21,3)]}:r)]}};
        var s=new GameSession(new(d));var paver=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic));while(s.Engine.InspectRoads()!.Works.Single().Batch is null)s.Step();
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic,enabled:false));
        StorageC1Tests.Move(s,source.Position,2);RoadE2Tests.Step(s,new(ActionKind.BuyEquipment,Target:source.Position,EquipmentTypeId:d.EquipmentTypes!.Single(t=>t.ItemId=="wood").Id),2);
        var machine=s.Engine.Observe(new(2)).OwnEquipment.Single();
        RoadE2Tests.Step(s,new(ActionKind.SetExtractionRoute,Equipment:machine.Id,Stops:[target],Enabled:true),2);
        RoadE2Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic),2);
        bool bypass=false,restored=false;int bound=700;
        while(s.Engine.Observe(new(2)).OwnEquipment.Single().Position!=target&&bound-->0)
        {
            var e=s.Engine.Observe(new(2)).OwnEquipment.Single();Assert.NotEqual(new Cell(10,1),e.Position);
            Assert.Equal(e.Position,StorageC1Tests.Inventory(s,e.StorageId).Position);bypass|=e.Position.Z==2&&e.Position.X is >=9 and <=11;
            if(!restored&&e.Position==new Cell(10,2)){s=CheckpointStore.Decode(CheckpointStore.Encode(s));restored=true;}
            s.Step();
        }
        if(bound<=0&&Environment.GetEnvironmentVariable("DELSK_E2_DEBUG") is {} debug)
        {Directory.CreateDirectory(debug);File.WriteAllText(Path.Combine(debug,"extraction-stopped.json"),s.Engine.CreateCheckpointJson());}
        Assert.True(bound>0,"Extraction movement did not arrive");Assert.True(bypass,"Extraction did not physically use the bypass");Assert.True(restored,"Expected a restore inside the bypass");s.AdvanceTo(s.Engine.Tick+20);
        Assert.True(FleetE1Tests.Stock(s,machine.StorageId,"wood")>0);Assert.Single(s.Engine.Inspect().Equipment);Assert.Single(s.Engine.InspectFleet()!.Vehicles);
        Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="EquipmentTravelWages");RoadE2Tests.Exact(s);
    }
    private static ScenarioDefinition Definition()
    {
        var s=FleetE1Tests.FreightDefinition();var layout=s.Layout!;
        Cell[] cells=[new(36,4),new(37,4),new(36,5),new(37,5),new(36,6),new(37,6)];
        var centre=layout.RoadCenterCells.Concat(new[]{new Cell(36,2),new Cell(36,3)}).Distinct().ToArray();
        var corridor=centre.Concat(Enumerable.Range(0,40).Select(x=>new Cell(x,2))).Distinct().ToArray();
        return s with{Id="e2-freight",StartingTreasuryMinor=500000,Economy=RoadE2Tests.Profile().World.Rules,
            Buildings=[..s.Buildings!.Where(b=>b.Id!=10),new(10,1,new(36,4),"warehouse")],
            Plots=[..s.Plots!,..cells.Select((c,n)=>new PlotDefinition(150+n,c,10000,OwnerId:1))],
            Layout=layout with{IndustrialLots=[..layout.IndustrialLots,new("lot-3",cells,new(36,3))],RoadCenterCells=centre,RoadCorridorCells=corridor,InitialCityDirtCells=centre,
                TurnCells=[new(4,1),new(12,1),new(20,1),new(28,1),new(36,1)]}};
    }
    [Fact]public void ExistingFreightCarriesOneRealCargoThroughPaverBypassAndRestoresEveryStage()
    {
        var s=new GameSession(new(Definition()));var paver=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic));while(s.Engine.InspectRoads()!.Works.Single().Batch is null)s.Step();
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic,enabled:false));
        var car=FleetE1Tests.Buy(s,"muravey70");StorageC1Tests.Harvest(s,"wood",30);
        var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",30);
        FleetE1Tests.Board(s,car);StorageC1Tests.Move(s,new(4,4));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,new(ActionKind.AssignVehicle,VehicleId:car.Id,Enterprise:new(8)));
        var logistics=Assert.Single(s.Engine.Observe(new(1)).OwnLogistics);var destination=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;
        RoadE2Tests.Step(s,new(ActionKind.SetTransportRoute,Logistics:logistics.Id,Source:source,Destination:destination,ItemId:"wood",Enabled:true));
        int bound=1000;while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is null&&bound-->0)s.Step();Assert.True(bound>0);
        RoadE2Tests.Step(s,new(ActionKind.SetLogisticsEnabled,Logistics:logistics.Id,Direction:LogisticsDirection.Output,Enabled:false));
        var stages=new HashSet<string>();bool bypass=false;bound=1400;
        while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is {} trip&&bound-->0)
        {
            Assert.Equal(trip.Position,StorageC1Tests.Inventory(s,car.CargoInventory).Position);
            Assert.NotEqual(new Cell(10,1),trip.Position);bypass|=trip.Position.Z==2&&trip.Position.X is >=9 and <=11;
            if(stages.Add(trip.State))
            {
                var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
                for(int i=0;i<3;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
            }
            else s.Step();
        }
        Assert.True(bound>0);Assert.True(bypass);Assert.Contains("Outbound",stages);Assert.Contains("ReturningEmpty",stages);
        Assert.Equal(30,FleetE1Tests.Stock(s,destination,"wood"));Assert.Equal(0,FleetE1Tests.Stock(s,car.CargoInventory,"wood"));
        Assert.Equal(car.CargoInventory,s.Engine.Observe(new(1)).OwnLogistics.Single().CargoInventory);RoadE2Tests.Exact(s);
    }
}

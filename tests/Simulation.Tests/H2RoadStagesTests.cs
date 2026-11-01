using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class H2RoadStagesTests
{
    [Fact]public void RoadForty()=>Run(2,"sputnik20");
    internal static void Run(int level,string model)
    {
        long[] times=new long[2];
        for(int fast=0;fast<2;fast++)
        {
            var d=BillingE3Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=fast==1?new():null}}));
            var paver=FleetE1Tests.Buy(s,"trakt_d1");
            for(int stage=2;stage<=level;stage++)for(int begin=6;begin<=18;begin+=4)
            {
                int count=Math.Min(4,19-begin);
                if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==paver.Id).Driver!=0)
                {var at=s.Engine.FleetOf(new(1))!.Vehicles.Single(v=>v.Vehicle.Id==paver.Id).Position;StorageC1Tests.Move(s,new(at.X,2));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));}
                foreach(var material in s.Engine.EconomyRules.Roads!.Stages.Single(r=>r.Level==stage).Materials)
                {long quantity=material.Quantity*count;StorageC1Tests.Harvest(s,material.ItemId,quantity);StorageC1Tests.Put(s,paver.CargoInventory,material.ItemId,quantity);}
                FleetE1Tests.Board(s,paver);
                for(int x=begin;x<begin+count;x++)
                {
                    var cell=new Cell(x,1);StorageC1Tests.Move(s,cell);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,stage,target:cell));
                    int guard=50;while(s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==cell).Level<stage&&guard-->0)s.Step();Assert.True(guard>0);s.Step();
                }
            }
            StorageC1Tests.Move(s,new(19,2));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
            var car=BillingE3Tests.Car(s,model,2);long eta=fast==1?MovementTiming.PersonalTicks(s.Engine.ObserveController(new(2),true),new(6,1),new(18,1)):0;
            if(fast==1&&level==3&&model=="vector200")
            {
                if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==car.Id).FacingX==-1)
                    RoadE2Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id),2);
                s.SetTravelHeading(new(2),1,0);s.AdvanceTravelQuantum();
                var partial=s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id);
                Assert.True(partial.ProgressFraction==500000,System.Text.Json.JsonSerializer.Serialize(partial,SimulationJson.Options));
                var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
                for(int q=0;q<13;q++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
                s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();s.Step();StorageC1Tests.Move(s,new(4,1),2);StorageC1Tests.Move(s,new(6,1),2);
                eta=MovementTiming.PersonalTicks(s.Engine.ObserveController(new(2),true),new(6,1),new(18,1));
            }
            times[fast]=H2MovementTests.MoveTime(s,new(18,1),2);if(fast==1){Assert.Equal(eta,times[fast]);RoadE2Tests.Exact(s,"h2-surface-"+level+model);}
            Assert.Equal(new[]{20,40,60},s.Engine.EconomyRules.Roads!.Stages.Select(r=>r.Speed));
        }
        Assert.InRange((double)times[0]/times[1],2.3,2.8);
        H2MovementTests.Evidence(new{kind=model,level,distance=12,legacyTicks=times[0],fastTicks=times[1],ratio=(double)times[0]/times[1]});
    }
}

public class H2HighwayCarTests
{[Fact]public void HighwaySixty()=>H2RoadStagesTests.Run(3,"sputnik20");}
public class H2HighwayTruckTests
{[Fact]public void HighwayStillCapsAtModelFortyFive()=>H2RoadStagesTests.Run(3,"vector200");}

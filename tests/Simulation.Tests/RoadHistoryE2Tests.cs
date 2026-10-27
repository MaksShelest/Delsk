using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.RoadE2Tests;

namespace Simulation.Tests;

public class RoadHistoryE2Tests
{
    [Fact]public void RoadAndOtherNewNumericCrewsRemainValidAcrossElectionHistory()
    {
        var d=Prepared();d=d with{Participants=[..d.Participants,new(3,10000,new(2,1),2000),new(4,10000,new(3,1),2000)],Plots=[..d.Plots!,new(92,new(32,3),10000,IsShopSite:true),new(93,new(33,3),10000,IsShopSite:true)],Layout=d.Layout! with{MarketStalls=[..d.Layout!.MarketStalls,new("stall3",new(32,3)),new("stall4",new(33,3))]},Elections=new(PeriodTicks:800)};
        var s=new GameSession(new(d));var v=Paver(s);Step(s,new(ActionKind.LeaveVehicle));Step(s,Work(v,mode:EquipmentMode.Automatic));
        s.AdvanceTo(800);Assert.Contains(s.Engine.Inspect().Elections.Results.Single().Employment,g=>g.Role==EmploymentRole.RoadWorks);
        StorageC1Tests.Move(s,new(4,4));Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(8),EquipmentLevel:1));
        var car=FleetE1Tests.Buy(s,"muravey70");FleetE1Tests.Board(s,car);StorageC1Tests.Move(s,new(4,4));Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.AssignVehicle,VehicleId:car.Id,Enterprise:new(8)));
        s.AdvanceTo(1600);Assert.Equal(2,s.Engine.Inspect().Elections.Results.Length);Exact(s);
    }
}

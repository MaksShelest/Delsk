using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

// Funded diagnostic starts isolate driving from the many-hour earning gate. They
// retain real map geometry, NPC controllers, purchases, boarding and all Core rules.
public class I2NativeFixtureTests
{
    [Theory]
    [InlineData("compact_valley",3)][InlineData("compact_valley",6)][InlineData("compact_valley",10)]
    [InlineData("small_river",3)][InlineData("small_river",6)][InlineData("small_river",10)]
    [InlineData("large_districts",3)][InlineData("large_districts",6)][InlineData("large_districts",10)]
    public void PrepareAnOrdinaryClientDrivingSessionWithExplicitFunding(string map,int count)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),NpcEconomyG1Tests.Profile(),new(count),vnextBalance:MapDesignTests.Balance()).Scenario;
        d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{CashMinor=2_000_000}:p)]};
        var s=new GameSession(new(d));s.AttachControllers(d.Participants.Skip(1).Select(p=>NpcEconomyG1Tests.Controller(p.Id,p.PublicPreferenceItem)));
        I2Fixture.Move(s,s.Engine.FleetOf(new(1))!.Dealer);
        var roads=d.Layout!.InitialCityDirtCells.ToHashSet();var corridor=d.Layout.RoadCorridorCells.ToHashSet();
        var candidates=from cell in roads from direction in new[]{new Cell(1,0),new Cell(0,1)}
            where Enumerable.Range(-4,9).All(n=>roads.Contains(new(cell.X+n*direction.X,cell.Z+n*direction.Z)))
            where new[]{-1,1}.All(n=>corridor.Contains(new(cell.X-direction.Z*n,cell.Z+direction.X*n)))
            orderby Math.Abs(cell.X-s.Engine.FleetOf(new(1))!.Dealer.X)+Math.Abs(cell.Z-s.Engine.FleetOf(new(1))!.Dealer.Z),cell.Z,cell.X
            select new{cell,direction};
        var selected=candidates.First();string name="native-"+map+"-"+count;
        I2Fixture.Save(s,name);I2Fixture.Evidence(name+"-plan",new{map,count,selected.cell,selected.direction,preparedFunding=true});
        Assert.Equal(20000,d.Participants[1].CashMinor);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
}

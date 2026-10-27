using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.FleetE1Tests;

namespace Simulation.Tests;

public class FleetE1StockTests
{
    [Fact] public void DeliveryWaitsForMinimumAgeAndNeverReplacesPurchasedOrOverfillsParking()
    {
        var s=new GameSession(new(Prepared()));var initial=s.Engine.InspectFleet()!.Stock.Select(v=>v.Id).ToArray();s.AdvanceTo(2499);
        Assert.Equal(initial,s.Engine.InspectFleet()!.Stock.Select(v=>v.Id));var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(5,initial.Intersect(s.Engine.InspectFleet()!.Stock.Select(v=>v.Id)).Count());
        var car=Buy(s,"trakt_d1");s.AdvanceTo(3000);Assert.Contains(s.Engine.InspectFleet()!.Vehicles,v=>v.Id==car.Id);Assert.True(s.Engine.InspectFleet()!.Stock.Length<=6);
        Assert.DoesNotContain(s.Engine.InspectFleet()!.Stock,v=>v.Position==StorageC1Tests.Inventory(s,car.CargoInventory).Position);
        RestoreAndReplay(s);
    }
}

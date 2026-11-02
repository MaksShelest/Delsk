using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class CityCapitalReviewTests
{
    [Fact]
    public void UnpaidMunicipalRoadClaimsCountTowardTheSameCapitalUsedByCityStage()
    {
        var original = BillingE3Tests.Prepared();
        var reference = new GameSession(new(original));
        BillingE3Tests.Step(reference, new(ActionKind.SetAutoPay, Enabled: false));
        BillingE3Tests.Car(reference);
        long threshold = reference.Engine.PublicEconomy().CapitalMinor;
        var definition = original with
        {
            Economy = original.Economy! with
            {
                City = new(1, new(threshold, 0, 0), new(threshold + 1, 0, 0), new(threshold + 2, 0, 0))
            }
        };
        var session = GameSession.Replay(definition, reference.CommandTape, reference.Engine.Tick);
        StorageC1Tests.Move(session, new(16, 1));
        session.AdvanceTo((session.Engine.Tick / 120 + 1) * 120);
        Assert.True(session.Engine.AccountOf(new(1)).DebtMinor > 0);
        Assert.Equal(threshold, session.Engine.PublicEconomy().CapitalMinor);
        Assert.Equal(1, session.Engine.PublicEconomy().Stage);
        BillingE3Tests.Exact(session, "review-city-municipal-claim");
    }
}

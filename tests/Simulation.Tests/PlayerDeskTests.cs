using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PlayerDeskTests
{
    [Fact]
    public void WageCommandDoesNotWriteCashAndForeignSupportStaysHidden()
    {
        var left = new GameSession(new SimulationEngine(SupportTests.Scenario()));
        var right = new GameSession(new SimulationEngine(SupportTests.Scenario()));
        var group = left.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g => g.Role == EmploymentRole.Production);
        long requested = group.WageMinor + PlayerDesk.WageStepMinor;
        Assert.Equal("Accepted", LandContentFixtures.Reason(left, PlayerDesk.Wage(group.Id.Value, requested)));
        right.Step();
        Assert.Equal(right.Engine.Observe(new(1)).Account.BalanceMinor, left.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(requested, left.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g => g.Id == group.Id).PendingWageMinor);

        long election = left.Engine.Inspect().Elections.NextElectionId!.Value.Value;
        Assert.Equal("Accepted", LandContentFixtures.Reason(left, new(ActionKind.ProposeSupport, SupportSeller: new(3),
            Election: new(election), Compensation: [new("wood", 1)]), 2));
        long hidden = left.Engine.InspectSupport().Proposals.Single().Id;
        Assert.DoesNotContain(PlayerDesk.Read(left.Engine, new(1)).Support.Proposals, p => p.Id == hidden);
        Assert.Contains(PlayerDesk.Read(left.Engine, new(2)).Support.Proposals, p => p.Id == hidden && p.Seller == new ParticipantId(3));
        Assert.Throws<ArgumentException>(() => PlayerDesk.Check(new(ActionKind.CreateBuyOffer)));
    }

    [Fact]
    public void CardCommandsStaySeparateAndBothLanguagesShareKeys()
    {
        var work = PlayerDesk.Work(1, true);
        var supplies = PlayerDesk.Supplies(1, LogisticsDirection.Inbound, true);
        var wage = PlayerDesk.Wage(1, 100);
        Assert.Equal(ActionKind.SetProductionEnabled, work.Kind);
        Assert.Equal(ActionKind.SetLogisticsEnabled, supplies.Kind);
        Assert.Equal(ActionKind.SetWage, wage.Kind);
        Assert.Equal(PlayerText.Keys("ru").Order(), PlayerText.Keys("en").Order());
        Assert.Contains("card.work", PlayerText.Keys("ru"));
        Assert.Contains("card.supplies", PlayerText.Keys("ru"));
        Assert.Contains("card.wages", PlayerText.Keys("ru"));
        Assert.DoesNotContain(PlayerText.Keys("ru"), key => key.Contains("cash.write", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeSeedMatchesTheRecordedControllerFormula()
    {
        ulong seed = Agents.Algorithmic.AlgorithmicController.RuntimeSeed(42, 2);
        ulong again = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("controller-v1:42:2")));
        Assert.Equal(again, seed);
        Assert.NotEqual(seed, Agents.Algorithmic.AlgorithmicController.RuntimeSeed(42, 3));
    }
}

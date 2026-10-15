using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void CapitalDemandCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId owner = new(1);
        if (session.Engine.Observe(owner).Account.BalanceMinor != 0) throw new InvalidOperationException("Demand recovery fixture requires cash0");
        Execute(session, owner, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
        Wait(session, () => !session.Engine.Observe(owner).Participant.Busy);
        Move(session, owner, definition.CapitalDemand!.Terminal);
        var offer = session.Engine.Observe(owner).CapitalOffers.First(o => o.ItemId == "wood" && o.RemainingQuantity >= 2);
        Execute(session, owner, new(ActionKind.SellAvailableToCapital, OfferId: offer.Id, ItemId: "wood", Quantity: 2));
        Wait(session, () => !session.Engine.Observe(owner).Participant.Busy);
        session.AdvanceTo(185);
        var w = session.Engine.Inspect(); var demand = w.CapitalDemand!;
        if (session.Engine.Observe(owner).Account.BalanceMinor != 2 * offer.UnitPriceMinor ||
            w.Flows.Single(f => f.ItemId == "wood").Exported != 2 || demand.Periods.Length != 4 ||
            demand.Periods.Sum(p => p.GrossOutputBaseMinor) != 1000 ||
            demand.Periods.Any(p => p.TargetBaseMinor != 20000 || p.OrderedBaseMinor > p.TargetBaseMinor || p.TargetBaseMinor - p.OrderedBaseMinor >= 300) ||
            w.ExternalMoneyInMinor != demand.Periods.Sum(p => p.ExternalFundingMinor) || w.ExternalMoneyOutMinor <= 0)
            throw new InvalidOperationException("Capital demand recovery or periodic budgets differ from expected balances");
    }
}

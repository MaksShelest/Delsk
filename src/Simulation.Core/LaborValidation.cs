namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateLaborSettings(ScenarioDefinition s)
    {
        var l = s.Labor ?? new();
        if (l.StepTicks <= 0 || l.SalaryChangeDelayTicks < 0 || l.MobilityNumerator <= 0 ||
            l.MobilityDenominator is < 1 or > 1000000 || l.MobilityNumerator > l.MobilityDenominator ||
            l.GrowthPeriodTicks <= 0 || l.GrowthPerPeriod < 0 || l.MaximumPopulation is {} max && max<s.Population)
            throw new ArgumentException("Invalid labor settings");
        try { if(l.MaximumPopulation is null)_ = checked(s.Population + checked(s.MaxTicks / l.GrowthPeriodTicks * l.GrowthPerPeriod));
            _=checked(s.StartingTreasuryMinor+s.Participants.Aggregate(0L,(n,p)=>checked(n+p.CashMinor))); }
        catch (OverflowException ex) { throw new ArgumentException("Population growth overflow", ex); }
    }
}

namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateLaborSettings(ScenarioDefinition s)
    {
        var l = s.Labor ?? new();
        if (l.StepTicks <= 0 || l.SalaryChangeDelayTicks < 0 || l.MobilityNumerator <= 0 ||
            l.MobilityDenominator is < 1 or > 1000000 || l.MobilityNumerator > l.MobilityDenominator ||
            l.GrowthPeriodTicks <= 0 || l.GrowthPerPeriod < 0)
            throw new ArgumentException("Invalid labor settings");
        try { _ = checked(s.Population + checked(s.MaxTicks / l.GrowthPeriodTicks * l.GrowthPerPeriod)); }
        catch (OverflowException ex) { throw new ArgumentException("Population growth overflow", ex); }
    }
}

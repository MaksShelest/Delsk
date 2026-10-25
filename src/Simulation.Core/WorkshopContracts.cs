using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Simulation.Core;

/// <summary>Opt-in D1 execution; old resolved profiles keep their original production rules.</summary>
public sealed record WorkshopRules(long ServiceMinorPerFullTick = 2)
{
    public static int Rate(int staff) => staff switch { 0 => 0, 1 => 25, 2 => 45, 3 => 65, 4 => 85, 5 => 100, _ => throw new ArgumentOutOfRangeException(nameof(staff)) };
    public void Validate() { if (ServiceMinorPerFullTick is < 0 or > 10000) throw new ArgumentException("Invalid workshop service cost"); }
}
public sealed record WorkshopOutfit(long PriceMinor, ImmutableArray<MaterialCost> Materials, long Ticks = 60);
public sealed record WorkshopBatchTerms(RecipeDefinition Recipe, long WageMinor, long ServiceMinorPerFullTick);
public sealed record WorkshopView(long Id, EmploymentGroupId GroupId, string RecipeId, bool Enabled,
    int Staff, string ReasonCode, long BatchId, long WorkDone, long WorkRequired,
    long CompletedBatches, long ServicePaidMinor, long? BatchWageMinor, int ProductivityPercent);
public sealed record WorkshopInstallation(long Id, EnterpriseId Enterprise, long StartTick, long EndTick,
    long PriceMinor, ImmutableArray<MaterialCost> Materials, string CommandId);
internal sealed record SavedWorkshop(long Enterprise, SavedEnterprise Unit);
internal sealed record WorkshopTurn(long Id, long LastStart);
internal sealed record WorkshopCheckpoint(long StartSerial, WorkshopTurn[] Turns,
    SavedWorkshop[] Additional, WorkshopInstallation[] Installations);

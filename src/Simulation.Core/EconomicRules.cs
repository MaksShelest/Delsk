namespace Simulation.Core;

/// <summary>Balance knobs. Integers, minor currency units and basis points (10000 = 100%).</summary>
public sealed record EconomicRules(long PlayerWageStepMinor = 100, int ReservationFeeBasisPoints = 1000,
    int WageAttractionExponent = 3, int CommonPriceMinimumBasisPoints = 9000, int CommonPriceMaximumBasisPoints = 11000,
    int ItemPriceMinimumBasisPoints = 8000, int ItemPriceMaximumBasisPoints = 12000,
    int NoiseMinimumBasisPoints = 9800, int NoiseMaximumBasisPoints = 10200,
    int DemandWeightMinimumBasisPoints = 5000, int DemandWeightMaximumBasisPoints = 15000, CityProgression? City = null, HandlingRules? Handling = null,NegotiationSettings? Negotiations=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StorageRules? Storage=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DemolitionRules? Demolition=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkshopRules? Workshops=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DevelopmentRules? Development=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ProductionContentRules? ProductionContent=null)
{
    public long ReservationFee(long price) => checked((long)(((Int128)price * ReservationFeeBasisPoints + 9999) / 10000));
    public void Validate()
    {
        Storage?.Validate();Demolition?.Validate();Workshops?.Validate();Development?.Validate();
        (Negotiations??new()).Validate();
        (City ?? new()).Validate(); (Handling ?? new()).Validate();
        if (PlayerWageStepMinor is < 1 or > 10000 || ReservationFeeBasisPoints is < 0 or > 10000 || WageAttractionExponent is < 1 or > 5)
            throw new ArgumentException("Invalid economy wage step, reservation fee or attraction exponent");
        foreach (var (min, max) in new[] { (CommonPriceMinimumBasisPoints, CommonPriceMaximumBasisPoints),
            (ItemPriceMinimumBasisPoints, ItemPriceMaximumBasisPoints), (NoiseMinimumBasisPoints, NoiseMaximumBasisPoints),
            (DemandWeightMinimumBasisPoints, DemandWeightMaximumBasisPoints) })
            if (min < 1 || max < min || max > 30000) throw new ArgumentException("Invalid economy wave range (1..30000 basis points)");
    }
}

public sealed partial class SimulationEngine
{
    public EconomicRules EconomyRules => ResearchWageExponent is { } exponent ? (_config.Economy??new()) with {WageAttractionExponent=exponent} : _config.Economy ?? new();
}

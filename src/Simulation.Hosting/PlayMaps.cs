namespace Simulation.Hosting;

/// <summary>Prepared maps only. Reading this catalog does not start a world.</summary>
public static class PlayMaps
{
    public const string First = "first";
    public const string FirstScenarioId="presentation-map-one-v3";
    public const string CurrentSecondScenarioId="presentation-map-two-v3";
    public const string Third = "third";
    public const string Balanced = "balanced-four";
    public const string BalancedScenarioId = "four-corners-v1";
    public const string ThirdScenarioId = "presentation-map-three-v2";
    public const string Second = "second";
    public const string SecondScenarioId = "presentation-map-two-v1";
    public static string Resource(string key) => key switch
    {
        Balanced => "Play.four-corners-v1.json",
        Third => "Play.presentation-map-three-v2.json",
        First => "Play.presentation-map-one-v3.json",
        Second => "Play.presentation-map-two-v3.json",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
    public static string KeyForScenario(string id) => id == BalancedScenarioId ? Balanced : (id == ThirdScenarioId || id == "presentation-map-three-v1") ? Third : (id == SecondScenarioId || id == CurrentSecondScenarioId || id == "presentation-map-two-v2") ? Second : First;
    public static string Name(string key, string language) => key == Balanced ? PlayerText.Local(language, "Четыре квартала", "Four quarters") : PlayerText.Local(language,
        key == Third ? "Третья карта" : key == Second ? "Вторая карта" : "Первая карта", key == Third ? "Third map" : key == Second ? "Second map" : "First map");
}

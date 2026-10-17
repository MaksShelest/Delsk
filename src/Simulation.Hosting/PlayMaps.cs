namespace Simulation.Hosting;

/// <summary>Prepared maps only. Reading this catalog does not start a world.</summary>
public static class PlayMaps
{
    public const string First = "first";
    public const string Second = "second";
    public const string SecondScenarioId = "presentation-map-two-v1";
    public static string Resource(string key) => key switch
    {
        First => "Play.baseline-economy.json",
        Second => "Play.presentation-map-two.json",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
    public static string KeyForScenario(string id) => id == SecondScenarioId ? Second : First;
    public static string Name(string key, string language) => PlayerText.Local(language,
        key == Second ? "Вторая карта" : "Первая карта", key == Second ? "Second map" : "First map");
}

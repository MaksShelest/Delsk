namespace Simulation.Hosting;

/// <summary>Prepared maps only. Reading this catalog does not start a world.</summary>
public static class PlayMaps
{
    public const string SmallRiver="small-river";
    public const string LargeDistricts="large-districts";
    public static bool IsDesigned(string key)=>key is SmallRiver or LargeDistricts;
    public const string FourQuarters="four-quarters";
    public const string CityCorridor="city-corridor";
    public static readonly string[] All=[First,Second,Third,FourQuarters,CityCorridor,SmallRiver,LargeDistricts];
    public const string First = "first";
    public const string FirstScenarioId="presentation-map-one-v3";
    public const string CurrentSecondScenarioId="presentation-map-two-v3";
    public const string Third = "third";
    public const string ThirdScenarioId = "presentation-map-three-v2";
    public const string Second = "second";
    public const string SecondScenarioId = "presentation-map-two-v1";
    public static string Resource(string key) => key switch
    {
        SmallRiver => "Play.small-river.json",
        LargeDistricts => "Play.large-districts.json",
        FourQuarters => "Play.four-corners-v1.json",
        CityCorridor => "Play.city-corridor-v1.json",
        Third => "Play.presentation-map-three-v2.json",
        First => "Play.presentation-map-one-v3.json",
        Second => "Play.presentation-map-two-v3.json",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
    public static string KeyForScenario(string id) => id=="small_river" ? SmallRiver : id=="large_districts" ? LargeDistricts : id=="four-corners-v1" ? FourQuarters : id=="city-corridor-v1" ? CityCorridor : (id == ThirdScenarioId || id == "presentation-map-three-v1") ? Third : (id == SecondScenarioId || id == CurrentSecondScenarioId || id == "presentation-map-two-v2") ? Second : First;
    public static string Name(string key, string language) => PlayerText.Local(language,
        key == SmallRiver ? "Речные кварталы · D3" : key == LargeDistricts ? "Большой округ · D3" : key == FourQuarters ? "Четыре квартала" : key == CityCorridor ? "Городской тракт" : key == Third ? "Третья карта" : key == Second ? "Вторая карта" : "Первая карта", key == SmallRiver ? "River quarters · D3" : key == LargeDistricts ? "Large district · D3" : key == FourQuarters ? "Four quarters" : key == CityCorridor ? "City corridor" : key == Third ? "Third map" : key == Second ? "Second map" : "First map");
}

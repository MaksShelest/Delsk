namespace Simulation.Hosting;

/// <summary>Prepared maps only. Reading this catalog does not start a world.</summary>
public static class PlayMaps
{
    public static readonly string[] UserCatalog=[CompactValley,Second];
    public const string SmallRiver="small-river";
    public const string LargeDistricts="large-districts";
    public const string CompactValley="compact-valley";
    public static bool IsDesigned(string key)=>key is SmallRiver or LargeDistricts or CompactValley or Second;
    public const string CompactValleyScenarioId="compact_valley_i6c2";
    public const string WoodlandScenarioId="woodland_region_i6c2";
    public const string FourQuarters="four-quarters";
    public const string CityCorridor="city-corridor";
    public static readonly string[] All=[First,Second,Third,FourQuarters,CityCorridor,SmallRiver,LargeDistricts,CompactValley];
    public const string First = "first";
    public const string FirstScenarioId="presentation-map-one-v3";
    public const string CurrentSecondScenarioId="presentation-map-two-v3";
    public const string Third = "third";
    public const string ThirdScenarioId = "presentation-map-three-v2";
    public const string Second = "second";
    public const string SecondScenarioId = "presentation-map-two-v1";
    // Versioned new starts. Resource retains the historical snapshots for explicit legacy callers.
    public static string NewGameResource(string key)=>key switch
    {
        CompactValley=>"Play.compact-valley-i6c2.json",
        Second=>"Play.woodland-region-i6c2.json",
        _=>Resource(key)
    };
    public static string Resource(string key) => key switch
    {
        SmallRiver => "Play.small-river.json",
        LargeDistricts => "Play.large-districts.json",
        CompactValley => "Play.compact-valley.json",
        FourQuarters => "Play.four-corners-v1.json",
        CityCorridor => "Play.city-corridor-v1.json",
        Third => "Play.presentation-map-three-v2.json",
        First => "Play.presentation-map-one-v3.json",
        Second => "Play.presentation-map-two-v3.json",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
    public static string KeyForScenario(string id) => id is "compact_valley" or CompactValleyScenarioId ? CompactValley : id=="small_river" ? SmallRiver : id=="large_districts" ? LargeDistricts : id=="four-corners-v1" ? FourQuarters : id=="city-corridor-v1" ? CityCorridor : (id == ThirdScenarioId || id == "presentation-map-three-v1") ? Third : (id == SecondScenarioId || id == CurrentSecondScenarioId || id == WoodlandScenarioId || id == "presentation-map-two-v2") ? Second : First;
    public static string Name(string key, string language) => PlayerText.Local(language,
        key == CompactValley ? "Малая долина" : key == SmallRiver ? "Речные кварталы · E2" : key == LargeDistricts ? "Большой округ · E2" : key == FourQuarters ? "Четыре квартала" : key == CityCorridor ? "Городской тракт" : key == Third ? "Третья карта" : key == Second ? "Лесной край" : "Первая карта", key == CompactValley ? "Small valley" : key == SmallRiver ? "River quarters · E2" : key == LargeDistricts ? "Large district · E2" : key == FourQuarters ? "Four quarters" : key == CityCorridor ? "City corridor" : key == Third ? "Third map" : key == Second ? "Woodland Region" : "First map");
    public static string Description(string key,string language)=>key==CompactValley?PlayerText.Local(language,
        "Лесные массивы и поля, городской центр и 15 производственных площадок. Восемь ресурсов и все шесть отраслей; игрок и 3–10 NPC.",
        "Woodlands and fields, a town center and 15 industrial sites. Eight resources and all six industries; one player and 3–10 NPCs."):key==Second?PlayerText.Local(language,
        "Небольшое лесное поселение с ратушей, рынком и шестью производственными площадками. Современная экономика и восемь ресурсов; игрок и три NPC.",
        "A small woodland settlement with a town hall, market and six industrial sites. Current economy and eight resources; one player and three NPCs."):"";
}

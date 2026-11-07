namespace Simulation.Hosting;

/// <summary>Prepared maps only. Reading this catalog does not start a world.</summary>
public static class PlayMaps
{
    public static readonly string[] UserCatalog=[CompactValley,Second];
    public const string SmallRiver="small-river";
    public const string LargeDistricts="large-districts";
    public const string CompactValley="compact-valley";
    public static bool IsDesigned(string key)=>key is SmallRiver or LargeDistricts or CompactValley or Second;
    public const string CompactValleyScenarioId="compact_valley_i6c21";
    public const string CompactValleyI6C2ScenarioId="compact_valley_i6c2";
    public const string WoodlandScenarioId="woodland_region_i6c2";
    public const string WoodlandI6C4ScenarioId="woodland_region_i6c4";
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
        CompactValley=>"Play.compact-valley-i6c21.json",
        Second=>"Play.woodland-region-i6c4.json",
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
    public static string KeyForScenario(string id) => id is "compact_valley" or CompactValleyScenarioId or CompactValleyI6C2ScenarioId ? CompactValley : id=="small_river" ? SmallRiver : id=="large_districts" ? LargeDistricts : id=="four-corners-v1" ? FourQuarters : id=="city-corridor-v1" ? CityCorridor : (id == ThirdScenarioId || id == "presentation-map-three-v1") ? Third : (id == SecondScenarioId || id == CurrentSecondScenarioId || id == WoodlandScenarioId || id == WoodlandI6C4ScenarioId || id == "presentation-map-two-v2") ? Second : First;
    public static string Name(string key, string language) => PlayerText.Local(language,
        key == CompactValley ? "Малая долина" : key == SmallRiver ? "Речные кварталы · E2" : key == LargeDistricts ? "Большой округ · E2" : key == FourQuarters ? "Четыре квартала" : key == CityCorridor ? "Городской тракт" : key == Third ? "Третья карта" : key == Second ? "Лесной край" : "Первая карта", key == CompactValley ? "Small valley" : key == SmallRiver ? "River quarters · E2" : key == LargeDistricts ? "Large district · E2" : key == FourQuarters ? "Four quarters" : key == CityCorridor ? "City corridor" : key == Third ? "Third map" : key == Second ? "Woodland Region" : "First map");
    public static string Description(string key,string language)=>key==CompactValley?PlayerText.Local(language,
        "Слева ресурсы, в центре пятнадцать площадок, справа город. Одна дорога и два узких ответвления; игрок и 3–10 NPC.",
        "Resources on the left, fifteen sites in the center and the town on the right. One road and two narrow branches; one player and 3–10 NPCs."):key==Second?PlayerText.Local(language,
        "Зелёная карта: лесные группы, шесть площадок внутри кольца и город на востоке. Одна дорога; игрок и три NPC.",
        "A green map: forest groups, six sites inside a ring and the town on the east. One road; one player and three NPCs."):"";
}

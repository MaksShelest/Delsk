namespace Simulation.Hosting;

/// <summary>Local presentation choices. Never serialized into world checkpoints or controller observations.</summary>
public sealed record AppearancePreferences
{
    public const string Classic = "classic", Cozy = "cozy";
    public string WorldSet { get; init; } = Cozy;
    public string PeopleSet { get; init; } = Cozy;
    public Dictionary<string,string> ModelSets { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<long,int> ActorSkins { get; init; } = [];
    public static readonly string[] ModelKeys = ["sawmill","bakery","dairy","sugar-factory","confectionery","shop","capital","resource:wood","resource:grain","resource:beet","resource:milk","truck","equipment","terminal","dealership"];
    public static bool IsSet(string? value) => value is Classic or Cozy;
    public AppearancePreferences Normalize() => new()
    {
        WorldSet = IsSet(WorldSet) ? WorldSet : Cozy,
        PeopleSet = IsSet(PeopleSet) ? PeopleSet : Cozy,
        ModelSets = (ModelSets ?? []).Where(p=>ModelKeys.Contains(p.Key,StringComparer.Ordinal) && IsSet(p.Value)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal),
        ActorSkins = (ActorSkins ?? []).Where(p=>p.Key>0 && p.Value>=0 && p.Value<AppearanceCatalog.People.Length).ToDictionary(p=>p.Key,p=>p.Value)
    };
    public string ForModel(string key) => ModelSets.TryGetValue(key,out var value) ? value : WorldSet;
    public int ForActor(long id) => ActorSkins.TryGetValue(id,out var value) ? value : AppearanceCatalog.DefaultIndex(id);
    public AppearancePreferences WithActor(long id,int index)
    {
        if(id<=0 || index<0 || index>=AppearanceCatalog.People.Length)throw new ArgumentOutOfRangeException(nameof(index));
        return this with { PeopleSet=Cozy, ActorSkins=new(ActorSkins){[id]=index} };
    }
    public AppearancePreferences WithModel(string key,string set)
    {
        if(!ModelKeys.Contains(key,StringComparer.Ordinal) || !IsSet(set))throw new ArgumentException("Unknown presentation skin");
        return this with { ModelSets=new(ModelSets,StringComparer.Ordinal){[key]=set} };
    }
}

public sealed record PersonAppearance(string Asset,string RussianName,string EnglishName);
public static class AppearanceCatalog
{
    public static readonly PersonAppearance[] People =
    [
        new("NPC_01_Alina","Алина","Alina"), new("NPC_02_Mikhail","Михаил","Mikhail"),
        new("NPC_03_James","Джеймс","James"), new("NPC_04_Lin","Линь Мэй","Lin Mei"),
        new("NPC_05_Sofia","София","Sofia"), new("NPC_06_Thomas","Томас","Thomas"),
        new("NPC_07_Elena","Елена","Elena"), new("NPC_08_Amir","Амир","Amir"),
        new("NPC_09_Diego","Диего","Diego"), new("NPC_10_Nora","Нора","Nora")
    ];
    public static int DefaultIndex(long actor) => actor<=0 ? 0 : (int)((actor-1)%People.Length);
    public static string Name(int index,string language) => language=="en" ? People[index].EnglishName : People[index].RussianName;
    public static string ResourceModel(string item,long stock,long capacity,bool sparse)
    {
        if(item=="wood")return "Forest_"+(sparse?"Sparse":"Rich")+"_"+(stock<=0?"Cut":stock<capacity-stock?"Young":"Full");
        if(item=="milk")return sparse?"Pasture_1Cow":"Pasture_2Cow";
        return item is "grain" or "beet" ? stock<=0?"EmptyField":item=="beet"?"BeetField":"WheatField" : "";
    }
}

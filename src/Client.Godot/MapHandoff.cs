using Agents.Algorithmic;

/// <summary>Передача карты между редактором и игровой сценой. Геометрия партии берётся из сохранения, не из файла после старта.</summary>
public static class MapHandoff
{
    public static string? Play{get;private set;}
    public static string? Edit{get;private set;}
    public static int Npcs{get;private set;}=3;
    public static NpcDifficulty Difficulty{get;private set;}=NpcDifficulty.Medium;
    public static void QueuePlay(string path,int npcs,NpcDifficulty difficulty){Play=path;Npcs=Math.Clamp(npcs,3,10);Difficulty=difficulty;}
    public static void QueueEdit(string path){Edit=path;}
    public static string? ConsumeEdit(){var path=Edit;Edit=null;return path;}
    public static void ClearPlay(){Play=null;}
}

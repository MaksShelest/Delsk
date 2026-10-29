namespace Simulation.Hosting;
public static class GameDuration
{
    public static string Format(long ticks,string language)
    {ticks=Math.Max(0,ticks);return ticks>=3600?$"{ticks/3600}:{ticks/60%60:00}:{ticks%60:00}":$"{ticks/60}:{ticks%60:00}";}
}

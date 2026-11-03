namespace Simulation.Hosting;

/// <summary>Общий курс для клавиш и будущего стика. Ядро принимает только -1, 0, 1.</summary>
public readonly record struct TravelHeading(int X, int Z)
{
    public static TravelHeading FromKeys(bool east, bool west, bool south, bool north)
    {
        int x = (east ? 1 : 0) - (west ? 1 : 0);
        int z = (south ? 1 : 0) - (north ? 1 : 0);
        return new(x, z);
    }

    public static TravelHeading FromStick(int xMilli, int zMilli)
    {
        if (xMilli is < -1000 or > 1000) throw new ArgumentOutOfRangeException(nameof(xMilli));
        if (zMilli is < -1000 or > 1000) throw new ArgumentOutOfRangeException(nameof(zMilli));
        return new(Quantize(xMilli), Quantize(zMilli));
    }

    private static int Quantize(int milli) => milli >= 500 ? 1 : milli <= -500 ? -1 : 0;
}

/// <summary>Per-device hysteresis prevents analogue noise from changing held intent.</summary>
public sealed class TravelStick
{
    public TravelHeading Heading {get;private set;}
    public TravelHeading Sample(int xMilli,int zMilli)
    {
        if(xMilli is < -1000 or >1000||zMilli is < -1000 or >1000)throw new ArgumentOutOfRangeException(nameof(xMilli));
        int Axis(int value,int previous)=>value>=550?1:value<=-550?-1:Math.Abs(value)<350?0:previous;
        Heading=new(Axis(xMilli,Heading.X),Axis(zMilli,Heading.Z));return Heading;
    }
    public void Clear()=>Heading=default;
}

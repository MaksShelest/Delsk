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

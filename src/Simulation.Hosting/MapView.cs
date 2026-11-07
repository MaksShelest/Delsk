using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Поворот только картинки. Игровые координаты и JSON не меняются.</summary>
public static class MapView
{
    public static (int Width, int Height) DisplaySize(int width, int height, int quarter)
        => (quarter & 1) == 1 ? (height, width) : (width, height);

    public static (int X, int Y) ToScreen(int width, int height, int quarter, Cell world)
    {
        quarter = ((quarter % 4) + 4) % 4;
        return quarter switch
        {
            1 => (height - 1 - world.Z, world.X),
            2 => (width - 1 - world.X, height - 1 - world.Z),
            3 => (world.Z, width - 1 - world.X),
            _ => (world.X, world.Z)
        };
    }

    public static Cell ToWorld(int width, int height, int quarter, int screenX, int screenY)
    {
        quarter = ((quarter % 4) + 4) % 4;
        return quarter switch
        {
            1 => new(screenY, height - 1 - screenX),
            2 => new(width - 1 - screenX, height - 1 - screenY),
            3 => new(width - 1 - screenY, screenX),
            _ => new(screenX, screenY)
        };
    }
}

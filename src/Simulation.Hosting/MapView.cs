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

    public static (float X, float Y) MapPoint(float panX, float panY, float zoom, float cursorX, float cursorY)
        => ((cursorX - panX) / zoom, (cursorY - panY) / zoom);

    public static (float X, float Y) ZoomOffset(float cursorX, float cursorY, float anchorX, float anchorY, float zoom)
        => (cursorX - anchorX * zoom, cursorY - anchorY * zoom);

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

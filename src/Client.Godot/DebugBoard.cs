using Godot;
using Simulation.Hosting;

/// <summary>Плоская карта той же партии. Значок и подпись различают объект, не только цвет.</summary>
public partial class DebugBoard : Control
{
    public event System.Action<int, int>? CellChosen;
    private PresentationFrame? _frame;
    private int? _selectedX;
    private int? _selectedZ;

    public DebugBoard()
    {
        CustomMinimumSize = new Vector2(520, 360);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void Show(PresentationFrame frame, int? selectedX, int? selectedZ)
    {
        _frame = frame;
        _selectedX = selectedX;
        _selectedZ = selectedZ;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_frame is null || @event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left)
            return;
        if (CellAt(button.Position) is not { } cell) return;
        CellChosen?.Invoke(cell.X, cell.Y);
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (_frame is null || _frame.MapWidth < 1 || _frame.MapHeight < 1 || Size.X < 8 || Size.Y < 8) return;
        float cell = Mathf.Min(Size.X / _frame.MapWidth, Size.Y / _frame.MapHeight);
        var font = ThemeDB.FallbackFont;
        for (int x = 0; x < _frame.MapWidth; x++)
            for (int z = 0; z < _frame.MapHeight; z++)
                DrawRect(new Rect2(x * cell, z * cell, cell - 1, cell - 1), new Color(0.36f, 0.44f, 0.3f));
        foreach (var marker in _frame.Markers)
            DrawMarker(marker, cell);
        foreach (var actor in _frame.Actors)
            DrawActor(actor, cell, font);
        foreach (var trip in _frame.Trips)
            DrawCargo(trip, cell);
        if (_selectedX is int sx && _selectedZ is int sz)
            DrawRect(new Rect2(sx * cell, sz * cell, cell - 1, cell - 1), new Color(1, 0.9f, 0.3f), false, 2);
    }

    private void DrawMarker(PresentationMarker marker, float cell)
    {
        var area = new Rect2(marker.X * cell + 2, marker.Z * cell + 2, cell - 5, cell - 5);
        var center = area.GetCenter();
        switch (marker.Kind)
        {
            case "blocked":
                DrawRect(area, new Color(0.2f, 0.2f, 0.22f));
                DrawLine(area.Position, area.End, Colors.White, 1);
                DrawLine(new Vector2(area.End.X, area.Position.Y), new Vector2(area.Position.X, area.End.Y), Colors.White, 1);
                break;
            case "corridor":
                DrawRect(area, new Color(0.62f, 0.56f, 0.42f));
                DrawLine(new Vector2(area.Position.X, center.Y), new Vector2(area.End.X, center.Y), new Color(0.3f, 0.26f, 0.18f), 2);
                break;
            case "plot-free":
                DrawRect(area, new Color(0.62f, 0.45f, 0.26f), false, 2);
                break;
            case "plot-own":
                DrawRect(area, new Color(0.45f, 0.7f, 0.3f));
                DrawRect(area, Colors.White, false, 2);
                break;
            case "plot-foreign":
                DrawRect(area, new Color(0.5f, 0.28f, 0.24f));
                break;
            case "plot-reserved":
                DrawRect(area, new Color(0.75f, 0.58f, 0.2f), false, 2);
                DrawRect(area.Grow(-4), new Color(0.75f, 0.58f, 0.2f), false, 1);
                break;
            case "shop-plot":
                DrawRect(area, new Color(0.46f, 0.36f, 0.55f));
                break;
            case "resource":
                if (marker.Label == "grain")
                    DrawColoredPolygon(Diamond(center, cell * 0.22f), new Color(0.9f, 0.78f, 0.2f));
                else
                    DrawCircle(center, cell * 0.18f, new Color(0.2f, 0.62f, 0.24f));
                break;
            case "production":
                DrawRect(new Rect2(center.X - cell * 0.16f, center.Y - cell * 0.16f, cell * 0.32f, cell * 0.32f), new Color(0.55f, 0.32f, 0.16f));
                break;
            case "warehouse":
                DrawRect(new Rect2(center.X - cell * 0.28f, center.Y - cell * 0.12f, cell * 0.56f, cell * 0.24f), new Color(0.25f, 0.42f, 0.7f));
                break;
            case "shop":
                DrawColoredPolygon([center + new Vector2(0, -cell * 0.2f), center + new Vector2(cell * 0.18f, cell * 0.16f), center + new Vector2(-cell * 0.18f, cell * 0.16f)], new Color(0.62f, 0.28f, 0.7f));
                break;
            case "capital":
                DrawColoredPolygon(Diamond(center, cell * 0.2f), new Color(0.95f, 0.72f, 0.15f));
                break;
        }
    }

    private void DrawActor(PresentationActor actor, float cell, Font? font)
    {
        int index = _frame!.Actors.Where(a => a.X == actor.X && a.Z == actor.Z && a.Id <= actor.Id).Count() - 1;
        var center = new Vector2((actor.X + 0.28f + index * 0.22f) * cell, (actor.Z + 0.72f) * cell);
        var color = actor.Human ? Colors.White : actor.Id switch
        {
            2 => new Color(0.9f, 0.25f, 0.22f),
            3 => new Color(0.25f, 0.48f, 0.95f),
            _ => new Color(0.95f, 0.55f, 0.15f)
        };
        DrawCircle(center, cell * 0.12f, color);
        font?.DrawString(GetCanvasItem(), center + new Vector2(-4, 4), actor.Id.ToString(), HorizontalAlignment.Left, -1, 12, Colors.Black);
    }

    private void DrawCargo(PresentationTrip trip, float cell)
    {
        var origin = new Vector2((trip.X + 0.55f) * cell, (trip.Z + 0.15f) * cell);
        DrawRect(new Rect2(origin, new Vector2(cell * 0.28f, cell * 0.22f)), new Color(0.95f, 0.82f, 0.25f));
    }

    private Vector2I? CellAt(Vector2 position)
    {
        if (_frame is null) return null;
        float cell = Mathf.Min(Size.X / _frame.MapWidth, Size.Y / _frame.MapHeight);
        if (cell <= 0) return null;
        int x = Mathf.FloorToInt(position.X / cell);
        int z = Mathf.FloorToInt(position.Y / cell);
        if (x < 0 || z < 0 || x >= _frame.MapWidth || z >= _frame.MapHeight) return null;
        return new Vector2I(x, z);
    }

    private static Vector2[] Diamond(Vector2 center, float radius) =>
    [
        center + new Vector2(0, -radius),
        center + new Vector2(radius, 0),
        center + new Vector2(0, radius),
        center + new Vector2(-radius, 0)
    ];
}

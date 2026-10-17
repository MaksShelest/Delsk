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
        CustomMinimumSize = new Vector2(320, 210);
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
            if (marker.Kind != "truck-home" || !_frame.Trips.Any(t => t.LogisticsId == marker.EntityId)) DrawMarker(marker, cell);
        foreach (var actor in _frame.Actors)
            DrawActor(actor, cell, font);
        foreach (var trip in _frame.Trips)
            DrawCargo(trip, cell);
        if (_selectedX is int sx && _selectedZ is int sz)
            DrawRect(new Rect2(sx * cell, sz * cell, cell - 1, cell - 1), new Color(1, 0.9f, 0.3f), false, 2);
    }

    private void DrawMarker(PresentationMarker marker, float cell)
    {
        MapSymbols.Draw(this, marker.Kind,
            new Rect2(marker.X * cell + 2, marker.Z * cell + 2, cell - 5, cell - 5), marker.Label);
    }

    private void DrawActor(PresentationActor actor, float cell, Font? font)
    {
        int index = _frame!.Actors.Where(a => a.X == actor.X && a.Z == actor.Z && a.Id <= actor.Id).Count() - 1;
        var center = new Vector2((actor.X + 0.28f + index * 0.22f) * cell, (actor.Z + 0.72f) * cell);
        var color = MapSymbols.ActorColor(actor.Id, actor.Human);
        DrawCircle(center, cell * 0.12f, color);
        font?.DrawString(GetCanvasItem(), center + new Vector2(-4, 4), actor.Id.ToString(), HorizontalAlignment.Left, -1, 12, Colors.Black);
    }

    private void DrawCargo(PresentationTrip trip, float cell)
    {
        var origin = new Vector2((trip.X + 0.55f) * cell, (trip.Z + 0.15f) * cell);
        DrawRect(new Rect2(origin, new Vector2(cell * 0.28f, cell * 0.22f)), trip.Loaded ? MapSymbols.CargoColor : new Color("5b94b1"));
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

}

using Godot;

/// <summary>The board and its legend use exactly the same symbols.</summary>
public static class MapSymbols
{
    public static readonly Color CargoColor = new(0.95f, 0.82f, 0.25f);
    public static Color ActorColor(long id, bool human) => human ? Colors.White : id switch
    { 2 => new(0.9f, 0.25f, 0.22f), 3 => new(0.25f, 0.48f, 0.95f), _ => new(0.95f, 0.55f, 0.15f) };
    public static void Draw(Control canvas, string kind, Rect2 area, string label = "")
    {
        float cell = Mathf.Min(area.Size.X, area.Size.Y) + 5;
        var center = area.GetCenter();
        switch (kind)
        {
            case "blocked":
                canvas.DrawRect(area, new Color(0.2f, 0.2f, 0.22f));
                canvas.DrawLine(area.Position, area.End, Colors.White, 1);
                canvas.DrawLine(new Vector2(area.End.X, area.Position.Y), new Vector2(area.Position.X, area.End.Y), Colors.White, 1);
                break;
            case "corridor":
                canvas.DrawRect(area, new Color("78858a"));
                canvas.DrawLine(new Vector2(area.Position.X, center.Y), new Vector2(area.End.X, center.Y), new Color("e6ddba"), 2);
                break;
            case "plot-free":
                canvas.DrawRect(area, new Color(0.62f, 0.45f, 0.26f), false, 2);
                break;
            case "plot-own":
                canvas.DrawRect(area, new Color(0.45f, 0.7f, 0.3f));
                canvas.DrawRect(area, Colors.White, false, 2);
                break;
            case "plot-foreign":
                canvas.DrawRect(area, new Color(0.5f, 0.28f, 0.24f));
                break;
            case "plot-reserved":
                canvas.DrawRect(area, new Color(0.75f, 0.58f, 0.2f), false, 2);
                canvas.DrawRect(area.Grow(-4), new Color(0.75f, 0.58f, 0.2f), false, 1);
                break;
            case "shop-plot":
                canvas.DrawRect(area, new Color(0.46f, 0.36f, 0.55f));
                break;
            case "resource-depleted":
                canvas.DrawCircle(center,cell*.13f,new Color("795438"));
                break;
            case "resource":
                if (label == "grain")
                    canvas.DrawColoredPolygon(Diamond(center, cell * 0.22f), new Color(0.9f, 0.78f, 0.2f));
                else if(label=="milk")canvas.DrawRect(new Rect2(center-new Vector2(cell*.14f,cell*.18f),new Vector2(cell*.28f,cell*.36f)),new Color("b6e3f0"));
                else if(label=="beet")canvas.DrawCircle(center,cell*.18f,new Color("b85684"));
                else
                    canvas.DrawCircle(center, cell * 0.18f, new Color(0.2f, 0.62f, 0.24f));
                break;
            case "production":
                canvas.DrawRect(new Rect2(center.X - cell * 0.16f, center.Y - cell * 0.16f, cell * 0.32f, cell * 0.32f), new Color(0.55f, 0.32f, 0.16f));
                break;
            case "warehouse":
                canvas.DrawRect(new Rect2(center.X - cell * 0.28f, center.Y - cell * 0.12f, cell * 0.56f, cell * 0.24f), new Color(0.25f, 0.42f, 0.7f));
                break;
            case "shop":
                canvas.DrawColoredPolygon([center + new Vector2(0, -cell * 0.2f), center + new Vector2(cell * 0.18f, cell * 0.16f), center + new Vector2(-cell * 0.18f, cell * 0.16f)], new Color(0.62f, 0.28f, 0.7f));
                break;
            case "equipment":
                canvas.DrawRect(new Rect2(center - new Vector2(cell * 0.18f, cell * 0.12f), new Vector2(cell * 0.36f, cell * 0.24f)), new Color("d4af4d"));
                break;
            case "truck-home":
                canvas.DrawRect(new Rect2(center - new Vector2(cell * 0.18f, cell * 0.1f), new Vector2(cell * 0.36f, cell * 0.2f)), new Color("5b94b1"));
                break;
            case "capital":
                canvas.DrawColoredPolygon(Diamond(center, cell * 0.2f), new Color(0.95f, 0.72f, 0.15f));
                break;
        }

    }
    private static Vector2[] Diamond(Vector2 center, float radius) =>
    [center + new Vector2(0,-radius), center + new Vector2(radius,0), center + new Vector2(0,radius), center + new Vector2(-radius,0)];
}

public partial class LegendIcon : Control
{
    public string Kind { get; set; } = "";
    public string Item { get; set; } = "";
    public override void _Draw()
    {
        var area = new Rect2(5, 4, 24, 24);
        if (Kind is "player" or "npc")
        {
            long id = Kind == "player" ? 1 : long.TryParse(Item, out long parsed) ? parsed : 2;
            DrawCircle(area.GetCenter(), 8, MapSymbols.ActorColor(id, Kind == "player"));
            ThemeDB.FallbackFont.DrawString(GetCanvasItem(), area.GetCenter()+new Vector2(-3,4), id.ToString(), HorizontalAlignment.Left,-1,12,Colors.Black);
        }
        else if (Kind == "cargo") DrawRect(new Rect2(area.GetCenter() - new Vector2(7, 5.5f), new Vector2(14, 11)), MapSymbols.CargoColor);
        else MapSymbols.Draw(this, Kind, area, Item);
    }
}

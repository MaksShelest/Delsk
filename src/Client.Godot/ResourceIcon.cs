using Godot;

/// <summary>Small code-native stock icons, without font-dependent emoji.</summary>
public partial class ResourceIcon : Control
{
    public string Item { get; set; } = "";
    private static readonly System.Collections.Generic.Dictionary<string,Texture2D> Icons = new();
    public override void _Draw() => Paint(this, Item, new Rect2(Vector2.Zero, Size));

    public static void Paint(CanvasItem canvas, string item, Rect2 area)
    {
        string icon = item == "plank" ? "boards" : item == "wood" ? "logs" : item;
        string path = $"res://tablets_v1/assets/icons/{icon}.svg";
        if (!Icons.TryGetValue(icon, out var texture) && ResourceLoader.Exists(path))
            Icons[icon] = texture = GD.Load<Texture2D>(path);
        if (texture is not null)
        {
            float side = Mathf.Min(area.Size.X, area.Size.Y);
            canvas.DrawTextureRect(texture, new Rect2(area.Position + (area.Size - new Vector2(side, side)) / 2, new Vector2(side, side)), false);
            return;
        }
        var center = area.GetCenter();
        float scale = Mathf.Max(0.45f, Mathf.Min(area.Size.X, area.Size.Y) / 28f);
        Vector2 At(float x, float y) => center + new Vector2(x, y) * scale;
        void Box(float x, float y, float w, float h, Color color) => canvas.DrawRect(new Rect2(At(x, y), new Vector2(w, h) * scale), color);
        void Dot(float x, float y, float radius, Color color) => canvas.DrawCircle(At(x, y), radius * scale, color);
        void Stroke(float x1, float y1, float x2, float y2, Color color, float width) => canvas.DrawLine(At(x1, y1), At(x2, y2), color, width * scale);
        switch (item)
        {
            case "wood":
                Box(-9, -4, 17, 8, new Color("a77b48"));
                Dot(8, 0, 4, new Color("e8c38d"));
                break;
            case "grain":
                Stroke(0, 9, 0, -9, new Color("e4bd58"), 2);
                for (int i = -1; i <= 1; i++)
                { Dot(-3, i * 5, 3, new Color("edcd68")); Dot(3, i * 5 - 2, 3, new Color("edcd68")); }
                break;
            case "plank":
                for (int i = -1; i <= 1; i++) Box(-10, i * 5 - 2, 20, 4, new Color("d6ae76"));
                break;
            case "bread":
                canvas.DrawStyleBox(new StyleBoxFlat { BgColor = new Color("d5a15c"), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 }, new Rect2(At(-10, -6), new Vector2(20, 12) * scale));
                Stroke(-4, -3, -1, 3, new Color("f5d49d"), 2);
                Stroke(2, -3, 5, 3, new Color("f5d49d"), 2);
                break;
            case "milk":
                Box(-6, -7, 12, 15, new Color("e1efef")); Box(-4, -10, 8, 3, new Color("6aa5c0")); break;
            case "beet":
                Dot(0, 3, 7, new Color("ab4c78")); Stroke(0, 0, -4, -9, new Color("62a347"), 3); Stroke(0, 0, 4, -8, new Color("62a347"), 3); break;
            case "cheese":
                canvas.DrawColoredPolygon([At(-10, 7), At(10, 7), At(7, -8)], new Color("edc64e")); Dot(3, 3, 2, new Color("b88a32")); break;
            case "sugar":
                Box(-8, -9, 16, 18, new Color("f0eedf")); Stroke(-8, 2, 8, 2, new Color("6da0c6"), 4); break;
            case "pastry":
                Box(-9, -1, 18, 9, new Color("cf9b52")); Box(-9, -5, 18, 5, new Color("f4e5d0")); Dot(0, -6, 3, new Color("b74c72")); break;
            case "stone":
                canvas.DrawColoredPolygon([At(-8, 4), At(-5, -7), At(2, -8), At(9, -2), At(7, 7), At(-2, 8)], new Color("8d8f86"));
                Stroke(-2, -2, 3, 1, new Color("d5d6cf"), 2);
                break;
            case "sand":
                Dot(-5, -3, 3, new Color("e4c98a")); Dot(3, -4, 3, new Color("f0d7a2")); Dot(0, 3, 4, new Color("d7b56d")); Dot(6, 2, 2, new Color("c9a45a")); break;
            case "iron_ore":
                canvas.DrawColoredPolygon([At(-8, 5), At(-6, -6), At(1, -8), At(8, -1), At(5, 7)], new Color("6d5348"));
                canvas.DrawColoredPolygon([At(-2, -1), At(2, -5), At(5, 0), At(1, 3)], new Color("b9c4c8"));
                break;
            case "copper_ore":
                canvas.DrawColoredPolygon([At(-8, 5), At(-4, -7), At(4, -6), At(8, 2), At(2, 8), At(-5, 6)], new Color("8d5a3c"));
                Dot(1, -1, 3, new Color("d4894f"));
                break;
            default: Dot(0, 0, 5, Colors.White); break;
        }
    }
}

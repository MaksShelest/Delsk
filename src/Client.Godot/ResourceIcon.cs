using Godot;

/// <summary>Small code-native stock icons, without font-dependent emoji.</summary>
public partial class ResourceIcon : Control
{
    public string Item { get; set; } = "";
    public override void _Draw()
    {
        var center = Size / 2;
        switch (Item)
        {
            case "wood":
                DrawRect(new Rect2(center - new Vector2(9, 4), new Vector2(17, 8)), new Color("a77b48"));
                DrawCircle(center + new Vector2(8, 0), 4, new Color("e8c38d"));
                break;
            case "grain":
                DrawLine(center + new Vector2(0, 9), center - new Vector2(0, 9), new Color("e4bd58"), 2);
                for (int i = -1; i <= 1; i++)
                { DrawCircle(center + new Vector2(-3, i * 5), 3, new Color("edcd68")); DrawCircle(center + new Vector2(3, i * 5 - 2), 3, new Color("edcd68")); }
                break;
            case "plank":
                for (int i = -1; i <= 1; i++) DrawRect(new Rect2(center + new Vector2(-10, i * 5 - 2), new Vector2(20, 4)), new Color("d6ae76"));
                break;
            case "bread":
                DrawStyleBox(new StyleBoxFlat { BgColor = new Color("d5a15c"), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 }, new Rect2(center - new Vector2(10, 6), new Vector2(20, 12)));
                DrawLine(center + new Vector2(-4,-3), center + new Vector2(-1,3), new Color("f5d49d"), 2);
                DrawLine(center + new Vector2(2,-3), center + new Vector2(5,3), new Color("f5d49d"), 2);
                break;
            default: DrawCircle(center, 5, Colors.White); break;
        }
    }
}

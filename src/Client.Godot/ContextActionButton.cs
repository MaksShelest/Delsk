using Godot;

/// <summary>Frame-rate presentation of committed harvest progress. Never changes the world.</summary>
public partial class ContextActionButton : Button
{
    private readonly Label _caption;
    private double _progress;
    private readonly StyleBoxFlat _fill = new() { BgColor = new Color(0.34f, 0.66f, 0.48f, 0.64f),
        CornerRadiusTopLeft = 5, CornerRadiusBottomLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomRight = 5 };
    public ContextActionButton(string caption)
    {
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(240, 54);
        ClipContents = true;
        _caption = new Label { Text = caption, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _caption.OffsetLeft = 12; _caption.OffsetRight = -12;
        AddChild(_caption);
    }
    public void ShowProgress(double value)
    {
        _progress = System.Math.Clamp(value, 0, 100);
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_progress <= 0) return;
        DrawStyleBox(_fill, new Rect2(Vector2.One, new Vector2((Size.X - 2) * (float)(_progress / 100), Size.Y - 2)));
    }
}

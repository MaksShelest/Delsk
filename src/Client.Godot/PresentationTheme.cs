using Godot;

public static class PresentationTheme
{
    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", new Color("e8eee8"));
        theme.SetColor("font_color", "Button", new Color("f2f2e8"));
        theme.SetColor("font_disabled_color", "Button", new Color("a9b4ad"));
        foreach (var entry in new[] { ("normal", "334b50"), ("hover", "46656a"), ("pressed", "527778"), ("disabled", "303c41"), ("focus", "5c7779") })
        {
            var style = new StyleBoxFlat { BgColor = new Color(entry.Item2), BorderColor = new Color("647c7b"),
                BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
                CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
                ContentMarginLeft = 9, ContentMarginRight = 9, ContentMarginTop = 5, ContentMarginBottom = 5 };
            theme.SetStylebox(entry.Item1, "Button", style);
        }
        theme.SetConstant("separation", "VBoxContainer", 6);
        theme.SetConstant("separation", "HBoxContainer", 6);
        theme.SetConstant("h_separation", "GridContainer", 10);
        theme.SetConstant("v_separation", "GridContainer", 4);
        return theme;
    }
}

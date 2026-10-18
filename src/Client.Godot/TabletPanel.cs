using Godot;
using Simulation.Hosting;

/// <summary>Inset overlay. It owns UI widgets, never the simulation clock.</summary>
public partial class TabletPanel : PanelContainer
{
    public event System.Action? Closed;
    public event System.Action? HelpRequested;
    public event System.Action<bool>? TimeRunningChanged;
    public VBoxContainer Body { get; }
    private readonly CheckButton _time;
    private readonly Label _status;
    private readonly string _language;

    public TabletPanel(string title, string language)
    {
        _language = language;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OffsetLeft = 12; OffsetRight = -12; OffsetTop = 66; OffsetBottom = -12;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("263b40"),
            BorderColor = new Color("6a8a88"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 });
        var column = new VBoxContainer(); AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        var heading = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeFontSizeOverride("font_size", 21); header.AddChild(heading);
        _time = new CheckButton { FocusMode = FocusModeEnum.None, TooltipText = L("Пауза останавливает весь мир, включая NPC. Разрешённое движение запускает время.", "Pause stops the whole world, including NPCs. Allowed movement resumes time.") };
        _time.Toggled += running => TimeRunningChanged?.Invoke(running); header.AddChild(_time);
        var help = new Button { Text = "?", TooltipText = L("Справка по этому экрану", "Help for this screen"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 32) };
        help.Pressed += () => HelpRequested?.Invoke(); header.AddChild(help);
        var close = new Button { Text = "×", TooltipText = L("Закрыть", "Close"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 32) };
        close.Pressed += () => Closed?.Invoke(); header.AddChild(close);
        column.AddChild(new HSeparator());
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        Body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(Body);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 3 }; column.AddChild(_status);
    }
    public void ShowClock(bool timeRunning, bool paused, long tick, string feedback)
    {
        _time.SetPressedNoSignal(timeRunning);
        _time.Text = L(timeRunning ? "Время идёт" : "Время остановлено", timeRunning ? "Time running" : "Time paused");
        _status.Text = feedback;
        _status.Visible = feedback.Length > 0;
    }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Выбор стартовой специализации. Карточка только отмечает направление, партию начинает отдельная кнопка.</summary>
public partial class SpecializationScreen : ColorRect
{
    public event System.Action<string>? Confirmed;
    public event System.Action? Cancelled;
    private readonly string _language;
    private readonly Dictionary<string, Button> _cards = new(StringComparer.Ordinal);
    private Button? _confirm;
    private GridContainer? _grid;
    private string? _selected;

    public SpecializationScreen(string language)
    {
        _language = language;
        Name = "SpecializationScreen";
        Color = new Color(0.08f, .13f, .11f, .72f);
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Ready()
    {
        var panel = new PanelContainer { Theme = CompactTabletDesign.Theme, Name = "SpecializationPanel" };
        panel.AddThemeStyleboxOverride("panel", CompactTabletDesign.Surface("e8eee5", 12));
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(panel);
        UiSafeArea.Panel(panel, 12, 12, 12, 12);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        body.AddChild(TabletDesign.Heading(L("Стартовая специализация", "Starting specialization"), 26));
        var intro = new Label
        {
            Text = L(
                "Выберите направление будущей поддержки. Участок, строительство и материалы оплачиваются по обычным ценам. Бесплатного предприятия нет.",
                "Choose a direction for future support. Land, construction and materials keep their ordinary prices. There is no free enterprise."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        intro.AddThemeColorOverride("font_color", new Color("425448"));
        body.AddChild(intro);
        var scroll = new ScrollContainer { Name = "SpecializationScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        body.AddChild(scroll);
        _grid = new GridContainer { Name = "SpecializationCards", SizeFlagsHorizontal = SizeFlags.ExpandFill, Columns = 2 };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(_grid);
        foreach (var direction in StartingSpecializations.Directions)
            _grid.AddChild(Card(direction.IndustryId));
        var note = new Label
        {
            Text = L(
                "Выбор один на партию и не запрещает другие отрасли. Нажатие на карточку партию не начинает.",
                "The choice is once per game and does not block other industries. Pressing a card does not start the game."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        note.AddThemeFontSizeOverride("font_size", 14);
        note.AddThemeColorOverride("font_color", new Color("425448"));
        body.AddChild(note);
        var actions = new HBoxContainer();
        body.AddChild(actions);
        var back = new Button { Name = "BackToMapSelection", Text = L("К выбору карты", "Back to maps"), CustomMinimumSize = new(160, 44) };
        back.Pressed += () => Cancelled?.Invoke();
        actions.AddChild(back);
        _confirm = new Button { Name = "ConfirmSpecialization", Text = L("Подтвердить направление", "Confirm direction"), Disabled = true, CustomMinimumSize = new(220, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _confirm.TooltipText = L("Сначала выберите карточку", "Select a card first");
        GameUi.Primary(_confirm);
        _confirm.Pressed += () => { if (_selected is { } industry) Confirmed?.Invoke(industry); };
        actions.AddChild(_confirm);
        Resized += Reflow;
        Reflow();
        back.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Cancelled?.Invoke(); }
    }

    private void Reflow()
    {
        if (_grid is null || !IsInsideTree()) return;
        var size = GetViewportRect().Size;
        _grid.Columns = size.X < 900 || size.Y < 520 ? 1 : 2;
        foreach (var button in _cards.Values) FitCard(button);
    }

    private static void FitCard(Button button)
    {
        float inner = button.Size.X - 24;
        if (inner < 80) return;
        float text = button.GetThemeFont("font").GetMultilineStringSize(button.Text, HorizontalAlignment.Left, inner, button.GetThemeFontSize("font_size")).Y;
        float height = text + 28;
        if (Mathf.Abs(button.CustomMinimumSize.Y - height) > 1) button.CustomMinimumSize = new Vector2(0, height);
    }

    private Control Card(string industry)
    {
        var copy = Copy(industry);
        var button = new Button
        {
            Name = "Specialization_" + industry,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = L(copy.TitleRu, copy.TitleEn) + "\n" + L(copy.EnterpriseRu, copy.EnterpriseEn) + "\n" + L(copy.ChainRu, copy.ChainEn) + "\n" + L(copy.TextRu, copy.TextEn)
        };
        button.AddThemeStyleboxOverride("normal", CompactTabletDesign.Surface("f7f8f1", 12));
        button.AddThemeStyleboxOverride("hover", CompactTabletDesign.Surface("f7f8f1", 12));
        button.AddThemeStyleboxOverride("pressed", CompactTabletDesign.Surface("f7f8f1", 12));
        button.AddThemeStyleboxOverride("focus", CompactTabletDesign.Surface("f7f8f1", 12));
        button.AddThemeColorOverride("font_color", new Color("243c30"));
        button.AddThemeColorOverride("font_hover_color", new Color("243c30"));
        button.AddThemeColorOverride("font_pressed_color", new Color("243c30"));
        button.AddThemeColorOverride("font_focus_color", new Color("243c30"));
        button.Resized += () => FitCard(button);
        button.Pressed += () => Select(industry);
        _cards[industry] = button;
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var accent = new ColorRect { Name = "Accent_" + industry, Color = new Color(copy.Accent), CustomMinimumSize = new(8, 0), MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(accent);
        row.AddChild(button);
        return row;
    }

    private void Select(string industry)
    {
        _selected = industry;
        if (_confirm is not null) { _confirm.Disabled = false; _confirm.TooltipText = ""; }
        foreach (var (id, button) in _cards)
        {
            string fill = id == industry ? "e7f0e4" : "f7f8f1";
            var box = CompactTabletDesign.Surface(fill, 12);
            if (id == industry)
            {
                box.BorderColor = new Color(Copy(id).Accent);
                box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = 3;
            }
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(state, box);
        }
        if (_grid?.GetParent() is ScrollContainer scroll && _cards[industry].GetParent() is Control row)
            scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, row);
    }

    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);

    private readonly record struct CardCopy(string TitleRu, string TitleEn, string EnterpriseRu, string EnterpriseEn, string ChainRu, string ChainEn, string TextRu, string TextEn, string Accent);

    private static CardCopy Copy(string industry) => industry switch
    {
        "woodworking" => new("Деревообработка", "Woodworking", "Лесопилка", "Sawmill", "древесина → доски", "wood → planks",
            "Первое предприятие пилит древесину в доски. Дальше можно развивать деревообработку.",
            "The first enterprise saws wood into planks. Woodworking can grow from there.", "6b8f71"),
        "food" => new("Пищевая промышленность", "Food industry", "Пищевое предприятие", "Food factory", "зерно → мука", "grain → flour",
            "Первая доступная цепочка ведёт к муке. На этом же предприятии сразу есть выпечка хлеба, дальше — более сложные пищевые товары.",
            "The first available chain leads to flour. The same enterprise can also bake bread, and later foods follow.", "c4a15a"),
        "dairy" => new("Молочная промышленность", "Dairy", "Сыроварня", "Cheese dairy", "молоко → сыр", "milk → cheese",
            "Сыроварня делает сыр из молока. Дальше открываются другие молочные товары.",
            "The dairy turns milk into cheese. Other dairy goods follow.", "6f8f9c"),
        "materials" => new("Строительные материалы", "Building materials", "Дробильный цех", "Crusher", "камень → щебень", "stone → crushed stone",
            "Дробильный цех делает щебень из камня. Дальше открываются другие строительные материалы.",
            "The crusher turns stone into crushed stone. Other building materials follow.", "8d7b66"),
        _ => throw new System.InvalidOperationException(industry)
    };
}

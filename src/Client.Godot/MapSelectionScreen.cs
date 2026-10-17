using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Static scenario previews; no session or NPC decisions before Start.</summary>
public partial class MapSelectionScreen : ColorRect
{
    public event System.Action<string>? MapChosen;
    public event System.Action? Continued;
    public event System.Action? LanguageChanged;
    public event System.Action? QuitRequested;

    public MapSelectionScreen(string language, bool canContinue, System.Func<string, ScenarioDefinition> read)
    {
        Color = new Color("26383d");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(900, 0) };
        center.AddChild(column);
        column.AddChild(new Label { Text = PlayerText.Local(language, "Выберите карту", "Choose a map"), HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label { Text = PlayerText.Local(language, "Мир начнёт работать после выбора карты.", "The world starts after you choose a map."), HorizontalAlignment = HorizontalAlignment.Center });
        var row = new HBoxContainer();
        column.AddChild(row);
        foreach (string key in new[] { PlayMaps.First, PlayMaps.Second })
        {
            string selected = key;
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(panel);
            var card = new VBoxContainer();
            panel.AddChild(card);
            var scenario = read(key);
            card.AddChild(new Label { Text = PlayMaps.Name(key, language), HorizontalAlignment = HorizontalAlignment.Center });
            card.AddChild(new MapPreview(scenario) { CustomMinimumSize = new Vector2(420, 240) });
            card.AddChild(new Label { Text = PlayerText.Local(language,
                key == PlayMaps.Second ? "16 × 11 · лес и зерно по 6 клеток\n12 участков · защищённая дорога" : "16 × 7 · исходная карта\n6 участков · прежние правила",
                key == PlayMaps.Second ? "16 × 11 · 6 wood and 6 grain cells\n12 plots · protected road" : "16 × 7 · original map\n6 plots · existing rules"), HorizontalAlignment = HorizontalAlignment.Center });
            AddButton(card, PlayerText.Local(language, "Начать", "Start"), () => MapChosen?.Invoke(selected));
        }
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddChild(actions);
        if (canContinue) AddButton(actions, PlayerText.Local(language, "Продолжить", "Continue"), () => Continued?.Invoke());
        AddButton(actions, PlayerText.Local(language, "English", "Русский"), () => LanguageChanged?.Invoke());
        AddButton(actions, PlayerText.Local(language, "Выйти из игры", "Quit"), () => QuitRequested?.Invoke());
    }

    private static void AddButton(Container parent, string text, System.Action action)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += action;
        parent.AddChild(button);
    }
}

public partial class MapPreview(ScenarioDefinition scenario) : Control
{
    public override void _Draw()
    {
        float cell = Mathf.Min(Size.X / scenario.MapWidth, Size.Y / scenario.MapHeight);
        var offset = (Size - new Vector2(scenario.MapWidth, scenario.MapHeight) * cell) / 2;
        Rect2 Area(int x, int z) => new(offset + new Vector2(x, z) * cell + Vector2.One, Vector2.One * (cell - 2));
        for (int x = 0; x < scenario.MapWidth; x++) for (int z = 0; z < scenario.MapHeight; z++)
            DrawRect(Area(x,z), new Color("729365"));
        foreach (var plot in scenario.Plots ?? [])
            MapSymbols.Draw(this, plot.IsCorridor ? "corridor" : plot.ShopParticipantId.HasValue ? "shop-plot" : "plot-free", Area(plot.Position.X,plot.Position.Z));
        foreach (var node in scenario.ResourceNodes)
            MapSymbols.Draw(this, "resource", Area(node.Position.X,node.Position.Z), node.ItemId);
        if (scenario.CapitalDemand is { } demand)
            MapSymbols.Draw(this, "capital", Area(demand.Terminal.X,demand.Terminal.Z));
    }
}

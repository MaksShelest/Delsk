using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Single selected map; catalog selection does not create or replace a session.</summary>
public partial class MapSelectionScreen : ColorRect
{
    public event System.Action<string>? MapChosen;
    public event System.Action? Continued;
    public event System.Action? LanguageChanged;
    public event System.Action? QuitRequested;
    public event System.Action? DisplaySettingsRequested;
    public string SelectedMap { get; private set; }
    private readonly VBoxContainer _preview = new();
    private readonly string _language;
    private readonly System.Func<string, ScenarioDefinition> _read;
    public MapSelectionScreen(string language, bool canContinue, System.Func<string, ScenarioDefinition> read, string selected = PlayMaps.First)
    {
        SelectedMap = selected; _read = read; _language = language;
        Color = new Color("26383d"); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer(); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) }; center.AddChild(column);
        column.AddChild(new Label { Text = "Delsk", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_preview); ShowPreview();
        AddButton(column, L("Сменить карту", "Change map"), ShowCatalog);
        AddButton(column, L("Начать", "Start"), () => MapChosen?.Invoke(SelectedMap));
        if (canContinue) AddButton(column, L("Продолжить", "Continue"), () => Continued?.Invoke());
        AddButton(column, L("Настройки экрана", "Display settings"), () => DisplaySettingsRequested?.Invoke());
        AddButton(column, L("English", "Русский"), () => LanguageChanged?.Invoke());
        AddButton(column, L("Выйти из игры", "Quit"), () => QuitRequested?.Invoke());
    }
    private void ShowPreview()
    {
        foreach (Node child in _preview.GetChildren()) { _preview.RemoveChild(child); child.QueueFree(); }
        var scenario = _read(SelectedMap);
        _preview.AddChild(new Label { Text = PlayMaps.Name(SelectedMap, _language), HorizontalAlignment = HorizontalAlignment.Center });
        _preview.AddChild(new MapPreview(scenario) { CustomMinimumSize = new Vector2(600, 240) });
        _preview.AddChild(new Label { Text = $"{scenario.MapWidth} × {scenario.MapHeight}" + L(" · Мир запустится только по «Начать».", " · The world starts only after Start."), HorizontalAlignment = HorizontalAlignment.Center });
    }
    private void ShowCatalog()
    {
        var dialog = new AcceptDialog { Title = L("Каталог карт", "Map catalog"), Size = new Vector2I(720, 560) };
        dialog.GetOkButton().Text = L("Отмена", "Cancel");
        var scroll = new ScrollContainer(); dialog.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(list);
        foreach (string key in new[] { PlayMaps.First, PlayMaps.Second, PlayMaps.Third })
        {
            string choice = key;
            list.AddChild(new Label { Text = PlayMaps.Name(key, _language) });
            list.AddChild(new MapPreview(_read(key)) { CustomMinimumSize = new Vector2(640, 210) });
            AddButton(list, L("Выбрать", "Select"), () => { SelectedMap = choice; ShowPreview(); dialog.Hide(); dialog.QueueFree(); });
        }
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered();
    }
    private static void AddButton(Container parent, string text, System.Action action)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None }; button.Pressed += action; parent.AddChild(button); }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
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
            MapSymbols.Draw(this, plot.IsCorridor ? "corridor" : plot.IsShopSite || plot.ShopParticipantId.HasValue ? "shop-plot" : "plot-free", Area(plot.Position.X,plot.Position.Z));
        foreach (var node in scenario.ResourceNodes)
            MapSymbols.Draw(this, "resource", Area(node.Position.X,node.Position.Z), node.ItemId);
        if (scenario.CapitalDemand is { } demand)
            MapSymbols.Draw(this, "capital", Area(demand.Terminal.X,demand.Terminal.Z));
    }
}

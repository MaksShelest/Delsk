using System.Globalization;
using Godot;
using Simulation.Hosting;

public partial class SavedWorldsScreen : ColorRect
{
    public SavedWorldsScreen(string language, bool friendly, IReadOnlyList<SavedWorld> worlds, string notice,
        Action<string> continueWorld, Action<SavedWorld> rename, Action<SavedWorld> delete, Action newGame, Action back)
    {
        Name = "SavedWorlds";
        Theme = CompactTabletDesign.ForPlayer(friendly);
        Color = new Color("e8eee5");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var scroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(margin);
        UiSafeArea.CenterColumn(scroll, margin, 620);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        column.AddChild(TabletDesign.Heading(L("Мои миры", "My worlds"), 29));
        var navigation = new HFlowContainer();
        column.AddChild(navigation);
        var backButton = AddButton(navigation, "WorldsBack", L("Назад", "Back"), back);
        var create = AddButton(navigation, "WorldsNewGame", L("Новая игра", "New game"), newGame);
        GameUi.Primary(create);
        if (notice.Length > 0) column.AddChild(GameUi.Text(notice, 16));
        if (worlds.Count == 0) column.AddChild(GameUi.Text(L("Сохранённых миров пока нет.", "No saved worlds yet."), 17));

        foreach (var world in worlds)
        {
            var panel = new PanelContainer { Name = "World_" + world.Id };
            column.AddChild(panel);
            var content = new VBoxContainer();
            panel.AddChild(content);
            string name = DisplayName(world, language);
            content.AddChild(GameUi.Text(name, 22));
            if (MapName(world, language) is { } map && map != name)
                content.AddChild(GameUi.Text(L($"Карта · {map}", $"Map · {map}"), 16));
            if (world.SavedAt is { } timestamp)
            {
                var culture = CultureInfo.GetCultureInfo(language == "ru" ? "ru-RU" : "en-US");
                string date = timestamp.ToLocalTime().ToString("g", culture);
                content.AddChild(GameUi.Text(L($"Сохранено · {date}", $"Saved · {date}"), 16));
            }
            if (world.Summary is { } stats)
            {
                content.AddChild(GameUi.Text(L($"Тик {stats.Tick} · Участники {stats.Participants}",
                    $"Tick {stats.Tick} · Participants {stats.Participants}"), 16));
                string cash = PlayerText.Money(stats.CashMinor, language);
                content.AddChild(GameUi.Text(L($"Баланс {cash} · Предприятия {stats.Enterprises}",
                    $"Balance {cash} · Enterprises {stats.Enterprises}"), 16));
            }
            var actions = new HFlowContainer();
            content.AddChild(actions);
            var resume = AddButton(actions, "Continue_" + world.Id, L("Продолжить", "Continue"), () => continueWorld(world.Id));
            GameUi.Primary(resume);
            AddButton(actions, "Rename_" + world.Id, L("Переименовать", "Rename"), () => rename(world));
            AddButton(actions, "Delete_" + world.Id, L("Удалить", "Delete"), () => delete(world));
        }
        Ready += () => backButton.GrabFocus();
    }

    public static string DisplayName(SavedWorld world, string language) => world.Name
        ?? (SavedWorldStore.IsImported(world.Id) ? PlayerText.Local(language, "Прежняя партия", "Previous game") : MapName(world, language))
        ?? PlayerText.Local(language, "Мир без названия", "Unnamed world");

    private static string? MapName(SavedWorld world, string language)
    {
        if (world.CustomMapName is { Length: > 0 } custom) return custom;
        if (world.Summary?.MapId is not { Length: > 0 } mapId) return null;
        string key = PlayMaps.KeyForScenario(mapId);
        return key != PlayMaps.First || mapId == PlayMaps.FirstScenarioId
            ? PlayMaps.Name(key, language)
            : world.Mode == "economy" ? mapId : PlayerText.Get(language, world.Mode);
    }

    private static Button AddButton(Container parent, string name, string label, Action action)
    {
        var button = new Button { Name = name, Text = label, CustomMinimumSize = new(0, 44) };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }
}

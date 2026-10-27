using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private HelpOverlay? _helpOverlay;
    private DebugBoard? _miniMap;
    private Label? _gameClock;
    private Button? _accountNotice;
    private readonly System.Collections.Generic.List<ContextActionButton> _harvestButtons = [];
    private string _harvestCycle = "";
    private double _harvestVisual;
    private string _noticeValue = "";
    private double _noticeSeconds;

    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
    private void OpenHelp(string topic = "basics")
    {
        DismissHelp();
        StopHeading(); _waitRelease = true;
        _helpOverlay = new HelpOverlay(_language, topic);
        _helpOverlay.Closed += CloseHelp;
        AddChild(_helpOverlay);
        ApplyPause(); Refresh();
    }
    private void DismissHelp()
    {
        if (_helpOverlay is null) return;
        RemoveChild(_helpOverlay); _helpOverlay.QueueFree(); _helpOverlay = null;
    }
    private void CloseHelp()
    {
        DismissHelp(); _waitRelease = true;
        ApplyPause(); ApplySurface(); Refresh();
    }
    private string ContextTopic() => _choices.ElementAtOrDefault(Math.Clamp(_contextIndex,0,Math.Max(0,_choices.Length-1)))?.Key.Split(':')[0] switch
    {
        "harvest" => "harvest", "buy" or "reserve" or "unreserve" or "build" => "land",
        "exchange" => "exchange", "market" => "market", "card" or "transfer" or "take" => "production", _ => "basics"
    };
    private void ConfigureDisplay()
    {
        if (DisplayServer.GetName() == "headless" || Engine.IsEmbeddedInEditor()) return;
        // Godot consumes its own display flags; explicit app overrides go after --.
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--windowed")) { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); return; }
        if (args.Contains("--fullscreen")) { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); return; }
        var config = new ConfigFile(); config.Load("user://display.cfg");
        DisplayServer.WindowSetMode(config.GetValue("display", "fullscreen", true).AsBool()
            ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }
    private void ToggleFullscreen()
    {
        if (DisplayServer.GetName() == "headless" || Engine.IsEmbeddedInEditor()) return;
        bool fullscreen = DisplayServer.WindowGetMode() is not (DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        DisplayServer.WindowSetMode(fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        var config = new ConfigFile(); config.SetValue("display", "fullscreen", fullscreen);
        if (config.Save("user://display.cfg") != Error.Ok) _feedback = L("Не удалось сохранить режим экрана.", "Could not save display mode.");
    }
    private void BuildPlayHud()
    {
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); bar.OffsetBottom = 54;
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("243a3e"),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 });
        _playChrome!.AddChild(bar);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin }; bar.AddChild(row);
        var menu = new Button { Text = L("Меню", "Menu"), FocusMode = FocusModeEnum.None };
        menu.Pressed += OpenMenu; row.AddChild(menu);
        var tablet = new Button { Text = L("Планшет", "Tablet"), FocusMode = FocusModeEnum.None };
        tablet.Pressed += OpenPlayerTablet; row.AddChild(tablet);
        _accountNotice=new(){Name="AccountNotification",Text=L("Счёт","Account"),FocusMode=FocusModeEnum.None};
        _accountNotice.Pressed+=()=>{OpenPlayerTablet();_playerTablet?.OpenAccount();};row.AddChild(_accountNotice);
        var help = new Button { Text = "?", TooltipText = L("Справка", "Help"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 36) };
        help.Pressed += () => OpenHelp(); row.AddChild(help);
        _gameClock = new Label { VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(185, 0) }; row.AddChild(_gameClock);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _playInventory = new Label { VerticalAlignment = VerticalAlignment.Center }; row.AddChild(_playInventory);
        _hudGoods = new ResourceInventoryHud { Name = "InventoryHud", Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(_hudGoods);
        _playNotice = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _playNotice.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _playNotice.OffsetTop = 64; _playNotice.OffsetLeft = 170; _playNotice.OffsetRight = -170; _playNotice.OffsetBottom = 96;
        _playChrome.AddChild(_playNotice);
        _tooltip = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        _tooltip.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _tooltip.Position = new Vector2(16, 108); _tooltip.Size = new Vector2(330, 0);
        _tooltip.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.12f,0.20f,0.22f,0.94f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        _playChrome.AddChild(_tooltip);
        _storageWarning=new Label{Name="StorageWarning",AutowrapMode=TextServer.AutowrapMode.WordSmart,MouseFilter=MouseFilterEnum.Ignore};_storageWarning.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);_storageWarning.OffsetLeft=16;_storageWarning.OffsetRight=-260;_storageWarning.OffsetTop=96;_storageWarning.OffsetBottom=158;_storageWarning.AddThemeColorOverride("font_color",new Color("ffcc75"));_playChrome.AddChild(_storageWarning);
        _miniMap=null;
        if(_scenario.Layout is not null)
        {
            var mapPanel=new PanelContainer{Name="PlayMiniMap"};mapPanel.SetAnchorsPreset(LayoutPreset.TopRight);
            mapPanel.OffsetLeft=-248;mapPanel.OffsetRight=-16;mapPanel.OffsetTop=110;mapPanel.OffsetBottom=284;
            _miniMap=new DebugBoard{CustomMinimumSize=new(232,174),MouseFilter=MouseFilterEnum.Ignore};mapPanel.MouseFilter=MouseFilterEnum.Ignore;mapPanel.AddChild(_miniMap);_playChrome.AddChild(mapPanel);
        }
        var dock = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        dock.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        dock.OffsetTop = -132; dock.OffsetBottom = -16; dock.OffsetLeft = 16; dock.OffsetRight = -16;
        _playChrome.AddChild(dock); _bottomChrome = dock;
        _contextHost = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; dock.AddChild(_contextHost);
        _contextKey = ""; _choices = [];
    }
    private void FillPlayContext(VBoxContainer host)
    {
        foreach (Node child in host.GetChildren()) { host.RemoveChild(child); child.QueueFree(); }
        _harvestButtons.Clear(); host.Visible = _choices.Length > 0;
        if (_choices.Length == 0) return;
        var row = new HBoxContainer(); host.AddChild(row);
        int count = _choices.Length > 3 ? 2 : _choices.Length;
        for (int i = 0; i < count; i++)
        {
            int index = i == 1 && _choices.Length > 3 && _contextIndex >= 2 ? _contextIndex : i;
            var choice = _choices[index];
            string caption = choice.Command?.Kind == ActionKind.StopHarvest ? L("Остановить добычу", "Stop harvesting") : choice.Label;
            var button = new ContextActionButton(caption) { Disabled = !choice.Enabled,
                TooltipText = choice.Enabled ? choice.Label : choice.Details };
            if (index == _contextIndex) button.AddThemeStyleboxOverride("normal", new StyleBoxFlat {
                BgColor = new Color("3b595b"), BorderColor = new Color("9fc6ad"),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
            button.Pressed += () => { _contextIndex = index; ConfirmContext(); };
            row.AddChild(button);
            if (choice.Command?.Kind == ActionKind.StopHarvest) _harvestButtons.Add(button);
        }
        if (_choices.Length > 3)
        {
            var more = new MenuButton { Text = L("Ещё действия", "More actions"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 54) };
            var popup = more.GetPopup();
            for (int i = 2; i < _choices.Length; i++) { popup.AddItem(_choices[i].Label, i); popup.SetItemDisabled(i - 2, !_choices[i].Enabled); }
            popup.IdPressed += id => { _contextIndex = (int)id; ConfirmContext(); };
            row.AddChild(more);
        }
        var help = new Button { Text = "?", TooltipText = L("Об этом действии", "About this action"), FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(44, 54) };
        help.Pressed += () => OpenHelp(ContextTopic()); row.AddChild(help);
    }
    private void AnimateInterface(double delta)
    {
        if (_session is null) return;
        var job = _session.Engine.HarvestOf(_human);
        string cycle = job is null ? "" : $"{job.StartTick}:{job.CompletionTick}:{job.ItemId}";
        double target = HarvestProgress.Percent(job, _session.Engine.Tick, _session.Engine.TravelQuantum);
        if (_harvestCycle != cycle) { _harvestCycle = cycle; _harvestVisual = 0; }
        if (!_session.Paused) _harvestVisual = Mathf.MoveToward(_harvestVisual, target, delta * 160);
        foreach (var button in _harvestButtons) button.ShowProgress(_harvestVisual);
        if (_noticeSeconds > 0) _noticeSeconds = Math.Max(0, _noticeSeconds - delta);
        if(_noticeSeconds==0)_electionNotice="";
        if (_playNotice is not null) _playNotice.Visible = _noticeSeconds > 0 && !_tabletState.IsOpen && _helpOverlay is null;
    }
    private void UpdatePlayNotice()
    {
        if (_playNotice is null) return;
        bool rejected = _trackedId is not null && _session.Engine.Result(_trackedId)?.Status == ResultStatus.Rejected;
        string value = _electionNotice.Length>0 ? _electionNotice : rejected ? _feedback : _gain;
        if (value.Contains(L("Незавершённый прогресс", "Unfinished progress"), StringComparison.Ordinal)) value = "";
        if (value != _noticeValue) { _noticeValue = value; _noticeSeconds = value.Length == 0 ? 0 : 3.5; }
        _playNotice.Text = value;
        _playNotice.Visible = _noticeSeconds > 0 && !_tabletState.IsOpen && _helpOverlay is null;
    }
    private static string GameTime(long tick) => $"{tick / 3600:00}:{tick / 60 % 60:00}:{tick % 60:00}";
}

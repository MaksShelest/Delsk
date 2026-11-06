using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private HelpOverlay? _helpOverlay;
    private Label? _gameClock;
    private Button? _accountNotice;
    private readonly System.Collections.Generic.List<ContextActionButton> _harvestButtons = [];
    private string _harvestCycle = "";
    private double _harvestVisual;
    private string _noticeValue = "";
    private double _noticeSeconds;
    private bool _contextDismissTouch;

    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
    private void OpenHelp(string topic = "basics")
    {
        DismissHelp();
        StopHeading();
        CloseContextActions(); _waitRelease = true;
        _helpOverlay = new HelpOverlay(_language, topic,_session.Engine.Configuration().Economy?.Movement,_session.Engine.UsesFreeVehicleControl);
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
        _stick=null;
        if(OS.HasFeature("mobile")||OS.GetCmdlineUserArgs().Contains("--touch-controls"))
        {_stick=new VirtualJoystick();_playChrome!.AddChild(_stick);}
        var bar = new PanelContainer { Theme=TabletDesign.Theme, MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); bar.OffsetBottom = 54;
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("f3f3ed"),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 });
        _playChrome!.AddChild(bar);
        UiSafeArea.Track(bar,()=>{var safe=UiSafeArea.Read(bar);bar.OffsetLeft=(float)safe.Left;bar.OffsetRight=-(float)safe.Right;bar.OffsetTop=(float)safe.Top;bar.OffsetBottom=54+(float)safe.Top;});
        bar.Resized+=()=>LayoutCityHud(bar);
        var row = new HBoxContainer { Name="HudRow" };row.AddThemeConstantOverride("separation",6); bar.AddChild(row);
        var menu = new Button { Name="HudMenu", Text = L("Меню", "Menu"), FocusMode = FocusModeEnum.None };
        menu.Pressed += OpenMenu; row.AddChild(menu);
        var tablet = new Button { Name="HudTablet", Text = L("Планшет", "Tablet"), FocusMode = FocusModeEnum.None };
        tablet.Pressed += OpenPlayerTablet; row.AddChild(tablet);
        var cityMap=new Button{Name="OpenCityMap",Text=L("Карта","Map"),TooltipText=L("Карта всего города · M","City map · M")};cityMap.Pressed+=OpenCityMap;row.AddChild(cityMap);
        _accountNotice=new(){Name="AccountNotification",Text=L("Счёт","Account"),FocusMode=FocusModeEnum.None};
        _accountNotice.Pressed+=()=>{OpenPlayerTablet();_playerTablet?.OpenAccount();};row.AddChild(_accountNotice);
        var help = new Button { Name="HudHelp", Text = "?", TooltipText = L("Справка", "Help"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 36) };
        help.Pressed += () => OpenHelp(); row.AddChild(help);
        _gameClock = new Label { VerticalAlignment = VerticalAlignment.Center, Name="HudClock" }; row.AddChild(_gameClock);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _playInventory = new Label { Name="HudStock", VerticalAlignment = VerticalAlignment.Center }; row.AddChild(_playInventory);
        _hudGoods = new ResourceInventoryHud { Name = "InventoryHud", Alignment = FlowContainer.AlignmentMode.End, SizeFlagsHorizontal=SizeFlags.ShrinkEnd };
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
        var dock = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore, GrowVertical=GrowDirection.End };
        dock.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        dock.OffsetTop = -132; dock.OffsetBottom = -16; dock.OffsetLeft = 16; dock.OffsetRight = -16;
        _playChrome.AddChild(dock); _bottomChrome = dock;
        UiSafeArea.Track(dock,LayoutContextDock);
        _contextHost = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; dock.AddChild(_contextHost);
        _contextActions=null;_contextCommandId=null;_contextMessage="";
        _contextShield=new ColorRect{Name="ContextShield",Color=new Color(0,0,0,.15f),MouseFilter=MouseFilterEnum.Stop,Visible=false,ZIndex=40};
        _contextShield.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_playChrome.AddChild(_contextShield);dock.ZIndex=41;
        // GUI hit testing follows sibling order, independently of CanvasItem.ZIndex.
        _playChrome.MoveChild(dock,_playChrome.GetChildCount()-1);
        _contextShield.GuiInput+=e=>
        {
            if(e is InputEventScreenTouch touch)
            {
                _contextShield.AcceptEvent();
                if(touch.Pressed)_contextDismissTouch=true;
                else if(_contextDismissTouch){_contextDismissTouch=false;Callable.From(CloseContextActions).CallDeferred();}
            }
            else if(e is InputEventMouseButton {ButtonIndex:MouseButton.Left} mouse)
            {
                _contextShield.AcceptEvent();
                if(mouse.Pressed&&!_contextDismissTouch)Callable.From(CloseContextActions).CallDeferred();
            }
        };
        foreach(var button in row.GetChildren().OfType<Button>())button.CustomMinimumSize=new(40,40);
        void FitHud()
        {
            // Optional goods yield space to navigation and the two useful metrics.
            if(_hudGoods is null)return;
            float fixedWidth=row.GetChildren().OfType<Control>().Where(c=>c!=_hudGoods).Sum(c=>c.GetCombinedMinimumSize().X)+row.GetThemeConstant("separation")*(row.GetChildCount()-1);
            _hudGoods.Visible=_hudGoods.GetChildCount()>0&&fixedWidth+_hudGoods.GetCombinedMinimumSize().X<=bar.Size.X-24;
        }
        row.Resized+=FitHud;row.MinimumSizeChanged+=()=>Callable.From(FitHud).CallDeferred();
        _contextKey = ""; _choices = [];LayoutCityHud(bar);
    }
    private void LayoutCityHud(Control bar)
    {
        float top=bar.Position.Y+bar.Size.Y;var safe=UiSafeArea.Read(bar);bool narrow=GetViewportRect().Size.X<800;
        _roadCardTop=top+12;LayoutRoadWork();
        if(_playNotice is not null){_playNotice.OffsetTop=top+8;_playNotice.OffsetBottom=top+42;_playNotice.OffsetLeft=16+(float)safe.Left;_playNotice.OffsetRight=-16-(float)safe.Right;}
        if(_storageWarning is not null){_storageWarning.OffsetTop=top+44;_storageWarning.OffsetBottom=top+100;_storageWarning.OffsetRight=-16-(float)safe.Right;}
        if(_tooltip is not null)_tooltip.Position=new(16+(float)safe.Left,top+96);
    }
    private void FillPlayContext(VBoxContainer host)
    {
        if(_contextActions is null)
        {
            _contextActions=new ContextActionsPanel();host.AddChild(_contextActions);
            _contextActions.Requested+=ConfirmContextChoice;
            _contextActions.ExpansionChanged+=expanded=>
            {if(expanded){StopHeading();_waitRelease=true;}else _contextDismissTouch=false;if(_contextShield is not null)_contextShield.Visible=expanded;ApplyPause();};
            _contextActions.Resized+=LayoutContextDock;
        }
        var response=_contextCommandId is {} id?_session.Engine.Result(id):null;
        string status=_contextCommandId is null?"":ContextActions.ResultText(_language,response);
        if(_contextMessage.Length>0)status=_contextMessage;
        var contextView=PlayerDesk.Read(_session.Engine,_human);
        _contextActions.ShowSummary(string.Join(" · ",contextView.Resources.Where(r=>r.Position==contextView.Participant.Position).Select(r=>PlayerText.Item(_language,r.ItemId)+$" · {r.Stock-r.Reserved}/{r.Capacity}")));
        _contextActions.ShowData(ContextActions.Scope(contextView),_choices,_language,WaitingCommand,status,_roadCardTop);
        LayoutContextDock();
    }
    private bool _layingContext;
    private Vector2 _contextViewport;
    private void LayoutContextDock()
    {
        if(_layingContext||_bottomChrome is not {} dock||_contextActions is null||!IsInsideTree())return;
        _layingContext=true;
        try
        {
            var viewport=GetViewportRect().Size;var safe=UiSafeArea.Read(dock);
            if(_contextActions.Pressing&&_contextViewport==viewport)return;
            float width=_contextActions.DockWidth(viewport.X-(float)safe.Left-(float)safe.Right-24);
            dock.Size=new(width,0);
            float height=Math.Max(dock.Size.Y,_contextActions.Size.Y);
            var point=new Vector2(((float)safe.Left+viewport.X-(float)safe.Right-width)/2,viewport.Y-height-(float)safe.Bottom-12);
            float left=(float)safe.Left+12,right=Math.Max(left,viewport.X-(float)safe.Right-width-12);
            float bottom=Math.Max((float)safe.Top+12,viewport.Y-(float)safe.Bottom-height-12),top=Math.Min(_roadCardTop+8,bottom);
            dock.Position=new(Math.Clamp(point.X,left,right),Math.Clamp(point.Y,top,bottom));
            _stick?.AvoidBottom(_contextActions.IsVisibleInTree()?height:0);
            _contextViewport=viewport;
        }
        finally{_layingContext=false;}
    }
    private void AnimateInterface(double delta)
    {
        if (_session is null) return;
        LayoutContextDock();
        var job = _session.Engine.HarvestOf(_human);
        string cycle = job is null ? "" : $"{job.StartTick}:{job.CompletionTick}:{job.ItemId}";
        double target = HarvestProgress.Percent(job, _session.Engine.Tick, _session.Engine.TravelQuantum);
        if (_harvestCycle != cycle) { _harvestCycle = cycle; _harvestVisual = 0; }
        if (!_session.Paused) _harvestVisual = Mathf.MoveToward(_harvestVisual, target, delta * 160);
        foreach (var button in _harvestButtons) button.ShowProgress(_harvestVisual);
        _contextActions?.ShowProgress(_harvestVisual);
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

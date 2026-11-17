using System.Globalization;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private HelpOverlay? _helpOverlay;
    private Button? _gameClock;
    private Button? _accountNotice;
    private Button? _hudMenu;
    private Button? _hudTablet;
    private Button? _hudMap;
    private Button? _hudPlots;
    private Button? _hudHelp;
    private Button? _hudNotices;
    private PanelContainer? _goalBar;
    private Label? _goalTitle;
    private Label? _goalHint;
    private Button? _goalWhere;
    private Button? _goalMore;
    private bool _goalOpen;
    private bool _friendly = true;
    private bool _guideVisible = true;
    private bool _userPaused;
    private readonly System.Collections.Generic.List<ContextActionButton> _harvestButtons = [];
    private string _harvestCycle = "";
    private double _harvestVisual;
    private string _noticeValue = "";
    private double _noticeSeconds;
    private bool _contextDismissTouch;
    private bool _contextCollapsed;

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
        var bar = _hudBar = new PanelContainer { Theme=TabletDesign.Theme, MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); bar.OffsetBottom = 54;
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("f3f3ed"),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 });
        _playChrome!.AddChild(bar);
        bar.Resized+=()=>LayoutCityHud(bar);
        var row = new HFlowContainer { Name="HudRow" };row.AddThemeConstantOverride("h_separation",6);row.AddThemeConstantOverride("v_separation",4); bar.AddChild(row);
        _hudMenu = new Button { Name="HudMenu", Text = L("Меню", "Menu"), FocusMode = FocusModeEnum.None };
        _hudMenu.Pressed += OpenMenu; row.AddChild(_hudMenu);
        _hudTablet = new Button { Name="HudTablet", Text = L("Планшет", "Tablet"), FocusMode = FocusModeEnum.None };
        _hudTablet.Pressed += OpenPlayerTablet; row.AddChild(_hudTablet);
        _hudMap=new Button{Name="OpenCityMap",Text=L("Карта","Map"),TooltipText=L("Карта всего города · M","City map · M"),FocusMode=FocusModeEnum.None};_hudMap.Pressed+=OpenCityMap;row.AddChild(_hudMap);
        _hudPlots=new Button{Name="HudPlots",Text=L("Участки","Plots"),ToggleMode=true,ButtonPressed=_showOwnership,TooltipText=L("Контуры собственности","Ownership outlines"),FocusMode=FocusModeEnum.None};
        _hudPlots.Toggled+=on=>{_showOwnership=on;_cityMapPanel?.SetWorldOwnership(on);SaveCityExperience();Refresh();};row.AddChild(_hudPlots);
        _accountNotice=new(){Name="AccountNotification",Text=L("Счёт","Account"),FocusMode=FocusModeEnum.None};
        _accountNotice.Pressed+=()=>{OpenPlayerTablet();_playerTablet?.OpenAccount();};row.AddChild(_accountNotice);
        _hudHelp = new Button { Name="HudHelp", Text = "?", TooltipText = L("Справка", "Help"), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 36) };
        _hudHelp.Pressed += () => OpenHelp(); row.AddChild(_hudHelp);
        _hudNotices=new Button{Name="HudNotices",FocusMode=FocusModeEnum.None,Visible=false,TooltipText=L("Важные события","Important notices")};
        _hudNotices.Pressed+=OpenCityNotices;row.AddChild(_hudNotices);
        foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})
            _hudNotices.AddThemeColorOverride(state,new Color("1b5c40"));
        _gameClock = new Button { Name="HudClock", FocusMode = FocusModeEnum.None, TooltipText = L("Пауза","Pause") };
        _gameClock.Pressed += () => { _userPaused = !_userPaused; ApplyPause(); Refresh(); };
        row.AddChild(_gameClock);
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
        var hoverActions=new HBoxContainer{Position=new(16,252)};hoverActions.AddThemeConstantOverride("separation",6);
        _openStorage=new Button{Name="OpenStorageCard",Text=L("Открыть хранение","Open storage"),FocusMode=FocusModeEnum.None,Visible=false};
        _openStorage.Pressed+=()=>{if(_holdingKind.Length==0)return;OpenPlayerTablet();_playerTablet?.OpenHolding(_holdingKind,_holdingId);};
        _openPlot=new Button{Name="OpenPlotCard",Text=L("Участок на карте","Plot on map"),FocusMode=FocusModeEnum.None,Visible=false};
        _openPlot.Pressed+=()=>{if(_plotId==0)return;_cityMapPreferences.ApplyFocus(CityMapFocus.Property);_cityMapPreferences.SelectedKey="plot:"+_plotId.ToString(CultureInfo.InvariantCulture);_cityMapPreferences.Center(_plotX+.5,_plotZ+.5);OpenCityMap();};
        hoverActions.AddChild(_openStorage);hoverActions.AddChild(_openPlot);_playChrome.AddChild(hoverActions);
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
        foreach(var button in row.GetChildren().OfType<Button>())
        {
            button.CustomMinimumSize=new(40,40);
            button.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;
            button.TextOverrunBehavior=TextServer.OverrunBehavior.NoTrimming;
        }
        var goal=new VBoxContainer();
        _goalTitle=TabletDesign.Heading("",18);_goalTitle.Name="StarterGoalTitle";_goalTitle.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _goalHint=new Label{Name="StarterGoalHint",AutowrapMode=TextServer.AutowrapMode.WordSmart,Visible=false,MaxLinesVisible=6,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};_goalHint.AddThemeColorOverride("font_color",new Color("24312d"));
        _goalWhere=new Button{Name="StarterGoalWhere",Text=L("К цели","Go"),CustomMinimumSize=new(0,36)};
        _goalWhere.Pressed+=()=>{if(_goalStep is {} step)ShowGuide(step);};
        _goalMore=new Button{Name="StarterGoalMore",Text=L("Ещё","More"),CustomMinimumSize=new(0,36)};
        _goalMore.Pressed+=()=>{_goalOpen=!_goalOpen;Refresh();};
        var hide=new Button{Name="StarterGoalHide",Text="×",CustomMinimumSize=new(36,36),TooltipText=L("Скрыть подсказку","Hide the hint")};
        hide.Pressed+=()=>{_guideVisible=false;SaveInterface();Refresh();};
        var goalRow=TabletDesign.Flow();goalRow.AddChild(_goalWhere);goalRow.AddChild(_goalMore);goalRow.AddChild(hide);
        goal.AddChild(_goalTitle);goal.AddChild(goalRow);goal.AddChild(_goalHint);
        _goalBar=new PanelContainer{Name="StarterGoal",MouseFilter=MouseFilterEnum.Stop,Visible=false,Theme=CompactTabletDesign.ForPlayer(true)};
        _goalBar.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("f7f4ea"),ContentMarginLeft=8,ContentMarginRight=8,ContentMarginTop=4,ContentMarginBottom=4,CornerRadiusTopLeft=8,CornerRadiusTopRight=8,CornerRadiusBottomRight=8,CornerRadiusBottomLeft=8});
        _goalBar.AddChild(goal);_playChrome.AddChild(_goalBar);
        void FitHud()
        {
            // Значки рюкзака уступают место навигации. В простом режиме их нет совсем.
            // Узкая ширина переносит ту же панель на вторую строку, не обрезая кнопки.
            if(_hudGoods is null)return;
            bool room=bar.Size.X>=520;
            if(_playInventory is not null)_playInventory.Visible=!_friendly||room;
            if(_friendly)_hudGoods.Visible=false;
            else
            {
                float fixedWidth=row.GetChildren().OfType<Control>().Where(c=>c.Visible&&c!=_hudGoods).Sum(c=>c.GetCombinedMinimumSize().X)+row.GetThemeConstant("h_separation")*Math.Max(0,row.GetChildCount()-1);
                _hudGoods.Visible=_hudGoods.GetChildCount()>0&&fixedWidth+_hudGoods.GetCombinedMinimumSize().X<=bar.Size.X-24;
            }
            float width=Math.Max(1,bar.Size.X-24),x=0,y=0,rowH=0;
            foreach(var child in row.GetChildren().OfType<Control>().Where(c=>c.Visible))
            {
                var size=child.GetCombinedMinimumSize();
                if(x>0&&x+size.X>width){x=0;y+=rowH+4;rowH=0;}
                x+=size.X+6;rowH=Math.Max(rowH,size.Y);
            }
            float height=Math.Max(54,y+rowH+12);
            var safe=UiSafeArea.Read(bar);float next=height+(float)safe.Top;
            if(Math.Abs(bar.OffsetBottom-next)>0.5f)bar.OffsetBottom=next;
        }
        UiSafeArea.Track(bar,()=>{var safe=UiSafeArea.Read(bar);bar.OffsetLeft=(float)safe.Left;bar.OffsetRight=-(float)safe.Right;bar.OffsetTop=(float)safe.Top;FitHud();});
        row.Resized+=FitHud;row.MinimumSizeChanged+=()=>Callable.From(FitHud).CallDeferred();
        _contextKey = ""; _choices = [];ApplyHudMode();LayoutCityHud(bar);
        _playChrome.MoveChild(_goalBar,_playChrome.GetChildCount()-1);
    }
    private PanelContainer? _hudBar;
    private void FitTooltip(float width)
    {
        if(_tooltip is null)return;
        _tooltip.CustomMinimumSize=new(width,0);
        _tooltip.Size=new(width,Math.Max(1,_tooltip.GetCombinedMinimumSize().Y));
    }
    private void LayoutCityHud(Control bar)
    {
        float top=bar.Position.Y+bar.Size.Y;var safe=UiSafeArea.Read(bar);
        var view=GetViewportRect().Size;
        _roadCardTop=top+12;LayoutRoadWork();
        if(_playNotice is not null){_playNotice.OffsetTop=top+8;_playNotice.OffsetBottom=top+42;_playNotice.OffsetLeft=16+(float)safe.Left;_playNotice.OffsetRight=-16-(float)safe.Right;}
        if(_storageWarning is not null){_storageWarning.OffsetTop=top+44;_storageWarning.OffsetBottom=top+100;_storageWarning.OffsetRight=-16-(float)safe.Right;}
        if(_goalHint is not null)_goalHint.MaxLinesVisible=view.Y<480?3:6;
        if(_goalBar is {Visible:true})
        {
            // Заголовок на своей строке и переносится. Иначе длинный английский текст сжимается до пары букв.
            float available=view.X-24-(float)safe.Left-(float)safe.Right;
            float width=Math.Min(available,340);
            _goalBar.Position=new(12+(float)safe.Left,top+8);
            _goalBar.CustomMinimumSize=new(width,0);
            _goalBar.Size=new(width,Math.Max(_goalBar.GetCombinedMinimumSize().Y,1));
        }
        if(_tooltip is not null)
        {
            float left=16+(float)safe.Left;
            float right=view.X-16-(float)safe.Right;
            if(_goalBar is {Visible:true})
            {
                float beside=right-(_goalBar.Position.X+_goalBar.Size.X)-8;
                if(beside>=180)
                {
                    _tooltip.Position=new(_goalBar.Position.X+_goalBar.Size.X+8,_goalBar.Position.Y);
                    FitTooltip(Math.Min(330,beside));
                }
                else
                {
                    _tooltip.Position=new(left,_goalBar.Position.Y+_goalBar.Size.Y+8);
                    FitTooltip(Math.Min(330,Math.Max(120,right-left)));
                }
            }
            else
            {
                _tooltip.Position=new(left,top+8);
                FitTooltip(Math.Min(330,Math.Max(120,right-left)));
            }
        }
    }
    private void LoadInterface()
    {
        var config=new ConfigFile();
        if(config.Load("user://interface.cfg")!=Error.Ok)return;
        _friendly=config.GetValue("ui","mode",FriendlyGuidance.Simple).AsString()!=FriendlyGuidance.Advanced;
        _guideVisible=config.GetValue("ui","guide",true).AsBool();
    }
    private void SaveInterface()
    {
        var config=new ConfigFile();
        config.SetValue("ui","mode",_friendly?FriendlyGuidance.Simple:FriendlyGuidance.Advanced);
        config.SetValue("ui","guide",_guideVisible);
        if(config.Save("user://interface.cfg")!=Error.Ok)_feedback=L("Не удалось сохранить вид интерфейса.","Could not save the interface preference.");
    }
    private void SetSimple(bool simple)
    {
        if(_friendly==simple)return;
        _friendly=simple;SaveInterface();ApplyHudMode();
        _tabletPanel?.SetSimple(simple);
        _playerTablet?.ApplyPresentation(simple);
        if(_tabletPanel is not null&&_tabletState.Kind==TabletKind.Player)
            _tabletPanel.SetTitle(simple?L("Хозяйство","Business"):L("Планшет игрока","Player tablet"));
        if(_contextActions is not null)_contextActions.Friendly=simple;
        Refresh();
    }
    private void ApplyHudMode()
    {
        if(_hudMenu is null||_hudTablet is null||_hudMap is null||_hudPlots is null||_hudHelp is null||_accountNotice is null||_gameClock is null)return;
        var row=(Container)_hudMenu.GetParent();
        _hudTablet.Text=_friendly?L("Хозяйство","Business"):L("Планшет","Tablet");
        _hudMenu.Text=L("Меню","Menu");_hudMap.Text=L("Карта","Map");
        foreach(var control in new Control?[]{_hudMenu,_hudTablet,_hudMap,_accountNotice,_hudNotices,_gameClock})
        {
            if(_friendly)control?.AddThemeFontSizeOverride("font_size",18);
            else control?.RemoveThemeFontSizeOverride("font_size");
        }
        _hudPlots.Visible=!_friendly;_hudHelp.Visible=!_friendly;
        if(_hudGoods is not null&&_friendly)_hudGoods.Visible=false;
        void Place(Control control,int index){if(control.GetParent()==row)row.MoveChild(control,index);}
        if(_friendly)
        {
            Place(_hudMap,0);Place(_hudTablet,1);Place(_hudMenu,2);Place(_accountNotice,3);
            if(_hudNotices is not null)Place(_hudNotices,4);Place(_gameClock,5);
        }
        else
        {
            Place(_hudMenu,0);Place(_hudTablet,1);Place(_hudMap,2);Place(_hudPlots,3);Place(_accountNotice,4);Place(_hudHelp,5);
            if(_hudNotices is not null)Place(_hudNotices,6);Place(_gameClock,7);
        }
    }
    private StarterStep? _goalStep;
    private void UpdateGoal(PlayerView view)
    {
        if(_goalBar is null||_goalTitle is null||_goalHint is null||_goalWhere is null)return;
        _goalStep=_session is null?null:FriendlyGuidance.Read(view,_scenario,_language,_cityRead.Noted("sale"));
        bool show=_friendly&&_guideVisible&&_goalStep is not null&&_surface==Surface.Play&&!_tabletState.IsOpen&&_helpOverlay is null;
        _goalBar.Visible=show;
        if(_goalStep is not {} step||_goalMore is null)return;
        string title=StarterGoalUi.Title(step,_language);
        if(step.Count&&step.Need>0)
        {
            long shown=Math.Min(Math.Max(0,step.Have),step.Need);
            title+=" — "+shown+"/"+step.Need;
        }
        _goalTitle.Text=title;
        string explanation=StarterGoalUi.Next(step,_language);
        string gaps=FriendlyGuidance.Describe(step.Gaps,_language,true);
        _goalHint.Text=gaps.Length==0?explanation:explanation+"\n"+gaps;
        _goalHint.Visible=_goalOpen;
        _goalMore.Text=_goalOpen?L("Свернуть","Fold"):L("Как?","How?");
        _goalMore.TooltipText=_goalOpen?L("Свернуть","Fold"):L("Подробнее","Details");
        bool guiding=_guideCell is {} at && step.X==at.X && step.Z==at.Z;
        var purchase = GuidePurchase(step, view.Participant.Position);
        _goalWhere.Text = purchase?.Label ?? StarterGoalUi.Action(step,_language,guiding);
        _goalWhere.Disabled = purchase is { Enabled: false } || !StarterGoalUi.CanOpen(step);
        _goalWhere.TooltipText = purchase?.Details ?? "";
        if(_hudBar is not null)LayoutCityHud(_hudBar);
        Callable.From(()=>{if(_hudBar is not null)LayoutCityHud(_hudBar);}).CallDeferred();
    }
    private string _previewKey="";
    private void FillPlayContext(VBoxContainer host, PlayerView contextView)
    {
        if(_contextActions is null)
        {
            _contextActions=new ContextActionsPanel();host.AddChild(_contextActions);
            _contextActions.Requested+=ConfirmContextChoice;
            _contextActions.Preview+=choice=>{_previewKey=choice?.Key??"";var preview=FriendlyGuidance.Preview(choice?.Command,_scenario);_stage?.SetPreview(preview.Cells,preview.Entry,preview.Anchor,choice is {Enabled:false});};
            _contextActions.CollapsedChanged+=collapsed=>{_contextCollapsed=collapsed;LayoutContextDock();};
            if(_contextCollapsed)_contextActions.Dismiss();
            _contextActions.ExpansionChanged+=expanded=>
            {if(expanded){StopHeading();_waitRelease=true;}else _contextDismissTouch=false;if(_contextShield is not null)_contextShield.Visible=expanded;ApplyPause();};
            _contextActions.Resized+=LayoutContextDock;
        }
        _contextActions.Friendly=_friendly;
        var response=_contextCommandId is {} id?_session.Engine.Result(id):null;
        string status=_contextCommandId is null?"":ContextActions.ResultText(_language,response);
        if(_contextMessage.Length>0)status=_contextMessage;
        _contextActions.ShowSummary(string.Join(" | ",contextView.Resources.Where(r=>r.Position==contextView.Participant.Position).Select(r=>PlayContext.ResourceLine(contextView,r,_language))));
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
            var point=new Vector2(_contextActions.Dismissed?viewport.X-(float)safe.Right-width-12:((float)safe.Left+viewport.X-(float)safe.Right-width)/2,viewport.Y-height-(float)safe.Bottom-12);
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

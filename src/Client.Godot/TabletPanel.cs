using Godot;
using Simulation.Hosting;

/// <summary>Shared kit frame. Time controls delegate to the existing TabletState.</summary>
public partial class TabletPanel : PanelContainer
{
    public event System.Action? Closed;
    public event System.Action? HelpRequested;
    public event System.Action<bool>? TimeRunningChanged;
    public VBoxContainer Body {get;}
    private readonly HBoxContainer _header;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _column;
    private readonly Label _title;
    private readonly HBoxContainer _clockRow;
    private bool _compactFrame;
    private bool ShortLandscape=>_compactFrame&&GetViewportRect().Size.Y<540;
    public void AttachNavigation(OptionButton picker)
    {
        picker.GetParent()?.RemoveChild(picker);_header.AddChild(picker);_header.MoveChild(picker,1);picker.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        picker.VisibilityChanged+=()=>_title.Visible=!picker.Visible;_title.Visible=!picker.Visible;
    }
    private void LayoutFrame()
    {
        var safe=UiSafeArea.Read(this);bool shortScreen=ShortLandscape;float side=shortScreen?6:GetViewportRect().Size.X>=1360?64:12;
        OffsetLeft=side+(float)safe.Left;OffsetRight=-side-(float)safe.Right;OffsetTop=(shortScreen?46:66)+(float)safe.Top;OffsetBottom=-(shortScreen?6:18)-(float)safe.Bottom;
        if(!_compactFrame)return;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("fafafa",shortScreen?8:12));
        _column.AddThemeConstantOverride("separation",shortScreen?3:8);
        _title.AddThemeFontSizeOverride("font_size",shortScreen?18:20);
        _header.GetChild<Control>(0).CustomMinimumSize=new(shortScreen?20:28,shortScreen?20:28);
        foreach(var button in _header.GetChildren().OfType<Button>().Where(b=>b is not OptionButton)){button.CustomMinimumSize=new(shortScreen?40:44,shortScreen?40:44);button.SizeFlagsVertical=SizeFlags.ShrinkCenter;}
        _clockRow.GetChild<Control>(0).CustomMinimumSize=new(shortScreen?18:26,shortScreen?18:26);_status.MaxLinesVisible=shortScreen?1:3;
    }
    /// <summary>Opt-in I6-A frame: the child owns scrolling; legacy tablets keep their frame.</summary>
    public void UseCompactFrame()
    {
        _compactFrame=true;Theme=CompactTabletDesign.Theme;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("fafafa",12));
        _title.AddThemeFontSizeOverride("font_size",20);
        _header.GetChild<Control>(0).CustomMinimumSize=new(28,28);
        foreach(var button in _header.GetChildren().OfType<Button>())button.SizeFlagsVertical=SizeFlags.ShrinkCenter;
        // Contain a legacy page's minimum width inside the body. Disabled propagates it
        // into the whole frame and can push Help/Close beyond a narrow viewport.
        _scroll.HorizontalScrollMode=ScrollContainer.ScrollMode.ShowNever;
        _scroll.VerticalScrollMode=ScrollContainer.ScrollMode.Disabled;
        Body.SizeFlagsVertical=SizeFlags.ExpandFill;
        if(IsInsideTree())LayoutFrame();
    }
    public void UseArtworkHeader(bool active)=>_header.Visible=!active;
    public void RequestHelp()=>HelpRequested?.Invoke();
    public void RequestClose()=>Closed?.Invoke();
    private readonly CheckButton _time;
    private readonly Label _status,_clock;
    private readonly string _language;
    public TabletPanel(string title,string language,string icon="home")
    {
        Name="GameTablet";_language=language;Theme=CompactTabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;_compactFrame=true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);OffsetTop=66;OffsetBottom=-18;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("fafafa",12));
        var column=new VBoxContainer();_column=column;AddChild(column);
        var header=new HBoxContainer();_header=header;column.AddChild(header);header.AddChild(TabletDesign.Art(icon,new(56,56)));
        header.AddChild(new Label{Text=title,SizeFlagsHorizontal=SizeFlags.ExpandFill,AutowrapMode=TextServer.AutowrapMode.WordSmart,ThemeTypeVariation="TabletTitle"});
        _title=(Label)header.GetChild(1);_title.AddThemeFontSizeOverride("font_size",27);
        var help=new Button{Name="TabletHelp",Text="?",TooltipText=L("Справка по этому экрану","Help for this screen"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        help.Pressed+=()=>HelpRequested?.Invoke();header.AddChild(help);
        var close=new Button{Name="TabletClose",Text="×",TooltipText=L("Закрыть","Close"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        close.Pressed+=()=>Closed?.Invoke();header.AddChild(close);column.AddChild(new HSeparator());
        var scroll=new ScrollContainer{Name="TabletScroll",SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Auto};_scroll=scroll;column.AddChild(scroll);
        Body=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(Body);
        _status=new Label{Name="TabletFeedback",AutowrapMode=TextServer.AutowrapMode.WordSmart,MaxLinesVisible=3};column.AddChild(_status);
        column.AddChild(new HSeparator());var footer=new TabletColumns{Breakpoint=560};column.AddChild(footer);var clockRow=new HBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};_clockRow=clockRow;footer.AddChild(clockRow);clockRow.AddChild(TabletDesign.Art("clock",new(26,26)));
        _clock=new Label{SizeFlagsHorizontal=SizeFlags.ExpandFill};clockRow.AddChild(_clock);
        _time=new CheckButton{Name="TabletTime",FocusMode=FocusModeEnum.None,TooltipText=L("Пауза останавливает весь мир, включая NPC. Разрешённое движение запускает время.","Pause stops the whole world, including NPCs. Allowed movement resumes time.")};
        _time.Toggled+=running=>TimeRunningChanged?.Invoke(running);footer.AddChild(_time);
        ClipContents=true;UiSafeArea.Track(this,LayoutFrame);Ready+=()=>UiSafeArea.Keyboard(this);
    }
    public void ShowClock(bool timeRunning,bool paused,long tick,string feedback)
    {
        _time.SetPressedNoSignal(timeRunning);_time.Text=ShortLandscape?L(timeRunning?"Время идёт":"На паузе",timeRunning?"Running":"Paused"):L(timeRunning?"Время идёт":"Время остановлено",timeRunning?"Time running":"Time paused");
        _clock.Text=PlayerText.Local(_language,paused?"Мир на паузе":"Активное время",paused?"World paused":"Active time")+" · "+$"{tick/3600:00}:{tick/60%60:00}:{tick%60:00}";
        if(ShortLandscape)_clock.Text=$"{tick/3600:00}:{tick/60%60:00}:{tick%60:00}";
        _status.Text=feedback;_status.Visible=feedback.Length>0;
        if(IsInsideTree()&&Body.Size.X>0)GameUi.FitControls(Body,Body.Size.X);
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

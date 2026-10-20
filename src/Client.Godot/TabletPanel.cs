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
    public void UseArtworkHeader(bool active)=>_header.Visible=!active;
    public void RequestHelp()=>HelpRequested?.Invoke();
    public void RequestClose()=>Closed?.Invoke();
    private readonly CheckButton _time;
    private readonly Label _status,_clock;
    private readonly string _language;
    public TabletPanel(string title,string language,string icon="home")
    {
        Name="GameTablet";_language=language;Theme=TabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);OffsetTop=66;OffsetBottom=-18;
        AddThemeStyleboxOverride("panel",TabletDesign.Box("f7f3e8",28,22));
        var column=new VBoxContainer();AddChild(column);
        var header=new HBoxContainer();_header=header;column.AddChild(header);header.AddChild(TabletDesign.Art(icon,new(56,56)));
        header.AddChild(new Label{Text=title,SizeFlagsHorizontal=SizeFlags.ExpandFill,AutowrapMode=TextServer.AutowrapMode.WordSmart,ThemeTypeVariation="TabletTitle"});
        ((Label)header.GetChild(1)).AddThemeFontSizeOverride("font_size",27);
        var help=new Button{Name="TabletHelp",Text="?",TooltipText=L("Справка по этому экрану","Help for this screen"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        help.Pressed+=()=>HelpRequested?.Invoke();header.AddChild(help);
        var close=new Button{Name="TabletClose",Text="×",TooltipText=L("Закрыть","Close"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        close.Pressed+=()=>Closed?.Invoke();header.AddChild(close);column.AddChild(new HSeparator());
        var scroll=new ScrollContainer{Name="TabletScroll",SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};column.AddChild(scroll);
        Body=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(Body);
        _status=new Label{Name="TabletFeedback",AutowrapMode=TextServer.AutowrapMode.WordSmart,MaxLinesVisible=3};column.AddChild(_status);
        column.AddChild(new HSeparator());var footer=new HBoxContainer();column.AddChild(footer);footer.AddChild(TabletDesign.Art("clock",new(26,26)));
        _clock=new Label{SizeFlagsHorizontal=SizeFlags.ExpandFill};footer.AddChild(_clock);
        _time=new CheckButton{Name="TabletTime",FocusMode=FocusModeEnum.None,TooltipText=L("Пауза останавливает весь мир, включая NPC. Разрешённое движение запускает время.","Pause stops the whole world, including NPCs. Allowed movement resumes time.")};
        _time.Toggled+=running=>TimeRunningChanged?.Invoke(running);footer.AddChild(_time);
        Resized+=()=>{float margin=GetViewportRect().Size.X>=1360?64:12;OffsetLeft=margin;OffsetRight=-margin;};OffsetLeft=12;OffsetRight=-12;
    }
    public void ShowClock(bool timeRunning,bool paused,long tick,string feedback)
    {
        _time.SetPressedNoSignal(timeRunning);_time.Text=L(timeRunning?"Время идёт":"Время остановлено",timeRunning?"Time running":"Time paused");
        _clock.Text=PlayerText.Local(_language,paused?"Мир на паузе":"Активное время",paused?"World paused":"Active time")+" · "+$"{tick/3600:00}:{tick/60%60:00}:{tick%60:00}";
        _status.Text=feedback;_status.Visible=feedback.Length>0;
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

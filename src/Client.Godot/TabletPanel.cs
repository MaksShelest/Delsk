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
    private bool _compactFrame;
    private bool _friendly;
    private readonly Button _help;
    public bool SideDock{get;private set;}
    public void SetTitle(string title)=>_title.Text=title;
    public void SetSimple(bool friendly)
    {
        _friendly=friendly;
        Theme=CompactTabletDesign.ForPlayer(friendly);
        if(IsInsideTree())LayoutFrame();
    }
    /// <summary>Левая панель под верхним HUD: заголовок и закрытие фиксированы, мир остаётся справа.</summary>
    public void UseSideDock()
    {
        SideDock=true;UseCompactFrame();
        _scroll.VerticalScrollMode=ScrollContainer.ScrollMode.Disabled;_scroll.HorizontalScrollMode=ScrollContainer.ScrollMode.ShowNever;
        if(IsInsideTree())LayoutFrame();
    }
    private bool ShortLandscape=>_compactFrame&&GetViewportRect().Size.Y<540;
    public void AttachNavigation(OptionButton picker)
    {
        picker.GetParent()?.RemoveChild(picker);_header.AddChild(picker);_header.MoveChild(picker,1);picker.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        picker.VisibilityChanged+=()=>_title.Visible=!picker.Visible;_title.Visible=!picker.Visible;
    }
    private void LayoutFrame()
    {
        _help.Text=_friendly&&!SideDock&&GetViewportRect().Size.X>=700?L("Справка","Help"):"?";
        if(SideDock)
        {
            var dockSafe=UiSafeArea.Read(this);var size=GetViewportRect().Size;bool dockShort=size.Y<540;bool phone=size.X<1000;bool tiny=phone&&size.X<=820&&dockShort;
            float fraction=!phone?.38f:tiny?.62f:.56f;float left=(float)dockSafe.Left+(dockShort?4:8);float right=(float)dockSafe.Right+8;
            float minWorld=size.X*(phone?.28f:.55f);float width=Mathf.Min(size.X*fraction,size.X-left-right-minWorld);width=Mathf.Max(width,Mathf.Min(260,size.X*.5f));
            OffsetLeft=left;OffsetRight=-(size.X-left-width);OffsetTop=(dockShort?46:62)+(float)dockSafe.Top;OffsetBottom=-(dockShort?4:8)-(float)dockSafe.Bottom;
            AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f1f2ec",dockShort?6:8));
            _column.AddThemeConstantOverride("separation",dockShort?3:5);_title.AddThemeFontSizeOverride("font_size",dockShort?16:18);
            _header.GetChild<Control>(0).Visible=false;
            foreach(var button in _header.GetChildren().OfType<Button>().Where(b=>b is not OptionButton)){button.CustomMinimumSize=new(dockShort?40:44,dockShort?40:44);button.SizeFlagsVertical=SizeFlags.ShrinkCenter;}
            _status.MaxLinesVisible=dockShort?1:2;return;
        }
        var safe=UiSafeArea.Read(this);bool shortScreen=ShortLandscape;float side=shortScreen?6:GetViewportRect().Size.X>=1360?64:12;
        OffsetLeft=side+(float)safe.Left;OffsetRight=-side-(float)safe.Right;OffsetTop=(shortScreen?46:66)+(float)safe.Top;OffsetBottom=-(shortScreen?6:18)-(float)safe.Bottom;
        if(!_compactFrame)return;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f1f2ec",shortScreen?8:12));
        _column.AddThemeConstantOverride("separation",shortScreen?3:5);
        _title.AddThemeFontSizeOverride("font_size",(shortScreen?18:20)+(_friendly?2:0));
        _header.GetChild<Control>(0).Visible=!_friendly||GetViewportRect().Size.X>=560;_header.GetChild<Control>(0).CustomMinimumSize=new(shortScreen?20:28,shortScreen?20:28);
        foreach(var button in _header.GetChildren().OfType<Button>().Where(b=>b is not OptionButton)){button.CustomMinimumSize=new(shortScreen?40:44,shortScreen?40:44);button.SizeFlagsVertical=SizeFlags.ShrinkCenter;}
        _status.MaxLinesVisible=shortScreen?1:3;
    }
    /// <summary>Opt-in I6-A frame: the child owns scrolling; legacy tablets keep their frame.</summary>
    public void UseCompactFrame()
    {
        _compactFrame=true;Theme=CompactTabletDesign.Theme;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f1f2ec",12));
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
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f1f2ec",12));
        var column=new VBoxContainer();_column=column;AddChild(column);
        var header=new HBoxContainer();_header=header;column.AddChild(header);header.AddChild(TabletDesign.Art(icon,new(56,56)));
        header.AddChild(new Label{Text=title,SizeFlagsHorizontal=SizeFlags.ExpandFill,AutowrapMode=TextServer.AutowrapMode.WordSmart,ThemeTypeVariation="TabletTitle"});
        _title=(Label)header.GetChild(1);_title.AddThemeFontSizeOverride("font_size",27);
        var help=_help=new Button{Name="TabletHelp",Text="?",TooltipText=L("Справка по этому экрану","Help for this screen"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        help.Pressed+=()=>HelpRequested?.Invoke();header.AddChild(help);
        var close=new Button{Name="TabletClose",Text="×",TooltipText=L("Закрыть","Close"),FocusMode=FocusModeEnum.None,CustomMinimumSize=new(44,44)};
        close.Pressed+=()=>Closed?.Invoke();header.AddChild(close);column.AddChild(new HSeparator());
        var scroll=new ScrollContainer{Name="TabletScroll",SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Auto};_scroll=scroll;column.AddChild(scroll);
        Body=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(Body);
        _status=new Label{Name="TabletFeedback",AutowrapMode=TextServer.AutowrapMode.WordSmart,MaxLinesVisible=3};column.AddChild(_status);
        var footer=new HBoxContainer{Name="TabletFooter"};footer.AddThemeConstantOverride("separation",6);column.AddChild(footer);
        _clock=new Label{Name="TabletClock",SizeFlagsHorizontal=SizeFlags.ExpandFill,VerticalAlignment=VerticalAlignment.Center};footer.AddChild(_clock);
        _time=new CheckButton{Name="TabletTime",CustomMinimumSize=new(48,44),FocusMode=FocusModeEnum.None,TooltipText=L("Пауза останавливает весь мир, включая NPC. Разрешённое движение запускает время.","Pause stops the whole world, including NPCs. Allowed movement resumes time.")};
        foreach(string state in new[]{"normal","hover","pressed"})_time.AddThemeStyleboxOverride(state,new StyleBoxEmpty{ContentMarginLeft=2,ContentMarginRight=2});
        _time.AddThemeStyleboxOverride("focus",new StyleBoxFlat{BgColor=Colors.Transparent,BorderColor=TabletDesign.Green,BorderWidthLeft=2,BorderWidthRight=2,BorderWidthTop=2,BorderWidthBottom=2,CornerRadiusTopLeft=6,CornerRadiusTopRight=6,CornerRadiusBottomLeft=6,CornerRadiusBottomRight=6});
        foreach(string state in new[]{"checked","unchecked","checked_disabled","unchecked_disabled"})
        {
            var image=TabletDesign.Theme.GetIcon(state,"CheckButton").GetImage();image.Resize(40,23,Image.Interpolation.Lanczos);_time.AddThemeIconOverride(state,ImageTexture.CreateFromImage(image));
        }
        _time.Toggled+=running=>TimeRunningChanged?.Invoke(running);footer.AddChild(_time);
        ClipContents=true;UiSafeArea.Track(this,LayoutFrame);Ready+=()=>UiSafeArea.Keyboard(this);
    }
    public void ShowClock(bool timeRunning,bool paused,long tick,string feedback)
    {
        _time.SetPressedNoSignal(timeRunning);_time.Text=_friendly&&!SideDock?L("Ход времени","World time"):"";
        _time.TooltipText=_friendly?L(timeRunning?"Поставить мир на паузу":"Возобновить ход времени",timeRunning?"Pause the world":"Resume world time")
            :L("Пауза останавливает весь мир, включая NPC. Разрешённое движение запускает время.","Pause stops the whole world, including NPCs. Allowed movement resumes time.");
        _clock.Text="◷ "+$"{tick/3600:00}:{tick/60%60:00}:{tick%60:00}"+" · "+L(paused?"Пауза":"Время идёт",paused?"Paused":"Running");
        _status.Text=feedback;_status.Visible=feedback.Length>0;
        if(IsInsideTree()&&Body.Size.X>0)GameUi.FitControls(Body,Body.Size.X);
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

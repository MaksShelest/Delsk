using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Persistent keyed controls. Refresh updates data without discarding focus, scroll or a pressed row.</summary>
public partial class ContextActionsPanel : PanelContainer
{
    public event Action<ContextChoice>? Requested;
    public event Action<bool>? ExpansionChanged;
    public readonly ContextSelection Selection=new();
    public bool Expanded=>Selection.Expanded;
    public bool Dismissed {get;private set;}
    public event Action<bool>? CollapsedChanged;
    public int ActiveCount=>Selection.Choices.Count(c=>c.Enabled&&(!Selection.Pending||c.Command is null));
    private readonly VBoxContainer _body=new();
    private readonly Button _reopen=new(){Name="ContextReopen",CustomMinimumSize=new(68,44)};
    public bool Pressing=>_reopen.ButtonPressed||_dismiss.ButtonPressed||_more.ButtonPressed||_rows.Values.Any(r=>r.Button.ButtonPressed);
    private readonly HFlowContainer _primary=new(){SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly HBoxContainer _actions=new();
    private readonly Button _dismiss=new(){Name="ContextDismiss",Text="▾",CustomMinimumSize=new(44,44),SizeFlagsVertical=SizeFlags.ShrinkBegin};
    private readonly HFlowContainer _list=new(){SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly ScrollContainer _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};
    private readonly Button _more=new(){Name="ContextMore",CustomMinimumSize=new(44,44),SizeFlagsVertical=SizeFlags.ShrinkBegin};
    private readonly Label _status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart,MaxLinesVisible=1,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
    private readonly Label _summary=new(){TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,MouseFilter=MouseFilterEnum.Ignore};
    private readonly Dictionary<string,(VBoxContainer Row,Button Button,Label Reason,ProgressBar Progress)> _rows=[];

    private string _language="en";
    private int _touchIndex=-1,_touchScroll;
    private Vector2 _touchOrigin;private float _heightTop;private bool _fittingHeight;
    private bool _touchDragged;
    public ContextActionsPanel()
    {
        Name="ContextActions";Theme=CompactTabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f3f3ed",8));
        var body=_body;body.AddThemeConstantOverride("separation",4);AddChild(body);AddChild(_reopen);_reopen.Hide();_reopen.Pressed+=Reopen;
        body.AddChild(_scroll);_scroll.AddChild(_list);_list.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        body.AddChild(_summary);_summary.AddThemeFontSizeOverride("font_size",13);body.AddChild(_status);body.AddChild(_actions);
        _actions.AddChild(_primary);_actions.AddChild(_more);_actions.AddChild(_dismiss);
        _more.Pressed+=()=>SetExpanded(!Expanded);_dismiss.Pressed+=Dismiss;
        Resized+=LayoutRows;
    }
    public void Dismiss()
    {
        Dismissed=true;SetExpanded(false);ApplyCollapsed();CollapsedChanged?.Invoke(true);
        if(GetViewport().GuiGetFocusOwner() is Control focused&&IsAncestorOf(focused))focused.ReleaseFocus();
    }
    public void Reopen(){Dismissed=false;ApplyCollapsed();CollapsedChanged?.Invoke(false);}
    private void ApplyCollapsed()
    {
        _body.Visible=!Dismissed;_reopen.Visible=Dismissed;
        _reopen.Text="⚡ "+ActiveCount;_reopen.TooltipText=PlayerText.Local(_language,"Раскрыть локальные действия","Show local actions");
        AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f3f3ed",Dismissed?0:8));
        Visible=Selection.Choices.Length>0;CustomMinimumSize=Vector2.Zero;Size=new(Size.X,0);
    }
    public float DockWidth(float maximum)
    {
        if(Dismissed)return _reopen.GetCombinedMinimumSize().X;
        float natural=40+44+(_more.Visible?52:0)+8;
        foreach(string key in Selection.PrimaryKeys())if(_rows.TryGetValue(key,out var row))
            natural+=Math.Clamp(row.Button.GetThemeFont("font").GetStringSize(row.Button.Text,fontSize:row.Button.GetThemeFontSize("font_size")).X+28,90,280)+8;
        return Math.Min(maximum,Expanded?Math.Max(natural,560):natural);
    }
    public void SetExpanded(bool value)
    {
        if(Selection.Expanded==value)return;
        Selection.Expanded=value;_scroll.Visible=value;_more.TooltipText=MoreHint();if(!value){_touchIndex=-1;_touchDragged=false;}LayoutRows();ExpansionChanged?.Invoke(value);
    }
    public override void _Input(InputEvent @event)
    {
        if(!Expanded||!IsVisibleInTree())return;
        if(@event is InputEventScreenTouch touch)
        {
            if(touch.Pressed&&_scroll.GetGlobalRect().HasPoint(touch.Position))
            {_touchIndex=touch.Index;_touchOrigin=touch.Position;_touchScroll=_scroll.ScrollVertical;_touchDragged=false;}
            else if(!touch.Pressed&&touch.Index==_touchIndex)
            {
                _touchIndex=-1;
                if(_touchDragged){GetViewport().SetInputAsHandled();Callable.From(()=>_touchDragged=false).CallDeferred();}
            }
        }
        else if(@event is InputEventScreenDrag drag&&drag.Index==_touchIndex)
        {
            if(Math.Abs(drag.Position.Y-_touchOrigin.Y)<10&&!_touchDragged)return;
            _touchDragged=true;
            // A drag cancels a row press before either touch or emulated mouse release can activate it.
            foreach(var row in _rows.Values)row.Button.SetPressedNoSignal(false);
            _scroll.ScrollVertical=_touchScroll+(int)(_touchOrigin.Y-drag.Position.Y);
            GetViewport().SetInputAsHandled();
        }
        else if(_touchDragged&&@event is InputEventMouseButton or InputEventMouseMotion)GetViewport().SetInputAsHandled();
    }
    public void ShowSummary(string text){_summary.Text=text;_summary.Visible=text.Length>0;}
    public void ShowData(string scope,ContextChoice[] choices,string language,bool pending,string status,float top)
    {
        bool was=Expanded;_language=language;Selection.Update(scope,choices);if(choices.Length==0)Selection.Expanded=false;Selection.Pending=pending;
        if(was!=Expanded){_touchIndex=-1;_touchDragged=false;ExpansionChanged?.Invoke(Expanded);}
        var primary=Selection.PrimaryKeys();
        foreach(string key in _rows.Keys.Except(choices.Select(c=>c.Key)).ToArray())
        {var row=_rows[key].Row;row.GetParent()?.RemoveChild(row);row.QueueFree();_rows.Remove(key);}
        var positions=new Dictionary<Container,int>();
        foreach(var choice in choices)
        {
            if(!_rows.TryGetValue(choice.Key,out var item))
            {
                var row=new VBoxContainer{MouseFilter=MouseFilterEnum.Pass};
                var button=new Button{Name=ButtonName(choice.Key),CustomMinimumSize=new(90,44),SizeFlagsHorizontal=SizeFlags.ShrinkBegin,AutowrapMode=TextServer.AutowrapMode.WordSmart};
                var reason=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,MouseFilter=MouseFilterEnum.Ignore};row.AddChild(button);row.AddChild(reason);
                var progress=new ProgressBar{ShowPercentage=false,MaxValue=100,CustomMinimumSize=new(0,8),MouseFilter=MouseFilterEnum.Ignore};row.AddChild(progress);
                string key=choice.Key;ContextChoice? pressed=null;string pressedScope="";bool additionalPress=false;
                button.ButtonDown+=()=>{pressed=Selection.Choices.FirstOrDefault(c=>c.Key==key);pressedScope=Selection.Scope;additionalPress=row.GetParent()!=_primary;};
                button.FocusEntered+=()=>Selection.Select(key);
                button.Pressed+=()=>{var snapshot=pressed;pressed=null;if(!_touchDragged&&snapshot is not null&&pressedScope==Selection.Scope&&(!additionalPress||Expanded))Requested?.Invoke(snapshot);};
                item=(row,button,reason,progress);_rows.Add(key,item);
            }
            Container host=_primary;
            if(!primary.Contains(choice.Key))
            {
                host=_list;
            }
            if(item.Row.GetParent()!=host){item.Row.GetParent()?.RemoveChild(item.Row);host.AddChild(item.Row);}
            int position=positions.GetValueOrDefault(host,0);positions[host]=position+1;
            if(!Pressing&&item.Row.GetIndex()!=position)host.MoveChild(item.Row,position);
            item.Button.Text=choice.Command?.Kind switch
            {ActionKind.BuyVehicle=>choice.Label.Replace(" · ","\n",StringComparison.Ordinal),ActionKind.BoardVehicle=>PlayerText.Local(language,"Сесть","Board"),ActionKind.LeaveVehicle=>PlayerText.Local(language,"Выйти","Leave"),_=>choice.Label};item.Button.Disabled=!choice.Enabled||pending&&choice.Command is not null;
            item.Button.TooltipText=choice.Label+"\n"+choice.Details;
            item.Reason.Text=!choice.Enabled?choice.Details:"";item.Reason.Visible=item.Reason.Text.Length>0;
            item.Progress.Visible=choice.Command?.Kind==ActionKind.StopHarvest;
        }
        _more.Visible=choices.Length>primary.Length;
        _more.Text="⋯";_more.TooltipText=MoreHint();_dismiss.TooltipText=PlayerText.Local(language,"Свернуть действия","Collapse actions");
        string feedback=string.Join(" · ",new[]{status}.Concat(choices.Where(c=>primary.Contains(c.Key)&&!c.Enabled).Select(c=>c.Details)).Where(t=>t.Length>0).Distinct());
        _scroll.Visible=Expanded;_status.Text=feedback;_status.TooltipText=feedback;_status.Visible=feedback.Length>0;
        _heightTop=top;
        ApplyCollapsed();LayoutRows();
    }
    private void LayoutRows()
    {
        if(!IsInsideTree())return;
        float maximum=GetViewportRect().Size.X-40;
        float width=Math.Max(90,Math.Min(Size.X-16,maximum));
        float Natural(Button button)=>Math.Clamp(button.GetThemeFont("font").GetStringSize(button.Text,fontSize:button.GetThemeFontSize("font_size")).X+28,90,280);
        foreach(var item in _rows.Values)
        {
            bool primary=item.Row.GetParent()==_primary;
            float rowWidth=Math.Min(Natural(item.Button),Math.Max(90,width-112));
            item.Button.AutowrapMode=TextServer.AutowrapMode.WordSmart;item.Button.ClipText=false;
            item.Button.TextOverrunBehavior=TextServer.OverrunBehavior.NoTrimming;
            item.Button.CustomMinimumSize=new(rowWidth,44);item.Row.CustomMinimumSize=new(rowWidth,0);
            item.Reason.CustomMinimumSize=new(rowWidth,0);item.Reason.Visible=!primary&&item.Reason.Text.Length>0;
            item.Button.SizeFlagsHorizontal=SizeFlags.ExpandFill;item.Button.Alignment=HorizontalAlignment.Center;
        }
        FitListHeight();
    }
    private void FitListHeight()
    {
        if(_fittingHeight||!Expanded||!IsInsideTree())return;_fittingHeight=true;
        try
        {
            var body=_body;var visible=body.GetChildren().OfType<Control>().Where(c=>c.Visible).ToArray();
            float chrome=GetThemeStylebox("panel").GetMinimumSize().Y+body.GetThemeConstant("separation")*Math.Max(0,visible.Length-1);
            foreach(var child in visible.Where(c=>c!=_scroll))chrome+=child.GetCombinedMinimumSize().Y;
            // Include the actual wrapped summary, primary rows and result footer. The old
            // fixed allowance missed their extra line after a state change in a narrow window.
            float available=GetViewportRect().Size.Y-_heightTop-(float)UiSafeArea.Read(this).Bottom-24;
            float content=_list.GetCombinedMinimumSize().Y;
            _scroll.CustomMinimumSize=new(0,Math.Min(content,Math.Max(44,available-chrome-16)));
        }
        finally{_fittingHeight=false;}
    }
    private string MoreHint()=>PlayerText.Local(_language,Expanded?"Свернуть действия":"Все действия",Expanded?"Collapse actions":"All actions")+" · "+Math.Max(0,Selection.Choices.Length-Selection.PrimaryKeys().Length);
    public void Navigate(int delta)
    {
        Reopen();
        Selection.Cycle(delta);
        if(_rows.TryGetValue(Selection.SelectedKey,out var row))
        {
            if(row.Row.GetParent()!=_primary)SetExpanded(true);
            row.Button.GrabFocus();UiNavigation.Reveal(row.Button);
        }
    }
    public void ConfirmSelected()
    {if(Dismissed){Reopen();return;}var choice=Selection.Choices.FirstOrDefault(c=>c.Key==Selection.SelectedKey);if(choice is not null)Requested?.Invoke(choice);}
    public void ShowProgress(double percent){foreach(var row in _rows.Values)if(row.Progress.Visible)row.Progress.Value=percent;}
    public static string ButtonName(string key)=>"Context_"+key.Replace(':','_');
}

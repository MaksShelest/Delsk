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
    public bool Pressing=>_more.ButtonPressed||_rows.Values.Any(r=>r.Button.ButtonPressed);
    private readonly HFlowContainer _primary=new();
    private readonly VBoxContainer _list=new();
    private readonly ScrollContainer _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};
    private readonly Button _more=new(){Name="ContextMore",CustomMinimumSize=new(0,44),SizeFlagsHorizontal=SizeFlags.ShrinkBegin};
    private readonly Label _status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Label _summary=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart,MouseFilter=MouseFilterEnum.Ignore};
    private readonly Dictionary<string,(VBoxContainer Row,Button Button,Label Reason,ProgressBar Progress)> _rows=[];
    private readonly Dictionary<string,VBoxContainer> _groups=[];
    private string _language="en";
    private int _touchIndex=-1,_touchScroll;
    private Vector2 _touchOrigin;
    private bool _touchDragged;
    public ContextActionsPanel()
    {
        Name="ContextActions";Theme=TabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("f6f8f2"),ContentMarginLeft=10,ContentMarginRight=10,ContentMarginTop=8,ContentMarginBottom=8,CornerRadiusTopLeft=10,CornerRadiusTopRight=10,CornerRadiusBottomLeft=10,CornerRadiusBottomRight=10});
        var body=new VBoxContainer();AddChild(body);body.AddChild(_summary);body.AddChild(_primary);body.AddChild(_more);body.AddChild(_scroll);_scroll.AddChild(_list);_list.SizeFlagsHorizontal=SizeFlags.ExpandFill;body.AddChild(_status);
        _more.Pressed+=()=>SetExpanded(!Expanded);
        Resized+=LayoutRows;
    }
    public void SetExpanded(bool value)
    {
        if(Selection.Expanded==value)return;
        Selection.Expanded=value;_scroll.Visible=value;if(!value){_touchIndex=-1;_touchDragged=false;}LayoutRows();ExpansionChanged?.Invoke(value);
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
        bool was=Expanded;_language=language;Selection.Update(scope,choices);Selection.Pending=pending;
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
                string group=ContextActions.Group(choice);
                if(!_groups.TryGetValue(group,out var target))
                {target=new VBoxContainer{Name="ContextGroup_"+group};target.AddChild(new Label{Text=ContextActions.GroupName(group,language),AutowrapMode=TextServer.AutowrapMode.WordSmart});_groups.Add(group,target);_list.AddChild(target);}
                host=target;
            }
            if(item.Row.GetParent()!=host){item.Row.GetParent()?.RemoveChild(item.Row);host.AddChild(item.Row);}
            int position=positions.GetValueOrDefault(host,host==_primary?0:1);positions[host]=position+1;
            if(!Pressing&&item.Row.GetIndex()!=position)host.MoveChild(item.Row,position);
            item.Button.Text=choice.Label;item.Button.Disabled=!choice.Enabled||pending&&choice.Command is not null;
            item.Button.TooltipText=choice.Label+"\n"+choice.Details;
            item.Reason.Text=!choice.Enabled?choice.Details:"";item.Reason.Visible=item.Reason.Text.Length>0;
            item.Progress.Visible=choice.Command?.Kind==ActionKind.StopHarvest;
        }
        foreach(var group in _groups.Values)group.Visible=group.GetChildCount()>1;
        _more.Visible=choices.Length>primary.Length;
        _more.Text=PlayerText.Local(language,Expanded?"Закрыть действия":"Ещё действия",Expanded?"Close actions":"More actions")+" · "+(choices.Length-primary.Length);
        _scroll.Visible=Expanded;_status.Text=status;_status.Visible=status.Length>0;
        float height=GetViewportRect().Size.Y;
        _scroll.CustomMinimumSize=new(0,Math.Clamp(height-top-_primary.GetCombinedMinimumSize().Y-150,60,280));
        Visible=choices.Length>0;LayoutRows();
    }
    private void LayoutRows()
    {
        if(!IsInsideTree())return;
        float width=Math.Max(90,Math.Min(Size.X-24,GetViewportRect().Size.X-56));
        foreach(var item in _rows.Values)
        {
            float measured=item.Button.GetThemeDefaultFont().GetStringSize(item.Button.Text,HorizontalAlignment.Left,-1,item.Button.GetThemeDefaultFontSize()).X+28;
            item.Button.CustomMinimumSize=new(Math.Min(width,Math.Max(90,measured)),44);
            item.Row.CustomMinimumSize=new(Math.Min(width,Math.Max(90,measured)),0);
            item.Reason.CustomMinimumSize=new(Math.Min(width,Math.Max(90,measured)),0);
        }
    }
    public void Navigate(int delta)
    {
        Selection.Cycle(delta);
        if(_rows.TryGetValue(Selection.SelectedKey,out var row))
        {
            if(row.Row.GetParent()!=_primary)SetExpanded(true);
            row.Button.GrabFocus();UiNavigation.Reveal(row.Button);
        }
    }
    public void ConfirmSelected()
    {var choice=Selection.Choices.FirstOrDefault(c=>c.Key==Selection.SelectedKey);if(choice is not null)Requested?.Invoke(choice);}
    public void ShowProgress(double percent){foreach(var row in _rows.Values)if(row.Progress.Visible)row.Progress.Value=percent;}
    public static string ButtonName(string key)=>"Context_"+key.Replace(':','_');
}

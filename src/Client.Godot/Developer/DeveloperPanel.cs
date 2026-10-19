using Godot;
using Simulation.Core;
using Simulation.Developer;
using Simulation.Hosting;

public partial class DeveloperPanel:PanelContainer
{
    public event System.Action? Closed;
    private readonly DeveloperTools _tools;
    private readonly GameSession _session;
    private readonly string _language;
    private readonly System.Action _refresh;
    private readonly OptionButton _actor=new(){FocusMode=FocusModeEnum.None};
    private readonly OptionButton _inventory=new(){FocusMode=FocusModeEnum.None};
    private readonly OptionButton _item=new(){FocusMode=FocusModeEnum.None};
    private readonly SpinBox _money=new(){MinValue=0,MaxValue=1000000000000,Step=1,CustomMinimumSize=new(180,0)};
    private readonly SpinBox _stock=new(){MinValue=0,MaxValue=1000000,Step=1,Value=1};
    private readonly SpinBox _exponent=new(){MinValue=1,MaxValue=5,Step=1,Value=3};
    private readonly SpinBox _target=new(){MinValue=1,MaxValue=1000000,Step=1,Value=600};
    private readonly SpinBox _count=new(){MinValue=1,MaxValue=100000,Step=1,Value=100};
    private readonly CheckButton _self=new(){FocusMode=FocusModeEnum.None};
    private readonly Label _status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly ItemList _log=new(){CustomMinimumSize=new(0,130)};
    private readonly TextEdit _detail=new(){Editable=false,CustomMinimumSize=new(0,260),WrapMode=TextEdit.LineWrappingMode.Boundary};
    private DecisionLogEntry[] _entries=[];
    private InventoryId[] _inventories=[];
    private string _inventoryKey="",_journalKey="";
    private string _notice="";
    public DeveloperPanel(DeveloperTools tools,GameSession session,ParticipantId human,string language,System.Action refresh)
    {
        _tools=tools;_session=session;_language=language;_refresh=refresh;
        AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("243a3e"),ContentMarginLeft=12,ContentMarginRight=12,ContentMarginTop=12,ContentMarginBottom=12});
        _detail.AddThemeColorOverride("font_readonly_color",new Color("e8eee8"));
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);OffsetLeft=45;OffsetRight=-45;OffsetTop=60;OffsetBottom=-25;
        var box=new VBoxContainer();AddChild(box);var header=new HBoxContainer();box.AddChild(header);
        header.AddChild(new Label{Text=L("DEVELOPER · партия исключена из чистых сравнений","DEVELOPER · excluded from clean comparisons"),SizeFlagsHorizontal=SizeFlags.ExpandFill});
        Button(header,L("Закрыть","Close"),()=>Closed?.Invoke());box.AddChild(_status);
        var tabs=new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill};box.AddChild(tabs);
        var scroll=new ScrollContainer{Name=L("Вмешательства","Interventions"),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};tabs.AddChild(scroll);
        var edit=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(edit);
        var actorRow=new HBoxContainer();edit.AddChild(actorRow);actorRow.AddChild(new Label{Text=L("Участник","Participant")});actorRow.AddChild(_actor);
        foreach(var p in session.Engine.Inspect().Participants)_actor.AddItem(p.Id.Value.ToString(),checked((int)p.Id.Value));
        _actor.ItemSelected+=_=>{_inventoryKey="";RefreshData();};
        Row(edit,L("Целевой денежный остаток (minor)","Target cash balance (minor)"),_money,()=>_tools.Money(Actor,(long)_money.Value));
        var goods=new HFlowContainer();edit.AddChild(goods);goods.AddChild(_inventory);goods.AddChild(_item);goods.AddChild(_stock);
        foreach(var item in session.Engine.Configuration().Items)_item.AddItem(item.Id);
        Button(goods,L("Установить запас (всего)","Set total stock target"),()=>Guard(()=>_tools.Stock(Actor,_inventories[_inventory.Selected],session.Engine.Configuration().Items[_item.Selected].Id,(long)_stock.Value)));
        Row(edit,L("Показатель привлекательности зарплаты (1–5)","Wage attraction exponent (1–5)"),_exponent,()=>_tools.WageExponent((int)_exponent.Value));
        edit.AddChild(new Label{Text=L("Только между тиками, без голосования. Резервы и занятый объём защищены; вместимость и unitVolume здесь не редактируются.","Only at tick boundaries, outside voting. Holds and occupied capacity stay protected; capacity and unitVolume cannot be edited here."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        _self.Text=L("Явная временная политика человека: голос за себя (с учётом договора)","Explicit temporary human policy: self ballot (respect signed support)");edit.AddChild(_self);
        Row(edit,L("Перемотать до тика","Advance to tick"),_target,()=>{_tools.StartAdvance((long)_target.Value,_self.ButtonPressed);return null;});
        Row(edit,L("На N тиков","Advance by N ticks"),_count,()=>{_tools.StartAdvance(checked(session.Engine.Tick+(long)_count.Value),_self.ButtonPressed);return null;});
        Button(edit,L("Остановить перемотку","Cancel advance"),()=>{_tools.CancelAdvance();RefreshData();});
        var journal=new VBoxContainer{Name=L("Решения NPC","NPC decisions")};tabs.AddChild(journal);journal.AddChild(_log);journal.AddChild(_detail);
        _log.ItemSelected+=index=>ShowEntry((int)index);
    }
    private ParticipantId Actor=>new(_actor.GetSelectedId());
    private void Row(Container parent,string label,Control input,System.Func<ResearchModification?> action)
    {var row=new HFlowContainer();parent.AddChild(row);row.AddChild(new Label{Text=label});row.AddChild(input);Button(row,L("Применить","Apply"),()=>Guard(()=>action()));}
    private void Guard(System.Action action)
    {try{action();_notice=L("Выполнено; запись добавлена в журнал.","Applied; audit entry recorded.");_refresh();}catch(Exception e){_notice=e.Message;}RefreshData();}
    private static void Button(Container parent,string title,System.Action action)
    {var b=new Button{Text=title,FocusMode=FocusModeEnum.None};b.Pressed+=action;parent.AddChild(b);}
    public void RefreshData(bool updateStatus=true)
    {
        if(updateStatus)_status.Text=$"Tick {_session.Engine.Tick} · {_tools.AdvanceStatus} · target {_tools.AdvanceTarget?.ToString()??"—"} · edits {_session.Engine.ResearchModifications.Length}\n{_notice}";
        var stocks=_session.Engine.Inspect().Inventories.Where(i=>i.OwnerId==Actor).ToArray();string key=string.Join(";",stocks.Select(i=>i.Id.Value));
        if(key!=_inventoryKey){_inventoryKey=key;_inventories=stocks.Select(i=>i.Id).ToArray();_inventory.Clear();foreach(var i in stocks)_inventory.AddItem("Inventory #"+i.Id.Value);}
        var entries=_tools.Journal;string journalKey=entries.Length+":"+(entries.LastOrDefault()?.Tick??-1)+":"+(entries.LastOrDefault()?.Actions??"");
        if(journalKey!=_journalKey)
        {
            _journalKey=journalKey;_entries=entries;_log.Clear();foreach(var e in entries)_log.AddItem($"{e.Tick} · NPC {e.Actor} · {e.Goal} · step {e.PlanStep}");
            if(entries.Length>0){_log.Select(entries.Length-1);ShowEntry(entries.Length-1);}
        }
    }
    private void ShowEntry(int index)
    {
        if(index<0 || index>=_entries.Length)return;var e=_entries[index];
        _detail.Text=$"Tick {e.Tick} · actor {e.Actor} · goal {e.Goal} · reason {e.Reason} · step {e.PlanStep}\nCandidates / scores:\n{e.Candidates}\nActions:\n{e.Actions}\nCommand results / refusal reasons:\n{e.Results}\nObservation:\n{e.Observation}\nModel input:\n{e.ModelRequest}\nModel response:\n{e.ModelReply}\nLatency: {e.Latency} ms · Fallback: {e.Fallback}";
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

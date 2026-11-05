using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>One cell, live Core quote and ordinary commands; no client economic rules.</summary>
public partial class RoadWorkCard : PanelContainer
{
    public event Action? CloseRequested;
    public event Action<Cell?>? SelectionChanged;
    private readonly long _vehicle;
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly Action<GameAction> _submit;
    private readonly Func<Cell,EquipmentMode,RoadCellQuote> _quote;
    private readonly OptionButton _cell=new(){Name="RoadWorkCell"},_mode=new(){Name="RoadWorkMode"},_source=new(){Name="RoadMaterialSource"},_material=new(){Name="RoadMaterial"};
    private readonly Label _details=Text(),_stock=Text(),_status=Text(),_result=Text();
    private readonly ProgressBar _progress=new(){Name="RoadWorkProgress",MinValue=0,MaxValue=100,CustomMinimumSize=new(0,18),ShowPercentage=true};
    private readonly Button _start=new(){Name="RoadCellStart"},_pause=new(){Name="RoadCellPause"},_go=new(){Name="RoadCellGo"},_load=new(){Name="RoadMaterialLoad"};
    private readonly Button _wage=new(){Name="RoadCrewWage"};
    private readonly Button _board=new(){Name="RoadPaverBoard"};
    private readonly VBoxContainer _loadBlock=new();
    private readonly SpinBox _quantity=new(){Name="RoadMaterialQuantity",MinValue=1,MaxValue=70,Step=1,Value=1,UpdateOnTextChanged=true,CustomMinimumSize=new(78,44)};
    private readonly ScrollContainer _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Auto,SizeFlagsVertical=SizeFlags.ExpandFill};
    private Cell[] _cells=[];private long[] _sources=[];private PlayerView? _view;private RoadCellQuote? _current;private bool _pending;
    private string _cellsKey="",_sourcesKey="";
    public Cell? Selected=>_cell.Selected>=0&&_cell.Selected<_cells.Length?_cells[_cell.Selected]:null;
    private EquipmentMode Mode=>_mode.Selected==0?EquipmentMode.Personal:EquipmentMode.Automatic;
    public RoadWorkCard(ScenarioDefinition scenario,string language,long vehicle,Action<GameAction> submit,Func<Cell,EquipmentMode,RoadCellQuote> quote)
    {
        Name="RoadWorkCard";_scenario=scenario;_language=language;_vehicle=vehicle;_submit=submit;_quote=quote;
        var theme=(Theme)TabletDesign.Theme.Duplicate(true);theme.DefaultFontSize=15;
        Texture2D SmallIcon(string name){var icon=GD.Load<Texture2D>("res://tablets_v1/assets/icons/"+name+".svg").GetImage();icon.Resize(18,18,Image.Interpolation.Lanczos);return ImageTexture.CreateFromImage(icon);}
        theme.SetIcon("arrow","OptionButton",SmallIcon("down"));theme.SetIcon("updown","SpinBox",SmallIcon("stepper"));Theme=theme;
        AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new("eef0df"),BorderColor=new("416257"),BorderWidthLeft=2,BorderWidthRight=2,BorderWidthTop=2,BorderWidthBottom=2,
            ContentMarginLeft=12,ContentMarginRight=12,ContentMarginTop=10,ContentMarginBottom=10,CornerRadiusTopLeft=8,CornerRadiusTopRight=8,CornerRadiusBottomLeft=8,CornerRadiusBottomRight=8});
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",6);AddChild(box);
        var header=TabletDesign.Flow();var heading=TabletDesign.Heading(L("Дорожные работы","Road works"),20);heading.AutowrapMode=TextServer.AutowrapMode.Off;header.AddChild(heading);
        var close=new Button{Name="RoadCellClose",Text=L("Закрыть","Close")};close.Pressed+=()=>CloseRequested?.Invoke();header.AddChild(close);box.AddChild(header);
        var content=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};_scroll.AddChild(content);box.AddChild(_scroll);
        var choice=TabletDesign.Flow();choice.AddChild(_cell);choice.AddChild(_mode);content.AddChild(choice);
        _mode.AddItem(L("Лично · 50%","Personal · 50%"));_mode.AddItem(L("Автоматика","Automatic"));
        _cell.ItemSelected+=_=>Update();_mode.ItemSelected+=_=>Update();
        content.AddChild(TabletDesign.Art("truck",new(72,72)));content.AddChild(_details);content.AddChild(_stock);content.AddChild(_progress);content.AddChild(_status);content.AddChild(_result);
        var actions=TabletDesign.Flow();_start.Text=L("Начать строительство","Start construction");_pause.Text=L("Приостановить","Pause");_go.Text=L("К участку","Go to cell");
        foreach(var b in new[]{_start,_pause,_go})actions.AddChild(b);content.AddChild(actions);
        _wage.Text=L("Повысить ставку группы","Raise crew wage");actions.AddChild(_wage);
        _wage.Pressed+=()=>{if(!_wage.Disabled&&Work is {} w&&!_pending)Send(PlayerDesk.Wage(w.Group.Id.Value,(w.Group.PendingWageMinor??w.Group.WageMinor)+(_view?.Economy?.PlayerWageStepMinor??PlayerDesk.WageStepMinor)));};
        _start.AddThemeColorOverride("font_color",new Color("16533b"));_start.Pressed+=Start;_pause.Pressed+=Pause;
        actions.AddChild(_board);_board.Pressed+=()=>{if(_view?.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Id==_vehicle) is {} v)Send(new(v.Vehicle.Driver==0?ActionKind.BoardVehicle:ActionKind.LeaveVehicle,VehicleId:_vehicle));};
        _go.Pressed+=()=>
        {
            if(Selected is not {} cell||_view is null)return;
            if(Mode==EquipmentMode.Automatic&&_view.Participant.Position==cell)
                cell=_cells.FirstOrDefault(c=>c!=cell&&!_view.Blocked.Contains(c),cell);
            Send(PlayerDesk.Move(cell.X,cell.Z));
        };
        content.AddChild(new UiDisclosure(L("Загрузка материалов","Load materials"),_loadBlock,true));_loadBlock.AddThemeConstantOverride("separation",4);
        _loadBlock.AddChild(new HSeparator());_loadBlock.AddChild(Text(L("Загрузить из своих запасов рядом","Load from your nearby stock")));
        var load=TabletDesign.Flow();foreach(var c in new Control[]{_source,_material,_quantity,_load})load.AddChild(c);_loadBlock.AddChild(load);
        foreach(string item in scenario.Economy!.Roads!.Stages.SelectMany(s=>s.Materials).Select(m=>m.ItemId).Distinct())
        {_material.AddItem(PlayerText.Item(language,item));_material.SetItemMetadata(_material.ItemCount-1,item);}
        _load.Text=L("Загрузить","Load");_load.Pressed+=Load;_source.ItemSelected+=_=>Update();_material.ItemSelected+=_=>Update();_quantity.ValueChanged+=_=>Update();
        foreach(var option in new[]{_cell,_mode,_source,_material}){option.FitToLongestItem=false;option.ClipText=true;}
        _cell.CustomMinimumSize=new(126,44);_mode.CustomMinimumSize=new(170,44);_source.CustomMinimumSize=new(160,44);_material.CustomMinimumSize=new(140,44);
        foreach(var button in new[]{_start,_pause,_go,_load,_board,_wage,close})button.CustomMinimumSize=new(0,44);
    }
    private static Label Text(string value="")=>new(){Text=value,AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private string Reason(string code)=>code switch
    {
        "NoStaff" when Work?.Group.Headcount>0=>L("Работники наняты; ожидается рабочий тик","Crew hired; waiting for a work tick"),
        "NoStaff" or "Ready"=>TabletText.Reason(_language,code),
        "InvalidMode"=>L("Выберите личный или автоматический режим","Choose personal or automatic mode"),
        _=>PlayerText.Reason(_language,code)
    };
    private string Surface(int level)=>level switch{0=>L("Незастроенный коридор","Unbuilt corridor"),1=>L("Грунтовка","Dirt road"),2=>L("Дорога I","Road I"),3=>L("Магистраль II","Highway II"),_=>"—"};
    private RoadWork? Work=>_view?.Roads?.OwnWorks.FirstOrDefault(w=>w.VehicleId==_vehicle);
    public void ShowData(PlayerView view,bool pending,string result="")
    {
        _view=view;_pending=pending;_result.Text=result;_result.Visible=result.Length>0;
        var vehicle=view.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Id==_vehicle);if(vehicle is null){CloseRequested?.Invoke();return;}
        var at=vehicle.Position;var work=Work;
        var cells=work is {Route.Length:>0}&&(work.Enabled||work.Batch is not null||work.Reason=="PaverPositioningPaused")?[work.Route[Math.Min(work.Cursor,work.Route.Length-1)]]:
            new[]{at,new Cell(at.X+1,at.Z),new(at.X,at.Z+1),new(at.X-1,at.Z),new(at.X,at.Z-1)}.Where(c=>_scenario.Layout!.RoadCorridorCells.Contains(c)).ToArray();
        string key=string.Join(';',cells);if(key!=_cellsKey){var selected=Selected;_cellsKey=key;_cells=cells;_cell.Clear();foreach(var c in cells)_cell.AddItem($"X: {c.X}, Y: {c.Z}");int index=selected is {} old?Array.IndexOf(cells,old):-1;if(index>=0)_cell.Select(index);}
        if(work is not null&&(work.Enabled||work.Batch is not null))_mode.Select(work.Mode==EquipmentMode.Automatic?1:0);
        var sources=view.Inventories.Where(i=>i.Id!=vehicle.Vehicle.CargoInventory&&i.Position==at&&i.Position==view.Participant.Position).ToArray();
        string sk=string.Join(',',sources.Select(i=>i.Id.Value));if(sk!=_sourcesKey){_sourcesKey=sk;_source.Clear();_sources=sources.Select(i=>i.Id.Value).ToArray();foreach(var i in sources)_source.AddItem((i.Id==view.Participant.InventoryId?L("Рюкзак","Backpack"):L("Склад","Storage"))+" #"+i.Id.Value);}
        Update();
    }
    private void Update()
    {
        if(_view is null)return;var work=Work;var selected=Selected;SelectionChanged?.Invoke(selected);
        _current=selected is {} cell?_quote(cell,Mode):null;var q=_current;
        var cargo=_view.Inventories.First(i=>i.Id==_view.Fleet!.Vehicles.First(v=>v.Vehicle.Id==_vehicle).Vehicle.CargoInventory);
        bool active=work is {Enabled:true}||work?.Batch is not null||work?.Reason=="PaverPositioningPaused";
        _progress.Visible=work?.Batch is not null;_loadBlock.Visible=!active;_go.Visible=_board.Visible=!active;
        _cell.Disabled=_mode.Disabled=active||_pending;
        _details.Text=q is null?L("Не выбран участок.","Select a cell."):$"{Surface(q.CurrentLevel)} → {(q.Next is null?"—":Surface(q.Next.Level))}\n"+
            (q.Next is null?"":PlayerText.Money(q.Next.PriceMinor)+L(" монет · "," coins · ")+(q.ExpectedTicks is {} ticks?GameDuration.Format(ticks,_language):"—"));
        var order=_view.Roads?.Orders.FirstOrDefault(o=>o.Cell==selected&&o.Level==q?.Next?.Level&&o.PaidTick==0);if(order is not null)_details.Text+="\n"+L("Городской заказ · награда ","City order · reward ")+PlayerText.Money(order.Reward)+L(" монет"," coins")+(order.Assignee==0?L(" · примите в разделе дорог"," · accept in Roads"):order.Assignee==_view.Participant.Id.Value?L(" · принят вами"," · accepted by you"):L(" · принят другим участником"," · assigned to another participant"));
        _stock.Text=L("Тракт-Д1 #","Trakt-D1 #")+_vehicle+L(" · запас "," · cargo ")+$"{cargo.OccupiedVolume}+{cargo.ReservedCapacity}/{cargo.Capacity}\n"+
            L("Нужно / в кузове: ","Required / in cargo: ")+string.Join(" · ",(q?.Materials??[]).Select(m=>PlayerText.Item(_language,m.ItemId)+$" {m.Required}/{m.Available}"+ (m.Available<m.Required?L(" · загрузить "," · load ")+(m.Required-m.Available):"")));
        if(work?.Batch is not null)_stock.Text=L("Тракт-Д1 #","Trakt-D1 #")+_vehicle+L(" · запас "," · cargo ")+$"{cargo.OccupiedVolume}+{cargo.ReservedCapacity}/{cargo.Capacity}\n"+
            L("Материалы этой работы уже списаны. ","Materials for this work have been consumed. ")+string.Join(" · ",cargo.Stock.Select(s=>PlayerText.Item(_language,s.ItemId)+" ×"+s.Quantity));
        if(work?.Batch is {} batch)
        {
            long required=2L*_view.Roads!.Rules.Stages.Single(s=>s.Level==batch.Level).Ticks;
            int staff=work.Mode==EquipmentMode.Personal?1:work.Group.Headcount;
            _progress.Value=100d*batch.Work/required;
            _status.Text=Surface(batch.Level)+" · "+Reason(work.Reason)+L(" · осталось: "," · remaining: ")+(staff==0?"—":((required-batch.Work+staff-1)/staff).ToString())+L(" тиков"," ticks");
        }
        else{_progress.Value=0;_status.Text=q?.Reason=="Allowed"&&!active?L("Готово к запуску","Ready to start"):Reason(active?work!.Reason:q?.Reason??"NotAvailable");}
        _status.Text+=Mode==EquipmentMode.Personal?L(" · управляете вы"," · operated by you"):L(" · работников "," · workers ")+(work?.Group.Headcount??0)+"/2";
        if(Mode==EquipmentMode.Automatic&&!active)_status.Text+="\n"+L("Выйдите и отойдите; затем «Нанять и запустить».","Leave and step aside, then hire and start.");
        _wage.Visible=Mode==EquipmentMode.Automatic;_wage.Disabled=_pending||work is null;
        if(Mode==EquipmentMode.Automatic&&work is not null)_status.Text+=" · "+PlayerText.Money(work.Group.WageMinor)+L("/работник/рабочий тик","/worker/working tick");
        _start.Text=work is {Enabled:false}&&(work.Batch is not null||work.Reason=="PaverPositioningPaused")?L("Продолжить","Resume"):
            q?.Reason=="NoStaff"?L("Нанять и запустить","Hire and start"):L("Начать строительство","Start construction");
        _start.Disabled=_pending||(active?work!.Enabled:q?.CanStart!=true);
        _pause.Disabled=_pending||work is not {Enabled:true};_pause.Text=work?.Batch is null?L("Отменить запуск","Cancel start"):L("Приостановить","Pause");
        var vehicle=_view.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_vehicle);
        _board.Text=vehicle.Vehicle.Driver==0?L("Сесть","Board"):L("Выйти","Leave");
        _board.Disabled=_pending||active||_view.Participant.Busy||_view.Participant.Position!=cargo.Position;
        _go.Text=Mode==EquipmentMode.Automatic?L("Отойти","Step aside"):L("К участку","Go to cell");
        _go.Disabled=_pending||active||selected is null||(Mode==EquipmentMode.Automatic?vehicle.Vehicle.Driver!=0||_view.Participant.Position!=cargo.Position:cargo.Position==selected);
        _load.Disabled=_pending||_view.Participant.Busy||active||LoadAction() is null;
    }
    private void Send(GameAction action){if(_pending)return;_pending=true;Update();_submit(action);}
    public void FitHeight(float height){CustomMinimumSize=new(0,0);Size=new(Size.X,height);_scroll.CustomMinimumSize=new(0,Math.Max(80,height-80));if(Size.X>0)GameUi.FitControls(this,Size.X-28);}
    private void Start()
    {
        if(_pending||_start.Disabled)return;var w=Work;
        if(w is {Enabled:false}&&(w.Batch is not null||w.Reason=="PaverPositioningPaused"))
            Send(new(ActionKind.SetRoadCellEnabled,VehicleId:_vehicle,Enabled:true));
        else if(_current is {CanStart:true,Next:{} rule} q)Send(new(ActionKind.StartRoadCell,Target:q.Cell,VehicleId:_vehicle,EquipmentLevel:rule.Level,Mode:Mode));
    }
    private void Pause()
    {
        if(_pending||Work is not {} w)return;
        Send(new(ActionKind.SetRoadCellEnabled,VehicleId:_vehicle,Enabled:false));
    }
    private GameAction? LoadAction()
    {
        if(_view is null||_source.Selected<0||_source.Selected>=_sources.Length||_material.Selected<0)return null;
        var source=_view.Inventories.Single(i=>i.Id.Value==_sources[_source.Selected]);var vehicle=_view.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_vehicle);
        var cargo=_view.Inventories.Single(i=>i.Id==vehicle.Vehicle.CargoInventory);string item=_material.GetItemMetadata(_material.Selected).AsString();long quantity=(long)_quantity.Value;
        long available=source.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
        return source.Position==cargo.Position&&source.Position==_view.Participant.Position&&available>=quantity&&
            (Int128)quantity*_scenario.Items.Single(i=>i.Id==item).UnitVolume<=cargo.Capacity-cargo.OccupiedVolume-cargo.ReservedCapacity?
            PlayerDesk.Transfer(source.Id.Value,cargo.Id.Value,item,quantity):null;
    }
    private void Load(){if(!_load.Disabled&&!_pending&&LoadAction() is {} a)Send(a);}
}

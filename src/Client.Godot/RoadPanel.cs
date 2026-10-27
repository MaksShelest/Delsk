using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Collections.Immutable;

/// <summary>Ordinary E2 commands and own/public observations; no private world access.</summary>
public partial class RoadPanel : VBoxContainer
{
    private readonly string _language;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly OptionButton _paver=new(){Name="RoadPaver"},_level=new(){Name="RoadLevel"},_mode=new(){Name="RoadMode"};
    private readonly LineEdit _route=new(){Name="RoadRoute",PlaceholderText="10,1; 11,1",SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly Label _state=Wrap(),_cost=Wrap(),_contributions=Wrap(),_error=Wrap();
    private readonly Button _start=new(){Name="RoadStart"},_stop=new(){Name="RoadStop"},_turn=new(){Name="RoadTurn"};
    private readonly SpinBox _wage=new(){Name="RoadWage",MinValue=0,MaxValue=100000,Step=5,Value=20};
    private readonly Button _pay=new(){Name="RoadSetWage"};
    private readonly VBoxContainer _orders=new();
    private long[] _ids=[];private string _key="",_orderKey="";private PlayerView? _view;
    public RoadPanel(string language,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name="RoadPanel";_language=language;_submit=submit;_access=access;
        AddChild(TabletDesign.Heading(L("Дороги и движение","Roads and traffic")));
        AddChild(Wrap(L("Выберите Тракт, ступень и маршрут x,z; x,z. Материалы нужны в его кузове. Для личной работы сядьте, для автоматики выйдите и наймите группу у машины.","Select a Trakt, stage and route x,z; x,z. Materials must be in its cargo. Board for personal work; leave and hire its crew for automatic work.")));
        var row=TabletDesign.Flow();row.AddChild(_paver);row.AddChild(_level);row.AddChild(_mode);AddChild(row);
        foreach(string text in new[]{L("Грунтовка · 20","Dirt · 20"),L("Дорога I · 40","Road I · 40"),L("Магистраль II · 60","Highway II · 60")})_level.AddItem(text);
        _mode.AddItem(L("Лично · 50%","Personal · 50%"));_mode.AddItem(L("Автоматика · 1–2 рабочих","Automatic · 1–2 workers"));
        AddChild(_route);AddChild(_cost);AddChild(_state);AddChild(_contributions);AddChild(_error);
        var buttons=TabletDesign.Flow();_start.Text=L("Начать / продолжить","Start / resume");_stop.Text=L("Остановить работы","Pause work");_turn.Text=L("Развернуться","Turn around");
        foreach(var b in new[]{_start,_stop,_turn})buttons.AddChild(b);AddChild(buttons);
        var wages=TabletDesign.Flow();_pay.Text=L("Ставка группы · сотые монеты","Crew wage · minor coins");wages.AddChild(_wage);wages.AddChild(_pay);AddChild(wages);
        _pay.Pressed+=()=>{if(Selected>0)_submit(new(ActionKind.SetWage,Group:new(Selected),WageMinor:(long)_wage.Value));};
        _start.Pressed+=()=>Send(true);_stop.Pressed+=()=>Send(false);_turn.Pressed+=()=>{if(Selected>0)_submit(new(ActionKind.TurnVehicle,VehicleId:Selected));};
        AddChild(Wrap(L("Съезд: удерживайте направление 1 тик, отпускание отменяет. Разворот — только в свободном месте. После 30 тиков в проезде появится предупреждение, после 60 помощник ищет законный путь в карман.","Exit: hold a direction for 1 tick; releasing cancels. Turn only with free space. A passage warning appears after 30 ticks; after 60 the assistant seeks a legal path to a bay.")));
        AddChild(_orders);
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private static Label Wrap(string text="")=>new(){Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private long Selected=>_paver.Selected>=0&&_paver.Selected<_ids.Length?_ids[_paver.Selected]:0;
    private ImmutableArray<Cell> Route()
    {
        var cells=_route.Text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(part=>part.Split(',',StringSplitOptions.TrimEntries)).ToArray();
        if(cells.Length is <1 or >64||cells.Any(p=>p.Length!=2||!int.TryParse(p[0],out _)||!int.TryParse(p[1],out _)))throw new FormatException();
        return [..cells.Select(p=>new Cell(int.Parse(p[0]),int.Parse(p[1])))];
    }
    private void Send(bool enabled)
    {
        try
        {
            var work=_view?.Roads?.OwnWorks.FirstOrDefault(w=>w.VehicleId==Selected);
            var action=new GameAction(ActionKind.SetRoadWork,VehicleId:Selected,Enabled:enabled,
                Mode:work?.Batch is not null?work.Mode:_mode.Selected==0?EquipmentMode.Personal:EquipmentMode.Automatic,
                EquipmentLevel:work?.Batch is not null?work.Level:_level.Selected+1,Stops:work?.Batch is not null?work.Route:Route());
            _error.Text="";_submit(action);
        }
        catch(FormatException){_error.Text=L("Маршрут: целые координаты x,z; x,z, до 64 соседних клеток.","Route: integer coordinates x,z; x,z, up to 64 adjacent cells.");}
    }
    public void ShowData(PlayerView view,bool pending,bool paused)
    {
        _view=view;if(view.Roads is not {} roads||view.Fleet is not {} fleet){Visible=false;return;}
        var vehicles=fleet.Vehicles.Where(v=>v.Model.MaterialsOnly).ToArray();string key=string.Join(',',vehicles.Select(v=>v.Vehicle.Id));
        if(key!=_key){long previous=Selected;_key=key;_paver.Clear();_ids=vehicles.Select(v=>v.Vehicle.Id).ToArray();foreach(var v in vehicles)_paver.AddItem("Тракт-Д1 #"+v.Vehicle.Id);int selected=Array.IndexOf(_ids,previous);if(selected>=0)_paver.Select(selected);}
        var selectedVehicle=vehicles.FirstOrDefault(v=>v.Vehicle.Id==Selected);var work=roads.OwnWorks.FirstOrDefault(w=>w.VehicleId==Selected);
        if(_route.Text.Length==0&&selectedVehicle is not null)_route.Text=selectedVehicle.Position.X+","+selectedVehicle.Position.Z;
        var rule=roads.Rules.Stages[_level.Selected];
        _cost.Text=L("На клетку: ","Per cell: ")+rule.PriceMinor/100m+L(" монет · "," coins · ")+string.Join(", ",rule.Materials.Select(m=>m.Quantity+" "+PlayerText.Item(_language,m.ItemId)))+" · "+rule.Ticks+L(" тиков при 2 рабочих; ×2 лично/при одном."," ticks with 2 workers; ×2 personal/with one.");
        var traffic=roads.OwnTraffic.FirstOrDefault(t=>t.VehicleId==Selected);
        _state.Text=selectedVehicle is null?L("Сначала приобретите Тракт в автосалоне.","Purchase a Trakt at the dealer first."):
            L("Место ","Position ")+selectedVehicle.Position.X+","+selectedVehicle.Position.Z+" · "+PlayerText.Reason(_language,work?.Reason??traffic?.Reason??"Parked")+
            (work is null?"":$" · {work.Cursor}/{work.Route.Length} · "+L("работники ","workers ")+work.Group.Headcount+"/2"+(work.Batch is {} batch?$" · {batch.Work}/{2*roads.Rules.Stages[batch.Level-1].Ticks}":""));
        try{var cell=Route()[0];var road=roads.Cells.FirstOrDefault(c=>c.Cell==cell);_contributions.Text=L("Ступени / будущие права: ","Stages / future rights: ")+(road is null?L("нет покрытия","no surface"):string.Join(" → ",road.Contributions.Select(c=>c.Level+": "+(c.Builder.Value==0?L("город","city"):"#"+c.Builder.Value))));}catch(FormatException){_contributions.Text="";}
        _start.Disabled=_stop.Disabled=pending||Selected==0;
        _pay.Disabled=pending||work is null||!_access(new(ActionKind.SetWage,Group:new(Selected),WageMinor:(long)_wage.Value)).Allowed;
        _turn.Disabled=pending||Selected==0||!_access(new(ActionKind.TurnVehicle,VehicleId:Selected)).Allowed;
        string orderKey=string.Join(';',roads.Orders.Select(o=>$"{o.Id}/{o.Assignee}/{o.PaidTick}"));
        if(orderKey==_orderKey)return;_orderKey=orderKey;foreach(Node n in _orders.GetChildren()){_orders.RemoveChild(n);n.QueueFree();}
        foreach(var order in roads.Orders.Where(o=>o.PaidTick==0).Take(3))
        {
            var line=new VBoxContainer();line.AddChild(Wrap(L("Городской маршрут: ","City route: ")+$"{order.Cell.X},{order.Cell.Z} · {order.Level} · {order.Reward/100m}"+L(" монет"," coins")));
            var accept=new Button{Text=order.Assignee==0?L("Принять задание","Accept task"):L("Задание принято","Task accepted"),Disabled=order.Assignee!=0};
            accept.Pressed+=()=>_submit(new(ActionKind.AcceptRoadOrder,OfferId:order.Id));line.AddChild(accept);_orders.AddChild(line);
        }
    }
}

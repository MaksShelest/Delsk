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
    private readonly Label _state=Wrap(),_cost=Wrap(),_contributions=Wrap(),_error=Wrap(),_exit=Wrap();
    private readonly Button _start=new(){Name="RoadStart"},_stop=new(){Name="RoadStop"},_turn=new(){Name="RoadTurn"};
    private readonly SpinBox _wage=new(){Name="RoadWage",MinValue=0,MaxValue=1000,Step=.05,Value=.20};
    private readonly Button _pay=new(){Name="RoadSetWage"};
    private readonly VBoxContainer _orders=new();private readonly List<(Button Button,GameAction Action)> _orderButtons=[];
    private bool _pending;private long _selectedState;private long[] _ids=[];private string _key="",_orderKey="";private PlayerView? _view;
    public RoadPanel(string language,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name="RoadPanel";_language=language;_submit=submit;_access=access;
        AddChild(GameUi.Intro(L("Дороги и движение","Roads and traffic"),L("Для одной клетки откройте дорожную машину на карте. Здесь можно задать несколько соседних клеток и принять городской заказ.","Open a road machine on the map to work on one cell. Here you can set adjacent cells and accept a city order."),"truck",64));
        AddChild(Wrap(L("Выберите Тракт, ступень и маршрут x,z; x,z. Материалы нужны в его кузове. Для личной работы сядьте, для автоматики выйдите и наймите группу у машины.","Select a Trakt, stage and route x,z; x,z. Materials must be in its cargo. Board for personal work; leave and hire its crew for automatic work.")));
        var row=TabletDesign.Flow();row.AddChild(_paver);row.AddChild(_level);row.AddChild(_mode);AddChild(row);
        foreach(string text in new[]{L("Грунтовка · 20","Dirt · 20"),L("Дорога I · 40","Road I · 40"),L("Магистраль II · 60","Highway II · 60")})_level.AddItem(text);
        _mode.AddItem(L("Лично · 50%","Personal · 50%"));_mode.AddItem(L("Автоматика · 1–2 рабочих","Automatic · 1–2 workers"));
        AddChild(_state);AddChild(_cost);var advanced=new VBoxContainer();advanced.AddChild(GameUi.Hint(L("Маршрут: x,z; x,z (до 64 клеток)","Route: x,z; x,z (up to 64 cells)")));advanced.AddChild(_route);advanced.AddChild(_contributions);AddChild(new UiDisclosure(L("Маршрут и права на дорогу","Route & road rights"),advanced));AddChild(_error);
        var buttons=TabletDesign.Flow();_start.Text=L("Начать / продолжить","Start / resume");_stop.Text=L("Остановить работы","Pause work");_turn.Text=L("Развернуться","Turn around");
        GameUi.Primary(_start);foreach(var b in new[]{_start,_stop,_turn})buttons.AddChild(b);AddChild(buttons);
        var wages=TabletDesign.Flow();_pay.Text=L("Зарплата группы · монет/тик","Crew wage · coins/tick");wages.AddChild(_wage);wages.AddChild(_pay);AddChild(wages);
        _pay.Pressed+=()=>{if(Selected>0)Submit(new(ActionKind.SetWage,Group:new(Selected),WageMinor:(long)Math.Round(_wage.Value*100)));};
        _start.Pressed+=()=>Send(true);_stop.Pressed+=()=>Send(false);_turn.Pressed+=()=>{if(Selected>0)Submit(new(ActionKind.TurnVehicle,VehicleId:Selected));};
        AddChild(new UiDisclosure(L("Управление машиной","Vehicle controls"),_exit));
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
    private void Submit(GameAction action){if(_pending)return;_pending=true;_start.Disabled=_stop.Disabled=_turn.Disabled=_pay.Disabled=true;_submit(action);}
    private void Send(bool enabled)
    {
        try
        {
            var work=_view?.Roads?.OwnWorks.FirstOrDefault(w=>w.VehicleId==Selected);
            var action=new GameAction(ActionKind.SetRoadWork,VehicleId:Selected,Enabled:enabled,
                Mode:work?.Batch is not null?work.Mode:_mode.Selected==0?EquipmentMode.Personal:EquipmentMode.Automatic,
                EquipmentLevel:work?.Batch is not null?work.Level:_level.Selected+1,Stops:work?.Batch is not null?work.Route:Route());
            _error.Text="";Submit(action);
        }
        catch(FormatException){_error.Text=L("Маршрут: целые координаты x,z; x,z, до 64 соседних клеток.","Route: integer coordinates x,z; x,z, up to 64 adjacent cells.");}
    }
    public void ShowData(PlayerView view,bool pending,bool paused)
    {
        _view=view;_pending=pending;if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);if(view.Roads is not {} roads||view.Fleet is not {} fleet){Visible=false;return;}
        string hold=view.Economy?.VehicleControl is {} control?(control.ExitHoldQuanta/10m).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+L(" секунды"," seconds"):roads.Rules.ExitHoldTicks+L(" тик"," tick");
        _exit.Text=L("Съезд: удерживайте направление ","Exit: hold a direction for ")+hold+L("; отпускание отменяет. Разворот требует свободного места. Предупреждение через ","; releasing cancels. Turning needs free space. Warning after ")+roads.Rules.WarningTicks+L(" тиков; помощник через "," ticks; assistance after ")+roads.Rules.ReleaseTicks+L(" тиков."," ticks.");
        var vehicles=fleet.Vehicles.Where(v=>v.Model.MaterialsOnly).ToArray();string key=string.Join(',',vehicles.Select(v=>v.Vehicle.Id));
        if(key!=_key){long previous=Selected;_key=key;_paver.Clear();_ids=vehicles.Select(v=>v.Vehicle.Id).ToArray();foreach(var v in vehicles)_paver.AddItem("Тракт-Д1 #"+v.Vehicle.Id);int selected=Array.IndexOf(_ids,previous);if(selected>=0)_paver.Select(selected);}
        var selectedVehicle=vehicles.FirstOrDefault(v=>v.Vehicle.Id==Selected);var work=roads.OwnWorks.FirstOrDefault(w=>w.VehicleId==Selected);
        if(_selectedState!=Selected&&selectedVehicle is not null)
        {
            _selectedState=Selected;
            _route.Text=work is not null&&work.Route.Length>0?string.Join("; ",work.Route.Select(c=>c.X+","+c.Z)):selectedVehicle.Position.X+","+selectedVehicle.Position.Z;
            _level.Select(work is null?Math.Clamp(roads.Cells.FirstOrDefault(c=>c.Cell==selectedVehicle.Position)?.Level??0,0,2):work.Level-1);
            _mode.Select(work?.Mode==EquipmentMode.Automatic?1:0);
        }
        var rule=roads.Rules.Stages[_level.Selected];
        _cost.Text=L("На клетку: ","Per cell: ")+rule.PriceMinor/100m+L(" монет · "," coins · ")+string.Join(", ",rule.Materials.Select(m=>m.Quantity+" "+PlayerText.Item(_language,m.ItemId)))+" · "+rule.Ticks+L(" тиков при 2 рабочих; ×2 лично/при одном."," ticks with 2 workers; ×2 personal/with one.");
        var traffic=roads.OwnTraffic.FirstOrDefault(t=>t.VehicleId==Selected);
        _state.Text=selectedVehicle is null?L("Сначала приобретите Тракт в автосалоне.","Purchase a Trakt at the dealer first."):
            L("Место ","Position ")+selectedVehicle.Position.X+","+selectedVehicle.Position.Z+" · "+(work?.Reason=="NoStaff"?TabletText.Reason(_language,"NoStaff"):PlayerText.Reason(_language,work?.Reason??traffic?.Reason??"Parked"))+
            (work is null?"":$" · {work.Cursor}/{work.Route.Length} · "+L("работники ","workers ")+work.Group.Headcount+"/2"+(work.Batch is {} batch?$" · {batch.Work}/{2*roads.Rules.Stages[batch.Level-1].Ticks}":""));
        try{var cell=Route()[0];var road=roads.Cells.FirstOrDefault(c=>c.Cell==cell);_contributions.Text=L("Ступени / будущие права: ","Stages / future rights: ")+(road is null?L("нет покрытия","no surface"):string.Join(" → ",road.Contributions.Select(c=>c.Level+": "+(c.Builder.Value==0?L("город","city"):"#"+c.Builder.Value))));}catch(FormatException){_contributions.Text="";}
        _start.Disabled=_stop.Disabled=pending||Selected==0;
        _pay.Disabled=pending||work is null||!_access(new(ActionKind.SetWage,Group:new(Selected),WageMinor:(long)Math.Round(_wage.Value*100))).Allowed;
        _turn.Disabled=pending||Selected==0||!_access(new(ActionKind.TurnVehicle,VehicleId:Selected)).Allowed;
        string orderKey=string.Join(';',roads.Orders.Select(o=>$"{o.Id}/{o.Assignee}/{o.PaidTick}"));
        foreach(var entry in _orderButtons){var permission=_access(entry.Action);entry.Button.Disabled=pending||!permission.Allowed;entry.Button.TooltipText=permission.Allowed?"":GameUi.Reason(_language,permission.ReasonCode);}
        if(orderKey==_orderKey)return;_orderButtons.Clear();_orderKey=orderKey;foreach(Node n in _orders.GetChildren()){_orders.RemoveChild(n);n.QueueFree();}
        foreach(var order in roads.Orders.Where(o=>o.PaidTick==0).Take(3))
        {
            var line=new VBoxContainer();line.AddChild(Wrap(L("Городской маршрут: ","City route: ")+$"{order.Cell.X},{order.Cell.Z} · {order.Level} · {order.Reward/100m}"+L(" монет"," coins")));
            var accept=new Button{Text=order.Assignee==0?L("Принять задание","Accept task"):L("Задание принято","Task accepted"),Disabled=order.Assignee!=0};
            var action=new GameAction(ActionKind.AcceptRoadOrder,OfferId:order.Id);var permission=_access(action);accept.Disabled=pending||!permission.Allowed;line.AddChild(GameUi.Hint(permission.Allowed?L("Награда выплачивается за готовое покрытие.","The reward is paid for completed road surface."):GameUi.Reason(_language,permission.ReasonCode)));_orderButtons.Add((accept,action));accept.Pressed+=()=>Submit(action);line.AddChild(accept);_orders.AddChild(line);
        }
    }
}

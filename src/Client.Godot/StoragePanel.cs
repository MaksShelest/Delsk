using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own ground/warehouse view. All mutations use the existing authenticated command path.</summary>
public partial class StoragePanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;private readonly string _lang,_kind;private readonly long _id;private readonly bool _overflowOnly;
    private readonly Action<GameAction> _submit;private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _status=new(){Name="StorageStatus",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Label _progress=new(){Name="ConstructionProgress",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly SpinBox _quantity=new(){Name="StorageQuantity",MinValue=1,MaxValue=1000000,Value=1,Step=1,CustomMinimumSize=new(130,44)};
    private readonly VBoxContainer _actions=new();private string _signature="";
    private readonly List<(Button Button,Func<GameAction> Action,Func<PlayerView,bool> Allowed)> _buttons=[];
    private readonly Dictionary<long,Label> _deadlines=[];private PlayerView? _view;private bool _pending;
    public StoragePanel(ScenarioDefinition scenario,string language,string kind,long id,Action<GameAction> submit,Func<GameAction,ActionAccess> access,bool overflowOnly=false)
    {_overflowOnly=overflowOnly;_scenario=scenario;_lang=language;_kind=kind;_id=id;_submit=submit;_access=access;Name="StoragePanel";AddChild(_status);AddChild(_progress);AddChild(_quantity);AddChild(_actions);_quantity.ValueChanged+=_=>{if(_view is {} v)ShowData(v,_pending);};}
    private string L(string ru,string en)=>PlayerText.Local(_lang,ru,en);
    private long Q=>checked((long)_quantity.Value);
    private static long Free(InventoryView i,string item)=>i.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
    private long Space(InventoryView i,string item)=>Math.Max(0,i.Capacity-i.OccupiedVolume-i.ReservedCapacity)/_scenario.Items.Single(x=>x.Id==item).UnitVolume;
    private void Action(string label,Func<GameAction> action,Func<PlayerView,bool> allowed)
    {var b=new Button{Text=label,CustomMinimumSize=new(0,44),AutowrapMode=TextServer.AutowrapMode.WordSmart};b.Pressed+=()=>_submit(action());_actions.AddChild(b);_buttons.Add((b,action,allowed));}
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;
        var plot=_kind=="ground"?view.Plots.SingleOrDefault(p=>p.Id.Value==_id):null;
        var building=_kind=="ground"?view.Buildings.FirstOrDefault(b=>b.Position==plot?.Position&&b.Kind==BuildingKind.Warehouse):view.Buildings.SingleOrDefault(b=>b.Id.Value==_id);
        long? inventory=building?.InputId.Value??view.Storage?.Ground.SingleOrDefault(g=>g.PlotId==_id)?.InventoryId;
        Visible=inventory is not null;if(inventory is null)return;
        var stock=view.Inventories.Single(i=>i.Id.Value==inventory);var carrier=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);
        var work=view.Storage?.Construction.SingleOrDefault(c=>c.InventoryId==inventory);var overflow=stock.Overflow??[];
        long expected=view.Contracts?.Where(c=>c.Buyer==view.Participant.Id&&c.DestinationInventory==stock.Id&&c.ClosedTick is null).Sum(c=>checked(c.RemainingQuantity*_scenario.Items.Single(i=>i.Id==c.ItemId).UnitVolume))??0;
        long excess=overflow.Sum(l=>checked(l.Quantity*_scenario.Items.Single(i=>i.Id==l.ItemId).UnitVolume));
        _status.Text=(building is null?L("Пустая собственная клетка · только ручное хранение","Owned bare ground · manual storage only"):L("Склад · уровень ","Warehouse · level ")+building.Level)+$"\n{stock.OccupiedVolume+excess}/{stock.Capacity}"+L(" объёма · нормальный резерв "," volume · normal capacity held ")+stock.ReservedCapacity+L(" · ожидается объёма "," · expected volume ")+expected+
            (excess>0?L(" · под риском "," · at risk ")+excess:"")+"\n"+string.Join(" · ",stock.Stock.Where(s=>s.Quantity>0).Select(s=>PlayerText.Item(_lang,s.ItemId)+" ×"+s.Quantity+L(" (резерв "," (held ")+s.Reserved+")"));
        _status.Visible=!_overflowOnly||excess>0;_quantity.Visible=!_overflowOnly||overflow.Length>0;
        _progress.Text=work is null?"":L("Строительные работы: осталось ","Construction: remaining ")+(work.CompletesTick-view.Tick)+L(" активных тиков. Материалы и оплата уже внесены."," active ticks. Materials and payment already committed.");
        string signature=inventory+":"+building?.Level+":"+(work is not null)+":"+string.Join(",",stock.Stock.Select(s=>s.ItemId).Concat(carrier.Stock.Select(s=>s.ItemId)).Distinct().Order())+":"+string.Join(",",overflow.Select(l=>l.Id));
        if(signature!=_signature)
        {
            _signature=signature;foreach(Node child in _actions.GetChildren()){_actions.RemoveChild(child);child.QueueFree();}_buttons.Clear();_deadlines.Clear();
            InventoryView Current(PlayerView v)=>v.Inventories.Single(i=>i.Id.Value==inventory);
            InventoryView Pack(PlayerView v)=>v.Inventories.Single(i=>i.Id==v.Participant.InventoryId);
            if(!_overflowOnly)foreach(string item in stock.Stock.Select(s=>s.ItemId).Concat(carrier.Stock.Select(s=>s.ItemId)).Distinct().Order())
            {
                string key=item;
                Action(L("Выложить: ","Store: ")+PlayerText.Item(_lang,key),()=>new(ActionKind.Transfer,Source:carrier.Id,Destination:stock.Id,ItemId:key,Quantity:Q),v=>Free(Pack(v),key)>=Q&&Space(Current(v),key)>=Q);
                Action(L("Забрать: ","Take: ")+PlayerText.Item(_lang,key),()=>new(ActionKind.Transfer,Source:stock.Id,Destination:carrier.Id,ItemId:key,Quantity:Q),v=>Free(Current(v),key)>=Q&&Space(Pack(v),key)>=Q);
            }
            foreach(var lot in overflow)
            {
                long lotId=lot.Id;string item=lot.ItemId;var text=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};_actions.AddChild(text);_deadlines.Add(lotId,text);
                Action(L("Спасти излишек: ","Rescue overflow: ")+PlayerText.Item(_lang,item),()=>new(ActionKind.TakeOverflow,Source:stock.Id,OfferId:lotId,ItemId:item,Quantity:Q),v=>(Current(v).Overflow??[]).Any(l=>l.Id==lotId&&l.Quantity>=Q)&&Space(Pack(v),item)>=Q);
            }
            var type=_scenario.BuildingTypes!.Single(t=>t.Id=="warehouse");var next=type.Levels.SingleOrDefault(l=>l.Level==(building?.Level??0)+1);
            if(next is not null&&!_overflowOnly)
            {
                var cost=new Label{Text=PlayerText.Money(next.PriceMinor)+" + "+string.Join(", ",next.Materials.Select(m=>PlayerText.Item(_lang,m.ItemId)+" ×"+m.Quantity))+L(" · тиков "," · ticks ")+next.ConstructionTicks,AutowrapMode=TextServer.AutowrapMode.WordSmart};_actions.AddChild(cost);
                Action(building is null?L("Построить склад","Build warehouse"):L("Улучшить склад","Upgrade warehouse"),()=>building is null?new(ActionKind.Build,Target:stock.Position,BuildingTypeId:type.Id):new(ActionKind.UpgradeBuilding,Building:building.Id),v=>work is null&&v.AvailableCashMinor>=next.PriceMinor&&next.Materials.All(m=>Free(Current(v),m.ItemId)>=m.Quantity));
            }
        }
        foreach(var lot in overflow)if(_deadlines.TryGetValue(lot.Id,out var label))label.Text=PlayerText.Item(_lang,lot.ItemId)+" ×"+lot.Quantity+L(" · до потери "," · until loss ")+(lot.ExpiresTick-view.Tick)+L(" активных тиков"," active ticks");
        foreach(var entry in _buttons){var a=entry.Action();entry.Button.Disabled=pending||view.Participant.Busy||!entry.Allowed(view)||!_access(a).Allowed;}
    }
}

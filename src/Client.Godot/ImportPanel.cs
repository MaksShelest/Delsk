using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class ImportPanel : VBoxContainer
{
    private readonly string _language;private readonly Action<GameAction> _submit;private readonly ScenarioDefinition? _scenario;private readonly Func<GameAction,ActionAccess>? _access;
    private readonly Dictionary<string,(Label Label,SpinBox Quantity,Button Buy,Button Take,Button All,Label Why)> _rows=[];
    private readonly Label _status=GameUi.Hint("");private PlayerView? _view;private bool _pending;
    public ImportPanel(string language,Action<GameAction> submit,ScenarioDefinition? scenario=null,Func<GameAction,ActionAccess>? access=null)
    {
        Name="ImportPanel";_language=language;_submit=submit;_scenario=scenario;_access=access;_status.Name="ImportStatus";
        AddChild(GameUi.Intro(L("Импорт и получение","Import & pickup"),L("Купленный товар остаётся в вашем пункте выдачи у терминала. Заберите его в рюкзак и перевезите к месту работы.","Purchased goods stay in your pickup section at the terminal. Collect them into your backpack and transport them to work."),"market",64));AddChild(_status);
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private long Maximum(string item)
    {
        if(_view?.ProductionContent?.Pickup is not {} pickup)return 0;var source=_view.Inventories.Single(i=>i.Id==pickup);var pack=_view.Inventories.Single(i=>i.Id==_view.Participant.InventoryId);
        long stock=source.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved),volume=_scenario?.Items.Single(i=>i.Id==item).UnitVolume??1;
        return Math.Min(stock,Math.Max(0,pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity)/volume);
    }
    private void Send(GameAction action)
    {if(_pending||_access?.Invoke(action).Allowed==false)return;_pending=true;if(_view is not null)ShowView(_view,true);_submit(action);}
    public void ShowView(PlayerView view,bool pending=false)
    {
        _view=view;_pending=pending;if(view.ProductionContent is not {} content){_status.Text=L("В этом сохранении импорта нет.","This saved profile has no import supply.");return;}
        bool at=view.Participant.Position==content.Terminal;_status.Text=at?L("Вы у терминала. Выберите количество для покупки или получения.","You are at the terminal. Choose an amount to buy or collect."):L("Для покупки и получения подойдите к терминалу ","To buy and collect, visit the terminal ")+$"({content.Terminal.X},{content.Terminal.Z}).";
        foreach(var stock in content.Imports)
        {
            string item=stock.ItemId;var spec=content.Rules.Imports.Single(i=>i.ItemId==item);
            if(!_rows.TryGetValue(item,out var row))
            {
                var box=new VBoxContainer();var header=new HBoxContainer();header.AddChild(TabletDesign.Art(item,new(42,42)));var label=GameUi.Text("",18);label.Name="ImportStock_"+item;header.AddChild(label);box.AddChild(header);var buttons=TabletDesign.Flow();box.AddChild(buttons);
                var quantity=new SpinBox{Name="ImportQuantity_"+item,MinValue=1,MaxValue=1000000,Value=1,Step=1,Rounded=true,UpdateOnTextChanged=true,CustomMinimumSize=new(120,40)};
                Button buy=GameUi.Button("ImportBuy_"+item,L("Купить","Buy"),()=>{}),take=GameUi.Button("ImportTake_"+item,L("Забрать выбранное","Collect selected"),()=>{}),all=GameUi.Button("ImportTakeAll_"+item,"",()=>{});
                foreach(var control in new Control[]{quantity,buy,take,all})buttons.AddChild(control);var why=GameUi.Hint("");box.AddChild(why);AddChild(GameUi.Card(box));row=(label,quantity,buy,take,all,why);_rows.Add(item,row);
                buy.Pressed+=()=>{if(!buy.Disabled)Send(new(ActionKind.BuyImport,ItemId:item,Quantity:(long)quantity.Value));};
                void Collect(long amount){if(_view?.ProductionContent?.Pickup is {} source&&amount>0)Send(new(ActionKind.Transfer,Source:source,Destination:_view.Participant.InventoryId,ItemId:item,Quantity:amount));}
                take.Pressed+=()=>{if(!take.Disabled)Collect((long)quantity.Value);};all.Pressed+=()=>{if(!all.Disabled)Collect(Maximum(item));};quantity.ValueChanged+=_=>{if(_view is not null)ShowView(_view,_pending);};
            }
            long owned=content.Pickup is {} pickup?view.Inventories.Single(i=>i.Id==pickup).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved):0,q=(long)row.Quantity.Value,max=Maximum(item);
            row.Label.Text=PlayerText.Item(_language,item)+" · "+PlayerText.Money(spec.UnitPriceMinor)+L(" за 1"," per unit")+"\n"+L("В продаже ","In stock ")+stock.Remaining+L(" · оплачено к выдаче "," · paid for pickup ")+owned;
            row.Label.TooltipText=L("Проверка поставок через ","Supply review in ")+GameDuration.Format(stock.NextRefreshTick-view.Tick,_language);
            row.Buy.Disabled=pending||!at||view.Participant.Busy||q>stock.Remaining||(Int128)q*spec.UnitPriceMinor>view.AvailableCashMinor;
            row.Take.Disabled=pending||!at||view.Participant.Busy||q>max;row.All.Disabled=pending||!at||view.Participant.Busy||max==0;row.All.Text=L("Забрать всё (","Collect all (")+max+")";
            row.Why.Text=pending?L("Действие ожидает выполнения. Включите время.","Action queued. Run time."):!at?_status.Text:view.Participant.Busy?GameUi.Reason(_language,"ParticipantBusy"):owned>0&&max<owned?L("Рюкзак вместит ","Your backpack can hold ")+max+L(" ед. Остальное останется на выдаче."," units. The rest remains at pickup."):q>stock.Remaining?L("Выбранного количества нет в продаже.","That amount is not in stock."):(Int128)q*spec.UnitPriceMinor>view.AvailableCashMinor?GameUi.Reason(_language,"NoMoney"):"";
            row.Why.Visible=row.Why.Text.Length>0;row.Buy.TooltipText=row.Take.TooltipText=row.All.TooltipText=row.Why.Text;
        }
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
}

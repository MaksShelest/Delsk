using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own and bilateral orders only. Merely arriving notifications never open a modal or pause time.</summary>
public partial class ServicePanel : VBoxContainer
{
    private readonly string _language;private readonly ScenarioDefinition _scenario;private readonly Action<GameAction> _submit;
    private readonly Func<ServiceTerms,ServiceQuoteResult>? _query;
    private readonly OptionButton _vehicle=new(){Name="ServiceVehicle"},_source=new(){Name="ServiceSource"},_dest=new(){Name="ServiceDestination"},_item=new(){Name="ServiceItem"},_purchase=new(){Name="ServicePurchase"},_contract=new(){Name="ServiceContract"};
    private readonly SpinBox _quantity=new(){Name="ServiceQuantity",MinValue=1,MaxValue=1000000,Value=1};
    private readonly Label _quoteText=new(){Name="ServiceQuote",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Button _calculate=new(){Name="ServiceCalculate"},_request=new(){Name="ServiceRequest"};
    private readonly VBoxContainer _owned=new(),_orders=new();private readonly Label _catalog=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Dictionary<long,(Label Label,ProgressBar Bar)> _responses=[];private ServiceQuote? _quote;private string _key="";private readonly List<(Button Button,GameAction Action)> _buttons=[];
    public ServicePanel(ScenarioDefinition scenario,string language,Action<GameAction> submit,Func<ServiceTerms,ServiceQuoteResult>? query)
    {
        _scenario=scenario;_language=language;_submit=submit;_query=query;
        AddChild(TabletDesign.Heading(L("Услуги доставки","Delivery services"),23));
        AddChild(new Label{Text=L("Ответ перевозчика — 10 активных тиков. Уведомление не останавливает время. Принятый заказ закрепляет машину и груз; до погрузки отмена возвращает всю оплату.","Carrier response: 10 active ticks. Notifications do not pause time. Acceptance reserves the vehicle and cargo; cancellation before pickup refunds the full fee."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        AddChild(_owned);AddChild(_catalog);
        Field(L("Перевозчик и машина","Carrier and vehicle"),_vehicle);Field(L("Источник","Source"),_source);Field(L("Получатель","Recipient"),_dest);Field(L("Товар","Goods"),_item);Field(L("Количество","Quantity"),_quantity);
        Field(L("Право на оплаченную покупку (для чужого источника)","Paid purchase right (foreign source)"),_purchase);Field(L("Договор получателя (для чужого получателя)","Recipient contract (foreign recipient)"),_contract);
        foreach(var i in scenario.Items)_item.AddItem(PlayerText.Item(language,i.Id));
        _calculate.Text=L("Рассчитать","Calculate");_calculate.Pressed+=Calculate;AddChild(_calculate);AddChild(_quoteText);
        _request.Text=L("Подтвердить условия и зарезервировать","Confirm terms and reserve");_request.Disabled=true;_request.Pressed+=()=>{if(_quote is {} q)_submit(new(ActionKind.RequestDeliveryService,VehicleId:q.Terms.VehicleId,Source:q.Terms.Source,Destination:q.Terms.Destination,ItemId:q.Terms.ItemId,Quantity:q.Terms.Quantity,PurchaseId:q.Terms.PurchaseId,ContractId:q.Terms.ContractId,ServiceQuote:q.Token));};AddChild(_request);AddChild(_orders);
        foreach(var select in new[]{_vehicle,_source,_dest,_item,_purchase,_contract})select.ItemSelected+=_=>Invalidate();_quantity.ValueChanged+=_=>Invalidate();
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private void Field(string name,Control control){AddChild(new Label{Text=name});AddChild(control);}
    private void Invalidate(){_quote=null;_request.Disabled=true;_quoteText.Text=L("Нажмите «Рассчитать» для текущих условий.","Calculate the current terms.");}
    private static long Id(OptionButton b)=>b.Selected<0?0:b.GetItemMetadata(b.Selected).AsInt64();
    private void Fill(OptionButton b,IEnumerable<(long Id,string Text)> rows)
    {long old=Id(b);b.Clear();int n=0;foreach(var row in rows){b.AddItem(row.Text);b.SetItemMetadata(n,row.Id);if(row.Id==old)b.Select(n);n++;}if(n==0){b.AddItem(L("Нет доступных вариантов","No available options"));b.SetItemMetadata(0,0L);b.SetItemDisabled(0,true);}}
    private static string Position(Cell c)=>$"({c.X},{c.Z})";
    private void Calculate()
    {
        if(_query is null||_item.Selected<0)return;
        var result=_query(new(Id(_vehicle),new(Id(_source)),new(Id(_dest)),_scenario.Items[_item.Selected].Id,(long)_quantity.Value,Id(_purchase),Id(_contract)));
        _quote=result.Quote;_request.Disabled=_quote is null;
        if(_quote is not {} q){_quoteText.Text=TabletText.Reason(_language,result.Reason);return;}
        _quoteText.Text=L("Рейсов: ","Trips: ")+q.Trips+L(" · за рейс до "," · up to ")+q.QuantityPerTrip+L(" шт.\nСтоимость: "," units/trip\nFee: ")+PlayerText.Money(q.FeeMinor)+L(" · время ≈ "," · ETA ≈ ")+q.EtaTicks+L(" тиков (погрузка/разгрузка "," ticks (handling ")+q.HandlingTicks+").\n"+
            L("Срок: тик принятия + ","Due: acceptance tick + ")+(q.EtaTicks+q.BufferTicks)+L(". Порожних возвратов: ",". Empty return legs: ")+(q.Trips-1)+".\n"+
            L("Склад: занято ","Storage: occupied ")+q.DestinationOccupiedVolume+" / "+q.DestinationCapacity+L("; ожидается ещё ","; expected inbound ")+q.ExpectedIncomingVolume+L(" + этот заказ "," + this order ")+q.Volume+L(". Прогноз не резервирует вместимость; переполнение остановит приём.",". Forecast does not reserve capacity; overflow will stop receipt.")+"\n"+
            (q.CargoRightsDeadline>0?L("Право на груз до t","Cargo rights through t")+q.CargoRightsDeadline+"\n":"")+L("Маршрут: ","Route: ")+Position(q.VehiclePosition)+" → "+Position(q.SourcePosition)+" → "+Position(q.DestinationPosition)+L(". Оценка предполагает свободный путь и текущие средства погрузки. Выплата — после доставки.",". Estimate assumes clear roads and current handling resources. Payout follows actual delivery.");
    }
    private static void Clear(Container host){foreach(Node n in host.GetChildren()){host.RemoveChild(n);n.QueueFree();}}
    private void Button(Container host,string name,string text,GameAction action){var b=new Button{Name=name,Text=text,FocusMode=FocusModeEnum.None};b.Pressed+=()=>_submit(action);host.AddChild(b);_buttons.Add((b,action));}
    public void ShowData(PlayerView view,bool pending)
    {
        if(view.Services is not {} data)return;
        string key=string.Join(';',data.Catalog.Select(c=>$"{c.VehicleId}:{c.Available}"))+"|"+string.Join(',',data.OwnListings)+"|"+string.Join(';',data.OwnOrders.Select(o=>$"{o.Id}:{o.State}:{o.CustomerViewed}:{o.CarrierViewed}"))+"|"+string.Join(',',data.OwnPoints.Select(p=>p.Inventory.Value))+"|"+string.Join(',',view.Fleet?.Vehicles.Select(v=>$"{v.Vehicle.Id}:{v.Vehicle.Driver}")??[])+"|"+string.Join(',',view.Market?.Allocations.Select(a=>$"{a.Id}:{a.ClosedTick}")??[]);
        if(key!=_key)
        {
            _key=key;Invalidate();_buttons.Clear();_responses.Clear();Clear(_owned);Clear(_orders);
            foreach(var car in view.Fleet?.Vehicles??[])
            {var v=car.Vehicle;var model=view.Fleet!.Models.Single(m=>m.Id==v.ModelId);if(model.MaterialsOnly)continue;bool listed=data.OwnListings.Contains(v.Id);Button(_owned,"ServiceOffer_"+v.Id,model.Name+" #"+v.Id+" · "+(listed?L("Снять с линии","Stop accepting"):L("Принимать заказы доставки","Accept delivery orders")),new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:!listed));}
            Fill(_vehicle,data.Catalog.Where(c=>c.Carrier!=view.Participant.Id).Select(c=>(c.VehicleId,$"#{c.Carrier.Value} · {view.Fleet?.Models.FirstOrDefault(m=>m.Id==c.ModelId)?.Name??c.ModelId} #{c.VehicleId} · {c.CapacityVolume} · "+(c.Available?L("Свободна","Available"):L("Занята","Busy")))));
            Fill(_source,data.OwnPoints.Where(p=>p.CanSource).Select(p=>(p.Inventory.Value,$"#{p.Inventory.Value} · #{p.Owner.Value} · {Position(p.Position)}")));
            Fill(_dest,data.OwnPoints.Where(p=>p.CanReceive||view.Contracts?.Any(d=>d.Supplier==view.Participant.Id&&d.DestinationInventory==p.Inventory&&d.ClosedTick is null)==true).Select(p=>(p.Inventory.Value,$"#{p.Inventory.Value} · {Position(p.Position)}")));
            Fill(_purchase,new[]{(0L,L("Свой груз","Own goods"))}.Concat((view.Market?.Allocations??[]).Where(a=>a.Buyer==view.Participant.Id&&a.ClosedTick is null).Select(a=>(a.Id,$"#{a.Id} · {a.ItemId} ×{a.RemainingQuantity} · t{a.DeadlineTick}"))));
            Fill(_contract,new[]{(0L,L("Свой получатель","Own recipient"))}.Concat((view.Contracts??[]).Where(d=>d.Supplier==view.Participant.Id&&d.ClosedTick is null).Select(d=>(d.Id,$"#{d.Id} · {d.ItemId} · t{d.DeadlineTick}"))));
            foreach(var order in data.OwnOrders.Reverse())
            {
                var row=new VBoxContainer();_orders.AddChild(TabletDesign.Wrap(row));bool carrier=order.Carrier==view.Participant.Id;bool seen=carrier?order.CarrierViewed:order.CustomerViewed;
                row.AddChild(new Label{Text=(seen?"":"● ")+"#"+order.Id+" · "+State(order.State)+$" · #{order.Customer.Value} → #{order.Carrier.Value} · {PlayerText.Item(_language,order.Quote.Terms.ItemId)} ×{order.Quote.Terms.Quantity} · {order.Quote.Trips} "+L("рейсов","trips")+" · "+PlayerText.Money(order.Quote.FeeMinor)+" · ≈"+order.Quote.EtaTicks+"t · "+L("ответ до t","reply through t")+order.RespondByTick+" · "+L("срок t","due t")+order.DeadlineTick+"\n"+Position(order.Quote.SourcePosition)+" → "+Position(order.Quote.DestinationPosition)+" · "+L("срок от принятия +","due after acceptance +")+(order.Quote.EtaTicks+order.Quote.BufferTicks)+(order.Quote.CargoRightsDeadline>0?L(" · право на груз до t"," · cargo rights through t")+order.Quote.CargoRightsDeadline:"")+"\n"+TabletText.Reason(_language,order.Reason),AutowrapMode=TextServer.AutowrapMode.WordSmart});
                if(order.State is "Pending" or "Requote"){var time=new Label();var bar=new ProgressBar{Name="ServiceResponse_"+order.Id,MaxValue=data.Rules.ResponseTicks,ShowPercentage=false,CustomMinimumSize=new(0,12)};row.AddChild(time);row.AddChild(bar);_responses.Add(order.Id,(time,bar));}
                if(!seen)Button(row,"ServiceView_"+order.Id,L("Отметить просмотренным","Mark viewed"),new(ActionKind.ViewDeliveryService,ServiceId:order.Id));
                if(carrier&&order.State=="Pending")Button(row,"ServiceAccept_"+order.Id,L("Принять","Accept"),new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));
                if(carrier&&order.State is "Pending" or "Requote")Button(row,"ServiceReject_"+order.Id,L("Отклонить","Reject"),new(ActionKind.RejectDeliveryService,ServiceId:order.Id));
                if(!carrier&&order.State=="Requote"&&order.ProposedQuote is {} revised){row.AddChild(new Label{Text=L("Новый расчёт: ","Revised terms: ")+PlayerText.Money(revised.FeeMinor)+" · ≈"+revised.EtaTicks+"t · "+L("срок от принятия +","due after acceptance +")+(revised.EtaTicks+revised.BufferTicks),AutowrapMode=TextServer.AutowrapMode.WordSmart});Button(row,"ServiceConfirm_"+order.Id,L("Подтвердить новые условия","Confirm revised terms"),new(ActionKind.ConfirmDeliveryService,ServiceId:order.Id,ServiceQuote:revised.Token));}
                if(order.Active)Button(row,"ServiceCancel_"+order.Id,L("Отменить до погрузки","Cancel before pickup"),new(ActionKind.CancelDeliveryService,ServiceId:order.Id));
            }
        }
        foreach(var order in data.OwnOrders.Where(o=>o.State is "Pending" or "Requote")){if(_responses.TryGetValue(order.Id,out var response)){long left=Math.Max(0,order.RespondByTick-view.Tick);response.Label.Text=L("Осталось активных тиков: ","Active ticks remaining: ")+left;response.Bar.Value=left;}}
        _catalog.Text=L("Каталог: ","Catalog: ")+string.Join(" · ",data.Catalog.Select(c=>$"#{c.Carrier.Value} · {view.Fleet?.Models.FirstOrDefault(m=>m.Id==c.ModelId)?.Name??c.ModelId} #{c.VehicleId} · "+L("объём ","volume ")+c.CapacityVolume+" · "+Position(c.Position)+" · "+(c.Available?L("Свободна","Available"):L("Занята","Busy"))));
        foreach(var (button,_) in _buttons)button.Disabled=pending;_calculate.Disabled=pending||Id(_vehicle)==0||Id(_source)==0||Id(_dest)==0;_request.Disabled=pending||_quote is null;
    }
    private string State(string state)=>state switch{"Pending"=>L("Ожидает перевозчика","Awaiting carrier"),"Requote"=>L("Нужно подтверждение новых условий","Reconfirmation required"),"Accepted"=>L("Принят · ожидает погрузки","Accepted · awaiting pickup"),"Rejected"=>L("Отклонён","Rejected"),"Expired"=>L("Время ответа истекло","Response expired"),_=>L("Отменён","Cancelled")};
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Only own ledger and invoices; all writes use ordinary remote commands.</summary>
public partial class PersonalAccountPanel : VBoxContainer
{
    private readonly string _language;
    private readonly Action<GameAction> _submit;
    private readonly Func<int,int,PersonalAccount>? _query;
    private readonly Label _summary=Wrap(),_invoice=Wrap(),_history=Wrap(),_help=Wrap();
    private readonly CheckButton _auto=new(){Name="AccountAutoPay"};
    private readonly OptionButton _invoices=new(){Name="AccountInvoice"};
    private readonly Button _view=new(){Name="AccountViewInvoice"},_pay=new(){Name="AccountPayInvoice"};
    private readonly SpinBox _amount=new(){Name="AccountAmount",MinValue=0,MaxValue=1000000000000,Step=0.01,Value=0,CustomMinimumSize=new(160,0)};
    private readonly Button _older=new(){Name="AccountOlder"},_newer=new(){Name="AccountNewer"},_billOlder=new(){Name="AccountOlderBills"},_billNewer=new(){Name="AccountNewerBills"};
    private PersonalAccount? _data;private bool _pending,_refreshing;private int _offset,_billOffset,_knownBillCount;private long[] _ids=[];
    public PersonalAccountPanel(string language,Action<GameAction> submit,Func<int,int,PersonalAccount>? query=null)
    {
        Name="PersonalAccount";_language=language;_submit=submit;_query=query;AddChild(TabletDesign.Heading(L("Личный счёт","Personal account")));
        AddChild(_summary);foreach(string color in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color"})_auto.AddThemeColorOverride(color,TabletDesign.Ink);_auto.Text=L("Автоплата из свободных денег","Auto pay from available funds");AddChild(_auto);
        _auto.Toggled+=enabled=>{if(!_refreshing)_submit(new(ActionKind.SetAutoPay,Enabled:enabled));};
        var bills=TabletDesign.Flow();bills.AddChild(_invoices);bills.AddChild(_billNewer);bills.AddChild(_billOlder);AddChild(bills);
        _billNewer.Text=L("‹ Новее","‹ Newer");_billOlder.Text=L("Старее ›","Older ›");
        _billNewer.Pressed+=()=>{_billOffset=Math.Max(0,_billOffset-20);Reload();};_billOlder.Pressed+=()=>{_billOffset+=20;Reload();};
        _invoices.ItemSelected+=_=>RenderInvoice();AddChild(_invoice);
        var buttons=TabletDesign.Flow();_view.Text=L("Отметить просмотренным","Mark as viewed");_pay.Text=L("Оплатить","Pay");
        buttons.AddChild(_view);buttons.AddChild(_amount);buttons.AddChild(_pay);AddChild(buttons);
        _view.Pressed+=()=>{if(Selected>0)_submit(new(ActionKind.ViewInvoice,OfferId:Selected));};
        _pay.Pressed+=()=>{if(Selected>0)_submit(new(ActionKind.PayInvoice,OfferId:Selected,Quantity:checked((long)Math.Round((decimal)_amount.Value*100,MidpointRounding.AwayFromZero))));};
        _help.Text=L("Сумма в монетах; 0 — доступная часть всего счёта. Сначала основной долг, затем пени. Просмотр не означает оплату. Резервы и escrow защищены. Долг не запрещает работать.",
            "Amount in coins; 0 pays as much as available. Principal first, then penalties. Viewing does not pay. Holds and escrow stay protected. Debt does not block work.");AddChild(_help);
        AddChild(TabletDesign.Heading(L("Операции · доходы и расходы","Operations · income and expenses"),20));AddChild(_history);
        var pages=TabletDesign.Flow();_newer.Text=L("‹ Новые операции","‹ Newer operations");_older.Text=L("Ранние операции ›","Older operations ›");pages.AddChild(_newer);pages.AddChild(_older);AddChild(pages);
        _newer.Pressed+=()=>{_offset=Math.Max(0,_offset-20);Reload();};_older.Pressed+=()=>{_offset+=20;Reload();};
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private static Label Wrap()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private long Selected=>_invoices.Selected>=0&&_invoices.Selected<_ids.Length?_ids[_invoices.Selected]:0;
    public void FocusInvoice(long id)
    {_billOffset=0;Reload();int index=Array.IndexOf(_ids,id);if(index>=0){_invoices.Select(index);RenderInvoice();}}
    public void ShowData(PersonalAccount data,bool pending)
    {_data=data;_pending=pending;Reload();}
    private void Reload()
    {
        if(_data is null)return;var data=_query?.Invoke(_offset,_billOffset)??_data;_data=data;
        _summary.Text=L("Баланс / свободно / резерв: ","Balance / available / reserved: ")+string.Join(" / ",new[]{data.Account.BalanceMinor,data.Account.AvailableMinor,data.Account.ReservedMinor}.Select(PlayerText.Money))+"\n"+
            L("Деньги в расчётах по договорам: ","Funds in contract escrow: ")+PlayerText.Money(data.EscrowMinor)+"\n"+
            L("Доходы / расходы: ","Income / expenses: ")+PlayerText.Money(data.IncomeMinor)+" / "+PlayerText.Money(data.ExpensesMinor)+"\n"+
            L("Долг / ещё не выставлено / к получению: ","Debt / unbilled / receivable: ")+PlayerText.Money(data.DebtMinor)+" / "+PlayerText.Money(data.AccruedMinor)+" / "+PlayerText.Money(data.ReceivableMinor);
        _refreshing=true;_auto.SetPressedNoSignal(data.AutoPay);_auto.Disabled=_pending||data.Rules is null;_refreshing=false;
        long selected=Selected;
        if(selected>0&&_query is not null&&_knownBillCount>0&&data.InvoiceCount>_knownBillCount&&!data.Invoices.Take(20).Any(i=>i.Id==selected))
        {_billOffset+=data.InvoiceCount-_knownBillCount;data=_query(_offset,_billOffset);_data=data;}
        _knownBillCount=data.InvoiceCount;var invoices=data.Invoices.Take(20).ToArray();var ids=invoices.Select(i=>i.Id).ToArray();
        // Keep selection and page during live updates; never rebuild the surrounding scroll.
        if(!_ids.SequenceEqual(ids)){_ids=ids;_invoices.Clear();foreach(var i in invoices)_invoices.AddItem("#"+i.Id);if(_ids.Length>0)_invoices.Select(Math.Max(0,Array.IndexOf(_ids,selected)));}
        _newer.Disabled=_offset==0;_older.Disabled=_query is null||_offset+20>=data.OperationCount;
        _billNewer.Disabled=_billOffset==0;_billOlder.Disabled=_query is null||_billOffset+20>=data.InvoiceCount;
        _history.Text=string.Join('\n',data.Operations.Take(20).Select(o=>$"t{o.Tick} · {(o.AmountMinor>0?"+":"−")}{PlayerText.Money(Math.Abs(o.AmountMinor))} · {Kind(o.Kind)} · {Party(o.Counterparty,o.CounterpartyKind)}"));
        if(data.Operations.IsEmpty)_history.Text=L("Операций пока нет.","No operations yet.");RenderInvoice();
    }
    private string Party(ParticipantId? actor,string kind)=>actor?.Value==0?(kind=="Capital"?L("Столица","Capital"):L("Город","City")):actor is {} id?L("Участник #","Participant #")+id.Value:kind switch
    {"Treasury"=>L("Город","City"),"WagesSink"=>L("Числовой персонал","Staff"),"CapitalBudget" or "Capital"=>L("Столица","Capital"),"External"=>L("Внешний рынок","External market"),"RoadClearing"=>L("Получатели дорожной платы","Road recipients"),_=>L("Расчёты по договору","Contract settlement")};
    private string Kind(string kind)
    {
        if(kind.StartsWith("ServiceFunding:",StringComparison.Ordinal))return L("Резерв оплаты доставки","Delivery escrow funding");
        if(kind.StartsWith("ServicePayment:",StringComparison.Ordinal))return L("Оплата доставленной части","Delivered part payment");
        if(kind.StartsWith("ServiceLateRefund:",StringComparison.Ordinal))return L("Возврат за просроченную часть","Late delivery refund");
        if(kind.StartsWith("ServiceDriverWages:",StringComparison.Ordinal))return L("Зарплата водителя доставки","Delivery driver wages");
        if(kind.StartsWith("ServiceRefund:",StringComparison.Ordinal))return L("Возврат оплаты доставки","Delivery refund");
        if(kind.StartsWith("RoadPrincipal:",StringComparison.Ordinal))return L("Оплата дороги","Road payment");
        if(kind.StartsWith("RoadRevenue:",StringComparison.Ordinal))return L("Доход дороги","Road income");
        if(kind.StartsWith("RoadPenalty:",StringComparison.Ordinal))return L("Пени","Penalty");
        return kind switch
        {"Wages" or "EquipmentTravelWages" or "LogisticsWages" or "RoadWages" or "HandlingWages"=>L("Зарплата","Wages"),"VehiclePurchase"=>L("Покупка машины","Vehicle purchase"),"RoadConstruction"=>L("Запуск дорожных работ","Starting road works"),"RoadOrderReward"=>L("Городское дорожное задание","City road task"),"Energy"=>L("Производственная услуга","Production service"),"Import" or "ImportPurchase"=>L("Импорт","Import"),"PlotPurchase"=>L("Покупка земли","Land purchase"),"PlotReservation"=>L("Бронь земли","Land reservation"),"Construction"=>L("Строительство","Construction"),"BuildingUpgrade"=>L("Улучшение здания","Building upgrade"),"Demolition"=>L("Снос","Demolition"),
        "ResourcePurchase"=>L("Покупка месторождения","Resource purchase"),"ResourceLease"=>L("Аренда месторождения","Resource lease"),"EquipmentPurchase"=>L("Покупка добывающей техники","Equipment purchase"),"EquipmentUpgrade"=>L("Улучшение техники","Equipment upgrade"),"LogisticsPurchase"=>L("Оснащение логистики","Logistics purchase"),"LogisticsUpgrade"=>L("Улучшение логистики","Logistics upgrade"),
        "CapitalSettlement" or "DeliveryPayment" or "PickupPayment"=>L("Расчёт за товар","Goods settlement"),"CapitalEscrow" or "MarketEscrow" or "DeliveryFunding"=>L("Финансирование договора","Contract funding"),"DeliveryBond"=>L("Залог поставки","Delivery bond"),"DeliveryBondReturn"=>L("Возврат залога","Bond return"),"DeliveryRefund" or "PickupRefund"=>L("Возврат оплаты","Payment refund"),"DeliveryFine" or "PickupFine"=>L("Штраф договора","Contract fine"),
        "HandlingService"=>L("Услуга погрузки","Handling service"),"WorkshopOutfit"=>L("Оснащение цеха","Workshop outfit"),"WorkshopModernization"=>L("Модернизация цеха","Workshop modernization"),_=>L("Операция по счёту","Account operation")};
    }
    private void RenderInvoice()
    {
        var bill=_data?.Invoices.FirstOrDefault(i=>i.Id==Selected);_view.Disabled=_pending||bill is null||bill.Viewed;_pay.Disabled=_pending||bill is null||bill.Remaining==0||_data!.Account.AvailableMinor==0;
        if(bill is null){_invoice.Text=L("Счетов пока нет.","No invoices yet.");return;}
        _invoice.Text=$"#{bill.Id} · t{bill.CreatedTick} · "+(bill.Viewed?L("Просмотрен","Viewed"):L("● Новый","● New"))+" · "+(bill.Remaining==0?L("Оплачен","Paid"):L("Не погашен","Unpaid"))+"\n"+
            L("Основной долг / пени: ","Principal / penalties: ")+PlayerText.Money(bill.PrincipalRemaining)+" / "+PlayerText.Money(bill.PenaltyRemaining)+"\n"+
            L("До конца льготного срока: ","Grace remaining: ")+Math.Max(0,bill.DueTick-_data!.Tick)+L(" с активного времени. "," active seconds. ")+L("Без пеней до t","Penalty-free through t")+bill.DueTick+L(" включительно. "," inclusive. ")+(bill.PrincipalRemaining>0?L("Следующая пеня t","Next penalty t")+bill.NextPenaltyTick:L("Новые пени не начисляются.","No new penalties accrue."))+"\n"+
            string.Join(" · ",bill.Claims.Select(c=>(c.Recipient.Value==0?L("Город","City"):L("Участник #","Participant #")+c.Recipient.Value)+": "+PlayerText.Money(c.PrincipalMinor-c.PaidMinor)));
        _invoice.AddThemeColorOverride("font_color",bill.Viewed?TabletDesign.Ink:new Color("bb3030"));
    }
}

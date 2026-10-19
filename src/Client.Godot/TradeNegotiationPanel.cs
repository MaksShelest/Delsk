using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own/bilateral terms only. Core rechecks the selected version and all resources.</summary>
public partial class TradeNegotiationPanel : VBoxContainer
{
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly OptionButton _threads=new(),_kind=new(),_partner=new(),_item=new(),_source=new(),_destination=new();
    private readonly CheckBox _sellSupport=new();
    private readonly QuantityInput _quantity=new();
    private readonly SpinBox _price=new(){MinValue=.01,MaxValue=1000000,Value=5,Step=.01};
    private readonly SpinBox _duration=new(){MinValue=1,MaxValue=10000,Value=120,Step=1};
    private readonly Label _status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Button _publish=new(),_propose=new(),_counter=new(),_accept=new(),_reject=new(),_refresh=new();
    private PlayerView? _view;
    private TradeNegotiation? _shown;
    private string _inventoryKey="",_partyKey="",_threadKey="";
    private bool _pending;
    public TradeNegotiationPanel(ScenarioDefinition scenario,string language,System.Action<GameAction> submit)
    {
        _language=language;_submit=submit;
        AddChild(new Label{Text=L("Двусторонние переговоры","Bilateral negotiations")});AddChild(_status);
        Row(L("Переписка","Thread"),_threads);
        var fresh=new Button{Text=L("Новое предложение","New proposal")};AddChild(fresh);fresh.Pressed+=()=>{_shown=null;Update();};
        _refresh.Text=L("Показать последнюю версию","Show latest version");AddChild(_refresh);_refresh.Pressed+=LoadSelected;
        foreach(string name in new[]{L("Продажа наличия","Sell existing stock"),L("Закупка будущей поставки","Buy future delivery"),L("Предложение поставки","Supply delivery"),L("Политическая поддержка","Political support")})_kind.AddItem(name);
        Row(L("Тип","Type"),_kind);Row(L("Контрагент","Counterparty"),_partner);
        _sellSupport.Text=L("Предложить свой блок поддержки","Offer my own support block");AddChild(_sellSupport);
        foreach(var item in scenario.Items){_item.AddItem(PlayerText.Item(language,item.Id));_item.SetItemMetadata(_item.ItemCount-1,item.Id);}
        Row(L("Товар / компенсация","Item / compensation"),_item);Row(L("Количество","Quantity"),_quantity);
        Row(L("Цена за единицу","Unit price"),_price);Row(L("Срок исполнения, тиков","Fulfillment ticks"),_duration);
        Row(L("Свой источник","Own source"),_source);Row(L("Свой получатель","Own destination"),_destination);
        _propose.Text=L("Отправить предложение","Send proposal");_counter.Text=L("Предложить встречные условия","Counteroffer");
        _reject.Text=L("Отказаться от версии","Reject version");
        _publish.Text=L("Опубликовать выбранное на общем рынке","Publish selected stock on public market");
        _publish.Pressed+=()=>{var t=Terms();var shop=_view!.Buildings.First(b=>b.Kind==BuildingKind.Shop);_submit(PlayerDesk.PublishLot(shop.Id.Value,t.ItemId,t.Quantity,t.UnitPriceMinor,t.Source.Value));};
        foreach(var b in new[]{_publish,_propose,_counter,_accept,_reject})AddChild(b);
        _propose.Pressed+=()=>Send(ActionKind.ProposeTrade);_counter.Pressed+=()=>Send(ActionKind.CounterTrade);
        _accept.Pressed+=()=>Send(ActionKind.AcceptTrade);_reject.Pressed+=()=>Send(ActionKind.RejectTrade);
        _threads.ItemSelected+=_=>LoadSelected();_kind.ItemSelected+=_=>Update();_source.ItemSelected+=_=>Update();
    }
    private void Row(string name,Control value)
    {var row=new HBoxContainer();row.AddChild(new Label{Text=name,CustomMinimumSize=new(215,0)});value.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(value);AddChild(row);}
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;
        string parties=string.Join(',',view.TradingParticipants);
        if(parties!=_partyKey){_partyKey=parties;_partner.Clear();foreach(var p in view.TradingParticipants.Where(p=>p!=view.Participant.Id))_partner.AddItem("#"+p.Value,(int)p.Value);}
        var inventories=view.Inventories.Where(i=>view.Buildings.Any(b=>b.InputId==i.Id||b.OutputId==i.Id)).ToArray();
        string key=string.Join(',',inventories.Select(i=>i.Id.Value));
        if(key!=_inventoryKey)
        {
            _inventoryKey=key;_source.Clear();_destination.Clear();
            foreach(var inventory in inventories)
            {
                var b=view.Buildings.Single(b=>b.InputId==inventory.Id||b.OutputId==inventory.Id);
                string caption=PlayerText.Building(_language,b.TypeId)+" #"+b.Id.Value+$" ({inventory.Position.X},{inventory.Position.Z})"+(b.OutputId==inventory.Id?L(" · выход"," · output"):L(" · вход"," · input"));
                _source.AddItem(caption);_source.SetItemMetadata(_source.ItemCount-1,inventory.Id.Value);
                if(b.InputId==inventory.Id){_destination.AddItem(caption);_destination.SetItemMetadata(_destination.ItemCount-1,inventory.Id.Value);}
            }
        }
        key=string.Join(';',view.Negotiations.Select(n=>$"{n.Id}:{n.Current.Version}:{n.Status}"));
        if(key!=_threadKey)
        {
            long selectedId=_shown?.Id??(_threads.ItemCount>0?_threads.GetItemMetadata(_threads.Selected).AsInt64():0);
            _threadKey=key;_threads.Clear();foreach(var n in view.Negotiations)
            {_threads.AddItem($"#{n.Id} · v{n.Current.Version} · "+Status(n.Status));_threads.SetItemMetadata(_threads.ItemCount-1,n.Id);if(n.Id==selectedId)_threads.Select(_threads.ItemCount-1);}
        }
        Update();
    }
    private void LoadSelected()
    {
        if(_view is null || _threads.ItemCount==0)return;
        _shown=_view.Negotiations.Single(n=>n.Id==_threads.GetItemMetadata(_threads.Selected).AsInt64());var t=_shown.Current.Terms;
        _kind.Select((int)t.Kind);
        var other=t.Seller==_view.Participant.Id?t.Buyer:t.Seller;for(int i=0;i<_partner.ItemCount;i++)if(_partner.GetItemId(i)==other.Value)_partner.Select(i);_quantity.Value=t.Kind==TradeKind.Support?t.Compensation[0].Quantity:t.Quantity;
        string item=t.Kind==TradeKind.Support?t.Compensation[0].ItemId:t.ItemId;
        for(int i=0;i<_item.ItemCount;i++)if(_item.GetItemMetadata(i).AsString()==item)_item.Select(i);
        Select(_source,t.Source);Select(_destination,t.Destination);_price.Value=System.Math.Max(1,t.UnitPriceMinor)/100d;_duration.Value=t.FulfillmentTicks;Update();
    }
    private static void Select(OptionButton option,InventoryId id)
    {for(int i=0;i<option.ItemCount;i++)if(option.GetItemMetadata(i).AsInt64()==id.Value)option.Select(i);}
    private TradeTerms Terms()
    {
        var v=_view!;var kind=(TradeKind)_kind.Selected;var actor=v.Participant.Id;var other=new ParticipantId(_partner.GetSelectedId());
        var old=_shown?.Current.Terms;string item=_item.GetItemMetadata(_item.Selected).AsString();
        long price=checked((long)System.Math.Round(_price.Value*100,MidpointRounding.AwayFromZero));
        InventoryId Selected(OptionButton o)=>o.ItemCount>0?new(o.GetItemMetadata(o.Selected).AsInt64()):default;
        if(kind==TradeKind.Support)return new(kind,old?.Seller??(_sellSupport.ButtonPressed?actor:other),old?.Buyer??(_sellSupport.ButtonPressed?other:actor),
            Election:old?.Election??v.Elections.NextElectionId??default,Compensation:[new(item,_quantity.Value)]);
        var seller=old?.Seller??(kind==TradeKind.Purchase?other:actor);var buyer=old?.Buyer??(kind==TradeKind.Purchase?actor:other);
        return new(kind,seller,buyer,item,_quantity.Value,price,kind==TradeKind.Sale?(old?.Source??Selected(_source)):default,
            kind==TradeKind.Sale?default:buyer==actor?Selected(_destination):old?.Destination??default,(int)_duration.Value);
    }
    private void Send(ActionKind kind)
    {
        if(_pending || _view is null)return;
        // Keep the reviewed version, including when another response arrived during editing.
        _submit(new(kind,NegotiationId:_shown?.Id??0,NegotiationVersion:_shown?.Current.Version??0,
            Terms:kind is ActionKind.ProposeTrade or ActionKind.CounterTrade?Terms():null));
    }
    private void Update()
    {
        if(_view is null)return;var latest=_shown is null?null:_view.Negotiations.SingleOrDefault(n=>n.Id==_shown.Id);
        bool stale=_shown is not null && (latest?.Current.Version!=_shown.Current.Version || latest.Status!=NegotiationStatus.Proposed);
        bool other=_shown?.Current.ProposedBy!=_view.Participant.Id;
        bool access=_view.TradingParticipants.Contains(_view.Participant.Id),support=(TradeKind)_kind.Selected==TradeKind.Support;
        _kind.Disabled=_partner.Disabled=_shown is not null;_sellSupport.Disabled=_shown is not null||!support;
        _source.Disabled=_shown is not null||support||(TradeKind)_kind.Selected!=TradeKind.Sale;_destination.Disabled=support||(TradeKind)_kind.Selected==TradeKind.Sale;
        _price.Editable=!support;_duration.Editable=!support;
        _publish.Disabled=_pending||!access||_shown is not null||support||(TradeKind)_kind.Selected!=TradeKind.Sale||_source.ItemCount==0;
        _propose.Disabled=_pending||!access||_shown is not null||_partner.ItemCount==0;
        _counter.Disabled=_accept.Disabled=_pending||!access||_shown is null||stale||!other;
        _reject.Disabled=_pending||_shown is null||stale||!other;
        _accept.Text=L("Принять версию ","Accept version ")+(_shown?.Current.Version.ToString()??"—");
        _status.Text=!access?PlayerText.Reason(_language,"ShopRequired"):_shown is null?L("До согласия товар и деньги не резервируются. Поддержка подписывается только после фактической компенсации.","No stock or money is reserved during talks. Support is signed only after physical compensation."):
            $"#{_shown.Id} · v{_shown.Current.Version} · "+L("срок ответа ","reply due ")+_shown.ExpiresTick+" · "+_shown.Current.Terms.Seller.Value+" → "+_shown.Current.Terms.Buyer.Value+
            "\n"+Describe(_shown.Current.Terms)+(_shown.Current.Position is {} p?$" · ({p.X},{p.Z})":"")+"\n"+(stale?L("Условия изменились. Загрузите последнюю версию перед подтверждением.","Terms changed. Load the latest version before confirming."):Status(_shown.Status));
    }
    private string Describe(TradeTerms t)=>t.Kind==TradeKind.Support?string.Join(", ",t.Compensation.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity)):PlayerText.Item(_language,t.ItemId)+" ×"+t.Quantity+" · "+PlayerText.Money(t.UnitPriceMinor)+L(" за единицу · срок "," per unit · due in ")+t.FulfillmentTicks+L(" тиков · источник #"," ticks · source #")+t.Source.Value+L(" · получатель #"," · destination #")+t.Destination.Value;
    private string Status(NegotiationStatus s)=>s switch {NegotiationStatus.Proposed=>L("Ожидает ответа","Awaiting response"),NegotiationStatus.Accepted=>L("Договор заключён","Agreement concluded"),NegotiationStatus.Rejected=>L("Отказ","Rejected"),_=>L("Срок истёк","Expired")};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Public lots and this participant's requests/claims. All changes go through PlayerDesk.</summary>
public partial class ParticipantMarketTablet : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly CapitalTablePreferences _preferences;
    private readonly long _shop;
    private readonly System.Action<GameAction> _submit;
    private readonly VBoxContainer _rows = new();
    private readonly VBoxContainer _pickups = new();
    private readonly VBoxContainer _seller = new();
    private readonly VBoxContainer _ownOffers=new();
    private readonly HFlowContainer _filters,_inputs,_bidActions;
    private readonly Label _pickupTitle,_emptyOwn,_priceLabel;
    private readonly System.Func<GameAction,ActionAccess>? _access;
    private int _page;
    public void ShowPage(int page){_page=Math.Clamp(page,0,3);ApplyPage();}
    private void ApplyPage()
    {
        _filters.Visible=_rows.Visible=_selected.Visible=_bidActions.Visible=_page==0;_inputs.Visible=_page<3;_price.Visible=_priceLabel.Visible=_page is 0 or 2;
        _pickups.Visible=_pickupTitle.Visible=_page==1;_history.Visible=_page==3;
        bool owns=_view?.Buildings.Any(b=>b.Id.Value==_shop && b.Kind==BuildingKind.Shop)??false;
        _ownOffers.Visible=_page==2;if(_ownOffers.GetParent() is Control ownShell)ownShell.Visible=_ownOffers.Visible;
        _seller.Visible=_page==2 && owns;if(_seller.GetParent() is Control shell)shell.Visible=_seller.Visible;_emptyOwn.Visible=_page==2 && !owns;
    }
    private readonly Label _selected = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _stock = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _history = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly QuantityInput _quantity=new();
    private readonly OptionButton _source=new();
    private string _sourcesKey="";
    private readonly SpinBox _price = new() { MinValue = .01, MaxValue = 1000000, Value = 1, Step = .01 };
    private readonly Button _buy;
    private readonly Button _raise;
    private readonly Button _withdraw;
    private readonly Button _publish;
    private readonly Button _deposit;
    private readonly Button _take;
    private readonly List<(long Id, Label Text, ProgressBar Bar)> _windows = [];
    private readonly List<(long Id, Button Button)> _collectButtons = [];
    private PlayerView? _view;
    private long _offer;
    private string _item;
    private string _key = "";
    private bool _pending;
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
    private long Quantity => (long)_quantity.Value;
    private long Price => checked((long)Math.Round(_price.Value * 100, MidpointRounding.AwayFromZero));

    public ParticipantMarketTablet(ScenarioDefinition scenario, string language, CapitalTablePreferences preferences,
        long shop, System.Action<GameAction> submit,System.Func<GameAction,ActionAccess>? access=null)
    {
        _scenario = scenario; _language = language; _preferences = preferences; _shop = shop; _submit = submit;
        _access=access;_item = scenario.Items[0].Id; _price.Value = scenario.Items[0].BasePriceMinor / 100d;
        AddChild(new Label { Text = L("Для торговли нужна своя лавка. Источник лота выбирается отдельно; место выдачи видно до заявки. При дефиците действует аукцион. Покупку нужно физически получить до срока.",
            "Trading requires your own shop. Select a separate stock source; the pickup point is visible before bidding. Shortages trigger an auction. Physically collect purchases before the deadline."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var filters = TabletDesign.Flow();_filters=filters; AddChild(filters);
        var items = new[] { "" }.Concat(scenario.Items.Select(i => i.Id)).ToArray();
        var filter = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (string item in items) filter.AddItem(item.Length == 0 ? L("Все товары", "All goods") : PlayerText.Item(language, item));
        filter.Selected = Math.Max(0, Array.IndexOf(items, preferences.ItemId));
        filter.ItemSelected += i => { preferences.ItemId = items[(int)i]; _key = ""; Refresh(); }; filters.AddChild(filter);
        var sort = new OptionButton { FocusMode = FocusModeEnum.None };
        sort.AddItem(L("Сначала дешевле", "Cheapest first")); sort.AddItem(L("Сначала дороже", "Highest first"));
        sort.Selected = preferences.HighestPriceFirst ? 1 : 0;
        sort.ItemSelected += i => { preferences.HighestPriceFirst = i == 1; _key = ""; Refresh(); }; filters.AddChild(sort);
        var inputs = TabletDesign.Flow();_inputs=inputs; AddChild(inputs);
        inputs.AddChild(new Label { Text = L("Количество", "Quantity") }); inputs.AddChild(_quantity);
        _priceLabel=new Label { Text = L("Цена/лимит за 1", "Unit price/limit") };inputs.AddChild(_priceLabel); inputs.AddChild(_price);
        AddChild(_selected);
        var actions = TabletDesign.Flow();_bidActions=actions; AddChild(actions);
        _buy = Action(actions, L("Подать заявку", "Request goods"), () => _submit(PlayerDesk.Bid(_offer, Quantity, Price)));
        _raise = Action(actions, L("Повысить ставку до цены", "Raise bid to price"), () => {
            var offer = _view?.Market?.Offers.SingleOrDefault(o => o.Id == _offer);
            if (offer is not null) _submit(PlayerDesk.RaiseBid(_offer, offer.Round, Price)); });
        _withdraw = Action(actions, L("Отозвать заявку", "Withdraw request"), () => _submit(PlayerDesk.WithdrawBid(_offer, _view?.Market?.Offers.SingleOrDefault(o => o.Id == _offer)?.Round ?? 0)));
        _quantity.Changed += _ => UpdateActions(); _price.ValueChanged += _ => UpdateActions();
        AddChild(_rows);
        AddChild(TabletDesign.Wrap(_ownOffers));AddChild(TabletDesign.Wrap(_seller));
        _seller.AddChild(new Label { Text = L("Мой источник лота · свободный запас здания", "My lot source · free building stock") });
        _seller.AddChild(_source);_source.ItemSelected+=_=>UpdateActions();
        var goods = TabletDesign.Flow(); _seller.AddChild(goods);
        var goodsList = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var item in scenario.Items) goodsList.AddItem(PlayerText.Item(language, item.Id));
        goodsList.ItemSelected += i => { _item = scenario.Items[(int)i].Id; _price.Value = scenario.Items[(int)i].BasePriceMinor / 100d; Refresh(); };
        goods.AddChild(goodsList);
        _deposit = Action(goods, L("Положить из рюкзака", "Deposit from backpack"), () => Transfer(true));
        _take = Action(goods, L("Забрать в рюкзак", "Take to backpack"), () => Transfer(false));
        _publish = Action(goods, L("Опубликовать лот", "Publish lot"), () => _submit(PlayerDesk.PublishLot(_shop, _item, Quantity, Price,Source()?.Id.Value??0)));
        _seller.AddChild(_stock);
        _emptyOwn=new Label{Text=L("Чтобы публиковать лоты, откройте свою лавочку. Уже заключённые обязательства сохраняются.","Open your own shop to publish lots. Existing obligations remain valid."),AutowrapMode=TextServer.AutowrapMode.WordSmart};AddChild(_emptyOwn);
        _pickupTitle=new Label { Text = L("Мои покупки: физическое получение", "My purchases: physical pickup") };AddChild(_pickupTitle);
        AddChild(_pickups); AddChild(_history);TabletDesign.CompactInputs(this);ApplyPage();
    }
    private Button Action(Node host, string text, System.Action action)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += () => { if (!_pending) action(); }; host.AddChild(button); return button;
    }
    public void ShowData(PlayerView view, bool pending) { _view = view; _pending = pending; Refresh(); }
    private void Transfer(bool deposit)
    {
        var building = _view?.Buildings.SingleOrDefault(b => b.Id.Value == _shop);
        if (building is null || _view is null || Source() is not {} source) return;
        _submit(PlayerDesk.Transfer(deposit ? _view.Participant.InventoryId.Value : source.Id.Value,
            deposit ? source.Id.Value : _view.Participant.InventoryId.Value, _item, Quantity));
    }
    private InventoryView? Source()=>_view is null||_source.ItemCount==0?null:_view.Inventories.Single(i=>i.Id.Value==_source.GetItemMetadata(_source.Selected).AsInt64());
    private long PickupQuantity(MarketAllocationView claim)
    {
        var pack = _view!.Inventories.Single(i => i.Id == _view.Participant.InventoryId);
        long free = (pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity)/_scenario.Items.Single(i=>i.Id==claim.ItemId).UnitVolume;
        return Math.Min(claim.RemainingQuantity,Math.Min(Quantity,free));
    }
    private void Collect(long id)
    {
        var claim = _view?.Market?.Allocations.SingleOrDefault(a => a.Id == id && a.Buyer == _view.Participant.Id);
        if (claim is not null && PickupQuantity(claim) is var quantity && quantity > 0)
            _submit(PlayerDesk.CollectPurchase(id,quantity));
    }
    private void Refresh()
    {
        if (_view?.Market is not { } market) return;
        string key = _preferences.ItemId + _preferences.HighestPriceFirst + _pending + string.Join(';', market.Offers.Select(o => $"{o.Id}:{o.Status}:{o.Round}:{o.AllocatedQuantity}:" + string.Join(',',o.Bids))) +
            string.Join(';', market.Allocations.Select(a => $"{a.Id}:{a.Status}:{a.CollectedQuantity}"));
        if (key != _key)
        {
            _key = key; _windows.Clear();
            foreach(Node n in _ownOffers.GetChildren()){_ownOffers.RemoveChild(n);n.QueueFree();}
            _ownOffers.AddChild(TabletDesign.Heading(L("Мои опубликованные лоты","My published lots"),22));
            var mine=market.Offers.Where(o=>o.Seller==_view.Participant.Id).ToArray();
            foreach(var own in mine)_ownOffers.AddChild(new Label{Text="#"+own.Id+" · "+PlayerText.Item(_language,own.ItemId)+" ×"+own.Quantity+" · "+PlayerText.Money(own.BasePriceMinor)+" · "+(own.Status==MarketOfferStatus.Closed?L("Закрыт","Closed"):own.Status==MarketOfferStatus.Auction?L("Аукцион","Auction"):L("Сбор заявок","Collecting requests")),AutowrapMode=TextServer.AutowrapMode.WordSmart});
            if(mine.Length==0)_ownOffers.AddChild(new Label{Text=L("Вы ещё не публиковали лоты.","You have not published any lots yet.")});
            foreach (Node n in _rows.GetChildren()){_rows.RemoveChild(n);n.QueueFree();}
            var offers = market.Offers.Where(o => (_preferences.ItemId.Length == 0 || o.ItemId == _preferences.ItemId));
            offers = _preferences.HighestPriceFirst ? offers.OrderByDescending(o => o.BasePriceMinor).ThenBy(o => o.Id) : offers.OrderBy(o => o.BasePriceMinor).ThenBy(o => o.Id);
            foreach (var offer in offers)
            {
                var row = new TabletColumns{Breakpoint=800}; _rows.AddChild(TabletDesign.Wrap(row));
                row.AddChild(TabletDesign.Art(offer.ItemId,new(48,48)));
                var text = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart }; row.AddChild(text);
                Action(row, L("Выбрать", "Select") + " #" + offer.Id, () => { _offer = offer.Id; _price.Value = offer.BasePriceMinor / 100d; UpdateActions(); });
                var bar = new ProgressBar { CustomMinimumSize = new Vector2(100,8), ShowPercentage = false, MaxValue = 100 }; row.AddChild(bar);
                _windows.Add((offer.Id,text,bar));
            }
            if (!_windows.Any()) _rows.AddChild(new Label { Text = L("Лотов пока нет. Включите время и дождитесь публикации продавцом.", "No lots yet. Run time and wait for a seller to publish.") });
        }
        foreach (var row in _windows)
        {
            var o = market.Offers.Single(o => o.Id == row.Id);
            row.Text.Text = $"#{o.Id} · " + PlayerText.Item(_language,o.ItemId) + $" ×{o.Quantity} · " + PlayerText.Money(o.BasePriceMinor) +
                L(" за 1 · продавец ", " per unit · seller ") + o.Seller.Value + $" · ({o.Position.X},{o.Position.Z}) · " +
                (o.Status == MarketOfferStatus.Closed ? L("Закрыт", "Closed") : o.Status == MarketOfferStatus.Auction ? L("Аукцион, раунд ", "Auction, round ") + o.Round : L("Сбор заявок", "Collecting requests")) +
                L(" · осталось секунд: ", " · seconds left: ") + Math.Max(0,o.CutoffTick-_view.Tick) +
                (o.Bids.Length == 0 ? "" : "\n" + L("Публичные ставки: ", "Public bids: ") + string.Join("; ",o.Bids.Select(b => $"#{b.Buyer.Value} ×{b.Quantity}: " + PlayerText.Money(b.CurrentPriceMinor))));
            int duration = o.Status == MarketOfferStatus.Auction ? market.Rules.AuctionRoundTicks : _scenario.TradeWindowTicks;
            row.Bar.Value = o.Status == MarketOfferStatus.Closed ? 100 : 100d * Math.Clamp(duration-Math.Max(0,o.CutoffTick-_view.Tick),0,duration)/duration;
        }
        foreach (Node n in _pickups.GetChildren()){_pickups.RemoveChild(n);n.QueueFree();}
        _collectButtons.Clear();
        foreach (var claim in market.Allocations.Where(a => a.Buyer == _view.Participant.Id && a.RemainingQuantity > 0))
        {
            var offer = market.Offers.SingleOrDefault(o => o.Id == claim.OfferId);
            var row = new TabletColumns{Breakpoint=750}; _pickups.AddChild(TabletDesign.Wrap(row));
            row.AddChild(new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = $"#{claim.Id} · " + PlayerText.Item(_language,claim.ItemId) + $" ×{claim.RemainingQuantity} · " +
                L("забрать в течение ","collect within ") + Math.Max(0,claim.DeadlineTick-_view.Tick) + L(" с", " s") + (offer is null ? "" : $" · ({offer.Position.X},{offer.Position.Z})") });
            var collect = Action(row,L("Забрать", "Collect"),()=>Collect(claim.Id));
            _collectButtons.Add((claim.Id,collect));
            collect.TooltipText = L("Подойдите к точке выдачи лота. Выдача ограничена выбранным количеством и местом в рюкзаке.","Visit the lot pickup point. Pickup is limited by the selected quantity and backpack capacity.");
        }
        if(_collectButtons.Count==0)_pickups.AddChild(new Label{Text=L("Оплаченных покупок к выдаче нет.","No paid purchases awaiting collection."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        _history.Text = L("Результаты: ","Results: ") + string.Join("; ",market.Allocations.TakeLast(8).Select(a => $"#{a.Id} " + ClaimStatus(a.Status) + " · " + L("выдано ","collected ") + a.CollectedQuantity + L(" · штраф "," · fine ") + PlayerText.Money(a.FineMinor)));
        UpdateActions();
    }
    private string ClaimStatus(string status) => status switch
    {
        "AwaitingPickup" => L("Ожидает получения", "Awaiting pickup"),
        "PartiallyCollected" => L("Получено частично", "Partially collected"),
        "Fulfilled" => L("Получено полностью", "Collected in full"),
        "Expired" => L("Срок получения истёк", "Pickup expired"),
        _ => status
    };
    private void UpdateActions()
    {
        if (_view?.Market is not { } market) return;
        var offer = market.Offers.SingleOrDefault(o => o.Id == _offer);
        var bid = market.Bids.SingleOrDefault(b => b.OfferId == _offer);
        _selected.Text = offer is null ? L("Выберите лот; затем укажите количество и максимальную цену за 1.","Select a lot, then enter quantity and maximum unit price.") :
            $"#{offer.Id} · " + PlayerText.Item(_language,offer.ItemId) + (bid is null ? "" : L(" · моя ставка "," · my bid ") + PlayerText.Money(bid.CurrentPriceMinor) + L(" · мой лимит "," · my limit ") + PlayerText.Money(bid.MaximumPriceMinor));
        bool unavailable = _pending || offer is null || offer.Seller == _view.Participant.Id || offer.Status == MarketOfferStatus.Closed;
        _buy.Disabled = !_view.TradingParticipants.Contains(_view.Participant.Id) || unavailable || offer?.Status != MarketOfferStatus.Collecting || Price < offer?.BasePriceMinor || (Int128)Quantity * (offer?.BasePriceMinor ?? 0) > _view.AvailableCashMinor + (bid is null ? 0 : (Int128)bid.Quantity*bid.CurrentPriceMinor);
        _raise.Disabled = !_view.TradingParticipants.Contains(_view.Participant.Id) || unavailable || offer?.Status != MarketOfferStatus.Auction || bid is null || Price > bid.MaximumPriceMinor || Price-bid.CurrentPriceMinor < market.Rules.MinimumRaiseMinor;
        _withdraw.Disabled = unavailable || bid is null;
        foreach (var row in _collectButtons)
        {
            var claim = market.Allocations.Single(a => a.Id == row.Id);
            var source = market.Offers.SingleOrDefault(o => o.Id == claim.OfferId);
            row.Button.Disabled = _pending || _view.Participant.Busy || PickupQuantity(claim) <= 0 || _view.PurchaseGuides.SingleOrDefault(g=>g.PurchaseId==claim.Id)?.Position != _view.Participant.Position;
        }
        var shop = _view.Buildings.SingleOrDefault(b => b.Id.Value == _shop && b.Kind == BuildingKind.Shop);
        ApplyPage();
        if (shop is null) return;
        var sources=_view.Inventories.Where(i=>_view.Buildings.Any(b=>b.InputId==i.Id||b.OutputId==i.Id)).ToArray();
        string sourcesKey=string.Join(',',sources.Select(i=>i.Id.Value));
        if(sourcesKey!=_sourcesKey){_sourcesKey=sourcesKey;_source.Clear();foreach(var i in sources){_source.AddItem("#"+i.Id.Value+$" ({i.Position.X},{i.Position.Z})");_source.SetItemMetadata(_source.ItemCount-1,i.Id.Value);}}
        var storage=Source()!;
        var pack = _view.Inventories.Single(i => i.Id == _view.Participant.InventoryId);
        long Free(InventoryView i) => i.Stock.Where(s=>s.ItemId==_item).Sum(s=>s.Quantity-s.Reserved);
        long volume = _scenario.Items.Single(i=>i.Id==_item).UnitVolume;
        _stock.Text = PlayerText.Item(_language,_item) + L(" · свободно в источнике "," · free source stock ") + Free(storage) + L(" · в рюкзаке "," · backpack ") + Free(pack);
        _publish.Disabled = _pending || Free(storage)<Quantity || Denied(_publish,PlayerDesk.PublishLot(_shop,_item,Quantity,Price,storage.Id.Value));
        _deposit.Disabled = _pending || _view.Participant.Busy || Free(pack)<Quantity || (Int128)Quantity*volume > storage.Capacity-storage.OccupiedVolume-storage.ReservedCapacity || Denied(_deposit,PlayerDesk.Transfer(pack.Id.Value,storage.Id.Value,_item,Quantity));
        _take.Disabled = _pending || _view.Participant.Busy || Free(storage)<Quantity || (Int128)Quantity*volume > pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity || Denied(_take,PlayerDesk.Transfer(storage.Id.Value,pack.Id.Value,_item,Quantity));
    }
    private bool Denied(Button button,GameAction action)
    {var access=_access?.Invoke(action);if(access is null || access.Allowed)return false;button.TooltipText=PlayerText.Reason(_language,access.ReasonCode);return true;}
}

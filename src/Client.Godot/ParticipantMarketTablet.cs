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
    private readonly Label _selected = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _stock = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _history = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly SpinBox _quantity = new() { MinValue = 1, MaxValue = 1000000, Value = 1, Step = 1 };
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
        long shop, System.Action<GameAction> submit)
    {
        _scenario = scenario; _language = language; _preferences = preferences; _shop = shop; _submit = submit;
        _item = scenario.Items[0].Id; _price.Value = scenario.Items[0].BasePriceMinor / 100d;
        AddChild(new Label { Text = L("Продавец кладёт товар в свою лавку и публикует лот. Покупатель подаёт заявку; при дефиците идёт аукцион. Оплаченное нужно забрать в лавке продавца до срока — иначе штраф.",
            "Sellers deposit stock in their shop, then publish a lot. Buyers request a quantity; shortages trigger an auction. Collect paid goods at the seller's shop before the deadline to avoid a fine."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var filters = new HBoxContainer(); AddChild(filters);
        var items = new[] { "" }.Concat(scenario.Items.Select(i => i.Id)).ToArray();
        var filter = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (string item in items) filter.AddItem(item.Length == 0 ? L("Все товары", "All goods") : PlayerText.Item(language, item));
        filter.Selected = Math.Max(0, Array.IndexOf(items, preferences.ItemId));
        filter.ItemSelected += i => { preferences.ItemId = items[(int)i]; _key = ""; Refresh(); }; filters.AddChild(filter);
        var sort = new OptionButton { FocusMode = FocusModeEnum.None };
        sort.AddItem(L("Сначала дешевле", "Cheapest first")); sort.AddItem(L("Сначала дороже", "Highest first"));
        sort.Selected = preferences.HighestPriceFirst ? 1 : 0;
        sort.ItemSelected += i => { preferences.HighestPriceFirst = i == 1; _key = ""; Refresh(); }; filters.AddChild(sort);
        var inputs = new HBoxContainer(); AddChild(inputs);
        inputs.AddChild(new Label { Text = L("Количество", "Quantity") }); inputs.AddChild(_quantity);
        inputs.AddChild(new Label { Text = L("Цена/лимит за 1", "Unit price/limit") }); inputs.AddChild(_price);
        AddChild(_selected);
        var actions = new HBoxContainer(); AddChild(actions);
        _buy = Action(actions, L("Подать заявку", "Request goods"), () => _submit(PlayerDesk.Bid(_offer, Quantity, Price)));
        _raise = Action(actions, L("Повысить ставку до цены", "Raise bid to price"), () => {
            var offer = _view?.Market?.Offers.SingleOrDefault(o => o.Id == _offer);
            if (offer is not null) _submit(PlayerDesk.RaiseBid(_offer, offer.Round, Price)); });
        _withdraw = Action(actions, L("Отозвать заявку", "Withdraw request"), () => _submit(PlayerDesk.WithdrawBid(_offer, _view?.Market?.Offers.SingleOrDefault(o => o.Id == _offer)?.Round ?? 0)));
        _quantity.ValueChanged += _ => UpdateActions(); _price.ValueChanged += _ => UpdateActions();
        AddChild(_rows);
        AddChild(_seller);
        _seller.AddChild(new Label { Text = L("Моя лавка: товар для продажи должен лежать здесь, а не в рюкзаке.", "My shop: sale stock must be here, not in your backpack.") });
        var goods = new HBoxContainer(); _seller.AddChild(goods);
        var goodsList = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var item in scenario.Items) goodsList.AddItem(PlayerText.Item(language, item.Id));
        goodsList.ItemSelected += i => { _item = scenario.Items[(int)i].Id; _price.Value = scenario.Items[(int)i].BasePriceMinor / 100d; Refresh(); };
        goods.AddChild(goodsList);
        _deposit = Action(goods, L("Положить из рюкзака", "Deposit from backpack"), () => Transfer(true));
        _take = Action(goods, L("Забрать в рюкзак", "Take to backpack"), () => Transfer(false));
        _publish = Action(goods, L("Опубликовать лот", "Publish lot"), () => _submit(PlayerDesk.PublishLot(_shop, _item, Quantity, Price)));
        _seller.AddChild(_stock);
        AddChild(new Label { Text = L("Мои покупки: физическое получение", "My purchases: physical pickup") });
        AddChild(_pickups); AddChild(_history);
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
        if (building is null || _view is null) return;
        _submit(PlayerDesk.Transfer(deposit ? _view.Participant.InventoryId.Value : building.InputId.Value,
            deposit ? building.InputId.Value : _view.Participant.InventoryId.Value, _item, Quantity));
    }
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
            foreach (Node n in _rows.GetChildren()) n.QueueFree();
            var offers = market.Offers.Where(o => (_shop == 0 || o.Shop.Value == _shop) && (_preferences.ItemId.Length == 0 || o.ItemId == _preferences.ItemId));
            offers = _preferences.HighestPriceFirst ? offers.OrderByDescending(o => o.BasePriceMinor).ThenBy(o => o.Id) : offers.OrderBy(o => o.BasePriceMinor).ThenBy(o => o.Id);
            foreach (var offer in offers)
            {
                var row = new HBoxContainer(); _rows.AddChild(row);
                row.AddChild(new ResourceIcon { Item = offer.ItemId, CustomMinimumSize = new Vector2(26,26) });
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
        foreach (Node n in _pickups.GetChildren()) n.QueueFree();
        _collectButtons.Clear();
        foreach (var claim in market.Allocations.Where(a => a.Buyer == _view.Participant.Id && a.RemainingQuantity > 0))
        {
            var offer = market.Offers.SingleOrDefault(o => o.Id == claim.OfferId);
            var row = new HBoxContainer(); _pickups.AddChild(row);
            row.AddChild(new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = $"#{claim.Id} · " + PlayerText.Item(_language,claim.ItemId) + $" ×{claim.RemainingQuantity} · " +
                L("забрать в течение ","collect within ") + Math.Max(0,claim.DeadlineTick-_view.Tick) + L(" с", " s") + (offer is null ? "" : $" · ({offer.Position.X},{offer.Position.Z})") });
            var collect = Action(row,L("Забрать", "Collect"),()=>Collect(claim.Id));
            _collectButtons.Add((claim.Id,collect));
            collect.TooltipText = L("Подойдите к лавке продавца. Выдача ограничена выбранным количеством и местом в рюкзаке.","Visit the seller's shop. Pickup is limited by the selected quantity and backpack capacity.");
        }
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
        _buy.Disabled = unavailable || offer?.Status != MarketOfferStatus.Collecting || Price < offer?.BasePriceMinor || (Int128)Quantity * (offer?.BasePriceMinor ?? 0) > _view.AvailableCashMinor + (bid is null ? 0 : (Int128)bid.Quantity*bid.CurrentPriceMinor);
        _raise.Disabled = unavailable || offer?.Status != MarketOfferStatus.Auction || bid is null || Price > bid.MaximumPriceMinor || Price-bid.CurrentPriceMinor < market.Rules.MinimumRaiseMinor;
        _withdraw.Disabled = unavailable || bid is null;
        foreach (var row in _collectButtons)
        {
            var claim = market.Allocations.Single(a => a.Id == row.Id);
            var source = market.Offers.SingleOrDefault(o => o.Id == claim.OfferId);
            row.Button.Disabled = _pending || _view.Participant.Busy || PickupQuantity(claim) <= 0 || source?.Position != _view.Participant.Position;
        }
        var shop = _view.Buildings.SingleOrDefault(b => b.Id.Value == _shop && b.Kind == BuildingKind.Shop);
        _seller.Visible = shop is not null;
        if (shop is null) return;
        var storage = _view.Inventories.Single(i => i.Id == shop.InputId);
        var pack = _view.Inventories.Single(i => i.Id == _view.Participant.InventoryId);
        long Free(InventoryView i) => i.Stock.Where(s=>s.ItemId==_item).Sum(s=>s.Quantity-s.Reserved);
        long volume = _scenario.Items.Single(i=>i.Id==_item).UnitVolume;
        _stock.Text = PlayerText.Item(_language,_item) + L(" · свободно в лавке "," · free shop stock ") + Free(storage) + L(" · в рюкзаке "," · backpack ") + Free(pack);
        _publish.Disabled = _pending || Free(storage)<Quantity;
        _deposit.Disabled = _pending || _view.Participant.Busy || Free(pack)<Quantity || (Int128)Quantity*volume > storage.Capacity-storage.OccupiedVolume-storage.ReservedCapacity;
        _take.Disabled = _pending || _view.Participant.Busy || Free(storage)<Quantity || (Int128)Quantity*volume > pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity;
    }
}

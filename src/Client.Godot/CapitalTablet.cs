using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Capital spot orders only. Participant lots and future contracts remain separate.</summary>
public partial class CapitalTablet : VBoxContainer
{
    private sealed class OfferRow
    {
        public required CapitalOfferView Offer;
        public required Label Terms;
        public required Label Deadline;
        public required ProgressBar Window;
        public required Label WindowText;
        public required Button One;
        public required Button All;
        public required Button Cancel;
        public long SellQuantity;
    }
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly CapitalTablePreferences _preferences;
    private readonly System.Action<GameAction> _submit;
    private readonly VBoxContainer _rows;
    private readonly Label _history;
    private readonly Label _summary;
    private readonly Dictionary<long, OfferRow> _offers = [];
    private string _key = "";
    private PlayerView? _view;
    private OwnCapitalTrading? _trading;
    private bool _pending;
    private bool _paused;

    public CapitalTablet(ScenarioDefinition scenario, string language, CapitalTablePreferences preferences, System.Action<GameAction> submit)
    {
        _scenario = scenario; _language = language; _preferences = preferences; _submit = submit;
        AddChild(new Label { Text = L("Столица покупает ваши товары. Цена — за 1 единицу. Для продажи принесите товар сюда.", "The capital buys your goods. Prices are per unit. Bring goods here to sell."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var filters = new HBoxContainer(); AddChild(filters);
        filters.AddChild(new Label { Text = L("Товар", "Goods"), VerticalAlignment = VerticalAlignment.Center });
        var goods = new OptionButton { FocusMode = FocusModeEnum.None };
        var ids = new[] { "" }.Concat(scenario.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal)).ToArray();
        foreach (string id in ids) goods.AddItem(id.Length == 0 ? L("Все товары", "All goods") : PlayerText.Item(language, id));
        goods.Selected = Math.Max(0, Array.IndexOf(ids, preferences.ItemId));
        goods.ItemSelected += index => { preferences.ItemId = ids[(int)index]; RefreshRows(); }; filters.AddChild(goods);
        var sort = new OptionButton { FocusMode = FocusModeEnum.None };
        sort.AddItem(L("Цена: сначала дороже", "Price: highest first")); sort.AddItem(L("Цена: сначала дешевле", "Price: lowest first"));
        sort.Selected = preferences.HighestPriceFirst ? 0 : 1;
        sort.ItemSelected += index => { preferences.HighestPriceFirst = index == 0; RefreshRows(); }; filters.AddChild(sort);
        _summary = new Label(); AddChild(_summary);
        AddChild(new HSeparator());
        _rows = new VBoxContainer(); AddChild(_rows);
        AddChild(new HSeparator());
        AddChild(new Label { Text = L("Ваши последние результаты", "Your recent results") });
        _history = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(_history);
    }
    public void ShowData(PlayerView view, OwnCapitalTrading trading, bool pending, bool paused)
    { _view = view; _trading = trading; _pending = pending; _paused = paused; RefreshRows(); }

    private void RefreshRows()
    {
        if (_view is not { } view || _trading is not { } trading) return;
        var offers = _preferences.Offers(view, view.Participant.Position);
        string key = string.Join(",", offers.Select(o => o.Id));
        if (key != _key || _rows.GetChildCount() == 0)
        {
            _key = key;
            foreach (Node node in _rows.GetChildren()) { _rows.RemoveChild(node); node.QueueFree(); }
            _offers.Clear();
            if (offers.Length == 0) _rows.AddChild(new Label { Text = L("Подходящих заказов сейчас нет. Измените фильтр или запустите время до следующей публикации.", "No matching orders. Change the filter or run time until the next publication."), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            foreach (var offer in offers) AddOffer(offer);
        }
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        _summary.Text = L("Доступные деньги: ", "Available money: ") + PlayerText.Money(view.AvailableCashMinor) + L(" · Заказов: ", " · Orders: ") + offers.Length;
        foreach (var offer in offers)
        {
            var row = _offers[offer.Id]; row.Offer = offer;
            var stock = pack.Stock.SingleOrDefault(s => s.ItemId == offer.ItemId);
            long free = (stock?.Quantity ?? 0) - (stock?.Reserved ?? 0);
            row.SellQuantity = Math.Min(free, offer.RemainingQuantity);
            var request = trading.Requests.SingleOrDefault(r => r.OfferId == offer.Id);
            row.Terms.Text = PlayerText.Item(_language, offer.ItemId) + $" · #{offer.Id}\n" + L("Цена: ", "Price: ") + PlayerText.Money(offer.UnitPriceMinor) + L(" / шт. · спрос: ", " / unit · demand: ") + offer.RemainingQuantity +
                "\n" + L("Ваш запас: свободно ", "Your stock: free ") + free + L(", зарезервировано ", ", reserved ") + (stock?.Reserved ?? 0);
            row.Deadline.Text = offer.ExpiresAtTick is long end ? L("Заказ действует ещё ", "Order expires in ") + TabletProgress.Remaining(end, view.Tick) + L(" с", " s") : L("Заказ без срока истечения", "Order has no expiry");
            var progress = TabletProgress.Window(offer, view.Tick, _scenario.TradeWindowTicks);
            row.Window.MaxValue = progress.Total; row.Window.Value = progress.Done;
            row.WindowText.Text = (offer.CutoffTick is long cutoff
                ? L("Распределение через ", "Allocation in ") + TabletProgress.Remaining(cutoff, view.Tick) + L(" с", " s")
                : L("Окно откроется после первой заявки", "Window opens after the first request")) + (_paused ? L(" · пауза", " · paused") : "");
            row.One.Disabled = _pending || view.Participant.Busy || free < 1;
            row.All.Disabled = _pending || view.Participant.Busy || row.SellQuantity < 1;
            row.All.Text = L("Продать до ", "Sell up to ") + row.SellQuantity;
            string why = view.Participant.Busy ? L("Персонаж занят; дождитесь результата или отмените свою заявку.", "Participant busy; wait for the result or cancel your request.") : free < 1 ? L("Принесите свободный товар в личном инвентаре.", "Bring unreserved goods in your inventory.") : L("Товар резервируется до закрытия окна; распределение может быть частичным.", "Goods are reserved until closing; allocation may be partial.");
            row.One.TooltipText = row.All.TooltipText = why;
            row.Cancel.Visible = request is not null; row.Cancel.Disabled = _pending;
            if (request is not null) row.Deadline.Text += "\n" + L("Ваша заявка: ", "Your request: ") + request.Quantity + L(" · ждёт распределения", " · awaiting allocation");
        }
        _history.Text = trading.Settlements.Length == 0 ? L("Сделок ещё нет. Товар и деньги переходят при закрытии торгового окна.", "No completed trades yet. Goods and money transfer when the trading window closes.")
            : string.Join("\n", trading.Settlements.Take(5).Select(s => PlayerText.Item(_language,s.ItemId) + L(": принято ", ": accepted ") + $"{s.Accepted}/{s.Requested} · +{PlayerText.Money(s.PaidMinor)} · " + L("тик ", "tick ") + s.Tick));
    }
    private void AddOffer(CapitalOfferView offer)
    {
        var panel = new PanelContainer(); _rows.AddChild(panel);
        var row = new HBoxContainer(); panel.AddChild(row);
        row.AddChild(new ResourceIcon { Item = offer.ItemId, CustomMinimumSize = new Vector2(30, 30) });
        var terms = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300,0), AutowrapMode = TextServer.AutowrapMode.WordSmart }; row.AddChild(terms);
        var window = new VBoxContainer { CustomMinimumSize = new Vector2(235,0) }; row.AddChild(window);
        var deadline = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; window.AddChild(deadline);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(220,10) }; window.AddChild(bar);
        var left = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; window.AddChild(left);
        var actions = new VBoxContainer { CustomMinimumSize = new Vector2(150,0) }; row.AddChild(actions);
        var one = Action(actions, L("Продать 1", "Sell 1")); var all = Action(actions, ""); var cancel = Action(actions, L("Отменить заявку", "Cancel request"));
        var model = new OfferRow { Offer = offer, Terms = terms, Deadline = deadline, Window = bar, WindowText = left, One = one, All = all, Cancel = cancel };
        one.Pressed += () => _submit(PlayerDesk.Sell(model.Offer.Id, model.Offer.ItemId, 1));
        all.Pressed += () => { if (model.SellQuantity > 0) _submit(PlayerDesk.Sell(model.Offer.Id, model.Offer.ItemId, model.SellQuantity)); };
        cancel.Pressed += () => _submit(PlayerDesk.CancelSale(model.Offer.Id));
        _offers.Add(offer.Id, model);
    }
    private static Button Action(Container parent, string text)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None }; parent.AddChild(button); return button; }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

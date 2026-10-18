using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Compact public capital offers and a separate own-results page.</summary>
public partial class CapitalTablet : VBoxContainer
{
    private sealed class OfferRow
    {
        public required CapitalOfferView Offer;
        public required Label Terms;
        public required Label Deadline;
        public required ProgressBar Window;
        public required Label WindowText;
        public required Control WindowBox;
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
    private readonly VBoxContainer _orders;
    private readonly VBoxContainer _results;
    private readonly Label _history;
    private readonly Label _summary;
    private readonly Dictionary<long, OfferRow> _offers = [];
    private string _key = "";
    private int _historyCount = -1;
    private PlayerView? _view;
    private OwnCapitalTrading? _trading;
    private bool _pending;

    public CapitalTablet(ScenarioDefinition scenario, string language, CapitalTablePreferences preferences, System.Action<GameAction> submit)
    {
        _scenario = scenario; _language = language; _preferences = preferences; _submit = submit;
        var pages = new HBoxContainer(); AddChild(pages);
        var orders = Action(pages, L("Предложения", "Offers")); var history = Action(pages, L("История", "History"));
        _orders = new VBoxContainer(); AddChild(_orders);
        _results = new VBoxContainer { Visible = false }; AddChild(_results);
        orders.Pressed += () => { _orders.Visible = true; _results.Visible = false; };
        history.Pressed += () => { _orders.Visible = false; _results.Visible = true; };
        var filters = new HBoxContainer(); _orders.AddChild(filters);
        var goods = new OptionButton { FocusMode = FocusModeEnum.None };
        var ids = new[] { "" }.Concat(scenario.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal)).ToArray();
        foreach (string id in ids) goods.AddItem(id.Length == 0 ? L("Все товары", "All goods") : PlayerText.Item(language, id));
        goods.Selected = Math.Max(0, Array.IndexOf(ids, preferences.ItemId));
        goods.ItemSelected += index => { preferences.ItemId = ids[(int)index]; RefreshRows(); }; filters.AddChild(goods);
        var sort = new OptionButton { FocusMode = FocusModeEnum.None };
        sort.AddItem(L("Сначала дороже", "Highest price first")); sort.AddItem(L("Сначала дешевле", "Lowest price first"));
        sort.Selected = preferences.HighestPriceFirst ? 0 : 1;
        sort.ItemSelected += index => { preferences.HighestPriceFirst = index == 0; RefreshRows(); }; filters.AddChild(sort);
        _summary = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right }; filters.AddChild(_summary);
        _rows = new VBoxContainer(); _orders.AddChild(_rows);
        _history = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _results.AddChild(_history);
    }
    public void ShowData(PlayerView view, OwnCapitalTrading trading, bool pending, bool paused)
    { _view = view; _trading = trading; _pending = pending; RefreshRows(); }

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
            if (offers.Length == 0) _rows.AddChild(new Label { Text = L("Нет подходящих предложений", "No matching offers") });
            foreach (var offer in offers) AddOffer(offer);
        }
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        _summary.Text = L("Предложений: ", "Offers: ") + offers.Length;
        foreach (var offer in offers)
        {
            var row = _offers[offer.Id]; row.Offer = offer;
            var stock = pack.Stock.SingleOrDefault(s => s.ItemId == offer.ItemId);
            long free = (stock?.Quantity ?? 0) - (stock?.Reserved ?? 0);
            row.SellQuantity = Math.Min(free, offer.RemainingQuantity);
            var request = trading.Requests.SingleOrDefault(r => r.OfferId == offer.Id);
            row.Terms.Text = PlayerText.Item(_language, offer.ItemId) + "  ·  " + PlayerText.Money(offer.UnitPriceMinor) + L(" / шт.", " / unit") +
                "\n" + L("Спрос: ", "Demand: ") + offer.RemainingQuantity + L("   ·   Доступно: ", "   ·   Available: ") + free;
            row.Terms.TooltipText = request is null ? "" : L("Ваша заявка: ", "Your request: ") + request.Quantity;
            row.Deadline.Text = request is not null ? L("Ваша заявка: ", "Your request: ") + request.Quantity :
                offer.ExpiresAtTick is long end ? L("Заказ ещё ", "Order expires in ") + TabletProgress.Remaining(end, view.Tick) + L(" с", " s") : "";
            var progress = TabletProgress.Window(offer, view.Tick, _scenario.TradeWindowTicks);
            row.Window.MaxValue = progress.Total; row.Window.Value = progress.Done;
            row.WindowBox.Visible = offer.CutoffTick.HasValue;
            row.WindowText.Text = offer.CutoffTick is long cutoff ? L("Расчёт через ", "Settlement in ") + TabletProgress.Remaining(cutoff, view.Tick) + L(" с", " s") : "";
            row.One.Visible = row.SellQuantity > 1;
            row.One.Disabled = _pending || view.Participant.Busy || free < 1;
            row.All.Disabled = _pending || view.Participant.Busy || row.SellQuantity < 1;
            row.All.Text = row.SellQuantity > 0 ? L("Продать ", "Sell ") + row.SellQuantity : L("Продать", "Sell");
            string why = view.Participant.Busy ? L("Персонаж занят", "Participant busy") : free < 1 ? L("Нет доступного товара в рюкзаке", "No available goods in your backpack") : L("Подать заявку на продажу", "Submit a sale request");
            row.One.TooltipText = row.All.TooltipText = why;
            row.Cancel.Visible = request is not null; row.Cancel.Disabled = _pending;
        }
        if (_historyCount == trading.Settlements.Length) return;
        _historyCount = trading.Settlements.Length;
        _history.Text = trading.Settlements.Length == 0 ? L("Завершённых сделок пока нет", "No completed trades yet") :
            string.Join("\n\n", trading.Settlements.OrderByDescending(s => s.Tick).Select(s =>
                PlayerText.Item(_language, s.ItemId) + L("  ·  Продано ", "  ·  Sold ") + $"{s.Accepted}/{s.Requested}  ·  +{PlayerText.Money(s.PaidMinor)}" +
                "  ·  " + $"{s.Tick / 3600:00}:{s.Tick / 60 % 60:00}:{s.Tick % 60:00}"));
    }
    private void AddOffer(CapitalOfferView offer)
    {
        var panel = new PanelContainer(); _rows.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("30494d"),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12 });
        var row = new HBoxContainer(); panel.AddChild(row);
        row.AddChild(new ResourceIcon { Item = offer.ItemId, CustomMinimumSize = new Vector2(34, 34) });
        var terms = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(280, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart }; row.AddChild(terms);
        var timing = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) }; row.AddChild(timing);
        var deadline = new Label(); timing.AddChild(deadline);
        var window = new VBoxContainer(); timing.AddChild(window);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(175, 10) }; window.AddChild(bar);
        var left = new Label(); window.AddChild(left);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; row.AddChild(actions);
        var one = Action(actions, L("Продать 1", "Sell 1")); var all = Action(actions, ""); var cancel = Action(actions, L("Отменить", "Cancel"));
        var model = new OfferRow { Offer = offer, Terms = terms, Deadline = deadline, Window = bar, WindowText = left, WindowBox = window, One = one, All = all, Cancel = cancel };
        one.Pressed += () => _submit(PlayerDesk.Sell(model.Offer.Id, model.Offer.ItemId, 1));
        all.Pressed += () => { if (model.SellQuantity > 0) _submit(PlayerDesk.Sell(model.Offer.Id, model.Offer.ItemId, model.SellQuantity)); };
        cancel.Pressed += () => _submit(PlayerDesk.CancelSale(model.Offer.Id));
        _offers.Add(offer.Id, model);
    }
    private static Button Action(Container parent, string text)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; parent.AddChild(button); return button; }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

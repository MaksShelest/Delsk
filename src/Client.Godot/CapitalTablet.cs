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
        public required Label Why;
        public required Button Go;
        public long SellQuantity;
    }
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly CapitalTablePreferences _preferences;
    private readonly System.Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess>? _access;
    private readonly VBoxContainer _rows;
    private readonly VBoxContainer _orders;
    private readonly VBoxContainer _results;
    private readonly Label _history;
    private readonly Label _summary;
    private readonly HFlowContainer _pageButtons;
    public void ShowPage(bool history){_pageButtons.Visible=false;_orders.Visible=!history;_results.Visible=history;}
    private readonly Dictionary<long, OfferRow> _offers = [];
    private string _key = "",_packKey="";private readonly HFlowContainer _quickGoods=TabletDesign.Flow();private OptionButton _goodsFilter=null!;private string[] _goodsIds=[];
    private int _historyCount = -1;
    private PlayerView? _view;
    private OwnCapitalTrading? _trading;
    private bool _pending;

    public CapitalTablet(ScenarioDefinition scenario, string language, CapitalTablePreferences preferences, System.Action<GameAction> submit,Func<GameAction,ActionAccess>? access=null,bool friendly=false)
    {
        _scenario = scenario; _language = language; _preferences = preferences; _submit = submit;_access=access;
        var pages = TabletDesign.Flow();_pageButtons=pages; AddChild(pages);
        var orders = Action(pages, L("Предложения", "Offers")); var history = Action(pages, L("История", "History"));
        _orders = new VBoxContainer(); AddChild(_orders);
        _orders.AddChild(friendly?GameUi.Text(L("Выберите заказ на товар из рюкзака. Продать его можно на терминале.","Choose an order for goods in your backpack. Sell them at the terminal."),18)
            :GameUi.Hint(L("Столица покупает товары из вашего рюкзака. Выберите заказ и продайте доступное количество у точки приёмки.","The capital buys goods from your backpack. Choose an order and sell available stock at its receiving point.")));
        _results = new VBoxContainer { Visible = false }; AddChild(_results);
        orders.Pressed += () => { _orders.Visible = true; _results.Visible = false; };
        history.Pressed += () => { _orders.Visible = false; _results.Visible = true; };
        _orders.AddChild(_quickGoods);var filters = TabletDesign.Flow(); _orders.AddChild(filters);
        var goods = new OptionButton { FocusMode = FocusModeEnum.None };_goodsFilter=goods;
        var ids = new[] { "" }.Concat(scenario.Items.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal)).ToArray();
        _goodsIds=ids;foreach (string id in ids) goods.AddItem(id.Length == 0 ? L("Все товары", "All goods") : PlayerText.Item(language, id));
        goods.Selected = Math.Max(0, Array.IndexOf(ids, preferences.ItemId));
        goods.ItemSelected += index => { preferences.ItemId = ids[(int)index]; RefreshRows(); }; filters.AddChild(goods);
        var sort = new OptionButton { FocusMode = FocusModeEnum.None };
        sort.AddItem(L("Сначала дороже", "Highest price first")); sort.AddItem(L("Сначала дешевле", "Lowest price first"));
        sort.Selected = preferences.HighestPriceFirst ? 0 : 1;
        sort.ItemSelected += index => { preferences.HighestPriceFirst = index == 0; RefreshRows(); }; filters.AddChild(sort);
        _summary = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right }; filters.AddChild(_summary);
        _rows = new VBoxContainer(); _orders.AddChild(_rows);
        _history = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; _results.AddChild(TabletDesign.Wrap(_history));
        TabletDesign.CompactInputs(this);
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
        string packKey=string.Join('|',pack.Stock.Where(s=>s.Quantity>s.Reserved).Select(s=>s.ItemId+":"+(s.Quantity-s.Reserved)));if(packKey!=_packKey)
        {
            _packKey=packKey;foreach(Node child in _quickGoods.GetChildren()){_quickGoods.RemoveChild(child);child.QueueFree();}
            if(packKey.Length>0)_quickGoods.AddChild(GameUi.Hint(L("В рюкзаке:","In backpack:")));
            foreach(var held in pack.Stock.Where(s=>s.Quantity>s.Reserved))
            {string item=held.ItemId;_quickGoods.AddChild(GameUi.Button("CapitalGoods_"+item,PlayerText.Item(_language,item)+" ×"+(held.Quantity-held.Reserved),()=>{_preferences.ItemId=item;_goodsFilter.Select(Array.IndexOf(_goodsIds,item));RefreshRows();}));}
        }
        _summary.Text = L("Предложений: ", "Offers: ") + offers.Length+L(" · рюкзак "," · backpack ")+$"{pack.OccupiedVolume}/{pack.Capacity}";
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
            var permission=_access?.Invoke(PlayerDesk.Sell(offer.Id,offer.ItemId,Math.Max(1,row.SellQuantity)));
            row.One.Disabled = _pending || view.Participant.Busy || free < 1||permission?.Allowed==false;
            row.All.Disabled = _pending || view.Participant.Busy || row.SellQuantity < 1||permission?.Allowed==false;
            row.All.Text = row.SellQuantity > 0 ? L("Продать ", "Sell ") + row.SellQuantity : L("Продать", "Sell");
            string why = view.Participant.Busy ? L("Персонаж занят", "Participant busy") : free < 1 ? L("Нет доступного товара в рюкзаке", "No available goods in your backpack") : L("Подать заявку на продажу", "Submit a sale request");
            row.One.TooltipText = row.All.TooltipText = why;
            row.Why.Text=_pending?L("Заявка ожидает выполнения. Включите время.","Request queued. Run time to apply it."):permission?.Allowed==false?GameUi.Reason(_language,permission.ReasonCode):row.All.Disabled?why:"";row.Why.Visible=row.Why.Text.Length>0;
            row.Go.Visible=permission?.Allowed==false;row.Go.Disabled=_pending||view.Participant.Busy;
            row.Cancel.Visible = request is not null; row.Cancel.Disabled = _pending;
        }
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(_orders,Size.X);
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
        panel.AddThemeStyleboxOverride("panel",TabletDesign.Card());
        var row = new VBoxContainer();panel.AddChild(row);var header=new HBoxContainer();row.AddChild(header);
        header.AddChild(TabletDesign.Art(offer.ItemId,new(48,48)));
        var terms=GameUi.Text("",18);header.AddChild(terms);
        var timing=new VBoxContainer{CustomMinimumSize=new(180,0)};header.AddChild(timing);var deadline=GameUi.Hint("");timing.AddChild(deadline);
        var window=new VBoxContainer();timing.AddChild(window);var bar=new ProgressBar{ShowPercentage=false,CustomMinimumSize=new(175,10)};window.AddChild(bar);var left=GameUi.Hint("");window.AddChild(left);
        var actionBox=new VBoxContainer();row.AddChild(actionBox);var actions=TabletDesign.Flow();actionBox.AddChild(actions);
        var one = Action(actions, L("Продать 1", "Sell 1")); var all = Action(actions, ""); var cancel = Action(actions, L("Отменить", "Cancel"));
        var reason=GameUi.Hint("");actionBox.AddChild(reason);var go=Action(actions,L("Подойти","Go there"));
        var model = new OfferRow { Offer = offer, Terms = terms, Deadline = deadline, Window = bar, WindowText = left, WindowBox = window, One = one, All = all, Cancel = cancel,Why=reason,Go=go };
        one.Name="CapitalSellOne_"+offer.Id;all.Name="CapitalSellAll_"+offer.Id;cancel.Name="CapitalCancel_"+offer.Id;go.Name="CapitalGo_"+offer.Id;
        GameUi.Command(one,()=>{if(_pending)return;_pending=true;_submit(PlayerDesk.Sell(model.Offer.Id,model.Offer.ItemId,1));});
        GameUi.Command(all,()=>{if(_pending||model.SellQuantity<1)return;_pending=true;_submit(PlayerDesk.Sell(model.Offer.Id,model.Offer.ItemId,model.SellQuantity));});
        GameUi.Command(cancel,()=>{if(_pending)return;_pending=true;_submit(PlayerDesk.CancelSale(model.Offer.Id));});
        GameUi.Command(go,()=>{if(_pending)return;_pending=true;_submit(PlayerDesk.Move(model.Offer.Position.X,model.Offer.Position.Z));});
        _offers.Add(offer.Id, model);
    }
    private static Button Action(Container parent, string text)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) }; parent.AddChild(button); return button; }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

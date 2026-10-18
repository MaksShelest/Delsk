using Godot;
using Simulation.Hosting;

/// <summary>One contextual handbook. Pause belongs to Playfield and is restored when this closes.</summary>
public partial class HelpOverlay : ColorRect
{
    public event System.Action? Closed;
    private readonly string _language;
    private readonly RichTextLabel _body;
    private readonly OptionButton _topics;
    private static readonly string[] Keys = ["basics", "harvest", "land", "exchange", "market", "production", "workers", "logistics", "politics"];
    public HelpOverlay(string language, string topic)
    {
        _language = language;
        Color = new Color(0.04f, 0.08f, 0.09f, 0.88f);
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer(); panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.OffsetLeft = 32; panel.OffsetRight = -32; panel.OffsetTop = 72; panel.OffsetBottom = -32;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("293f44"),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18 });
        AddChild(panel);
        var column = new VBoxContainer(); panel.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        var title = new Label { Text = L("Справка", "Help"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 24); header.AddChild(title);
        header.AddChild(new Label { Text = L("Мир на паузе", "World paused") });
        var close = new Button { Text = "×", CustomMinimumSize = new Vector2(40, 36), FocusMode = FocusModeEnum.None };
        close.Pressed += () => Closed?.Invoke(); header.AddChild(close);
        _topics = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (string key in Keys) _topics.AddItem(Title(key));
        _topics.Selected = System.Math.Max(0, System.Array.IndexOf(Keys, topic));
        _topics.ItemSelected += _ => ShowTopic(); column.AddChild(_topics);
        _body = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeFontSizeOverride("normal_font_size", 18); column.AddChild(_body);
        ShowTopic();
    }
    private void ShowTopic() => _body.Text = "[b]" + Title(Keys[_topics.Selected]) + "[/b]\n\n" + Text(Keys[_topics.Selected]);
    private string Title(string key) => key switch
    {
        "harvest" => L("Добыча и инвентарь", "Harvest and inventory"),
        "land" => L("Земля и строительство", "Land and construction"),
        "exchange" => L("Биржа столицы", "Capital exchange"),
        "market" => L("Лавки участников", "Participant shops"),
        "production" => L("Производство и запасы", "Production and stock"),
        "workers" => L("Работники и зарплата", "Workers and wages"),
        "logistics" => L("Перевозки", "Transport"),
        "politics" => L("Выборы и поддержка", "Elections and support"),
        _ => L("Управление и время", "Controls and time")
    };
    private string Text(string key) => key switch
    {
        "harvest" => L("Встаньте на клетку ресурса и нажмите «Добывать». Добыча повторяется, пока вы не остановите её, не уйдёте или не заполните инвентарь.\n\nЗаполнение кнопки показывает текущий цикл. Товар появляется только после завершения цикла. При уходе незавершённая работа теряется, уже добытое остаётся.\n\nСверху показаны ваши ресурсы и занятый объём рюкзака. У разных товаров может быть разный объём.", "Stand on a resource cell and press Harvest. Cycles repeat until stopped, you leave, or your inventory fills.\n\nThe button fill shows the current cycle. Goods are awarded only when it completes. Leaving discards unfinished work; collected goods remain.\n\nThe top bar shows resources and used backpack volume. Items may have different unit volumes."),
        "land" => L("Купить или забронировать можно только отмеченный участок. Дороги и проходы не продаются.\n\nБронь временно защищает участок и оплачивается отдельно от покупки. Плата за бронь не возвращается. Для строительства нужны собственная свободная площадь, деньги и материалы в рюкзаке.\n\nМеста лавок общие. Каждый участник может купить или забронировать не больше одного такого места.", "Only designated plots can be bought or reserved. Roads and passages are protected.\n\nA reservation temporarily protects a plot and costs extra; the fee is not refunded. Construction requires owned free land, money and materials in your backpack.\n\nShop sites are shared. Each participant may own or reserve at most one site."),
        "exchange" => L("Столица покупает товары, которые вы принесли к бирже. Цена указана за одну единицу. «Доступно» — товар, которым вы сейчас можете распорядиться.\n\nНажатие «Продать» подаёт заявку. До завершения торгов товар удерживается для неё и не может быть обещан второй раз. Если спроса не хватает всем, принятый объём может оказаться меньше заявки.\n\nПеревод товара и денег происходит при закрытии торгового окна. На паузе можно подготовить действие, затем включить время. Результаты доступны на вкладке «История». Оставшийся срок заказа и время до расчёта — разные сроки.", "The capital buys goods brought to the exchange. Prices are per unit. Available stock excludes goods already committed.\n\nSell submits a request. Goods are held until the trading window closes and cannot be promised twice. If demand is insufficient, the accepted quantity may be lower than requested.\n\nGoods and money transfer at closing. You can prepare an action while paused, then run time. Results appear in History. Order expiry and time to settlement are separate deadlines."),
        "market" => L("Лавки продают реальные товары участников. Выберите лот, количество и предел цены. При конкуренции проводится аукцион. Деньги под заявку удерживаются.\n\nПокупка даёт право получить товар: его нужно забрать в лавке продавца до указанного срока. Следите за вместимостью рюкзака. Просрочка и частичное получение рассчитываются по правилам рынка.\n\nВ своей лавке можно передавать товары и публиковать лоты.", "Shops sell actual participant stock. Choose a lot, quantity and price limit. Competing requests enter an auction; request funds are held.\n\nA purchase grants a claim: collect it at the seller shop before the deadline. Check backpack capacity. Expiry and partial collection follow market rules.\n\nAt your shop you can transfer stock and publish lots."),
        "production" => L("Рецепт показывает, сколько сырья нужно и сколько продукции получится. Производству нужны сырьё, свободное место на выходе, работники и деньги на работу.\n\nВыключение производства сохраняет начатую партию. Заполнение показывает выполненную работу. Передача товаров работает только у объекта и учитывает свободный запас и вместимость.\n\nЗарезервированный товар уже обещан другой операции; зарезервированное место оставлено для ожидаемого груза.", "A recipe shows required inputs and resulting output. Production needs inputs, output space, workers and operating funds.\n\nStopping preserves the current batch. Progress shows completed work. Transfers require being at the object and respect available stock and capacity.\n\nReserved goods are committed to another operation; reserved capacity is held for expected cargo."),
        "workers" => L("«Работники: 1 из 2» означает, что нанят один человек при двух рабочих местах. Для запуска может требоваться минимальный штат.\n\nНайм и переход работников происходят постепенно. Новая ставка начинает влиять с задержкой. Зарплата платится за фактическую работу; при простое оплата не списывается, но люди постепенно уходят.\n\nСтавка показана за одного работника за рабочий тик. В обычной игре один тик соответствует одной секунде активного игрового времени.", "Workers: 1 of 2 means one hired worker and two workplaces. Starting may require a minimum crew.\n\nHiring and movement between employers take time. Wage changes have a delay. Pay is charged for actual work; idle workers are unpaid and gradually leave.\n\nWages are per worker per working tick. One tick corresponds to one second of active game time."),
        "logistics" => L("Грузовик предприятия может отвозить его продукцию на собственный склад или в лавку и подвозить сырьё оттуда. Выбирайте источник и получателя.\n\nРабота производства, подвоз сырья и отправка продукции включаются раздельно. Машине нужны товар, свободное место, экипаж и деньги на зарплату.\n\nВыключение направления не отменяет начатый рейс: машина заканчивает его. Нельзя менять маршрут уже выполняемого рейса.", "An enterprise truck can deliver its output to your warehouse or shop and collect inputs from there. Choose source and destination.\n\nProduction, inbound collection and outbound delivery are separate switches. Trucks need stock, destination space, crew and payroll funds.\n\nDisabling a direction does not cancel a trip in progress. An active trip route cannot be changed."),
        "politics" => L("Цель партии — победить на выборах. Голос участника и голоса его текущих работников образуют его блок. Для победы нужно строго больше половины голосов. Безработные в текущих правилах не голосуют.\n\nДоговор поддержки связывает голос после выполнения условий компенсации. Его подробности видят только стороны. Капитал и количество работников сами по себе не заменяют результат выборов.", "Win the election to win the game. Each participant controls their own vote and their current workers' votes. Victory requires strictly more than half. Unemployed citizens do not vote under current rules.\n\nSupport agreements bind votes after compensation conditions are fulfilled. Details are private to the parties. Capital and workforce alone do not replace the election result."),
        _ => L("WASD — движение. Стрелки выбирают контекстное действие, Enter или Space подтверждают. Мышью можно нажимать кнопки и узнавать сведения об объектах.\n\nEscape открывает меню и останавливает мир. Планшеты по умолчанию тоже открываются с паузой. Можно подготовить действие и включить время переключателем. Разрешённое движение возобновляет время; при выходе из области объекта планшет закрывается.\n\nСправка останавливает мир. После её закрытия сохраняется прежнее состояние паузы. В Debug действия выполняются по тикам.", "WASD moves. Arrows select a contextual action; Enter or Space confirms. Use the mouse for buttons and object information.\n\nEscape opens the menu and pauses the world. Tablets open paused by default. Prepare an action and run time with the switch. Allowed movement resumes time; leaving the object area closes its tablet.\n\nHelp pauses the world and preserves the previous pause state when closed. Debug executes actions one tick at a time.")
    };
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

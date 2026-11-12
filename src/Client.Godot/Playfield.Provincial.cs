using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private ProvincialProgramPanel? _provincial;
    private bool _provincialIntro;

    private void OpenProvincial()
    {
        if (_surface != Surface.Play) return;
        _provincial ??= new ProvincialProgramPanel();
        if (_provincial.GetParent() is null) AddChild(_provincial);
        _provincial.Show(PlayerDesk.Read(_session.Engine, _human), _language, WaitingCommand, action => Offer(action));
        _provincial.Visible = true;
    }
}

/// <summary>Компактное окно программы провинции. Не заменяет планшеты.</summary>
public sealed partial class ProvincialProgramPanel : PanelContainer
{
    private readonly Label _title;
    private readonly Label _body = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _join = new() { CustomMinimumSize = new(180, 40) };
    private readonly Button _close = new() { CustomMinimumSize = new(88, 40) };
    private bool _confirm;
    private string _shown = "";

    public ProvincialProgramPanel()
    {
        Name = "ProvincialProgram";
        Theme = CompactTabletDesign.Theme;
        AddThemeStyleboxOverride("panel", CompactTabletDesign.Surface("e8eee5", 12));
        SetAnchorsPreset(LayoutPreset.TopLeft);
        OffsetLeft = 16;
        OffsetTop = 72;
        OffsetRight = 430;
        OffsetBottom = 430;
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        _title = TabletDesign.Heading("", 18);
        _title.Name = "ProvincialTitle";
        box.AddChild(_title);
        _body.Name = "ProvincialBody";
        _body.AddThemeColorOverride("font_color", new Color("243028"));
        _body.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_body);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        GameUi.Primary(_join);
        _join.Name = "JoinProvincialProgram";
        _join.Pressed += ConfirmJoin;
        row.AddChild(_join);
        _close.Pressed += () => Visible = false;
        row.AddChild(_close);
    }

    public void Show(PlayerView view, string language, bool waiting, System.Action<GameAction> offer)
    {
        if (view.ProvincialProgram is not { } program) { Visible = false; _confirm = false; return; }
        bool ru = !language.StartsWith("en", System.StringComparison.OrdinalIgnoreCase);
        _title.Text = ru ? "Программа развития провинции" : "Provincial development program";
        string industry = PlayerText.Enterprise(language, program.IndustryId, 1);
        long state = program.ParcelReserved + program.ConstructionReserved + program.OperatingReserved + program.SupplyValueMinor;
        string status = program.Status switch
        {
            "Offered" => ru ? "Предложена" : "Offered",
            "Open" => ru ? "Открыта" : "Open",
            _ => ru ? "Завершена" : "Completed"
        };
        string next = program.NextStep switch
        {
            "accept" => ru ? "Подтвердите участие." : "Confirm participation.",
            "buy-parcel" => ru ? "Найдите участок 2×3 и купите его: оплату внесёт столица." : "Find a 2×3 parcel and buy it: the capital pays.",
            "collect-supply" => ru ? "Заберите материалы и сырьё на терминале." : "Collect materials and input at the terminal.",
            "build" => ru ? "Постройте первое предприятие на участке." : "Build the first enterprise on the parcel.",
            "wait-construction" => ru ? "Дождитесь окончания стройки." : "Wait until construction finishes.",
            "produce" => ru ? "Включите работу, дождитесь работников и первой партии." : "Enable work, wait for staff and the first batch.",
            "sell" => ru ? "Продайте первую продукцию столице. Если заказа нет, провинция выкупает одну партию на терминале." : "Sell the first output to the capital. If there is no order, the province buys one batch at the terminal.",
            _ => ru ? "Программа выполнена. Дальше хозяйство ваше." : "The program is complete. The business is yours from here."
        };
        _body.Text = (ru ? "Отрасль: " : "Industry: ") + industry + "\n" +
            (ru ? "Первое предприятие: " : "First enterprise: ") + industry + "\n" +
            (ru ? "Участок: " : "Parcel: ") + PlayerText.Money(program.ParcelReserved) + "\n" +
            (ru ? "Финансирование столицы: " : "Capital funding: ") + PlayerText.Money(state) + "\n" +
            (ru ? "Свои расходы по пакету: 0.00 · на руках " : "Your cost for the package: 0.00 · cash on hand ") + PlayerText.Money(view.AvailableCashMinor) + "\n" +
            (ru ? "Статус: " : "Status: ") + status + "\n" + next;
        _join.Visible = program.Status == "Offered";
        _join.Disabled = waiting;
        _join.Text = waiting ? (ru ? "Ждём ответ" : "Waiting") : _confirm ? (ru ? "Подтвердить помощь" : "Confirm the grant") : (ru ? "Участвовать" : "Join");
        _close.Text = ru ? "Закрыть" : "Close";
        if (_shown != program.Status) { _shown = program.Status; _confirm = false; }
        _offer = offer;
        var viewport = GetViewportRect().Size;
        if (viewport.X > 1 && viewport.Y > 1)
        {
            OffsetRight = OffsetLeft + System.Math.Min(420, System.Math.Max(220, viewport.X - 24));
            OffsetBottom = OffsetTop + System.Math.Min(360, System.Math.Max(180, viewport.Y - OffsetTop - 12));
        }
    }

    private System.Action<GameAction>? _offer;

    private void ConfirmJoin()
    {
        if (!_confirm) { _confirm = true; _join.Text = _join.Text.Contains("Join") || _join.Text.Contains("Confirm") ? "Confirm the grant" : "Подтвердить помощь"; return; }
        _confirm = false;
        _offer?.Invoke(new(ActionKind.AcceptProvincialSupport));
    }
}

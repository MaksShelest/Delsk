using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Existing fixed offers/contracts only. P45 counteroffers are intentionally absent.</summary>
public partial class CurrentDealsPanel : VBoxContainer
{
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction, ActionAccess> _access;
    private readonly Label _summary = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _actions = new();
    private readonly OptionButton _item = new() { FocusMode = FocusModeEnum.None };
    private readonly OptionButton _destination = new() { FocusMode = FocusModeEnum.None };
    private readonly OptionButton _seller = new() { FocusMode = FocusModeEnum.None };
    private readonly SpinBox _quantity = new() { MinValue = 1, MaxValue = 1000000, Value = 1, Step = 1 };
    private readonly SpinBox _price = new() { MinValue = 1, MaxValue = 1000000, Value = 5, Step = 0.01 };
    private readonly Button _buy;
    private readonly Button _propose;
    private readonly Dictionary<string, (Button Button, GameAction Action)> _buttons = [];
    private PlayerView _view = null!;
    private InventoryId[] _destinations = [];
    private string _destinationKey = "";
    private bool _pending;
    public CurrentDealsPanel(ScenarioDefinition scenario, string language, System.Action<GameAction> submit, System.Func<GameAction, ActionAccess> access)
    {
        _scenario = scenario; _language = language; _submit = submit; _access = access;
        AddChild(_summary);
        var form = new HFlowContainer(); AddChild(form);
        form.AddChild(new Label { Text = L("Товар / количество / цена за единицу", "Item / quantity / unit price") });
        foreach (var item in scenario.Items) _item.AddItem(PlayerText.Item(language, item.Id));
        form.AddChild(_item); form.AddChild(_quantity); form.AddChild(_price); form.AddChild(_destination);
        _buy = Button(form, L("Опубликовать закупку", "Publish buy offer"), () => submit(new(ActionKind.CreateBuyOffer,
            Destination: _destinations[_destination.Selected], ItemId: Item, Quantity: Quantity, UnitPriceMinor: Price)));
        foreach (var actor in scenario.Participants) _seller.AddItem(L("Участник ", "Participant ") + actor.Id, checked((int)actor.Id));
        var politics = new HFlowContainer(); AddChild(politics); politics.AddChild(_seller);
        _propose = Button(politics, L("Предложить товар за поддержку", "Offer these goods for support"), () => submit(new(ActionKind.ProposeSupport,
            Election: _view.Elections.NextElectionId!.Value, SupportSeller: new(_seller.GetSelectedId()), Compensation: [new(Item, Quantity)])));
        AddChild(_actions);
        _quantity.ValueChanged += _ => RefreshInputs(); _price.ValueChanged += _ => RefreshInputs();
        _item.ItemSelected += _ => RefreshInputs(); _seller.ItemSelected += _ => RefreshInputs();
        _destination.ItemSelected += _ => RefreshInputs();
    }
    private void RefreshInputs() { if (_view is not null) ShowData(_view,_pending); }
    private string Item => _scenario.Items[_item.Selected].Id;
    private long Quantity => (long)_quantity.Value;
    private long Price => checked((long)Math.Round(_price.Value * 100, MidpointRounding.AwayFromZero));
    public void ShowData(PlayerView view, bool pending)
    {
        _view = view; _pending = pending;
        var destinations = view.Buildings.Select(b => b.InputId).ToArray();
        string key = string.Join(";", destinations.Select(d => d.Value));
        if (key != _destinationKey)
        {
            long old = _destination.Selected >= 0 && _destination.Selected < _destinations.Length ? _destinations[_destination.Selected].Value : 0;
            _destinationKey = key; _destinations = destinations; _destination.Clear();
            foreach (var b in view.Buildings) _destination.AddItem(PlayerText.Building(_language, b.TypeId) + " #" + b.Id.Value);
            if (destinations.Length > 0) _destination.Select(Math.Max(0, Array.FindIndex(destinations, d => d.Value == old)));
        }
        _buy.Disabled = pending || destinations.Length == 0;
        _propose.Disabled = pending || view.Elections.NextElectionId is null || view.Support.Rules is null || _seller.GetSelectedId() == view.Participant.Id.Value;
        _summary.Text = L("Компенсацию нужно физически доставить в общую торговую точку. Подпись возможна только после полной передачи.", "Physically deliver compensation to the common trading point. Signing requires the complete compensation.") + "\n" +
            string.Join("\n", view.Contracts!.Select(c => L("Договор #", "Contract #") + c.Id + " · " + PlayerText.Item(_language, c.ItemId) + $" {c.DeliveredQuantity}/{c.Quantity}" + L(" · срок ", " · due ") + c.DeadlineTick)) + "\n" +
            string.Join("\n", view.Support.Proposals.Select(p => "#" + p.Id + " · " + Status(p.Status) + " · " + p.Candidate.Value + " → " + p.Seller.Value + " · " + string.Join(", ", p.Compensation.Select(m => PlayerText.Item(_language, m.ItemId) + " ×" + m.Quantity))));
        var actions = new List<(string Key, string Label, GameAction Action)>();
        void Add(string id, string caption, GameAction action) => actions.Add((id, caption, action));
        foreach (var offer in view.CapitalOffers.Where(o => o.RemainingQuantity > 0 && (o.ExpiresAtTick is null || o.ExpiresAtTick >= view.Tick)))
        {
            Add("supply-capital:" + offer.Id, L("Принять поставку столице: ", "Commit to capital delivery: ") + PlayerText.Item(_language, offer.ItemId) + $" ×{Quantity} · " + PlayerText.Money(offer.UnitPriceMinor),
                new(ActionKind.AcceptDeliveryQuantity, OfferId: offer.Id, Quantity: Quantity, OfferWindow: offer.Window));
            if (view.SupplyRequests!.Any(r=>r.CapitalOfferId==offer.Id)) Add("withdraw-capital:" + offer.Id, L("Снять свою заявку поставки столице #", "Withdraw capital supply request #") + offer.Id,
                new(ActionKind.WithdrawSupplyRequest, OfferId: offer.Id, OfferWindow: offer.Window));
        }
        foreach (var offer in view.BuyOffers!.Where(o => o.Status == "Open"))
            if (offer.Buyer == view.Participant.Id) Add("cancel:" + offer.Id, L("Отменить незаключённый остаток закупки #", "Cancel uncontracted buy offer #") + offer.Id, new(ActionKind.CancelBuyOffer, BuyOfferId: offer.Id));
            else
            {
                Add("supply:" + offer.Id, L("Принять поставку #", "Commit to delivery #") + offer.Id + " · " + PlayerText.Item(_language, offer.ItemId) + $" ×{Quantity} · " + PlayerText.Money(offer.UnitPriceMinor), new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer.Id, Quantity: Quantity, OfferWindow: offer.Window));
                if (view.SupplyRequests!.Any(r=>r.BuyOfferId==offer.Id)) Add("withdraw:" + offer.Id, L("Снять свою заявку поставки #", "Withdraw supply request #") + offer.Id, new(ActionKind.WithdrawSupplyRequest, BuyOfferId: offer.Id, OfferWindow: offer.Window));
            }
        foreach (var contract in (view.Contracts ?? []).Where(c => c.Supplier == view.Participant.Id && c.ClosedTick is null))
        {
            Add("deliver:" + contract.Id, L("Передать по договору #", "Deliver contract #") + contract.Id + $" ×{Quantity}", new(ActionKind.DeliverContract, ContractId: contract.Id, Quantity: Quantity));
            Add("go:" + contract.Id, L("Подойти к получателю договора #", "Walk to recipient of contract #") + contract.Id, PlayerDesk.Move(contract.Position.X, contract.Position.Z));
        }
        foreach (var proposal in view.Support.Proposals.Where(p => p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources))
        {
            if (proposal.Seller == view.Participant.Id && proposal.Status == SupportProposalStatus.Proposed) Add("accept:" + proposal.Id, L("Принять поддержку #", "Accept support #") + proposal.Id, PlayerDesk.AcceptSupport(proposal.Id));
            Add("reject:" + proposal.Id, L("Отклонить поддержку #", "Reject support #") + proposal.Id, PlayerDesk.RejectSupport(proposal.Id));
            if (proposal.Status == SupportProposalStatus.AcceptedAwaitingResources) Add("sign:" + proposal.Id, L("Подписать после передачи #", "Sign after transfer #") + proposal.Id, new(ActionKind.SignSupport, SupportProposalId: proposal.Id));
        }
        if (view.Support.Rules is not null && _scenario.Support is { } settings)
        {
            Add("support-go", L("Подойти к общей торговой точке", "Walk to common trading point"), PlayerDesk.Move(settings.TradingPoint.X, settings.TradingPoint.Z));
            Add("compensation", L("Передать компенсацию из рюкзака ×", "Deposit compensation from backpack ×") + Quantity, PlayerDesk.Transfer(view.Participant.InventoryId.Value, view.Support.OwnSection.Value, Item, Quantity));
            Add("take-compensation", L("Забрать из своей секции ×", "Take from your section ×") + Quantity, PlayerDesk.Transfer(view.Support.OwnSection.Value, view.Participant.InventoryId.Value, Item, Quantity));
        }
        foreach (var plot in view.Plots.Where(p => p.ReservedBy == view.Participant.Id)) Add("unreserve:" + plot.Id.Value, L("Снять бронь без возврата #", "Release reservation without refund #") + plot.Id.Value, new(ActionKind.CancelPlotReservation, Plot: plot.Id));
        if (view.Elections.NextElectionId is { } election)
            foreach (var candidate in view.Elections.Participants) Add("vote:" + candidate.Value, L("Голосовать за ", "Vote for ") + candidate.Value, PlayerDesk.Vote(election.Value, candidate.Value));
        if (view.Elections.Mode == ElectionMode.Victory && view.Elections.FirstVictory?.Winner == view.Participant.Id) Add("sandbox", L("Продолжить после победы", "Continue after victory"), new(ActionKind.ContinueAfterVictory));
        foreach (var id in _buttons.Keys.Except(actions.Select(a => a.Key)).ToArray())
        { var button = _buttons[id].Button; _actions.RemoveChild(button); button.QueueFree(); _buttons.Remove(id); }
        foreach (var row in actions)
        {
            if (!_buttons.TryGetValue(row.Key, out var old))
            { string id = row.Key; old = (Button(_actions, row.Label, () => _submit(_buttons[id].Action)), row.Action); }
            var access = _access(row.Action); old.Button.Text = row.Label; old.Button.Disabled = pending || !access.Allowed;
            old.Button.TooltipText = access.Allowed ? "" : PlayerText.Reason(_language, access.ReasonCode);
            _buttons[row.Key] = (old.Button, row.Action);
        }
    }
    private string Status(SupportProposalStatus status) => status switch
    {
        SupportProposalStatus.Proposed => L("Предложено", "Proposed"), SupportProposalStatus.AcceptedAwaitingResources => L("Принято, ждёт ресурсы", "Accepted, awaiting resources"),
        SupportProposalStatus.Signed => L("Подписано", "Signed"), SupportProposalStatus.Expired => L("Истекло", "Expired"), _ => L("Отклонено", "Rejected")
    };
    private static Button Button(Container parent, string title, System.Action action)
    { var button = new Button { Text = title, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, AutowrapMode = TextServer.AutowrapMode.WordSmart }; button.Pressed += action; parent.AddChild(button); return button; }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

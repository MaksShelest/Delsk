using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private readonly TabletState _tabletState = new();
    private readonly CapitalTablePreferences _capitalPreferences = new();
    private TabletPanel? _tabletPanel;
    private CapitalTablet? _capitalTablet;
    private ExchangeTablet? _exchangeTablet;
    private ParticipantMarketTablet? _marketTablet;
    private readonly CapitalTablePreferences _marketPreferences = new();
    private void OpenMarketTablet(long shop)
    {
        if (_surface != Surface.Play || !_tabletState.OpenMarket(PlayerDesk.Read(_session.Engine,_human),_scenario,shop)) return;
        PrepareTablet();
    }
    private HouseholdObjectCard? _enterpriseTablet;
    private StoragePanel? _groundPanel;
    private PlayerTablet? _playerTablet;
    private string _objectKind = "building";
    private int _playerTab;
    private void OpenPlayerTablet()
    {
        if (_surface != Surface.Play) return;
        _tabletState.OpenPlayer(PlayerDesk.Read(_session.Engine,_human));
        PrepareTablet();
    }
    private void OpenObjectTablet(string kind, long id)
    {
        if (!_tabletState.OpenObject(PlayerDesk.Read(_session.Engine,_human),kind,id)) return;
        _objectKind = kind; PrepareTablet();
    }
    private VBoxContainer? _tabletPolitics;
    private Control? _bottomChrome;
    private string? _tabletCommand;
    private string _tabletError = "";
    private bool WaitingCommand => _trackedId is not null && _session.Engine.Result(_trackedId) is null;

    private void OpenCapitalTablet()
    {
        if (_surface != Surface.Play || !_tabletState.OpenCapital(PlayerDesk.Read(_session.Engine,_human),_scenario)) return;
        PrepareTablet();
    }
    private void OpenEnterpriseTablet(long id)
    {
        if (_surface != Surface.Play || !_tabletState.OpenEnterprise(PlayerDesk.Read(_session.Engine,_human),id)) return;
        _selectedEnterprise = id;
        PrepareTablet();
    }
    private void PrepareTablet()
    {
        StopHeading(); _waitRelease = true;
        _tabletCommand = null; _tabletError = "";
        ApplyPause(); RebuildTablet(); ApplySurface(); Refresh();
    }
    private void CloseTablet()
    {
        _tabletState.Close(); _tabletCommand = null; _tabletError = ""; DropTabletPanel(); ApplyPause(); ApplySurface(); Refresh();
    }
    private void DropTabletPanel()
    {
        if (_playerTablet is not null) _playerTab = _playerTablet.SelectedTab;
        if (_tabletPanel is not null)
        { _tabletPanel.GetParent()?.RemoveChild(_tabletPanel); _tabletPanel.QueueFree(); }
        _exchangeTablet=null;_playerTablet = null; _tabletPanel = null; _capitalTablet = null; _enterpriseTablet = null;_groundPanel=null; _marketTablet = null; _tabletPolitics = null;
    }
    private void ResetTablet(bool clearPreferences = false)
    {
        _tabletState.Close(); DropTabletPanel();
        if (clearPreferences) { _capitalPreferences.ItemId = ""; _capitalPreferences.HighestPriceFirst = true; _marketPreferences.ItemId = ""; _marketPreferences.HighestPriceFirst = false; }
    }
    private void RebuildTablet()
    {
        DropTabletPanel();
        if (!_tabletState.IsOpen || _surface != Surface.Play || _playChrome is null) return;
        var view = PlayerDesk.Read(_session.Engine,_human);
        string title = _tabletState.Kind == TabletKind.Player ? L("Планшет игрока", "Player tablet") : _tabletState.Kind == TabletKind.Object ? L("Карточка хозяйства", "Holdings card") : _tabletState.Kind == TabletKind.Market ? PlayerText.Local(_language,"Рынок участников · лавки","Participant market · shops") : _tabletState.Kind == TabletKind.Capital ? PlayerText.Local(_language,"Биржа · столичные заказы","Exchange · capital orders")
            : PlayerText.Building(_language,view.Buildings.Single(b => b.Id.Value == _tabletState.EnterpriseId).TypeId) + " #" + _tabletState.EnterpriseId;
        _tabletPanel = new TabletPanel(title,_language,_tabletState.Kind==TabletKind.Player?"home":_tabletState.Kind is TabletKind.Capital or TabletKind.Market?"market":"warehouse");
        _tabletPanel.Closed += CloseTablet;
        _tabletPanel.HelpRequested += () => OpenHelp(_tabletState.Kind switch
        { TabletKind.Player => _playerTablet?.SelectedTab switch { 0 => "holdings", 2 => "politics", _ => "city" },
            TabletKind.Object when _objectKind == "equipment" => "harvest",
            TabletKind.Capital or TabletKind.Market => _exchangeTablet?.SelectedPage==0?"exchange":"market", _ => "production" });
        _tabletPanel.TimeRunningChanged += running =>
        { StopHeading(); _waitRelease = true; _tabletState.SetTimeRunning(running); ApplyPause(); Refresh(); };
        if (_tabletState.Kind == TabletKind.Player)
        {
            _playerTablet = new PlayerTablet(_scenario,_language,Submit,a => _session.Engine.AccessFor(_human,a),(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction),(offset,bills)=>_session.Engine.AccountOf(_human,offset,20,bills));
            _playerTablet.HelpRequested+=()=>OpenHelp("production");
            _tabletPanel.Body.AddChild(_playerTablet); _playerTablet.SelectedTab = _playerTab;
            if(_scenario.Layout is not null){_tabletPolitics=_playerTablet.PoliticsHost;FillPolitics(view,_tabletPolitics);}
        }
        else if(_tabletState.Kind==TabletKind.Object&&_objectKind=="ground")
        {_groundPanel=new(_scenario,_language,"ground",_tabletState.EnterpriseId,Submit,a=>_session.Engine.AccessFor(_human,a),demolitionQuote:(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction));_tabletPanel.Body.AddChild(_groundPanel);}
        else if (_tabletState.Kind is TabletKind.Market or TabletKind.Capital)
        {
            long shop=view.Buildings.FirstOrDefault(b=>b.Kind==BuildingKind.Shop)?.Id.Value??0;
            _exchangeTablet=new(_scenario,_language,_capitalPreferences,_marketPreferences,shop,Submit,_tabletState.Kind==TabletKind.Market,a=>_session.Engine.AccessFor(_human,a));
            _capitalTablet=_exchangeTablet.Capital;_marketTablet=_exchangeTablet.Market;_tabletPanel.Body.AddChild(_exchangeTablet);
        }
        else
        {
            _enterpriseTablet = new HouseholdObjectCard(_scenario,_language,_tabletState.Kind == TabletKind.Object ? _objectKind : "building",_tabletState.EnterpriseId,Submit,a => _session.Engine.AccessFor(_human,a),(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction));
            _enterpriseTablet.ArtworkHeaderChanged+=_tabletPanel.UseArtworkHeader;
            _enterpriseTablet.HelpRequested+=_tabletPanel.RequestHelp;_enterpriseTablet.CloseRequested+=_tabletPanel.RequestClose;
            _tabletPanel.Body.AddChild(_enterpriseTablet);
        }
        TabletDesign.CompactInputs(_tabletPanel);_playChrome.AddChild(_tabletPanel);
    }
    private void RefreshTablet(PlayerView view)
    {
        if (!_tabletState.IsOpen || _surface != Surface.Play) return;
        if (_tabletPanel is null) RebuildTablet();
        _exchangeTablet?.ShowHeader(view);
        _capitalTablet?.ShowData(view,_session.Engine.CapitalTradingOf(_human),WaitingCommand,_session.Paused);
        _marketTablet?.ShowData(view,WaitingCommand);
        _enterpriseTablet?.ShowData(view,WaitingCommand,_session.Paused);
        _groundPanel?.ShowData(view,WaitingCommand);
        _playerTablet?.ShowData(view,WaitingCommand,_session.Paused);
        string status = WaitingCommand && _tabletState.BlocksTime
            ? PlayerText.Local(_language,"Действие подготовлено — включите время.","Action queued — run time to continue.") : _tabletError;
        if (_tabletCommand is { } command && _session.Engine.Result(command) is { Status: ResultStatus.Rejected } rejected)
            status = PlayerText.Reason(_language,rejected.ReasonCode);
        _tabletPanel?.ShowClock(!_tabletState.PauseTime,_session.Paused,view.Tick,status);
    }
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private readonly TabletState _tabletState = new();
    private readonly CapitalTablePreferences _capitalPreferences = new();
    private TabletPanel? _tabletPanel;
    private CapitalTablet? _capitalTablet;
    private ParticipantMarketTablet? _marketTablet;
    private readonly CapitalTablePreferences _marketPreferences = new();
    private void OpenMarketTablet(long shop)
    {
        if (_surface != Surface.Play || !_tabletState.OpenMarket(PlayerDesk.Read(_session.Engine,_human),_scenario,shop)) return;
        PrepareTablet();
    }
    private EnterpriseTablet? _enterpriseTablet;
    private VBoxContainer? _tabletPolitics;
    private Control? _bottomChrome;
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
        ApplyPause(); RebuildTablet(); ApplySurface(); Refresh();
    }
    private void CloseTablet()
    {
        _tabletState.Close(); DropTabletPanel(); ApplyPause(); ApplySurface(); Refresh();
    }
    private void DropTabletPanel()
    {
        if (_tabletPanel is not null)
        { _tabletPanel.GetParent()?.RemoveChild(_tabletPanel); _tabletPanel.QueueFree(); }
        _tabletPanel = null; _capitalTablet = null; _enterpriseTablet = null; _marketTablet = null; _tabletPolitics = null;
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
        string title = _tabletState.Kind == TabletKind.Market ? PlayerText.Local(_language,"Рынок участников · лавки","Participant market · shops") : _tabletState.Kind == TabletKind.Capital ? PlayerText.Local(_language,"Биржа · столичные заказы","Exchange · capital orders")
            : PlayerText.Building(_language,view.Buildings.Single(b => b.Id.Value == _tabletState.EnterpriseId).TypeId) + " #" + _tabletState.EnterpriseId;
        _tabletPanel = new TabletPanel(title,_language);
        _tabletPanel.Closed += CloseTablet;
        _tabletPanel.TimeRunningChanged += running =>
        { StopHeading(); _waitRelease = true; _tabletState.SetTimeRunning(running); ApplyPause(); Refresh(); };
        if (_tabletState.Kind == TabletKind.Market)
        {
            _marketTablet = new ParticipantMarketTablet(_scenario,_language,_marketPreferences,_tabletState.EnterpriseId,Submit);
            _tabletPanel.Body.AddChild(_marketTablet);
        }
        else if (_tabletState.Kind == TabletKind.Capital)
        {
            _capitalTablet = new CapitalTablet(_scenario,_language,_capitalPreferences,Submit);
            _tabletPanel.Body.AddChild(_capitalTablet);
        }
        else
        {
            _enterpriseTablet = new EnterpriseTablet(_scenario,_language,view,_tabletState.EnterpriseId,Submit);
            _tabletPanel.Body.AddChild(_enterpriseTablet);
            // Preserve the existing political controls without mixing them into enterprise operations.
            var politics = new Button { Text = PlayerText.Local(_language,"Политика и поддержка ▸","Politics and support ▸"), FocusMode = FocusModeEnum.None };
            _tabletPanel.Body.AddChild(politics);
            _tabletPolitics = new VBoxContainer { Visible = false }; _tabletPanel.Body.AddChild(_tabletPolitics);
            politics.Pressed += () => { if (_tabletPolitics is not null) _tabletPolitics.Visible = !_tabletPolitics.Visible; };
            FillPolitics(view,_tabletPolitics);
        }
        _playChrome.AddChild(_tabletPanel);
    }
    private void RefreshTablet(PlayerView view)
    {
        if (!_tabletState.IsOpen || _surface != Surface.Play) return;
        if (_tabletPanel is null) RebuildTablet();
        _capitalTablet?.ShowData(view,_session.Engine.CapitalTradingOf(_human),WaitingCommand,_session.Paused);
        _marketTablet?.ShowData(view,WaitingCommand);
        _enterpriseTablet?.ShowData(view,WaitingCommand,_session.Paused);
        string status = WaitingCommand && _tabletState.BlocksTime
            ? PlayerText.Local(_language,"Действие выбрано. Включите ход времени, чтобы ядро обработало его вместе с действиями NPC.","Action queued. Run time to process it alongside NPC actions.") : _feedback;
        _tabletPanel?.ShowClock(!_tabletState.PauseTime,_session.Paused,view.Tick,status);
    }
}

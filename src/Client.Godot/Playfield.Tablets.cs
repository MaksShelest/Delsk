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
    private CityMapPanel? _cityMapPanel;
    private readonly CityMapPreferences _cityMapPreferences=new();
    private void OpenCityMap()
    {
        if(_surface!=Surface.Play)return;
        _tabletState.OpenCityMap(PlayerDesk.Read(_session.Engine,_human));PrepareTablet();
    }
    private void OpenCitySection(string section,long id=0)
    {OpenPlayerTablet();_playerTablet?.OpenSection(section,id);}

    private string _objectKind = "building";
    private int _playerTab;
    private string _playerSection="overview";
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
        CloseRoadWork();StopHeading();
        CloseContextActions(); _waitRelease = true;
        _tabletCommand = null; _tabletError = "";
        ApplyPause(); RebuildTablet(); ApplySurface(); Refresh();
    }
    private void CloseTablet()
    {
        StopHeading();_waitRelease=true;SaveCityExperience();_tabletState.Close(); _tabletCommand = null; _tabletError = ""; DropTabletPanel(); ApplyPause(); ApplySurface(); Refresh();
    }
    private void DropTabletPanel()
    {
        if (_playerTablet is not null){_playerTab = _playerTablet.SelectedTab;_playerSection=_playerTablet.CurrentSection;}
        if (_tabletPanel is not null)
        { _tabletPanel.GetParent()?.RemoveChild(_tabletPanel); _tabletPanel.QueueFree(); }
        _cityInbox=null;_journey=null;_cityMapPanel=null;_exchangeTablet=null;_playerTablet = null; _tabletPanel = null; _capitalTablet = null; _enterpriseTablet = null;_groundPanel=null; _marketTablet = null; _tabletPolitics = null;
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
        string title = _tabletState.Kind==TabletKind.Notices?L("События хозяйства","Business notices"):_tabletState.Kind==TabletKind.Tutorial?L("Короткое обучение","Quick guide"):_tabletState.Kind == TabletKind.CityMap ? L("Карта всего города","City map") : _tabletState.Kind == TabletKind.Player ? (_friendly?L("Хозяйство","Business"):L("Планшет игрока", "Player tablet")) : _tabletState.Kind == TabletKind.Object ? L("Карточка хозяйства", "Holdings card") : _tabletState.Kind == TabletKind.Market ? PlayerText.Local(_language,"Рынок участников · лавки","Participant market · shops") : _tabletState.Kind == TabletKind.Capital ? PlayerText.Local(_language,"Биржа · столичные заказы","Exchange · capital orders")
            : PlayerText.Building(_language,view.Buildings.Single(b => b.Id.Value == _tabletState.EnterpriseId).TypeId) + " #" + _tabletState.EnterpriseId;
        bool woodworking=_tabletState.Kind is TabletKind.Enterprise or TabletKind.Object&&
            (_tabletState.Kind!=TabletKind.Object||_objectKind=="building")&&view.Buildings.Any(b=>b.Id.Value==_tabletState.EnterpriseId&&b.Kind==BuildingKind.Production&&view.Enterprises.Any(e=>e.Id==new EnterpriseId(b.Id.Value)));
        if(woodworking){var b=view.Buildings.Single(b=>b.Id.Value==_tabletState.EnterpriseId);title=GameUi.Industry(_language,b.TypeId,b.Level);}
        bool equipmentSide=_tabletState.Kind==TabletKind.Object&&_objectKind=="equipment"&&_scenario.Economy?.Fleet?.EquipmentDealerVersion is 1 or 2;
        if(equipmentSide&&view.Equipment?.FirstOrDefault(e=>e.Id.Value==_tabletState.EnterpriseId) is {} shown)
            title=ExtractionTabletPresenter.Title(_language,shown.TypeId,shown.Level);
        _tabletPanel = new TabletPanel(title,_language,_tabletState.Kind==TabletKind.Player?"home":_tabletState.Kind is TabletKind.Capital or TabletKind.Market?"market":"warehouse");
        if(equipmentSide){_tabletPanel.UseSideDock();_tabletPanel.Resized+=SyncSideFrame;}
        else if(_tabletState.Kind is TabletKind.Player or TabletKind.CityMap||woodworking)_tabletPanel.UseCompactFrame();
        _tabletPanel.Closed += CloseTablet;
        _tabletPanel.HelpRequested += () => OpenHelp(_tabletState.Kind switch
        { TabletKind.CityMap=>"map",TabletKind.Notices or TabletKind.Tutorial=>"journey",TabletKind.Player => _playerTablet?.SelectedTab switch { 0 => _playerTablet.HelpTopic, 2 => "politics", _ => "city" },
            TabletKind.Object when _objectKind == "equipment" => "harvest",
            TabletKind.Capital or TabletKind.Market => _exchangeTablet?.SelectedPage==0?"exchange":"market", _ => "production" });
        _tabletPanel.TimeRunningChanged += running =>
        { StopHeading(); _waitRelease = true; _tabletState.SetTimeRunning(running); ApplyPause(); Refresh(); };
        if(_tabletState.Kind==TabletKind.Notices)
        {_cityInbox=new(_language,OpenCityNotice,ReadCityNotice);_tabletPanel.Body.AddChild(_cityInbox);_cityInbox.ShowData(_cityNotices,view.Tick,WaitingCommand);}
        else if(_tabletState.Kind==TabletKind.Tutorial)
        {
            _journey=new(_language,_cityRead,IndustrialParcelRules.Applies(view.Economy));_journey.Changed+=SaveCityExperience;
            _journey.NavigateRequested+=page=>{if(page=="map")OpenCityMap();else if(page=="notices")OpenCityNotices();else OpenCitySection(page);};_tabletPanel.Body.AddChild(_journey);
        }
        else if(_tabletState.Kind==TabletKind.CityMap)
        {
            _cityMapPanel=new(_scenario,_language,_cityMapPreferences,Submit,c=>_session.Engine.PreviewMovement(_human,c));
            _cityMapPanel.SetWorldOwnership(_showOwnership);
            _cityMapPanel.OwnershipChanged+=on=>{_showOwnership=on;_hudPlots?.SetPressedNoSignal(on);SaveCityExperience();Refresh();};
            _cityMapPanel.OpenObject+=(kind,id)=>{if(kind=="equipment"&&_scenario.Economy?.Fleet?.EquipmentDealerVersion is 1 or 2){OpenObjectTablet(kind,id);return;}OpenPlayerTablet();_playerTablet?.OpenHolding(kind,id);};
            _cityMapPanel.OpenSection+=OpenCitySection;_tabletPanel.Body.AddChild(_cityMapPanel);
        }
        else if (_tabletState.Kind == TabletKind.Player)
        {
            _playerTablet = new PlayerTablet(_scenario,_language,Submit,a => _session.Engine.AccessFor(_human,a),(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction),(offset,bills)=>_session.Engine.AccountOf(_human,offset,20,bills),terms=>_session.Engine.QuoteDeliveryService(_human,terms),_friendly);
            _playerTablet.GuideRequested+=ShowGuide;_playerTablet.ExchangeRequested+=OpenExchangeOrMap;
            _playerTablet.RoadWorkRequested+=OpenRoadWork;
            _playerTablet.ObjectRequested+=OpenObjectTablet;_playerTablet.LocationRequested+=ShowOnCityMap;
            _tabletPanel.AttachNavigation(_playerTablet.SectionPicker);
            _playerTablet.HelpRequested+=()=>OpenHelp(_playerTablet.HelpTopic);
            _playerTablet.MapRequested+=OpenCityMap;_playerTablet.NoticesRequested+=OpenCityNotices;_playerTablet.TutorialRequested+=OpenCityJourney;
            _tabletPanel.Body.AddChild(_playerTablet);_playerTablet.OpenSection(_playerSection=="object"?"holdings":_playerSection);
            if(_scenario.Layout is not null){_tabletPolitics=_playerTablet.PoliticsHost;FillPolitics(view,_tabletPolitics);}
        }
        else if(_tabletState.Kind==TabletKind.Object&&_objectKind=="ground")
        {_groundPanel=new(_scenario,_language,"ground",_tabletState.EnterpriseId,Submit,a=>_session.Engine.AccessFor(_human,a),demolitionQuote:(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction));_groundPanel.GuideRequested+=ShowGuide;_tabletPanel.Body.AddChild(_groundPanel);}
        else if (_tabletState.Kind is TabletKind.Market or TabletKind.Capital)
        {
            long shop=view.Buildings.FirstOrDefault(b=>b.Kind==BuildingKind.Shop)?.Id.Value??0;
            _exchangeTablet=new(_scenario,_language,_capitalPreferences,_marketPreferences,shop,Submit,_tabletState.Kind==TabletKind.Market,a=>_session.Engine.AccessFor(_human,a));
            _capitalTablet=_exchangeTablet.Capital;_marketTablet=_exchangeTablet.Market;_tabletPanel.Body.AddChild(_exchangeTablet);
        }
        else
        {
            _enterpriseTablet = new HouseholdObjectCard(_scenario,_language,_tabletState.Kind == TabletKind.Object ? _objectKind : "building",_tabletState.EnterpriseId,Submit,a => _session.Engine.AccessFor(_human,a),(id,construction)=>_session.Engine.QuoteDemolition(_human,id,construction),woodworkingPrototype:woodworking,productionType:view.Buildings.FirstOrDefault(b=>b.Id.Value==_tabletState.EnterpriseId)?.TypeId??"woodworking");
            _enterpriseTablet.MapRequested+=OpenCityMap;_enterpriseTablet.FleetRequested+=()=>OpenCitySection("fleet");
            _enterpriseTablet.EquipmentFocusRequested+=target=>OpenObjectTablet("equipment",target);
            _enterpriseTablet.ArtworkHeaderChanged+=_tabletPanel.UseArtworkHeader;
            _enterpriseTablet.HelpRequested+=_tabletPanel.RequestHelp;_enterpriseTablet.CloseRequested+=_tabletPanel.RequestClose;
            _tabletPanel.Body.AddChild(_enterpriseTablet);
        }
        TabletDesign.CompactInputs(_tabletPanel);_playChrome.AddChild(_tabletPanel);Callable.From(SyncSideFrame).CallDeferred();
    }
    private void SyncSideFrame()
    {
        float covered=0;
        if(_tabletPanel is {SideDock:true} panel&&GodotObject.IsInstanceValid(panel)&&panel.IsVisibleInTree())
        {float width=GetViewportRect().Size.X;if(width>1&&panel.Size.X>1)covered=(panel.GlobalPosition.X+panel.Size.X)/width;}
        _stage?.SetSidePanelInset(covered);
    }
    private void OpenExchangeOrMap()
    {
        var view=PlayerDesk.Read(_session.Engine,_human);
        if(_surface==Surface.Play&&_tabletState.OpenCapital(view,_scenario)){PrepareTablet();return;}
        var step=FriendlyGuidance.Read(view,_scenario,_language,_cityRead.Noted("sale"));
        if(step.X>=0&&step.Focus=="orders"){ShowGuide(step);return;}
        _feedback=L("Подойдите к терминалу столицы, чтобы открыть продажу.","Walk to the capital terminal to open the sale.");
        Refresh();
    }
    private Cell? _guideCell;
    private Cell _guideFrom=new(int.MinValue,0);
    private ContextChoice? GuidePurchase(StarterStep step, Cell position) =>
        step.Id is "program-land" or "plot" && position == new Cell(step.X, step.Z)
            ? _choices.FirstOrDefault(c => c.Command is { Kind: ActionKind.BuyPlot } action && action.Plot.Value == step.TargetId)
            : null;

    private void ShowGuide(StarterStep step)
    {
        if (step.Id == "program") { ClearGuide(); OpenProvincial(); return; }
        if (step.Id is "program-land" or "plot" && GuidePurchase(step, _session.Engine.Observe(_human).Participant.Position) is { } purchase)
        {
            ClearGuide();
            StopHeading();
            _waitRelease = true;
            ConfirmContextChoice(purchase);
            return;
        }
        if(step.X<0&&step.TargetId>0&&step.TargetKind is "building" or "ground" or "equipment")
        {OpenPlayerTablet();_playerTablet?.OpenHolding(step.TargetKind=="ground"?"ground":step.TargetKind,step.TargetId);return;}
        if(step.X<0)
        {
            ClearGuide();
            _feedback=L("У этой цели нет точки на земле.","This goal has no point on the ground.");
            if(_feedbackLabel is not null)_feedbackLabel.Text=_feedback;
            Refresh();
            return;
        }
        var target=new Cell(step.X,step.Z);
        if(_guideCell==target){ClearGuide();Refresh();return;}
        _guideCell=target;
        _guideFrom=new(int.MinValue,0);
        Refresh();
    }
    private void ClearGuide()
    {
        _guideCell=null;
        _stage?.ShowRoute(null);
    }
    private void SyncGuide(Cell position)
    {
        if(_guideCell is null||_session is null)return;
        if(_goalStep is not {} step||step.X<0){ClearGuide();return;}
        var target=new Cell(step.X,step.Z);
        if(position==target){ClearGuide();return;}
        if(_guideCell==target&&_guideFrom==position)return;
        _guideCell=target;_guideFrom=position;
        var guide=_session.Engine.PreviewMovement(_human,target);
        if(!guide.Allowed)
        {
            ClearGuide();
            _feedback=L("Путь к цели сейчас недоступен.","The path to the goal is not available now.");
            if(_feedbackLabel is not null)_feedbackLabel.Text=_feedback;
            return;
        }
        _stage?.ShowRoute(guide.Path.ToArray());
    }
    private void ShowOnCityMap(Cell cell)
    {
        _cityMapPreferences.Layers|=CityLayer.Industry|CityLayer.People;_cityMapPreferences.Participant=0;
        _cityMapPreferences.SelectedKey=$"cell:{cell.X}:{cell.Z}";OpenCityMap();
        _cityMapPreferences.Center(cell.X+.5,cell.Z+.5);_cityMapPanel?.Canvas.Zoom(4);
    }
    private void RefreshTablet(PlayerView view)
    {
        if (!_tabletState.IsOpen || _surface != Surface.Play) return;
        if (_tabletPanel is null) RebuildTablet();
        _cityMapPanel?.ShowData(CityMap.Read(Presentation.Read(_scenario,_session.Engine,_human.Value),view),view,WaitingCommand);
        _exchangeTablet?.ShowHeader(view,WaitingCommand);
        _capitalTablet?.ShowData(view,_session.Engine.CapitalTradingOf(_human),WaitingCommand,_session.Paused);
        _marketTablet?.ShowData(view,WaitingCommand);
        _enterpriseTablet?.ShowData(view,WaitingCommand,_session.Paused);
        _groundPanel?.ShowData(view,WaitingCommand);
        if(_playerTablet is not null)_playerTablet.ConfirmedSale=_cityRead.Noted("sale");
        _playerTablet?.ShowData(view,WaitingCommand,_session.Paused);
        string status = WaitingCommand && _tabletState.BlocksTime
            ? PlayerText.Local(_language,"Действие подготовлено — включите время.","Action queued — run time to continue.") : _tabletError;
        if(status.Length==0&&_noticeViews.Count>0&&_tabletState.BlocksTime)status=L("Просмотр подготовлен — включите время. Принятие заявки можно подготовить сразу.","View queued — run time. You can queue order acceptance now.");
        if (_tabletCommand is { } command && _session.Engine.Result(command) is { Status: ResultStatus.Rejected } rejected)
            status = PlayerText.Reason(_language,rejected.ReasonCode);
        _tabletPanel?.ShowClock(!_tabletState.PauseTime,_session.Paused,view.Tick,status);
    }
}

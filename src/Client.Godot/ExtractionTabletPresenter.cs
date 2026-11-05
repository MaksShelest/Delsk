using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Resource-machine overview over the same I4 equipment, cargo and employment projections.</summary>
public partial class ExtractionTabletPresenter : VBoxContainer
{
    public event Action? MapRequested;
    public VBoxContainer WorkActions{get;}=new();public VBoxContainer TeamActions{get;}=new();public VBoxContainer ExtraActions{get;}=new();
    private readonly string _language;private readonly ScenarioDefinition _scenario;private readonly long _id;
    private readonly Label _title=TabletDesign.Heading("",23),_state=GameUi.Text(),_hint=GameUi.Hint(""),_crew=GameUi.Hint(""),_team=GameUi.Text(),_site=GameUi.Hint("");
    private readonly ProgressBar _progress=new(){ShowPercentage=true,CustomMinimumSize=new(0,12)};
    private readonly UiResourceList _stock=new();private readonly QuickStockTransfer _take;private readonly InventoryTransferPanel _transfer;
    private readonly VBoxContainer _art=new();private TextureRect? _picture;private bool _focused;
    public ExtractionTabletPresenter(ScenarioDefinition scenario,string language,long id,Action<GameAction> submit,Func<GameAction,ActionAccess> access,Control legacyDetails)
    {
        Name="ExtractionWorkspace";_scenario=scenario;_language=language;_id=id;Theme=CompactTabletDesign.Theme;
        var sections=new UiSections("ExtractionNav_");AddChild(sections);var work=sections.Add("work",L("Добыча","Extraction"));
        var hero=new HBoxContainer();_art.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;hero.AddChild(_art);var words=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};words.AddChild(_title);words.AddChild(_site);words.AddChild(_state);words.AddChild(_hint);words.AddChild(_progress);words.AddChild(_crew);hero.AddChild(words);work.AddChild(GameUi.Card(hero));work.AddChild(WorkActions);
        var goods=new VBoxContainer();goods.AddChild(TabletDesign.Heading(L("Добытый ресурс","Collected resource"),18));goods.AddChild(_stock);
        _take=new(scenario,language,"ExtractionTake",L("Забрать всё","Take all"),submit,access);_take.UseProductionCard(false,L("Добытый ресурс появится здесь после завершения цикла.","Resources appear here after a complete cycle."));goods.AddChild(_take);work.AddChild(GameUi.Card(goods));
        work.AddChild(GameUi.Button("ExtractionMap",L("Открыть карту участка","Open site map"),()=>MapRequested?.Invoke()));
        var team=sections.Add("team",L("Работники","Crew"));team.AddChild(GameUi.Intro(L("Команда добычи","Extraction crew"),L("В автоматическом режиме работает наёмная команда. Найм и изменение ставки происходят постепенно; простой не оплачивается.","Automatic extraction uses a hired crew. Hiring and wage changes take time; idle work is unpaid."),"people",64));team.AddChild(_team);team.AddChild(TeamActions);
        var cargo=sections.Add("cargo",L("Груз","Cargo"));_transfer=new(scenario,language,"ExtractionTransfer",submit,access);cargo.AddChild(GameUi.Card(_transfer));
        var more=sections.Add("more",L("Дополнительно","More"));more.AddChild(legacyDetails);more.AddChild(ExtraActions);
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    public void ShowData(PlayerView view,bool pending)
    {
        var machine=view.Equipment!.Single(e=>e.Id.Value==_id);var type=_scenario.EquipmentTypes!.Single(t=>t.Id==machine.TypeId);var definition=type.Levels.Single(l=>l.Level==machine.Level);
        var group=view.Employment.Single(g=>g.Id==machine.GroupId);var stock=view.Inventories.Single(i=>i.Id==machine.StorageId);var model=view.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_id).Model;
        if(_picture is null){_picture=GameUiArt.Vehicle(model,new(200,134));_art.AddChild(_picture);}
        _picture.CustomMinimumSize=GetViewportRect().Size.Y<540?new(140,94):new(200,134);
        _title.Text=L("Добыча: ","Extracting: ")+PlayerText.Item(_language,type.ItemId);_site.Text=L("Уровень ","Level ")+machine.Level+$" · ({machine.Position.X},{machine.Position.Z})";
        _state.Text=machine.Deployed!=true?L("Готова к перевозке","Packed for transport"):GameUi.Reason(_language,machine.ReasonCode);
        _hint.Text=pending?L("Действие запрошено. Включите время.","Action queued. Run time."):machine.Deployed!=true?L("Доставьте машину на свой участок с этим ресурсом, затем нажмите «Разместить».","Drive to your own site containing this resource, then choose Place."):machine.ReasonCode=="NoStaff"?L("Найм идёт постепенно. Проверьте ставку в разделе «Работники».","Hiring takes time. Review wages in Crew."):machine.Mode==EquipmentMode.Off?L("Выберите личный или автоматический режим, чтобы начать работу.","Choose personal or automatic mode to begin."):L("Завершённые циклы пополняют груз. Заберите ресурс ниже или откройте «Груз».","Completed cycles fill the cargo. Collect below or open Cargo.");
        _progress.MaxValue=Math.Max(1,machine.WorkRequired);_progress.Value=machine.WorkDone;
        _crew.Text=L("Команда ","Crew ")+group.Headcount+" / "+group.Capacity+" · "+definition.CycleQuantity+L(" за цикл"," per cycle");
        _team.Text=_crew.Text+"\n"+L("Текущая ставка: ","Current wage: ")+PlayerText.Money(group.WageMinor)+(group.PendingWageMinor is {} wage?L(" → объявлено "," → pending ")+PlayerText.Money(wage):"");
        _stock.ShowStock(stock,_language,[type.ItemId]);_take.ShowData(view,stock.Id,view.Participant.InventoryId,[type.ItemId],pending,"output");
        if(!_focused){_focused=true;_transfer.FocusInventory(stock.Id.Value);}_transfer.ShowData(view,pending);
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
}

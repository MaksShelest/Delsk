using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Physical warehouse/ground stocks and overflow. Writes remain authenticated ordinary actions.</summary>
public partial class StoragePanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;private readonly string _lang,_kind;private readonly long _id;private readonly bool _overflowOnly;
    private readonly Action<GameAction> _submit;private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _title=TabletDesign.Heading("",23),_status=GameUi.Text(),_progress=GameUi.Hint("");
    private readonly UiResourceList _resources=new();private readonly ProgressBar _fill=new(){ShowPercentage=false,CustomMinimumSize=new(0,10)};
    private readonly SpinBox _quantity=new(){Name="StorageQuantity",MinValue=1,MaxValue=1000000,Value=1,Step=1,CustomMinimumSize=new(130,40)};
    private readonly VBoxContainer _actions=new(),_legacyTransfers=new();private string _signature="";private long _focusedInventory;
    private readonly QuickStockTransfer _quickPut,_quickTake;private readonly TabletColumns _quick=new(){Name="StorageQuickTransfers",Breakpoint=700};
    private readonly List<(Button Button,Func<GameAction> Action,Func<PlayerView,bool> Allowed)> _buttons=[];
    private readonly Dictionary<long,Label> _deadlines=[];private readonly Dictionary<long,Button> _overflowAll=[];
    private readonly DemolitionPanel? _demolitionPanel;private PlayerView? _view;private bool _pending;
    private readonly UiSections? _pages;private readonly InventoryTransferPanel? _manual;private readonly Button? _buildOpen;
    public StoragePanel(ScenarioDefinition scenario,string language,string kind,long id,Action<GameAction> submit,Func<GameAction,ActionAccess> access,bool overflowOnly=false,Func<long,bool,DemolitionQuote?>? demolitionQuote=null)
    {
        _overflowOnly=overflowOnly;_scenario=scenario;_lang=language;_kind=kind;_id=id;_submit=submit;_access=access;Name="StoragePanel";_status.Name="StorageStatus";_progress.Name="ConstructionProgress";Theme=CompactTabletDesign.Theme;
        var overview=new VBoxContainer();var header=new HBoxContainer();if(!overflowOnly)header.AddChild(TabletDesign.Art("warehouse",new(76,76)));var words=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};words.AddChild(_title);words.AddChild(_status);header.AddChild(words);overview.AddChild(header);overview.AddChild(_fill);overview.AddChild(_progress);overview.AddChild(_resources);
        _quickPut=new(scenario,language,"StoragePut",L("Положить всё","Store all"),submit,access);_quickTake=new(scenario,language,"StorageTake",L("Забрать всё","Take all"),submit,access);_quickPut.UseProductionCard(true);_quickTake.UseProductionCard(false);
        foreach(var pair in new[]{(L("Из рюкзака в запас","From backpack to storage"),_quickPut),(L("Из запаса в рюкзак","From storage to backpack"),_quickTake)}){var box=new VBoxContainer();box.AddChild(GameUi.Hint(pair.Item1));box.AddChild(pair.Item2);_quick.AddChild(GameUi.Card(box));}
        var exact=new VBoxContainer();exact.AddChild(GameUi.Hint(L("Количество для прежних точных операций","Quantity for the detailed operations")));exact.AddChild(_quantity);exact.AddChild(_legacyTransfers);
        if(!overflowOnly)
        {
            _pages=new("StorageNav_");AddChild(_pages);var stocks=_pages.Add("stock",L("Запасы","Stock"));stocks.AddChild(GameUi.Card(overview));stocks.AddChild(_quick);
            _buildOpen=GameUi.Button("StorageBuildOpen",L("Строительство и развитие","Build & develop"),()=>_pages.Select("build"));stocks.AddChild(_buildOpen);
            var transfers=_pages.Add("transfer",L("Перенос","Transfer"));_manual=new(scenario,language,"StorageManual",submit,access);transfers.AddChild(GameUi.Card(_manual));transfers.AddChild(new UiDisclosure(L("Точные операции по товарам","Detailed item operations"),exact));
            var build=_pages.Add("build",L("Строительство","Construction"));build.AddChild(GameUi.Hint(L("Материалы должны лежать на этой клетке. Платёж и начало работ проверяет игра.","Stage the materials on this cell. The game validates payment and starts the work.")));build.AddChild(_actions);
            if(demolitionQuote is not null){var management=_pages.Add("management",L("Управление","Management"));_demolitionPanel=new(language,submit,demolitionQuote,access);management.AddChild(_demolitionPanel);}
        }
        else{AddChild(GameUi.Card(overview));AddChild(_actions);exact.Visible=false;AddChild(exact);_quick.Visible=false;AddChild(_quick);_fill.Visible=false;_resources.Visible=false;}
        _quantity.ValueChanged+=_=>{if(_view is {} v)ShowData(v,_pending);};
    }
    private string L(string ru,string en)=>GameUi.L(_lang,ru,en);
    private long Q=>checked((long)_quantity.Value);
    private static long Free(InventoryView i,string item)=>i.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
    private long Space(InventoryView i,string item)=>(long)Int128.Max(0,(Int128)i.Capacity-i.OccupiedVolume-i.ReservedCapacity)/_scenario.Items.Single(x=>x.Id==item).UnitVolume;
    private Button Action(string label,Func<GameAction> action,Func<PlayerView,bool> allowed,Container? host=null,string name="")
    {
        var button=GameUi.Button(name,label,()=>{});GameUi.Command(button,()=>{if(_pending||_view is null)return;var current=action();if(!_access(current).Allowed||!allowed(_view))return;_pending=true;_submit(current);});
        (host??(action().Kind==ActionKind.Transfer?_legacyTransfers:_actions)).AddChild(button);_buttons.Add((button,action,allowed));return button;
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;
        var plot=_kind=="ground"?view.Plots.SingleOrDefault(p=>p.Id.Value==_id):null;
        var building=_kind=="ground"?view.Buildings.FirstOrDefault(b=>b.Position==plot?.Position&&b.Kind==BuildingKind.Warehouse):view.Buildings.SingleOrDefault(b=>b.Id.Value==_id);
        long? inventory=building?.InputId.Value??view.Storage?.Ground.SingleOrDefault(g=>g.PlotId==_id)?.InventoryId;
        Visible=inventory is not null;if(inventory is null)return;
        var stock=view.Inventories.Single(i=>i.Id.Value==inventory);var pack=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);var work=view.Storage?.Construction.SingleOrDefault(c=>c.InventoryId==inventory);var overflow=stock.Overflow??[];
        if(_overflowOnly){Visible=overflow.Length>0;if(!Visible)return;}
        _quick.Visible=!_overflowOnly;
        if(!_overflowOnly)
        {
            var items=stock.Stock.Select(s=>s.ItemId).Concat(pack.Stock.Select(s=>s.ItemId));_quickPut.ShowData(view,pack.Id,stock.Id,items,pending,"storage");_quickTake.ShowData(view,stock.Id,pack.Id,items,pending,"output");_resources.ShowStock(stock,_lang);
            if(_focusedInventory!=inventory){_focusedInventory=inventory.Value;_manual!.FocusInventory(inventory.Value);}_manual!.ShowData(view,pending);_buildOpen!.Visible=building is null||building.Kind==BuildingKind.Warehouse;_pages!.SetAvailable("build",_buildOpen.Visible);
        }
        long expected=view.Contracts?.Where(c=>c.Buyer==view.Participant.Id&&c.DestinationInventory==stock.Id&&c.ClosedTick is null).Sum(c=>checked(c.RemainingQuantity*_scenario.Items.Single(i=>i.Id==c.ItemId).UnitVolume))??0;
        long excess=overflow.Sum(l=>checked(l.Quantity*_scenario.Items.Single(i=>i.Id==l.ItemId).UnitVolume));
        _title.Text=_overflowOnly?L("Излишек под риском","Overflow at risk"):building is null?L("Собственный участок","Owned plot"):PlayerText.Building(_lang,building.TypeId)+L(" · уровень "," · level ")+building.Level;
        _status.Text=_overflowOnly?L("Спасите ресурс до истечения срока каждой партии.","Move each lot before its expiry."):
            L("Занято ","Used ")+stock.OccupiedVolume+" / "+stock.Capacity+(stock.ReservedCapacity>0?L(" · место в резерве "," · space held ")+stock.ReservedCapacity:"")+(expected>0?L(" · ожидается "," · expected ")+expected:"")+(excess>0?L(" · излишек "," · overflow ")+excess:"");
        _fill.MaxValue=Math.Max(1,stock.Capacity);_fill.Value=stock.OccupiedVolume+stock.ReservedCapacity;
        _progress.Text=work is null?"":L("Строительство: осталось ","Construction: remaining ")+GameDuration.Format(work.CompletesTick-view.Tick,_lang)+L(". Материалы и оплата уже внесены.",". Materials and payment already committed.");_progress.Visible=_progress.Text.Length>0;
        _demolitionPanel?.ShowData(view,pending,work?.Id??building?.Id.Value??0,work is not null);
        string signature=inventory+":"+building?.Level+":"+(work is not null)+":"+string.Join(',',stock.Stock.Select(s=>s.ItemId).Concat(pack.Stock.Select(s=>s.ItemId)).Distinct().Order())+":"+string.Join(',',overflow.Select(l=>l.Id));
        if(signature!=_signature)
        {
            _signature=signature;foreach(var host in new[]{_actions,_legacyTransfers})foreach(Node child in host.GetChildren()){host.RemoveChild(child);child.QueueFree();}_buttons.Clear();_deadlines.Clear();_overflowAll.Clear();
            InventoryView Current(PlayerView v)=>v.Inventories.Single(i=>i.Id.Value==inventory);InventoryView Pack(PlayerView v)=>v.Inventories.Single(i=>i.Id==v.Participant.InventoryId);
            if(!_overflowOnly)foreach(string item in stock.Stock.Select(s=>s.ItemId).Concat(pack.Stock.Select(s=>s.ItemId)).Distinct().Order())
            {
                string key=item;var row=TabletDesign.Flow();_legacyTransfers.AddChild(row);
                Action(L("Положить: ","Store: ")+PlayerText.Item(_lang,key),()=>new(ActionKind.Transfer,Source:pack.Id,Destination:stock.Id,ItemId:key,Quantity:Q),v=>ManualTransferChoice.Read(_scenario,v,Pack(v).Id,Current(v).Id,key,_access,_pending,Q).Enabled,row);
                Action(L("Забрать: ","Take: ")+PlayerText.Item(_lang,key),()=>new(ActionKind.Transfer,Source:stock.Id,Destination:pack.Id,ItemId:key,Quantity:Q),v=>ManualTransferChoice.Read(_scenario,v,Current(v).Id,Pack(v).Id,key,_access,_pending,Q).Enabled,row);
            }
            foreach(var lot in overflow)
            {
                long lotId=lot.Id;string item=lot.ItemId;var box=new VBoxContainer();var deadline=GameUi.Text();box.AddChild(TabletDesign.Art(item,new(36,36)));box.AddChild(deadline);_deadlines.Add(lotId,deadline);
                var quantity=new SpinBox{MinValue=1,MaxValue=1000000,Value=1,Step=1,Rounded=true,CustomMinimumSize=new(120,40)};box.AddChild(quantity);var buttons=TabletDesign.Flow();box.AddChild(buttons);_actions.AddChild(GameUi.Card(box));
                long Maximum(PlayerView v)=>Math.Min((Current(v).Overflow??[]).FirstOrDefault(l=>l.Id==lotId)?.Quantity??0,Space(Pack(v),item));
                foreach(bool single in new[]{true,false})Action(single?L("Спасти 1","Rescue 1"):L("Спасти выбранное","Rescue selected"),()=>new(ActionKind.TakeOverflow,Source:stock.Id,OfferId:lotId,ItemId:item,Quantity:single?1:(long)quantity.Value),v=>Maximum(v)>=(single?1:(long)quantity.Value),buttons);
                var all=Action(L("Спасти всё","Rescue all"),()=>new(ActionKind.TakeOverflow,Source:stock.Id,OfferId:lotId,ItemId:item,Quantity:Math.Max(1,Maximum(_view!))),v=>Maximum(v)>0,buttons,"StorageOverflowAll_"+lotId);_overflowAll.Add(lotId,all);
                quantity.ValueChanged+=_=>{if(_view is {} current)ShowData(current,_pending);};
            }
            if(building is null&&!_overflowOnly&&view.Development is not null&&plot?.ExpansionFor is null)
            foreach(var production in _scenario.BuildingTypes!.Where(t=>t.Kind==BuildingKind.Production))
            {
                var initial=production.Levels.Single(l=>l.Level==1);bool anchor=_scenario.Layout is null||_scenario.Layout.IndustrialLots.Any(l=>l.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First()==stock.Position);if(!anchor)continue;
                var box=new VBoxContainer();box.AddChild(GameUi.Intro(GameUi.Industry(_lang,production.Id),PlayerText.Money(initial.PriceMinor)+" + "+string.Join(", ",initial.Materials.Select(m=>PlayerText.Item(_lang,m.ItemId)+" ×"+m.Quantity))+" · "+GameDuration.Format(initial.ConstructionTicks,_lang),production.Id,80));_actions.AddChild(box);string typeId=production.Id;
                Action(L("Построить","Build"),()=>new(ActionKind.Build,Target:stock.Position,BuildingTypeId:typeId),v=>work is null&&v.AvailableCashMinor>=initial.PriceMinor&&initial.Materials.All(m=>Free(Current(v),m.ItemId)>=m.Quantity),box);
            }
            var type=_scenario.BuildingTypes!.Single(t=>t.Id=="warehouse");var next=type.Levels.SingleOrDefault(l=>l.Level==(building?.Level??0)+1);
            if(next is not null&&!_overflowOnly&&(building is null||building.Kind==BuildingKind.Warehouse)&&_scenario.Economy?.Storage is not null)
            {
                var box=new VBoxContainer();box.AddChild(GameUi.Text(PlayerText.Money(next.PriceMinor)+" + "+string.Join(", ",next.Materials.Select(m=>PlayerText.Item(_lang,m.ItemId)+" ×"+m.Quantity))+" · "+GameDuration.Format(next.ConstructionTicks,_lang)));_actions.AddChild(GameUi.Card(box));
                Action(building is null?L("Построить склад","Build warehouse"):L("Улучшить склад","Upgrade warehouse"),()=>building is null?new(ActionKind.Build,Target:stock.Position,BuildingTypeId:type.Id):new(ActionKind.UpgradeBuilding,Building:building.Id),v=>work is null&&(building is not null||plot?.ExpansionFor is null)&&v.AvailableCashMinor>=next.PriceMinor&&next.Materials.All(m=>Free(Current(v),m.ItemId)>=m.Quantity),box);
            }
        }
        foreach(var lot in overflow)
        {
            if(_deadlines.TryGetValue(lot.Id,out var label))label.Text=PlayerText.Item(_lang,lot.ItemId)+" ×"+lot.Quantity+L(" · до потери "," · expiry in ")+GameDuration.Format(lot.ExpiresTick-view.Tick,_lang);
            if(_overflowAll.TryGetValue(lot.Id,out var button))button.Text=L("Спасти всё (","Rescue all (")+Math.Min(lot.Quantity,Space(pack,lot.ItemId))+")";
        }
        foreach(var entry in _buttons){var action=entry.Action();var permission=_access(action);entry.Button.Disabled=pending||view.Participant.Busy||!entry.Allowed(view)||!permission.Allowed;entry.Button.TooltipText=!permission.Allowed?GameUi.Reason(_lang,permission.ReasonCode):entry.Button.Disabled?L("Проверьте количество, свободное место и нужные материалы.","Check quantity, available space and required materials."):"";}
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
}

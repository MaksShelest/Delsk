using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Opt-in wood production workspace over the existing private player view and commands.</summary>
public partial class CompactProductionTablet : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly long _id;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _identity=Text(),_recipe=Text(),_stockIn=Text(),_stockOut=Text(),_upgrade=Text();
    private readonly TabContainer _tabs=new(){Name="ProductionSections",TabsVisible=false,SizeFlagsVertical=SizeFlags.ExpandFill};
    private readonly OptionButton _picker=new(){Name="ProductionSectionPicker",FitToLongestItem=false};
    private readonly HFlowContainer _nav=TabletDesign.Flow();
    private readonly VBoxContainer _lines=new();
    private readonly Dictionary<long,(Label Status,ProgressBar Progress,Label Wage,Button Work,Button Minus,Button Plus)> _rows=[];
    private readonly Dictionary<string,Button> _navigation=[];
    private PlayerView? _view;
    private bool _pending;
    public CompactProductionTablet(ScenarioDefinition scenario,string language,long id,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name="CompactProduction";_scenario=scenario;_language=language;_id=id;_submit=submit;_access=access;
        SizeFlagsVertical=SizeFlags.ExpandFill;Theme=CompactTabletDesign.Theme;
        AddChild(_identity);AddChild(_recipe);AddChild(_nav);AddChild(_picker);AddChild(_tabs);
        var primary=Page("production",L("Производство","Production"));
        primary.AddChild(_lines);
        var stocks=new TabletColumns{Breakpoint=600};stocks.AddChild(StockCard(L("Сырьё · вход","Materials · input"),_stockIn));stocks.AddChild(StockCard(L("Продукция · выход","Goods · output"),_stockOut));primary.AddChild(stocks);
        primary.AddChild(_upgrade);var development=TabletDesign.Flow();primary.AddChild(development);
        development.AddChild(CompactTabletDesign.Button("ProductionDevelopment",L("Расширение","Expansion"),()=>Select("development")));
        development.AddChild(CompactTabletDesign.Button("ProductionModernization",L("Оснащение и модернизация","Equipment & upgrades"),()=>Select("recipes")));
        Page("logistics",L("Логистика","Logistics"));Page("recipes",L("Цеха и рецепты","Workshops & recipes"));Page("development",L("Развитие","Development"));
        _picker.ItemSelected+=index=>Select(_navigation.Keys.ElementAt((int)index));
        Resized+=LayoutNavigation;Ready+=LayoutNavigation;Select("production");
    }
    private void LayoutNavigation(){bool narrow=Size.X<600;_nav.Visible=!narrow;_picker.Visible=narrow;CompactTabletDesign.FitControls(this,Size.X-56);}
    public VBoxContainer Page(string key,string? caption=null)
    {
        int existing=Array.IndexOf(_navigation.Keys.ToArray(),key);
        if(existing>=0)return (VBoxContainer)_tabs.GetChild<ScrollContainer>(existing).GetChild(0);
        var scroll=new ScrollContainer{Name="ProductionPage_"+key,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_tabs.AddChild(scroll);
        var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(box);
        var button=CompactTabletDesign.Button("ProductionNav_"+key,caption??key,()=>Select(key));button.ToggleMode=true;_nav.AddChild(button);_navigation.Add(key,button);_picker.AddItem(caption??key);return box;
    }
    private void Select(string key)
    {int index=Array.IndexOf(_navigation.Keys.ToArray(),key);if(index<0)return;_tabs.CurrentTab=index;_picker.Select(index);foreach(var entry in _navigation)entry.Value.SetPressedNoSignal(entry.Key==key);}
    private Control StockCard(string title,Label value)
    {var box=new VBoxContainer();box.AddChild(TabletDesign.Heading(title,16));box.AddChild(value);return CompactTabletDesign.Card(box);}
    private void Send(GameAction action){if(!_pending&&_access(action).Allowed)_submit(action);}
    private void Work(long workshop)
    {
        if(_view is null)return;var e=_view.Enterprises.Single(e=>e.Id.Value==_id);
        bool enabled=workshop==0?e.Enabled:e.Workshops.Single(w=>w.Id==workshop).Enabled;
        Send(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:workshop,Enabled:!enabled));
    }
    private void Wage(long groupId,bool increase)
    {
        if(_view is null)return;var g=_view.Employment.Single(g=>g.Id.Value==groupId);long wage=g.PendingWageMinor??g.WageMinor,step=_view.Economy!.PlayerWageStepMinor;
        if(increase&&wage>long.MaxValue-step)return;
        Send(PlayerDesk.Wage(groupId,increase?wage+step:Math.Max(0,wage-step)));
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;var e=view.Enterprises.Single(e=>e.Id.Value==_id);var b=view.Buildings.Single(b=>b.Id.Value==_id);
        _identity.Text=PlayerText.Building(_language,b.TypeId)+" #"+_id+L(" · уровень "," · level ")+b.Level+" · "+TabletText.Reason(_language,e.ReasonCode);
        var recipe=_scenario.Recipes.Single(r=>r.Id==e.RecipeId);
        _recipe.Text=Formula(recipe);
        var lines=e.Workshops.IsDefault?new[]{(Id:0L,Group:new EmploymentGroupId(_id),Enabled:e.Enabled,Done:e.WorkDone,Required:e.WorkRequired,Reason:e.ReasonCode,Recipe:e.RecipeId)}:
            e.Workshops.Select(w=>(Id:w.Id,Group:w.GroupId,Enabled:w.Enabled,Done:w.WorkDone,Required:w.WorkRequired,Reason:w.ReasonCode,Recipe:w.RecipeId)).ToArray();
        foreach(var line in lines)
        {
            var group=view.Employment.Single(g=>g.Id==line.Group);
            if(!_rows.TryGetValue(line.Id,out var row))
            {
                var box=new VBoxContainer();var status=Text();var progress=new ProgressBar{ShowPercentage=true,CustomMinimumSize=new(0,12)};
                var actions=TabletDesign.Flow();long lineId=line.Id,groupId=group.Id.Value;
                var work=CompactTabletDesign.Button("ProductionWork_"+lineId,"",()=>Work(lineId));
                var minus=CompactTabletDesign.Button("ProductionWageMinus_"+lineId,"−",()=>Wage(groupId,false));
                var plus=CompactTabletDesign.Button("ProductionWagePlus_"+lineId,"+",()=>Wage(groupId,true));
                var wage=new Label{SizeFlagsVertical=SizeFlags.ShrinkCenter};
                actions.AddChild(work);actions.AddChild(minus);actions.AddChild(wage);actions.AddChild(plus);box.AddChild(status);box.AddChild(progress);box.AddChild(actions);_lines.AddChild(CompactTabletDesign.Card(box));
                row=(status,progress,wage,work,minus,plus);_rows.Add(line.Id,row);
            }
            row.Status.Text=Formula(_scenario.Recipes.Single(r=>r.Id==line.Recipe))+" · "+TabletText.Reason(_language,line.Reason)+L(" · работники "," · staff ")+group.Headcount+"/"+group.Capacity;
            row.Progress.MaxValue=Math.Max(1,line.Required);row.Progress.Value=line.Done;
            row.Work.Text=line.Enabled?L("Остановить производство","Stop production"):L("Запустить производство","Start production");
            Configure(row.Work,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:line.Id,Enabled:!line.Enabled),pending);
            long pay=group.PendingWageMinor??group.WageMinor,step=view.Economy!.PlayerWageStepMinor;
            row.Wage.Text=L("Ставка ","Wage ")+PlayerText.Money(group.WageMinor)+(group.PendingWageMinor is {} next?" → "+PlayerText.Money(next):"");
            Configure(row.Minus,PlayerDesk.Wage(group.Id.Value,Math.Max(0,pay-step)),pending||pay==0);
            Configure(row.Plus,PlayerDesk.Wage(group.Id.Value,pay>long.MaxValue-step?pay:pay+step),pending||pay>long.MaxValue-step);
        }
        _stockIn.Text=Stock(view.Inventories.Single(i=>i.Id==e.InputId));_stockOut.Text=Stock(view.Inventories.Single(i=>i.Id==e.OutputId));
        var xp=view.Development?.Experience.SingleOrDefault(x=>x.Industry==b.TypeId);
        _upgrade.Text=L("Развитие: ","Development: ")+(xp is null?L("по доступным уровням","available tiers"):xp.Total+" XP · "+L("технология ","technology ")+xp.TechnologyLevel)+L(" · оснащение и рецепты — в соседних разделах."," · equipment and recipes in adjacent sections.");
        UiSafeArea.Keyboard(this);
        if(IsInsideTree()&&Size.X>0)LayoutNavigation();
    }
    private void Configure(Button button,GameAction action,bool disabled)
    {var access=_access(action);button.Disabled=disabled||!access.Allowed;button.TooltipText=access.Allowed?"":PlayerText.Reason(_language,access.ReasonCode);}
    private string Stock(InventoryView inventory)=>L("Объём ","Volume ")+$"{inventory.OccupiedVolume} / {inventory.Capacity}"+L(" · резерв места "," · reserved room ")+inventory.ReservedCapacity+"\n"+
        (inventory.Stock.Length==0?L("Пока пусто","Empty for now"):string.Join("\n",inventory.Stock.Select(s=>PlayerText.Item(_language,s.ItemId)+" · "+(s.Quantity-s.Reserved)+L(" свободно"," free")+(s.Reserved>0?L(" · резерв "," · reserved ")+s.Reserved:""))));
    private string Formula(RecipeDefinition recipe)=>string.Join(" + ",recipe.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId)+" ×"+i.Quantity))+" → "+PlayerText.Item(_language,recipe.OutputItem)+" ×"+recipe.OutputQuantity;
    private static Label Text()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Shared production workspace, evolved from the approved sawmill. Private projection and ordinary Core commands.</summary>
public partial class WoodworkingTabletPresenter : VBoxContainer
{
    public event Action? MapRequested;
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly long _enterprise;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly QuickStockTransfer _put,_take;
    private readonly TabContainer _pages=new(){Name="WoodworkingPages",TabsVisible=false,SizeFlagsVertical=SizeFlags.ExpandFill};
    private readonly Dictionary<string,Button> _navigation=[];
    private readonly Label _level=Text(),_headline=Text(),_status=Text(),_hint=Text(),_percent=Text(),_inputValue=Text(),_inputHint=Text(),_outputValue=Text(),_outputHint=Text(),_development=Text();
    private readonly UiRecipe _recipe=new(){Name="WoodworkingRecipe"};
    private readonly UiResourceList _ingredients=new();
    private TextureRect? _inputIcon,_outputIcon;
    private readonly ProgressBar _progress=new(){Name="WoodworkingProgress",ShowPercentage=false,CustomMinimumSize=new(0,10),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ShrinkCenter};
    private readonly Button _work,_staff,_map;
    private readonly TextureRect _illustration;
    private readonly PanelContainer _artCard;
    private readonly OptionButton _line=new(){Name="WoodworkingLine",FitToLongestItem=false,CustomMinimumSize=new(120,36),Visible=false};
    private PlayerView? _view;private bool _pending;private long _selectedLine;private string _lineSignature="";
    public VBoxContainer Management {get;}
    public string SelectedPage {get;private set;}="production";
    public WoodworkingTabletPresenter(ScenarioDefinition scenario,string language,long enterprise,Action<GameAction> submit,Func<GameAction,ActionAccess> access,QuickStockTransfer put,QuickStockTransfer take,string industry="woodworking")
    {
        Name="WoodworkingWorkspace";Theme=CompactTabletDesign.Theme;SizeFlagsVertical=SizeFlags.ExpandFill;
        _scenario=scenario;_language=language;_enterprise=enterprise;_submit=submit;_access=access;_put=put;_take=take;
        var navigation=TabletDesign.Flow();AddChild(navigation);AddChild(_pages);
        foreach(var page in new[]{("production",L("Производство","Production")),("workers",L("Цеха","Workshops")),("delivery",L("Доставка","Delivery")),("development",L("Развитие","Development")),("object",L("Объект","Object"))})
        {var button=CompactTabletDesign.Button("WoodworkingNav_"+page.Item1,page.Item2,()=>Select(page.Item1));button.ToggleMode=true;navigation.AddChild(button);_navigation.Add(page.Item1,button);var scroll=new ScrollContainer{Name="WoodworkingPage_"+page.Item1,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_pages.AddChild(scroll);scroll.AddChild(new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill});}
        var main=Page("production");var hero=new HBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};main.AddChild(hero);
        var art=new VBoxContainer();_level.AddThemeFontSizeOverride("font_size",13);_level.AddThemeColorOverride("font_color",new("58735a"));art.AddChild(_level);
        _illustration=TabletDesign.Art(industry,new(240,170));_illustration.Name="WoodworkingIllustration";art.AddChild(_illustration);
        _artCard=CompactTabletDesign.Card(art);_artCard.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;_artCard.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("eff4e9",10));hero.AddChild(_artCard);
        var production=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};hero.AddChild(production);
        _status.Name="WoodworkingStatus";_status.AddThemeFontSizeOverride("font_size",14);production.AddChild(_status);
        _headline.AddThemeFontSizeOverride("font_size",24);production.AddChild(_headline);production.AddChild(_recipe);
        _hint.Name="WoodworkingGuidance";production.AddChild(_hint);
        var progress=new HBoxContainer();progress.AddChild(_progress);_percent.AddThemeFontSizeOverride("font_size",14);_percent.AutowrapMode=TextServer.AutowrapMode.Off;_percent.SizeFlagsVertical=SizeFlags.ShrinkCenter;progress.AddChild(_percent);production.AddChild(progress);
        var actions=TabletDesign.Flow();_work=GameUi.Button("WoodworkingWork","",ToggleWork,true);actions.AddChild(_work);
        _staff=CompactTabletDesign.Button("WoodworkingStaff","",()=>Select("workers"));actions.AddChild(_staff);
        _map=CompactTabletDesign.Button("WoodworkingFindInputs",L("Найти сырьё","Find materials"),()=>MapRequested?.Invoke());actions.AddChild(_map);actions.AddChild(_line);production.AddChild(actions);
        _line.ItemSelected+=index=>{_selectedLine=_line.GetItemId((int)index);if(_view is not null)ShowData(_view,_pending);};
        var stocks=new TabletColumns{Breakpoint=700};main.AddChild(stocks);
        void Stock(string title,string icon,Label value,Label hint,QuickStockTransfer quick,bool deposit)
        {
            var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};box.AddThemeConstantOverride("separation",6);var caption=new HBoxContainer();var picture=TabletDesign.Art(icon,new(26,26));if(deposit)_inputIcon=picture;else _outputIcon=picture;caption.AddChild(picture);var stockTitle=TabletDesign.Heading(title,16);stockTitle.SizeFlagsHorizontal=SizeFlags.ExpandFill;caption.AddChild(stockTitle);box.AddChild(caption);if(deposit)box.AddChild(_ingredients);
            value.AddThemeFontSizeOverride("font_size",21);box.AddChild(value);hint.AddThemeFontSizeOverride("font_size",14);hint.AddThemeColorOverride("font_color",new("69766e"));box.AddChild(hint);
            quick.GetParent()?.RemoveChild(quick);quick.UseProductionCard(deposit);box.AddChild(quick);stocks.AddChild(CompactTabletDesign.Card(box));
        }
        Stock(L("Сырьё","Materials"),"wood",_inputValue,_inputHint,put,true);Stock(L("Готовая продукция","Finished goods"),"plank",_outputValue,_outputHint,take,false);
        var objectColumns=new TabletColumns{Breakpoint=700};Page("object").AddChild(objectColumns);
        var links=new VBoxContainer();links.AddChild(TabletDesign.Heading(L("Следующий шаг развития","Next development step"),20));links.AddChild(_development);
        links.AddChild(CompactTabletDesign.Button("WoodworkingOpenDevelopment",L("Развитие и расширение","Development & expansion"),()=>Select("development")));
        links.AddChild(CompactTabletDesign.Button("WoodworkingOpenWorkshops",L("Рецепты и модернизация","Recipes & equipment"),()=>Select("workers")));
        links.AddChild(CompactTabletDesign.Button("WoodworkingOpenDelivery",L("Настроить доставку","Set up delivery"),()=>Select("delivery")));objectColumns.AddChild(CompactTabletDesign.Card(links));
        Management=new VBoxContainer();Management.AddChild(TabletDesign.Heading(L("Перед сносом","Before demolition"),20));Management.AddChild(new Label{Text=L("Опасные действия отделены от производства. Перед сносом вы увидите стоимость, возврат материалов и препятствия.","Dangerous actions are kept separate. Before demolition, review the cost, recovered materials and blockers."),AutowrapMode=TextServer.AutowrapMode.WordSmart});objectColumns.AddChild(CompactTabletDesign.Card(Management));
        Page("workers").AddChild(new Label{Text=L("Настройте ставку, рецепт и оснащение выбранного цеха. Найм идёт постепенно по правилам города.","Set each workshop's wage, recipe and equipment. Hiring follows the city's rules and takes time."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        Resized+=LayoutArtwork;Ready+=LayoutArtwork;Select("production");
    }
    private void LayoutArtwork()
    {bool shortScreen=GetViewportRect().Size.Y<540;_illustration.CustomMinimumSize=shortScreen?new(180,128):new(240,170);_artCard.CustomMinimumSize=new(shortScreen?200:260,0);}
    public VBoxContainer Page(string key)=>_pages.GetChild<ScrollContainer>(Array.IndexOf(_navigation.Keys.ToArray(),key)).GetChild<VBoxContainer>(0);
    public void Select(string key)
    {int index=Array.IndexOf(_navigation.Keys.ToArray(),key);if(index<0)return;SelectedPage=key;_pages.CurrentTab=index;foreach(var row in _navigation)row.Value.SetPressedNoSignal(row.Key==key);}
    private void ToggleWork()
    {
        if(_view is null||_pending)return;var e=_view.Enterprises.Single(e=>e.Id.Value==_enterprise);var active=e.Workshops.IsDefault?null:e.Workshops.FirstOrDefault(w=>w.Id==_selectedLine)??e.Workshops[0];
        var action=new GameAction(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:active?.Id??0,Enabled:!(active?.Enabled??e.Enabled));if(!_access(action).Allowed)return;_pending=true;_work.Disabled=true;_submit(action);
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;var e=view.Enterprises.Single(e=>e.Id.Value==_enterprise);var building=view.Buildings.Single(b=>b.Id.Value==_enterprise);
        if(!e.Workshops.IsDefault)
        {
            string signature=string.Join(',',e.Workshops.Select(w=>w.Id));if(signature!=_lineSignature){_lineSignature=signature;_line.Clear();foreach(var row in e.Workshops)_line.AddItem(L("Цех #","Workshop #")+row.Id,checked((int)row.Id));}
            if(!e.Workshops.Any(w=>w.Id==_selectedLine))_selectedLine=e.Workshops[0].Id;
            for(int n=0;n<e.Workshops.Length;n++)_line.SetItemText(n,L("Цех ","Workshop ")+(n+1)+" · "+PlayerText.Item(_language,_scenario.Recipes.Single(r=>r.Id==e.Workshops[n].RecipeId).OutputItem));
            _line.Select(Array.FindIndex(e.Workshops.ToArray(),w=>w.Id==_selectedLine));_line.Visible=e.Workshops.Length>1;
        }
        var active=e.Workshops.IsDefault?null:e.Workshops.First(x=>x.Id==_selectedLine);string recipeId=active?.RecipeId??e.RecipeId;var recipe=_scenario.Recipes.Single(r=>r.Id==recipeId);
        var group=view.Employment.Single(g=>g.Id.Value==(active?.GroupId.Value??e.Id.Value));bool enabled=active?.Enabled??e.Enabled;string reason=active?.ReasonCode??e.ReasonCode;long done=active?.WorkDone??e.WorkDone,total=active?.WorkRequired??e.WorkRequired,batch=active?.BatchId??e.BatchId;
        _level.Text=L("УРОВЕНЬ ","LEVEL ")+building.Level+(!e.Workshops.IsDefault&&e.Workshops.Length>1?L(" · ЦЕХОВ "," · WORKSHOPS ")+e.Workshops.Length:"");_headline.Text=L("Производим ","Making ")+PlayerText.Item(_language,recipe.OutputItem);_recipe.ShowRecipe(recipe,_language);
        var input=view.Inventories.Single(i=>i.Id==e.InputId);var output=view.Inventories.Single(i=>i.Id==e.OutputId);var pack=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);
        bool canLoad=recipe.Ingredients.Any(i=>ManualTransferChoice.Read(_scenario,view,pack.Id,input.Id,i.ItemId,_access,pending).Enabled);
        string missing=string.Join(", ",recipe.Ingredients.Where(i=>Free(input,i.ItemId)<i.Quantity).Select(i=>PlayerText.Item(_language,i.ItemId)));
        bool preparing=view.Demolition?.Preparing.Contains(_enterprise)==true||view.Development?.Preparing.Contains(_enterprise)==true;
        (string status,string hint,string color)=preparing?(L("Подготовка к изменениям","Preparing changes"),L("Завершите подготовку в разделе «Объект» или «Развитие».","Review preparation in Object or Development."),"97651a"):!enabled?(L("Производство остановлено","Production stopped"),L("Добавьте сырьё и нажмите «Запустить».","Add materials, then choose Start."),"69766e"):
            reason switch
            {
                "Working"=>(L("Производство работает","Production running"),group.Headcount<group.Capacity?L("Работает часть команды. Полный штат повысит скорость выпуска.","Part of your team is at work. A full crew improves production speed."):L("Партия в работе. Готовую продукцию можно забрать ниже.","A batch is in progress. Collect finished goods below."),"24664f"),
                "NoInputs" or "NoInput"=>(L("Не хватает сырья","Materials needed"),canLoad?L("Сырьё уже в рюкзаке. Нажмите «Положить всё», чтобы продолжить.","Materials are in your backpack. Choose Store all to continue."):recipe.Ingredients.Length==1&&recipe.Ingredients[0].ItemId=="wood"?L("Нужна древесина. Добудьте её в лесу и принесите в рюкзаке.","Wood is needed. Harvest it in the forest and bring it in your backpack."):L("Для следующей партии нужно: ","The next batch needs: ")+missing+L(". Добавьте недостающие ресурсы.",". Add the missing materials."),"97651a"),
                "NoStaff"=>(L("Нужны работники","Workers needed"),L("Проверьте сырьё и ставку в разделе «Цеха». Найм происходит постепенно.","Check materials and wages in Workshops. Hiring takes time."),"97651a"),
                "NoOutputSpace"=>(L("Склад продукции заполнен","Finished-goods storage full"),L("Заберите продукцию, чтобы освободить место для следующей партии.","Collect finished goods to make room for the next batch."),"97651a"),
                "InsufficientFunds" or "NoMoney"=>(L("Не хватает денег для работы","Production funds needed"),L("Нужны свободные деньги для оплаты работы и услуги производства.","Available funds are needed for wages and production services."),"97651a"),
                "PreparingDemolition" or "PreparingDevelopment" or "ConstructionInProgress"=>(L("Предприятие готовится к изменениям","Enterprise changes in progress"),L("Проверьте состояние в разделах «Развитие» и «Объект».","Review progress in Development and Object."),"97651a"),
                _=>(L("Ожидание производства","Waiting to produce"),L("Проверьте сырьё, работников и условия выбранного цеха.","Check materials, workers and the selected workshop."),"69766e")
            };
        _status.Text="● "+status;_status.AddThemeColorOverride("font_color",new(color));_status.TooltipText=TabletText.Reason(_language,reason);_hint.Text=pending?L("Действие запрошено. Включите время, чтобы оно выполнилось.","Action queued. Run time to apply it."):hint;
        _progress.MaxValue=Math.Max(1,total);_progress.Value=done;_percent.Text=total>0?Int128.Clamp((Int128)100*done/total,0,100)+"%":L("Новая партия","Next batch");
        _work.Text=enabled?L("Остановить","Stop"):L("Запустить","Start");var access=_access(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:active?.Id??0,Enabled:!enabled));_work.Disabled=pending||!access.Allowed||preparing;_work.TooltipText=access.Allowed?pending?L("Действие ожидает выполнения.","An action is pending."):"":PlayerText.Reason(_language,access.ReasonCode);
        _staff.Text=(!e.Workshops.IsDefault&&e.Workshops.Length>1?L("Команда цеха ","Workshop crew "):L("Работники ","Workers "))+group.Headcount+" / "+group.Capacity;_map.Visible=enabled&&(reason is "NoInputs" or "NoInput")&&!canLoad;
        _ingredients.Visible=recipe.Ingredients.Length>1;_inputValue.Visible=!_ingredients.Visible;_ingredients.ShowStock(input,_language,recipe.Ingredients.Select(i=>i.ItemId));_inputIcon!.Texture=TabletDesign.Texture(recipe.Ingredients[0].ItemId);_outputIcon!.Texture=TabletDesign.Texture(recipe.OutputItem);
        _inputValue.Text=string.Join(" · ",recipe.Ingredients.Select(i=>Cap(PlayerText.Item(_language,i.ItemId))+" · "+Free(input,i.ItemId)));
        _inputHint.Text=L("В рюкзаке: ","In backpack: ")+string.Join(" · ",recipe.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId)+" ×"+Free(pack,i.ItemId)));
        _inputHint.Visible=recipe.Ingredients.Any(i=>Free(pack,i.ItemId)>0);_outputHint.Visible=Free(output,recipe.OutputItem)>0;
        _outputValue.Text=Cap(PlayerText.Item(_language,recipe.OutputItem))+" · "+Free(output,recipe.OutputItem);_outputHint.Text=batch>0?L("Готовая партия появится здесь.","The finished batch will appear here."):L("Можно забрать в рюкзак.","Collect into your backpack.");
        _put.ShowData(view,pack.Id,input.Id,recipe.Ingredients.Select(i=>i.ItemId),pending,"input");_take.ShowData(view,output.Id,pack.Id,[recipe.OutputItem],pending,"output");
        var xp=view.Development?.Experience.SingleOrDefault(x=>x.Industry==building.TypeId);_development.Text=L("Корпус уровня ","Building level ")+building.Level+(xp is null?"":" · "+xp.Total+" XP · "+L("технология ","technology ")+xp.TechnologyLevel)+"\n"+L("Расширение и оснащение оплачиваются отдельно. Условия и материалы показаны в соответствующих разделах.","Expansion and equipment are paid separately. Review conditions and materials in their sections.");
        foreach(string page in new[]{"workers","development"})CompactTabletDesign.FitControls(Page(page),Math.Max(40,Size.X-70));
        UiSafeArea.Keyboard(this);
    }
    private static long Free(InventoryView inventory,string item)=>inventory.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
    private static string Cap(string text)=>text.Length==0?text:char.ToUpperInvariant(text[0])+text[1..];
    private static Label Text()=>new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

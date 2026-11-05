using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Live workshop cards. Recipes, payroll and staff transfers use the existing Core actions.</summary>
public partial class WorkshopPanel : VBoxContainer
{
    private sealed class Row
    {
        public Label Title=TabletDesign.Heading("",20),Status=GameUi.Text(),Details=GameUi.Hint("");
        public ProgressBar Progress=new(){CustomMinimumSize=new(0,12),ShowPercentage=true};
        public UiRecipe Current=new(),Preview=new();
        public VBoxContainer Actions=new(),Team=new(),Advanced=new();
        public OptionButton Recipes=new(){FitToLongestItem=false,ClipText=true,CustomMinimumSize=new(150,40)};
        public string RecipeKey="";
    }
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly long _enterprise;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _summary=GameUi.Hint("");
    private readonly Dictionary<long,Row> _rows=[];
    private readonly Dictionary<VBoxContainer,HFlowContainer> _flows=[];
    private readonly Dictionary<string,(Button Button,GameAction Action)> _buttons=[];
    private readonly HashSet<string> _seen=[];
    private readonly VBoxContainer _installation=new();
    private bool _pending;private PlayerView? _view;
    public WorkshopPanel(ScenarioDefinition scenario,string language,long enterprise,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        _scenario=scenario;_language=language;_enterprise=enterprise;_submit=submit;_access=access;
        AddChild(TabletDesign.Heading(L("Команды и рецепты цехов","Workshop teams & recipes")));AddChild(_summary);AddChild(_installation);
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private void Action(VBoxContainer host,string key,string title,GameAction action,bool enabled,string why="")
    {
        if(!_buttons.TryGetValue(key,out var entry))
        {
            if(!_flows.TryGetValue(host,out var flow)){flow=TabletDesign.Flow();host.AddChild(flow);_flows.Add(host,flow);}
            var button=GameUi.Button("Workshop_"+key.Replace(':','_'),title,()=>
            {
                if(_pending||!_buttons.TryGetValue(key,out var current)||current.Button.Disabled)return;
                _pending=true;current.Button.Disabled=true;_submit(current.Action);
            });flow.AddChild(button);entry=(button,action);
        }
        _seen.Add(key);entry.Button.Visible=true;_buttons[key]=(entry.Button,action);var access=_access(action);entry.Button.Text=title;entry.Button.Disabled=_pending||!enabled||!access.Allowed;
        entry.Button.TooltipText=_pending?L("Действие ожидает выполнения. Включите время.","Action queued. Run time to apply it."):!access.Allowed?GameUi.Reason(_language,access.ReasonCode):!enabled?why:"";
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;_seen.Clear();var e=view.Enterprises.SingleOrDefault(x=>x.Id.Value==_enterprise);Visible=e is not null&&!e.Workshops.IsDefault;
        if(!Visible)return;
        var building=view.Buildings.Single(b=>b.Id.Value==_enterprise);var type=_scenario.BuildingTypes!.Single(t=>t.Id==building.TypeId);
        int slots=type.Levels.Single(l=>l.Level==building.Level).WorkshopSlots;
        _summary.Text=L("Цехов: ","Equipped workshops: ")+e!.Workshops.Length+" / "+slots+L(". Сырьё и продукция хранятся вместе. Найм идёт постепенно; перевод меняет существующий штат.",". Materials and goods use shared storage. Hiring takes time; transfers use your existing crew.");
        foreach(var w in e.Workshops)
        {
            if(!_rows.TryGetValue(w.Id,out var row))
            {
                row=new();var body=new VBoxContainer();body.AddChild(row.Title);body.AddChild(row.Current);body.AddChild(row.Status);body.AddChild(row.Progress);body.AddChild(row.Actions);body.AddChild(row.Team);
                row.Advanced.AddChild(row.Details);row.Advanced.AddChild(row.Recipes);row.Advanced.AddChild(row.Preview);body.AddChild(new UiDisclosure(L("Рецепт и оснащение","Recipe & equipment"),row.Advanced));AddChild(GameUi.Card(body));_rows.Add(w.Id,row);
                row.Recipes.Name="WorkshopRecipeChoice_"+w.Id;row.Recipes.ItemSelected+=_=>{if(_view is not null)ShowData(_view,_pending);};
            }
            var recipe=_scenario.Recipes.Single(r=>r.Id==w.RecipeId);var group=view.Employment.Single(g=>g.Id==w.GroupId);
            row.Title.Text=L("Цех ","Workshop ")+(Array.FindIndex(e.Workshops.ToArray(),x=>x.Id==w.Id)+1)+" · "+PlayerText.Item(_language,recipe.OutputItem);row.Current.ShowRecipe(recipe,_language);
            row.Status.Name="WorkshopStatus_"+w.Id;row.Status.Text=GameUi.Reason(_language,w.ReasonCode)+" · "+L("Команда ","Crew ")+w.Staff+" / "+group.Capacity+" · "+w.ProductivityPercent+"%\n"+L("Ставка ","Wage ")+PlayerText.Money(group.WageMinor)+(group.PendingWageMinor is {} pay?L(" → объявлено "," → pending ")+PlayerText.Money(pay):"");
            row.Progress.MaxValue=Math.Max(1,w.WorkRequired);row.Progress.Value=w.WorkDone;
            Action(row.Actions,"work:"+w.Id,w.Enabled?L("Остановить цех","Stop workshop"):L("Запустить цех","Start workshop"),new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:w.Id,Enabled:!w.Enabled),true);
            int technology=view.Development?.Experience.Single(x=>x.Industry==building.TypeId).TechnologyLevel??building.Level;
            int version=view.ProductionContent?.Technologies.SingleOrDefault(t=>t.Workshop==w.Id)?.Version??1;
            var recipes=type.Levels.Where(l=>l.Level<=technology).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal)
                .Where(id=>view.ProductionContent is not {} content||content.Rules.Versions.Single(v=>v.RecipeId==id).Version<=version).ToArray();
            string signature=string.Join(',',recipes);if(signature!=row.RecipeKey)
            {
                string selected=row.Recipes.Selected>=0?row.Recipes.GetItemMetadata(row.Recipes.Selected).AsString():w.RecipeId;row.RecipeKey=signature;row.Recipes.Clear();
                foreach(string id in recipes){var r=_scenario.Recipes.Single(r=>r.Id==id);row.Recipes.AddItem(PlayerText.Item(_language,r.OutputItem)+" · "+string.Join(" + ",r.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId))));row.Recipes.SetItemMetadata(row.Recipes.ItemCount-1,id);if(id==selected)row.Recipes.Select(row.Recipes.ItemCount-1);}
            }
            string chosen=row.Recipes.Selected>=0?row.Recipes.GetItemMetadata(row.Recipes.Selected).AsString():w.RecipeId;
            row.Preview.ShowRecipe(_scenario.Recipes.Single(r=>r.Id==chosen),_language);
            row.Details.Text=(w.BatchId>0?L("Партия сохранена. Для смены рецепта завершите её, затем остановите цех между партиями.","A batch is retained. Finish it, then stop between batches to change the recipe."):w.Enabled?L("Остановите цех, чтобы выбрать другой рецепт.","Stop this workshop to choose another recipe."):L("Выберите рецепт и запустите цех. Новые варианты открываются развитием и оснащением.","Choose a recipe, then start. Development and equipment unlock further recipes."))+"\n"+L("Оснащение ","Equipment ")+version+L(" · готовых партий "," · completed batches ")+w.CompletedBatches+L(" · услуга за полный рабочий тик "," · service per full work tick ")+PlayerText.Money(_scenario.Economy!.Workshops!.ServiceMinorPerFullTick);
            Action(row.Advanced,"recipe:"+w.Id,L("Выбрать рецепт","Use recipe"),new(ActionKind.SetRecipe,Enterprise:e.Id,WorkshopId:w.Id,RecipeId:chosen),!w.Enabled&&w.BatchId==0&&chosen!=w.RecipeId,row.Details.Text);
            if(view.ProductionContent is {} pc)
            {
                var project=pc.Projects.SingleOrDefault(p=>p.Workshop==w.Id);
                if(project is not null)row.Details.Text+="\n"+L("Модернизация: осталось ","Modernization: remaining ")+GameDuration.Format(project.EndTick-view.Tick,_language);
                if(pc.Rules.Modernizations.SingleOrDefault(m=>m.Version==version+1) is {} upgrade)
                {
                    var input=view.Inventories.Single(i=>i.Id==e.InputId);
                    bool ready=!w.Enabled&&w.BatchId==0&&project is null&&technology>=(upgrade.Version==2?4:5)&&view.AvailableCashMinor>=upgrade.PriceMinor&&upgrade.Materials.All(m=>input.Stock.Where(s=>s.ItemId==m.ItemId).Sum(s=>s.Quantity-s.Reserved)>=m.Quantity);
                    string terms=L("Оснащение ","Equipment ")+upgrade.Version+" · "+PlayerText.Money(upgrade.PriceMinor)+" + "+string.Join(", ",upgrade.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity))+" · "+GameDuration.Format(upgrade.Ticks,_language);
                    Action(row.Advanced,"modernize:"+w.Id,terms,new(ActionKind.ModernizeWorkshop,Enterprise:e.Id,WorkshopId:w.Id,Quantity:upgrade.Version),ready,L("Нужны технология, материалы и свободные деньги. Цех должен быть остановлен без партии.","Requires the technology, materials and available funds. Stop the workshop with no unfinished batch."));
                }
            }
            long wage=group.PendingWageMinor??group.WageMinor,step=view.Economy!.PlayerWageStepMinor;
            Action(row.Team,"wage-plus:"+w.Id,L("Ставка +","Wage +")+PlayerText.Money(step),PlayerDesk.Wage(w.GroupId.Value,wage>long.MaxValue-step?wage:wage+step),wage<=long.MaxValue-step);
            Action(row.Team,"wage-minus:"+w.Id,L("Ставка −","Wage −")+PlayerText.Money(step),PlayerDesk.Wage(w.GroupId.Value,Math.Max(0,wage-step)),wage>0,L("Ставка уже равна нулю.","The wage is already zero."));
            foreach(var donor in e.Workshops.Where(d=>d.Id!=w.Id))
                Action(row.Team,$"staff:{donor.Id}:{w.Id}",L("Перевести 1 из цеха ","Transfer 1 from workshop ")+(Array.FindIndex(e.Workshops.ToArray(),x=>x.Id==donor.Id)+1),new(ActionKind.AssignWorkshopWorkers,Enterprise:e.Id,WorkshopId:w.Id,Group:donor.GroupId,Quantity:1),donor.Staff>0&&w.Staff<group.Capacity,L("В исходном цехе нужен работник, а в этом — свободное место.","The source needs a worker and this workshop needs a free place."));
        }
        if(type.WorkshopOutfit is {} outfit)
            Action(_installation,"outfit",L("Оснастить свободное место · ","Equip a free place · ")+PlayerText.Money(outfit.PriceMinor)+" + "+string.Join(", ",outfit.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity))+" · "+GameDuration.Format(outfit.Ticks,_language),new(ActionKind.OutfitWorkshop,Enterprise:e.Id),e.Workshops.Length+e.Installations.Length<slots&&!view.Participant.Busy,L("Нужно готовое свободное место, материалы на входе и оплата.","Requires a ready free place, materials in input and payment."));
        foreach(var install in e.Installations)_summary.Text+="\n"+L("Монтаж цеха: осталось ","Workshop installation: remaining ")+GameDuration.Format(install.EndTick-view.Tick,_language);
        foreach(var item in _buttons)if(!_seen.Contains(item.Key))item.Value.Button.Visible=false;
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
}

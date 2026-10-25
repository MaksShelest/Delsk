using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own, live D1 lines. Every button submits an ordinary Core action.</summary>
public partial class WorkshopPanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly long _enterprise;
    private readonly System.Action<GameAction> _submit;
    private readonly System.Func<GameAction,ActionAccess> _access;
    private readonly Label _summary=new(){Name="WorkshopSummary",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Dictionary<long,(Label Status,ProgressBar Progress,VBoxContainer Actions)> _rows=[];
    private readonly Dictionary<string,(Button Button,GameAction Action)> _buttons=[];
    private readonly VBoxContainer _installation=new();
    private bool _pending;
    public WorkshopPanel(ScenarioDefinition scenario,string language,long enterprise,System.Action<GameAction> submit,System.Func<GameAction,ActionAccess> access)
    {
        _scenario=scenario;_language=language;_enterprise=enterprise;_submit=submit;_access=access;
        AddChild(TabletDesign.Heading(L("Цеха предприятия","Enterprise workshops")));AddChild(_summary);AddChild(_installation);
    }
    private string L(string ru,string en)=>_language=="ru"?ru:en;
    private void Action(VBoxContainer box,string key,string title,GameAction action,bool enabled)
    {
        if(!_buttons.TryGetValue(key,out var entry))
        {
            var button=new Button{Name="Workshop_"+key.Replace(':','_'),FocusMode=FocusModeEnum.None,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill};
            button.Pressed+=()=>{if(!_pending&&_buttons.TryGetValue(key,out var current)&&!current.Button.Disabled)_submit(current.Action);};box.AddChild(button);entry=(button,action);
        }
        _buttons[key]=(entry.Button,action);var access=_access(action);entry.Button.Text=title;entry.Button.Disabled=_pending||!enabled||!access.Allowed;
        entry.Button.TooltipText=access.Allowed?"":TabletText.Reason(_language,access.ReasonCode);
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _pending=pending;var e=view.Enterprises.SingleOrDefault(e=>e.Id.Value==_enterprise);Visible=e is not null&&!e.Workshops.IsDefault;
        if(!Visible)return;
        var b=view.Buildings.SingleOrDefault(b=>b.Id.Value==_enterprise);var type=b is null?null:_scenario.BuildingTypes!.Single(t=>t.Id==b.TypeId);
        int slots=type?.Levels.Single(l=>l.Level==b!.Level).WorkshopSlots??1;
        _summary.Text=L("Оснащено / готовых мест: ","Equipped / ready places: ")+$"{e!.Workshops.Length}/{slots}"+"\n"+
            L("Общие вход и выход ограничены. Работники переводятся из существующего штата. Новые места открываются отдельным развитием.","Input and output storage is shared and limited. Transfers use existing staff. New places require separate development.");
        foreach(var w in e.Workshops)
        {
            if(!_rows.TryGetValue(w.Id,out var row))
            {
                var body=new VBoxContainer();row=(new Label{Name="WorkshopStatus_"+w.Id,AutowrapMode=TextServer.AutowrapMode.WordSmart},new ProgressBar{CustomMinimumSize=new(0,18),ShowPercentage=true},new VBoxContainer());body.AddChild(row.Status);body.AddChild(row.Progress);body.AddChild(row.Actions);AddChild(TabletDesign.Wrap(body));_rows.Add(w.Id,row);
            }
            var recipe=_scenario.Recipes.Single(r=>r.Id==w.RecipeId);var group=view.Employment.Single(g=>g.Id==w.GroupId);
            string formula=string.Join(" + ",recipe.Ingredients.Select(i=>PlayerText.Item(_language,i.ItemId)+" ×"+i.Quantity))+" → "+PlayerText.Item(_language,recipe.OutputItem)+" ×"+recipe.OutputQuantity;
            row.Status.Text=L("Цех #","Workshop #")+w.Id+" · "+formula+"\n"+L("Персонал ","Staff ")+$"{w.Staff}/5 · {w.ProductivityPercent}% · "+TabletText.Reason(_language,w.ReasonCode)+"\n"+
                L("Готовых партий: ","Completed batches: ")+w.CompletedBatches+L(" · ставка "," · wage ")+PlayerText.Money(group.WageMinor)+
                (group.PendingWageMinor is {} pay?L(" → новая "," → pending ")+PlayerText.Money(pay):"")+"\n"+
                (w.BatchId==0?L("Нет незавершённой партии","No batch in progress"):L("Партия #","Batch #")+w.BatchId+L(" · ставка партии "," · batch wage ")+PlayerText.Money(w.BatchWageMinor??group.WageMinor))+"\n"+
                L("Услуга за полный рабочий тик: ","Service per full work tick: ")+PlayerText.Money(_scenario.Economy!.Workshops!.ServiceMinorPerFullTick);
            row.Progress.MaxValue=Math.Max(1,w.WorkRequired);row.Progress.Value=w.WorkDone;
            Action(row.Actions,"work:"+w.Id,w.Enabled?L("Остановить цех","Stop workshop"):L("Запустить цех","Start workshop"),new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:w.Id,Enabled:!w.Enabled),true);
            foreach(string id in type?.Levels.Where(l=>l.Level<=b!.Level).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal)??new[]{w.RecipeId})
            {
                var r=_scenario.Recipes.Single(r=>r.Id==id);
                Action(row.Actions,"recipe:"+w.Id+":"+id,L("Рецепт: ","Recipe: ")+PlayerText.Item(_language,r.OutputItem)+" ("+id+")",new(ActionKind.SetRecipe,Enterprise:e.Id,WorkshopId:w.Id,RecipeId:id),!w.Enabled&&w.BatchId==0&&w.RecipeId!=id);
            }
            long wage=group.PendingWageMinor??group.WageMinor,step=view.Economy!.PlayerWageStepMinor;
            Action(row.Actions,"wage-plus:"+w.Id,L("Ставка +","Wage +")+PlayerText.Money(step),PlayerDesk.Wage(w.GroupId.Value,wage>long.MaxValue-step?wage:wage+step),wage<=long.MaxValue-step);
            Action(row.Actions,"wage-minus:"+w.Id,L("Ставка −","Wage −")+PlayerText.Money(step),PlayerDesk.Wage(w.GroupId.Value,Math.Max(0,wage-step)),wage>0);
            foreach(var donor in e.Workshops.Where(d=>d.Id!=w.Id))
                Action(row.Actions,$"staff:{donor.Id}:{w.Id}",L("Перевести 1 из цеха #","Transfer 1 from workshop #")+donor.Id,new(ActionKind.AssignWorkshopWorkers,Enterprise:e.Id,WorkshopId:w.Id,Group:donor.GroupId,Quantity:1),donor.Staff>0&&w.Staff<5);
        }
        if(type?.WorkshopOutfit is {} outfit)
            Action(_installation,"outfit",L("Оснастить готовое место — ","Equip a ready place — ")+PlayerText.Money(outfit.PriceMinor)+" + "+string.Join(", ",outfit.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity))+L(" · тиков "," · ticks ")+outfit.Ticks,
                new(ActionKind.OutfitWorkshop,Enterprise:e.Id),e.Workshops.Length+e.Installations.Length<slots&&!view.Participant.Busy);
        foreach(var install in e.Installations)_summary.Text+="\n"+L("Монтаж #","Installation #")+install.Id+L(" · осталось тиков "," · ticks left ")+Math.Max(0,install.EndTick-view.Tick);
    }
}

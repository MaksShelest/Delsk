using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Own development projection; every mutation uses the ordinary command path.</summary>
public partial class DevelopmentPanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly long _building;
    private readonly Action<GameAction> _submit;
    private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _status=new(){Name="DevelopmentStatus",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly VBoxContainer _actions=new();
    private readonly Dictionary<string,(Button Button,GameAction Action)> _buttons=[];
    private readonly HashSet<string> _shown=[];
    private bool _pending;
    public DevelopmentPanel(ScenarioDefinition scenario,string language,long building,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name=building==0?"IndustryQuests":"EnterpriseDevelopment";_scenario=scenario;_language=language;_building=building;_submit=submit;_access=access;
        AddChild(TabletDesign.Heading(L(building==0?"Опыт и задания":"Развитие предприятия",building==0?"Experience and quests":"Enterprise development")));
        AddChild(_status);AddChild(_actions);
    }
    private string L(string ru,string en)=>_language=="ru"?ru:en;
    private void Button(string key,string text,GameAction action,bool enabled=true)
    {
        _shown.Add(key);
        if(!_buttons.TryGetValue(key,out var row))
        {
            var button=new Button{Name="Development_"+key.Replace(':','_'),FocusMode=FocusModeEnum.None,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill};
            button.Pressed+=()=>{var live=_buttons[key];if(!live.Button.Disabled)_submit(live.Action);};_actions.AddChild(button);row=(button,action);
        }
        var access=_access(action);row.Button.Text=text;row.Button.Disabled=_pending||!enabled||!access.Allowed;row.Button.TooltipText=access.Allowed?"":TabletText.Reason(_language,access.ReasonCode);_buttons[key]=(row.Button,action);
    }
    public void ShowData(PlayerView view,bool pending)
    {
        var d=view.Development;Visible=d is not null;if(d is null)return;_pending=pending;_shown.Clear();
        if(_building==0)
        {
            int active=d.Quests.Count(q=>q.Status=="Active");
            _status.Text=string.Join("\n",d.Experience.Select(e=>PlayerText.Building(_language,e.Industry)+$": {e.Total} XP · "+L("технология ","technology ")+e.TechnologyLevel+L(" · выпуск/задания "," · production/quests ")+$"{e.ProductionXp}/{e.QuestXp}"))+"\n\n"+
                L("Задания по подписанным договорам с реальным спросом. Покупные товары подходят. Награда — после фактической поставки; исходный договор остаётся обязательным.","Quests follow signed contracts with real demand. Purchased goods qualify. Rewards require actual delivery; the source contract remains binding.")+"\n"+
                L("Занято заданий: ","Active quests: ")+$"{active}/{d.Rules.QuestSlots}"+L(" · обновление через "," · review in ")+(d.NextReviewTick-view.Tick)+L(" активных тиков."," active ticks.")+"\n"+
                string.Join("\n",d.Quests.Where(q=>q.Status=="Active").Concat(d.Quests.Where(q=>q.Status!="Active").TakeLast(10)).Select(q=>$"#{q.Id} · {PlayerText.Item(_language,q.ItemId)} {q.Delivered}/{q.Quantity} · {q.Reward} XP · "+State(q.Status)+L(" · срок "," · deadline ")+q.DeadlineTick));
            foreach(var q in d.Quests.Where(q=>q.Status=="Active"))Button("abandon:"+q.Id,L("Отказаться от задания #","Abandon quest #")+q.Id+L(" (договор сохраняется)"," (contract remains)"),new(ActionKind.AbandonIndustryQuest,OfferId:q.Id));
            foreach(var c in d.Choices)Button($"quest:{c.ContractId}:{c.Industry}:{c.Batches}",PlayerText.Building(_language,c.Industry)+$" · #{c.ContractId} · {PlayerText.Item(_language,c.ItemId)} ×{c.Quantity} → {c.Reward} XP"+L(" · срок "," · deadline ")+c.DeadlineTick,
                new(ActionKind.AcceptIndustryQuest,ContractId:c.ContractId,BuildingTypeId:c.Industry,Quantity:c.Batches,OfferWindow:c.ReviewWindow),active<d.Rules.QuestSlots);
        }
        else
        {
            var b=view.Buildings.SingleOrDefault(b=>b.Id.Value==_building);Visible=b?.Kind==BuildingKind.Production;if(!Visible)return;
            var type=_scenario.BuildingTypes!.Single(t=>t.Id==b!.TypeId);var xp=d.Experience.Single(e=>e.Industry==b!.TypeId);var level=type.Levels.Single(l=>l.Level==b!.Level);var next=type.Levels.SingleOrDefault(l=>l.Level==b!.Level+1);
            var project=d.Projects.SingleOrDefault(p=>p.BuildingId==_building);var e=view.Enterprises.Single(x=>x.Id.Value==_building);bool preparing=d.Preparing.Contains(_building);
            _status.Text=$"{xp.Total} XP · "+L("общая технология владельца ","owner's shared technology ")+xp.TechnologyLevel+"\n"+L("Этот корпус: уровень ","This building: level ")+$"{b!.Level} · {b.Footprint.Length} "+L("клеток · мест ","cells · places ")+level.WorkshopSlots+$" · {level.InputCapacity}/{level.OutputCapacity}"+"\n"+
                (next is null?L("Максимальный корпус.","Maximum building size."):L("Следующая ступень: ","Next tier: ")+$"{next.Level} · {DevelopmentRules.Threshold(next.Level)} XP · {next.Footprint.Length} "+L("клеток · мест ","cells · places ")+next.WorkshopSlots+$" · {next.InputCapacity}/{next.OutputCapacity}\n"+PlayerText.Money(next.PriceMinor)+" + "+string.Join(", ",next.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity))+L(" · активных тиков "," · active ticks ")+next.ConstructionTicks+L(". Земля оплачивается отдельно. Материалы — со входа; новые места оснащаются отдельно.",". Land is paid separately. Materials come from input; new places need separate outfitting."))+"\n"+
                (project is null?preparing?L("Подготовка: завершаются начатые партии; новые не запускаются.","Preparing: current batches finish; new batches do not start."):"":L("Стройка: осталось ","Construction: remaining ")+(project.CompletesTick-view.Tick)+L(" активных тиков. Отмена — в разделе сноса с расчётом возврата."," active ticks. Cancel through the demolition preview and recovery calculation."));
            if(next is not null&&project is null)
            {
                Button("prepare",preparing?L("Отменить подготовку (цеха останутся выключены)","Cancel preparation (workshops stay off)"):L("Подготовить к расширению","Prepare expansion"),new(preparing?ActionKind.CancelExpansionPreparation:ActionKind.PrepareEnterpriseExpansion,Building:b.Id));
                var cells=next.Footprint.Select(c=>new Cell(b.Position.X+c.X,b.Position.Z+c.Z)).ToArray();
                var plots=cells.Select(c=>view.Plots.Single(p=>p.Position==c)).ToArray();
                foreach(var plot in plots.Where(p=>p.OwnerId is null))Button("land:"+plot.Id.Value,L("Купить землю ","Buy land ")+$"({plot.Position.X},{plot.Position.Z}) · "+PlayerText.Money(plot.PriceMinor),new(ActionKind.BuyExpansionLand,Building:b.Id,Plot:plot.Id),view.AvailableCashMinor>=plot.PriceMinor);
                var input=view.Inventories.Single(i=>i.Id==b.InputId);
                bool ready=xp.TechnologyLevel>=next.Level&&plots.All(p=>p.OwnerId==view.Participant.Id)&&view.AvailableCashMinor>=next.PriceMinor&&next.Materials.All(m=>input.Stock.Where(s=>s.ItemId==m.ItemId).Sum(s=>s.Quantity-s.Reserved)>=m.Quantity)&&e.Workshops.All(w=>!w.Enabled&&w.BatchId==0)&&e.Installations.Length==0;
                Button("expand",L("Оплатить расширение до ","Pay for expansion to ")+next.Level,new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:next.Level),ready);
            }
        }
        foreach(string key in _buttons.Keys.Where(k=>!_shown.Contains(k)).ToArray()){var button=_buttons[key].Button;_actions.RemoveChild(button);button.QueueFree();_buttons.Remove(key);}
    }
    private string State(string status)=>status switch{"Active"=>L("В работе","Active"),"Completed"=>L("Выполнено","Completed"),"Expired"=>L("Истекло","Expired"),"Abandoned"=>L("Отменено","Abandoned"),_=>status};
}

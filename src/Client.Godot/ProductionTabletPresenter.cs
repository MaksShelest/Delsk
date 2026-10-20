using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>The supplied artwork is a view over the same household actions, not a second economy.</summary>
public partial class ProductionTabletPresenter : Control
{
    private readonly bool _bakery;
    private readonly Control _scene;
    private readonly Control _panel;
    private readonly string _language;
    private readonly ScenarioDefinition _scenario;
    private readonly long _id;
    private readonly System.Action _details;
    private readonly Dictionary<string,Button> _actions=[];
    public ProductionTabletPresenter(ScenarioDefinition scenario,string language,long id,System.Action details,string type)
    {
        _bakery=type=="bakery";
        _scenario=scenario;_language=language;_id=id;_details=details;
        MouseFilter=MouseFilterEnum.Pass;
        _scene=GD.Load<PackedScene>(_bakery?"res://delsk_bakery_ui/BakeryTablet.tscn":"res://delsk_sawmill_ui/SawmillTablet.tscn").Instantiate<Control>();AddChild(_scene);
        _scene.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);_scene.Size=new(1280,924);
        _panel=_scene.GetNode<Control>("Panel");_panel.Position=Vector2.Zero;
        foreach(Node child in _scene.GetChildren())if(child!=_panel && child is CanvasItem item)item.Visible=false;
        // The outer TabletPanel supplies the shared help, close and global clock controls.
        foreach(string path in new[]{"Help","Close","TimeToggle"})Node<Control>(path).Visible=false;
        foreach(string path in new[]{"Delivery/DeliverySettings","Delivery/Source","Delivery/Destination"})Node<Button>(path).Pressed+=()=>_details();
        foreach(var pair in new[]{("Production/PauseProduction","work"),("Workers/WageMinus","wage-:"+id),("Workers/WagePlus","wage+:"+id),("Upgrade/UpgradeProduction","upgrade"),("Delivery/SupplyToggle","inbound"),("Delivery/SendToggle","output"),("Delivery/NoTransport/BuyTransport","truck")})Bind(pair.Item1,pair.Item2);
        Resized+=ResizeArtwork;
        Text("Title",PlayerText.Building(_language,type));Text("Production/Heading",L("Производство","Production"));Text("Workers/Heading",L("Работники","Workers"));
        Text("Workers/WageLabel",L("Зарплата","Wage"));Text("Workers/WageUnit",L("за тик","per tick"));Text("Stock/Heading",L("Склад","Stock"));
        Text("Delivery/Heading",L("Доставка","Logistics"));Text("Delivery/SupplyTitle",L("Подвоз сырья","Inbound supplies"));Text("Delivery/SendTitle",L("Отправка продукции","Outbound goods"));
        Text("Upgrade/Title",L("Улучшение производства","Production upgrade"));Text("Delivery/NoTransport/Title",L("Транспорт не приобретён","No transport purchased"));
        Text("Delivery/NoTransport/Hint",L("Купите грузовик и назначьте маршруты.\nПроизводство работает независимо.","Buy a truck and assign routes.\nProduction operates independently."));
        foreach(var label in Descendants(_panel).OfType<Label>()) {label.ClipText=true;label.TooltipText=label.Text;}
        Node<Label>("Workers/WageUnit").AddThemeFontSizeOverride("font_size",16);
        var note=Node<Label>("Production/RecipeNote");note.AddThemeFontSizeOverride("font_size",15);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;note.Size=new(440,42);
        foreach(var button in Descendants(_panel).OfType<Button>()){button.FocusMode=FocusModeEnum.None;button.ClipText=true;}
    }
    private static IEnumerable<Node> Descendants(Node n){foreach(Node child in n.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private T Node<T>(string path) where T:Node
    {if(_bakery && path.StartsWith("Stock/",StringComparison.Ordinal))path=path.Replace("Wood","Grain",StringComparison.Ordinal).Replace("Plank","Bread",StringComparison.Ordinal);return _panel.GetNode<T>(path);}
    private void Text(string path,string value){var label=Node<Label>(path);label.Text=value;label.TooltipText=value;}
    private void Bind(string path,string key)
    {
        var button=Node<Button>(path);button.Pressed+=()=>{if(_actions.TryGetValue(key,out var target) && !target.Disabled)target.EmitSignal(BaseButton.SignalName.Pressed);};
    }
    private void ButtonState(string path,string key,string? caption=null)
    {
        var button=Node<Button>(path);bool exists=_actions.TryGetValue(key,out var target);button.Disabled=!exists || target!.Disabled;
        button.TooltipText=exists?target!.TooltipText:L("Действие недоступно.","Action unavailable.");if(caption is not null)button.Text=caption;
    }
    private void ResizeArtwork()
    {
        float scale=Mathf.Min(1.2f,Mathf.Max(.1f,Size.X/1280f));_scene.Scale=Vector2.One*scale;CustomMinimumSize=new(0,924*scale);
    }
    public void ShowData(PlayerView view,bool paused,IReadOnlyDictionary<string,(Button Button,GameAction Action)> actions)
    {
        _actions.Clear();foreach(var (key,value) in actions)_actions[key]=value.Button;
        var e=view.Enterprises.Single(e=>e.Id.Value==_id);var b=view.Buildings.Single(b=>b.Id.Value==_id);var r=_scenario.Recipes.Single(r=>r.Id==e.RecipeId);
        var input=view.Inventories.Single(i=>i.Id==e.InputId);var output=view.Inventories.Single(i=>i.Id==e.OutputId);var pack=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);
        var g=view.Employment.Single(g=>g.Role==EmploymentRole.Production && g.Id.Value==_id);
        Text("Level",L("Уровень ","Level ")+b.Level);Text("StatusPill/Text",e.Enabled?L("Включено","Enabled"):L("Остановлено","Stopped"));Node<Label>("StatusPill/Text").TooltipText=TabletText.Reason(_language,e.ReasonCode);
        Text("Production/Input",r.Ingredients[0].Quantity.ToString());Text("Production/Output",r.OutputQuantity.ToString());
        Text("Production/InputName",PlayerText.Item(_language,r.Ingredients[0].ItemId));Text("Production/OutputName",PlayerText.Item(_language,r.OutputItem));
        Text("Production/ProgressLabel",L("Готовность партии","Batch progress"));var p=TabletProgress.Production(e);Node<ProgressBar>("Production/Progress").MaxValue=p.Total;Node<ProgressBar>("Production/Progress").Value=p.Done;
        Text("Production/ProgressValue",$"{p.Done}/{p.Total}");Text("Production/RecipeNote",L("Энергия за партию: ","Energy per batch: ")+PlayerText.Money(r.EnergyPerBatchMinor)+" · "+TabletText.Reason(_language,e.ReasonCode));
        ButtonState("Production/PauseProduction","work",e.Enabled?L("Остановить производство","Stop production"):L("Включить производство","Start production"));
        Text("Workers/Headcount",$"{g.Headcount} / {g.Capacity}");Text("Workers/Full",g.Headcount==g.Capacity?L("Все места заняты","Fully staffed"):L("Есть свободные места","Vacancies available"));
        Text("Workers/WageAmount",PlayerText.Money(g.WageMinor));Text("Workers/Hint",g.PendingWageMinor is long wage?L("Новая ставка ","Pending wage ")+PlayerText.Money(wage)+L(" с тика "," from tick ")+g.PayEffectiveTick:L("Наём происходит по правилам города.","Hiring follows city labor rules."));
        ButtonState("Workers/WageMinus","wage-:"+_id);ButtonState("Workers/WagePlus","wage+:"+_id);
        void Stock(string prefix,InventoryView stock,string item)
        {
            Text("Stock/"+prefix+"Title",PlayerText.Item(_language,item));var entry=stock.Stock.FirstOrDefault(s=>s.ItemId==item);
            Text("Stock/"+prefix+"Count",$"{(entry?.Quantity??0)-(entry?.Reserved??0)}"+L(" · резерв "," · reserved ")+(entry?.Reserved??0));
            Node<Label>("Stock/"+prefix+"Count").TooltipText=L("Свободно / зарезервировано. Объём склада: ","Free / reserved. Storage volume: ")+$"{stock.OccupiedVolume}+{stock.ReservedCapacity}/{stock.Capacity}";
            var bar=Node<ProgressBar>("Stock/"+prefix+"Bar");bar.MaxValue=stock.Capacity;bar.Value=stock.OccupiedVolume+stock.ReservedCapacity;bar.TooltipText=$"{stock.OccupiedVolume}+{stock.ReservedCapacity}/{stock.Capacity}";
        }
        Stock("Wood",input,r.Ingredients[0].ItemId);Stock("Plank",output,r.OutputItem);
        string put=$"put:{input.Id.Value}:{r.Ingredients[0].ItemId}:one",take=$"take:{output.Id.Value}:{r.OutputItem}:one";
        _depositKey=put;_withdrawKey=take;ButtonState("Stock/Deposit",put,L("Передать 1","Supply 1"));ButtonState("Stock/Withdraw",take,L("Забрать 1","Take 1"));
        var transfer=view.Transfers.FirstOrDefault(t=>t.Source==pack.Id.Value && t.Destination==input.Id.Value || t.Source==output.Id.Value && t.Destination==pack.Id.Value);
        if(transfer is not null)Text("Production/RecipeNote",L("Ручной перенос: ","Manual transfer: ")+transfer.Completed+"/"+transfer.Quantity+L(" · остановка в подробностях"," · stop in details"));
        var next=_scenario.BuildingTypes!.Single(t=>t.Id==b.TypeId).Levels.FirstOrDefault(l=>l.Level==b.Level+1);
        Text("Upgrade/CostMoney",next is null?"—":PlayerText.Money(next.PriceMinor));Text("Upgrade/CostPlanks",next is null?"—":string.Join(" + ",next.Materials.Select(m=>m.Quantity)));
        Node<Label>("Upgrade/CostPlanks").TooltipText=next is null?"":string.Join(" + ",next.Materials.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity));
        Text("Upgrade/Hint",next is null?L("Максимальный уровень","Maximum level"):L("Остановите работу; материалы — из местного склада.","Stop work; materials come from local stock."));ButtonState("Upgrade/UpgradeProduction","upgrade",L("Улучшить","Upgrade"));
        var l=view.Logistics.SingleOrDefault(l=>l.EnterpriseId==e.Id);Node<Control>("Delivery/NoTransport").Visible=l is null;
        Text("Delivery/NoTransport/Cost",PlayerText.Money((_scenario.Logistics??new()).PurchasePriceMinor));ButtonState("Delivery/NoTransport/BuyTransport","truck",L("Купить транспорт","Buy transport"));
        ButtonState("Delivery/SupplyToggle","inbound");ButtonState("Delivery/SendToggle","output");
        Node<CheckButton>("Delivery/SupplyToggle").SetPressedNoSignal(l?.InboundEnabled??false);Node<CheckButton>("Delivery/SendToggle").SetPressedNoSignal(l?.Enabled??false);
        Node<Button>("Delivery/SupplyToggle").Text=L("Вкл.","On");Node<Button>("Delivery/SendToggle").Text=L("Вкл.","On");
        var crew=l is null?null:view.Employment.Single(g=>g.Id==l.GroupId);
        Text("Delivery/CrewBrief",crew is null?"":L("Команда ","Crew ")+crew.Headcount+"/"+crew.Capacity+" · "+PlayerText.Money(crew.WageMinor));
        Text("Delivery/TripStatus",l is null?"":TabletText.Reason(_language,l.ReasonCode)+(l.Trip is {} trip?" · "+PlayerText.Item(_language,trip.ItemId)+" ×"+trip.Quantity:""));
        Node<Button>("Delivery/Source").Text=l is null?"—":L("Источники · ","Sources · ")+l.Queue.Count(j=>j.Enabled && (j.InboundSource.Value!=0 || j.Source.Value!=0));
        Node<Button>("Delivery/Destination").Text=l is null?"—":L("Получатели / договоры · ","Destinations / contracts · ")+l.Queue.Count(j=>j.Enabled && (j.OwnDestination.Value!=0 || j.ContractId!=0 || j.Destination.Value!=0));
        Text("PauseState",paused?L("Время остановлено · управление сверху","Time paused · controls above"):L("Время идёт · управление сверху","Time running · controls above"));ResizeArtwork();
    }
    private string _depositKey="",_withdrawKey="";
    public override void _Ready()
    {
        Node<Button>("Stock/Deposit").Pressed+=()=>{if(_actions.TryGetValue(_depositKey,out var b) && !b.Disabled)b.EmitSignal(BaseButton.SignalName.Pressed);};
        Node<Button>("Stock/Withdraw").Pressed+=()=>{if(_actions.TryGetValue(_withdrawKey,out var b) && !b.Disabled)b.EmitSignal(BaseButton.SignalName.Pressed);};ResizeArtwork();
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

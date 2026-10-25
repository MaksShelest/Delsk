using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Fresh Core quote, explicit preview, then a continuous two-second UI hold. Core owns all effects.</summary>
public partial class DemolitionPanel:VBoxContainer
{
    private readonly Func<GameAction,ActionAccess> _access;private readonly string _language;private readonly Action<GameAction> _submit;private readonly Func<long,bool,DemolitionQuote?> _quote;
    private readonly Label _summary=new(){Name="DemolitionSummary",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Button _prepare=new(){Name="PrepareDemolition"},_resume=new(){Name="CancelDemolitionPreparation"},_open=new(){Name="DemolitionPreview"},_hold=new(){Name="DemolitionHold",FocusMode=FocusModeEnum.None},_cancel=new(){Name="DemolitionCancel"};
    private Window? _window;
    private readonly VBoxContainer _preview=new(){Visible=false};private DemolitionQuote? _current;private long _target;private bool _construction,_holding;private double _held;private string _token="";
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private string Item(string id)=>id switch{"steel"=>L("сталь","steel"),"beam"=>L("балки","beams"),"concrete"=>L("бетон","concrete"),_=>PlayerText.Item(_language,id)};
    private string Materials(IEnumerable<MaterialCost> items){var list=items.Where(m=>m.Quantity>0).ToArray();return list.Length==0?L("нет","none"):string.Join(", ",list.Select(m=>Item(m.ItemId)+" ×"+m.Quantity));}
    public DemolitionPanel(string language,Action<GameAction> submit,Func<long,bool,DemolitionQuote?> quote,Func<GameAction,ActionAccess> access)
    {
        _access=access;_language=language;_submit=submit;_quote=quote;Name="DemolitionPanel";AddChild(new HSeparator());AddChild(_prepare);AddChild(_resume);AddChild(_open);AddChild(_preview);_preview.AddChild(_summary);_preview.AddChild(_hold);_preview.AddChild(_cancel);
        _prepare.Text=L("Подготовить к сносу","Prepare for demolition");_resume.Text=L("Отменить подготовку (без автозапуска)","Cancel preparation (no automatic restart)");_cancel.Text=L("Отмена — ничего не списывать","Cancel — spend nothing");
        foreach(var b in new[]{_prepare,_resume,_open,_hold,_cancel}){b.AutowrapMode=TextServer.AutowrapMode.WordSmart;b.CustomMinimumSize=new(0,44);}
        _prepare.Pressed+=()=>_submit(new(ActionKind.PrepareDemolition,Building:new(_target)));_resume.Pressed+=()=>_submit(new(ActionKind.CancelDemolitionPreparation,Building:new(_target)));
        _open.Pressed+=()=>{_preview.Visible=true;ResetHold();};_cancel.Pressed+=()=>{_preview.Visible=false;ResetHold();};
        _hold.ButtonDown+=()=>{if(_current is null||_hold.Disabled)return;_holding=true;_held=0;_token=_current.Token;};_hold.ButtonUp+=ResetHold;_hold.MouseExited+=ResetHold;VisibilityChanged+=()=>{if(!IsVisibleInTree())ResetHold();};
    }
    public override void _Ready(){_window=GetWindow();_window.FocusExited+=ResetHold;}
    public override void _ExitTree(){if(_window is not null)_window.FocusExited-=ResetHold;}
    public override void _Notification(int what){if(what==(int)NotificationApplicationPaused||what==(int)NotificationApplicationFocusOut)ResetHold();}
    private void ResetHold(){_holding=false;_held=0;_hold.Text=L("Удерживайте 2 секунды для подтверждения","Hold for 2 seconds to confirm");}
    public void ShowData(PlayerView view,bool pending,long target,bool construction)
    {
        if(_target!=target||_construction!=construction){_preview.Visible=false;ResetHold();}_target=target;_construction=construction;_current=_quote(target,construction);Visible=_current is not null;if(_current is null)return;
        bool preparing=view.Demolition?.Preparing.Contains(target)==true;_prepare.Visible=!construction&&!preparing;_resume.Visible=!construction&&preparing;_prepare.Disabled=pending;_resume.Disabled=pending;
        _open.Text=construction?L("Отменить начатую стройку…","Cancel started construction…"):L("Снос…","Demolish…");_open.Disabled=pending;
        _summary.Text=L("Стоимость сноса: ","Demolition cost: ")+PlayerText.Money(_current.PriceMinor)+L(" монет. Денежные вложения не возвращаются."," coins. Construction payments are not refunded.")+"\n"+
            L("Клетки/уровни: ","Cells/levels: ")+string.Join("; ",_current.Cells.Select(c=>$"({c.Position.X},{c.Position.Z}) L{c.Level}"))+"\n"+
            L("Возврат 60% фактических материалов: ","Recover 60% of actual materials: ")+Materials(_current.Recovery)+"\n"+
            L("Обычные запасы сохраняются полностью: ","Normal stocks preserved in full: ")+Materials(_current.Stock)+"\n"+
            L("Техника и земля сохраняются. Работники переходят в безработицу. Материалы остаются на клетках; новый излишек имеет срок 1200 активных тиков, существующий срок не продлевается.","Equipment and land are retained. Workers become unemployed. Materials stay on the cells; new excess has a 1200 active tick deadline; existing deadlines are unchanged.")+"\n"+
            string.Join("; ",_current.Blockers.Select(Reason));
        var sourceBuilding=construction?null:view.Buildings.FirstOrDefault(b=>b.Id.Value==target);var work=construction?view.Storage?.Construction.FirstOrDefault(c=>c.Id==target):null;
        var risk=view.Inventories.Where(i=>i.Id==sourceBuilding?.InputId||i.Id==sourceBuilding?.OutputId||i.Id.Value==work?.InventoryId).SelectMany(i=>i.Overflow??[]).ToArray();
        if(risk.Length>0)_summary.Text+="\n"+L("Существующий излишек: ","Existing overflow: ")+string.Join("; ",risk.Select(l=>PlayerText.Item(_language,l.ItemId)+" ×"+l.Quantity+" · "+(l.ExpiresTick-view.Tick)+L(" активных тиков"," active ticks")));
        _hold.Disabled=!_access(construction?new(ActionKind.CancelConstruction,OfferId:target):new(ActionKind.DemolishBuilding,Building:new(target))).Allowed||pending||view.Participant.Busy||_current.Blockers.Length>0||view.AvailableCashMinor<_current.PriceMinor;
        if(!_holding)ResetHold();
    }
    private string Reason(string key)=>key switch{"PrepareFirst"=>L("Сначала подготовьте объект","Prepare the object first"),"ProductionBatch"=>L("Дождитесь завершения партии","Wait for the current batch"),"DeliveryCommitment"=>L("Завершите входящие/назначенные договоры","Finish incoming/assigned contracts"),"CustomerPickup"=>L("Покупатель должен забрать свой товар","Customer must collect their goods"),"ActiveFreight"=>L("Дождитесь рейса и возврата груза","Wait for freight and cargo return"),"ReservedGoodsOrSpace"=>L("Освободите связанные резервы","Release related reservations"),"ManualTransfer"=>L("Завершите ручной перенос","Finish the manual transfer"),"ConstructionInProgress"=>L("Сначала завершите или отмените стройку","Finish or cancel construction first"),_=>key};
    public override void _Process(double delta)
    {
        if(!_holding)return;
        if(!_hold.IsVisibleInTree()||_hold.Disabled||_current?.Token!=_token){ResetHold();return;}
        _held+=delta;_hold.Text=L("Подтверждение: ","Confirming: ")+Math.Min(2,_held).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" / 2";
        if(_held>=2){string token=_token;ResetHold();_preview.Visible=false;_submit(_construction?new(ActionKind.CancelConstruction,OfferId:_target,DemolitionQuote:token):new(ActionKind.DemolishBuilding,Building:new(_target),DemolitionQuote:token));}
    }
}

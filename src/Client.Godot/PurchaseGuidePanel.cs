using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Shows the buyer's specific claim and route, without any foreign stock projection.</summary>
public partial class PurchaseGuidePanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly OptionButton _purchase=new();
    private readonly Label _details=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly RouteMiniMap _map;
    private readonly QuantityInput _quantity=new();
    private readonly Button _walk=new(),_collect=new(),_all=new(),_stop=new();
    private PlayerView? _view;
    private bool _pending;
    private string _key="";
    public PurchaseGuidePanel(ScenarioDefinition scenario,string language,System.Action<GameAction> submit)
    {
        _scenario=scenario;_language=language;_submit=submit;
        AddChild(new Label{Text=L("Активные покупки и путь получения","Active purchases and pickup route")});AddChild(_purchase);AddChild(_details);
        _map=new(scenario){CustomMinimumSize=new(520,210)};AddChild(_map);AddChild(_quantity);
        _walk.Text=L("Подойти к точке выдачи","Walk to pickup point");_collect.Text=L("Получить выбранное количество","Collect selected quantity");
        _all.Text=L("Получить всё по вместимости","Collect all that fits");_stop.Text=L("Остановить передачу","Stop transfer");
        foreach(var b in new[]{_walk,_collect,_all,_stop})AddChild(b);
        _walk.Pressed+=()=>{if(Guide() is {} g)_submit(PlayerDesk.Move(g.Position.X,g.Position.Z));};
        _collect.Pressed+=()=>Collect(_quantity.Value);_all.Pressed+=()=>Collect(long.MaxValue);_stop.Pressed+=()=>_submit(new(ActionKind.StopTransfer));
        _purchase.ItemSelected+=_=>Refresh();
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;string key=string.Join(',',view.PurchaseGuides.Select(g=>g.PurchaseId));
        if(key!=_key)
        {
            long old=_purchase.ItemCount==0?0:_purchase.GetItemMetadata(_purchase.Selected).AsInt64();_key=key;_purchase.Clear();
            foreach(var g in view.PurchaseGuides){_purchase.AddItem("#"+g.PurchaseId);_purchase.SetItemMetadata(_purchase.ItemCount-1,g.PurchaseId);if(g.PurchaseId==old)_purchase.Select(_purchase.ItemCount-1);}
        }
        Refresh();
    }
    private PurchaseGuide? Guide()=>_view is null||_purchase.ItemCount==0?null:_view.PurchaseGuides.SingleOrDefault(g=>g.PurchaseId==_purchase.GetItemMetadata(_purchase.Selected).AsInt64());
    private long Available()
    {
        if(Guide() is not {} g)return 0;var claim=_view!.Market!.Allocations.Single(a=>a.Id==g.PurchaseId);var pack=_view.Inventories.Single(i=>i.Id==_view.Participant.InventoryId);
        return System.Math.Min(claim.RemainingQuantity,System.Math.Max(0,pack.Capacity-pack.OccupiedVolume-pack.ReservedCapacity)/_scenario.Items.Single(i=>i.Id==claim.ItemId).UnitVolume);
    }
    private void Collect(long quantity){if(Guide() is {} g && Available()>0)_submit(PlayerDesk.CollectPurchase(g.PurchaseId,System.Math.Min(quantity,Available())));}
    private void Refresh()
    {
        if(_view is null)return;var g=Guide();
        if(g is null)_details.Text=L("Активных покупок нет. Уже полученный груз остаётся вашим после срока.","No active purchases. Collected goods remain yours after the deadline.");
        else
        {
            var a=_view.Market!.Allocations.Single(a=>a.Id==g.PurchaseId);
            _details.Text=PlayerText.Item(_language,a.ItemId)+$" · {a.CollectedQuantity}/{a.Quantity} · ({g.Position.X},{g.Position.Z}) · "+
                L("осталось тиков ","ticks remaining ")+g.RemainingTicks+" · "+PlayerText.Reason(_language,g.ReasonCode)+"\n"+
                L("Выдача касается только этого товара и количества. Остальной чужой запас недоступен.","Pickup grants only this item and quantity. Other foreign stock stays inaccessible.");
        }
        _map.Blocked=_view.Blocked.ToHashSet();_map.Current=_view.Participant.Position;_map.Path=g?.Path.ToArray()??[];_map.Stops=g is null?[]:[g.Position];_map.Allowed=g is null?[]:[g.Position];_map.QueueRedraw();
        _quantity.Limit(Available());bool busy=_view.Participant.Busy;
        _walk.Disabled=_pending||g is null||g.ReasonCode!="Ready"||busy;
        _collect.Disabled=_all.Disabled=_pending||g is null||g.ReasonCode!="Ready"||busy||_view.Participant.Position!=g.Position||Available()==0;
        _stop.Visible=_view.Transfers.Length>0;_stop.Disabled=_pending;
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}

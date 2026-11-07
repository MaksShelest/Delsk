using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>One explicit item, one ordinary transfer, capped by physical stock and destination room.</summary>
public partial class QuickStockTransfer : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;private readonly string _language,_caption;
    private readonly Action<GameAction> _submit;private readonly Func<GameAction,ActionAccess> _access;
    private readonly Label _stock=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly OptionButton _item=new(){FitToLongestItem=false,CustomMinimumSize=new(150,40),SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly Button _all=new(){CustomMinimumSize=new(40,40),SizeFlagsHorizontal=SizeFlags.ShrinkBegin};
    private readonly Label _hint=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private bool _friendly,_deposit;private string? _emptyHint;
    public void UseProductionCard(bool deposit,string? emptyHint=null){_friendly=true;_deposit=deposit;_emptyHint=emptyHint;_stock.Visible=false;_item.CustomMinimumSize=new(100,40);}
    private PlayerView? _view;private InventoryId _source,_destination;private bool _pending;private string[] _items=[];
    public QuickStockTransfer(ScenarioDefinition scenario,string language,string name,string caption,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        _scenario=scenario;_language=language;Name=name;SizeFlagsHorizontal=SizeFlags.ExpandFill;_caption=caption;_submit=submit;_access=access;
        _item.AddThemeIconOverride("arrow",CompactTabletDesign.Theme.GetIcon("arrow","OptionButton"));_all.SizeFlagsVertical=SizeFlags.ShrinkCenter;
        _all.Name=name+"All";_item.Name=name+"Item";AddChild(_stock);var flow=new HBoxContainer();flow.AddChild(_item);flow.AddChild(_all);AddChild(flow);AddChild(_hint);_hint.AddThemeFontSizeOverride("font_size",14);
        _item.ItemSelected+=_=>Render();_all.Pressed+=Send;
    }
    public void ShowData(PlayerView view,InventoryId source,InventoryId destination,IEnumerable<string> items,bool pending,string label)
    {
        _view=view;_source=source;_destination=destination;_pending=pending;
        var ids=items.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(!_items.SequenceEqual(ids))
        {
            string selected=_item.Selected>=0&&_item.Selected<_items.Length?_items[_item.Selected]:"";_items=ids;_item.Clear();
            foreach(string id in ids)_item.AddItem(PlayerText.Item(_language,id));int index=Array.IndexOf(ids,selected);
            if(index<0)index=Array.FindIndex(ids,id=>view.Inventories.Single(i=>i.Id==source).Stock.Any(s=>s.ItemId==id&&s.Quantity>s.Reserved));
            if(ids.Length>0)_item.Select(Math.Max(0,index));
        }
        PreferStocked();
        var inventory=view.Inventories.Single(i=>i.Id==(label=="input"||label=="storage"?destination:source));
        _stock.Text=PlayerText.Local(_language,label=="input"?"Вход":label=="storage"?"Хранилище":"Выход",label=="input"?"Input":label=="storage"?"Storage":"Output")+$" · {inventory.OccupiedVolume}+{inventory.ReservedCapacity}/{inventory.Capacity}";
        Render();
    }
    // Выбранный пустой ресурс не должен скрывать запас, который уже можно забрать.
    private void PreferStocked()
    {
        if(_view is null||_items.Length==0||_item.Selected<0||_item.Selected>=_items.Length)return;
        var inventory=_view.Inventories.SingleOrDefault(i=>i.Id==_source);if(inventory is null)return;
        bool free(string id)=>inventory.Stock.Any(s=>s.ItemId==id&&s.Quantity>s.Reserved);
        if(free(_items[_item.Selected]))return;
        int better=Array.FindIndex(_items,free);if(better>=0)_item.Select(better);
    }
    private ManualTransferChoice? Choice()=>_view is null||_item.Selected<0||_item.Selected>=_items.Length?null:
        ManualTransferChoice.Read(_scenario,_view,_source,_destination,_items[_item.Selected],_access,_pending);
    private void Render()
    {
        _item.Visible=!_friendly||_items.Length>1;
        var choice=Choice();_all.Text=_caption+" ("+(choice?.Maximum??0)+")";_all.Disabled=choice is null||!choice.Value.Enabled;
        _all.TooltipText=choice?.Explain(_language)??PlayerText.Local(_language,"Нет подходящего товара.","No item available.");
        _hint.Text=_all.Disabled?choice is {Reason:"NoFreeStock"}?PlayerText.Local(_language,"Нет свободного товара.","No available stock."):_all.TooltipText:"";if(_friendly&&choice is {Reason:"NoFreeStock"})_hint.Text=PlayerText.Local(_language,_deposit?"В рюкзаке пока нет сырья.":"Готовой продукции пока нет.",_deposit?"No materials in your backpack yet.":"No finished goods yet.");
        if(_friendly&&_items.Length>1&&choice is {Reason:"NoFreeStock"})_hint.Text=PlayerText.Local(_language,"Нет свободного запаса выбранного ресурса. Выберите другой товар или пополните рюкзак.","No free stock of this material. Select another item or refill your backpack.");
        if(_friendly&&_emptyHint is not null&&choice is {Reason:"NoFreeStock"})_hint.Text=_emptyHint;
        _hint.Visible=_hint.Text.Length>0;_item.Disabled=_pending;
    }
    private void Send()
    {
        var choice=Choice();if(choice is not ManualTransferChoice {Enabled:true} current)return;
        _pending=true;Render();_submit(current.Action);
    }
}

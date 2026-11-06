using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>One shared manual-transfer form for backpack and cargo. Every amount comes from ManualTransferChoice.</summary>
public partial class InventoryTransferPanel : VBoxContainer
{
    private readonly ScenarioDefinition _scenario;private readonly string _language;
    private readonly Action<GameAction> _submit;private readonly Func<GameAction,ActionAccess> _access;
    private readonly OptionButton _source=new(),_destination=new(),_item=new();
    private readonly SpinBox _quantity=new(){MinValue=1,MaxValue=1000000,Value=1,Step=1,Rounded=true,UpdateOnTextChanged=true,CustomMinimumSize=new(120,40)};
    private readonly Button _one=new(),_selected=new(),_all=new(),_go=new(),_stop=new();
    private readonly ProgressBar _progress=new(){ShowPercentage=true,CustomMinimumSize=new(0,10),Visible=false};
    private readonly Label _summary=GameUi.Text(),_why=GameUi.Hint("");
    private string _inventoryKey="",_itemKey="";private PlayerView? _view;private bool _pending;private long _preferred;
    public InventoryTransferPanel(ScenarioDefinition scenario,string language,string prefix,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        _scenario=scenario;_language=language;_submit=submit;_access=access;
        _source.Name=prefix+"Source";_destination.Name=prefix+"Destination";_item.Name=prefix+"Item";_quantity.Name=prefix+"Quantity";
        _one.Name=prefix+"One";_selected.Name=prefix;_all.Name=prefix+"All";_go.Name=prefix+"Go";
        var fields=new TabletColumns{Breakpoint=680};AddChild(fields);
        void Field(string caption,Control control){var box=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};box.AddChild(GameUi.Hint(caption));box.AddChild(control);fields.AddChild(box);}
        Field(L("Откуда","From"),_source);Field(L("Товар","Goods"),_item);Field(L("Куда","To"),_destination);
        AddChild(_summary);var actions=TabletDesign.Flow();actions.AddChild(_quantity);
        foreach(var b in new[]{_one,_selected,_all}){b.CustomMinimumSize=new(40,40);b.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;actions.AddChild(b);}AddChild(actions);AddChild(_why);AddChild(_progress);_stop.Name=prefix+"Stop";_stop.Text=L("Остановить перенос","Stop transfer");AddChild(_stop);
        var links=TabletDesign.Flow();links.AddChild(GameUi.Button(prefix+"Swap","⇄",Swap));_go.Text=L("К получателю","Go to recipient");links.AddChild(_go);AddChild(links);
        foreach(var select in new[]{_source,_destination,_item})select.ItemSelected+=_=>Render();_quantity.ValueChanged+=_=>Render();
        _one.Pressed+=()=>Send(1);_selected.Pressed+=()=>Send((long)_quantity.Value);_all.Pressed+=()=>Send(null);
        _stop.Pressed+=()=>{if(_pending||_stop.Disabled)return;_pending=true;Render();_submit(new(ActionKind.StopTransfer));};
        _go.Pressed+=()=>{if(_pending||_go.Disabled||_view is null)return;var inv=_view.Inventories.FirstOrDefault(i=>i.Id.Value==Id(_destination));if(inv is null)return;_pending=true;Render();_submit(PlayerDesk.Move(inv.Position.X,inv.Position.Z));};
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private static long Id(OptionButton option)=>option.Selected>=0&&option.ItemCount>0?option.GetItemMetadata(option.Selected).AsInt64():0;
    private bool Fill(OptionButton option,PlayerView view,long initial)
    {
        if(option.GetPopup().Visible)return false;
        long previous=Id(option);option.Clear();foreach(var inv in view.Inventories.OrderBy(i=>i.Id==view.Participant.InventoryId?0:1).ThenBy(i=>i.Id.Value))
        {option.AddItem(GameUi.InventoryName(view,inv.Id.Value,_language));option.SetItemMetadata(option.ItemCount-1,inv.Id.Value);if(inv.Id.Value==(previous==0?initial:previous))option.Select(option.ItemCount-1);}
        return true;
    }
    public void FocusInventory(long id){_preferred=id;if(_view is not null){Select(_source,id);Select(_destination,_view.Participant.InventoryId.Value);Render();}}
    public void FocusRoute(long source,long destination)
    {Select(_source,source);Select(_destination,destination);Render();}
    private static void Select(OptionButton option,long id){for(int i=0;i<option.ItemCount;i++)if(option.GetItemMetadata(i).AsInt64()==id)option.Select(i);}
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;string key=string.Join(',',view.Inventories.Select(i=>i.Id.Value));
        if(key!=_inventoryKey)
        {
            long other=view.Inventories.FirstOrDefault(i=>i.Id!=view.Participant.InventoryId&&i.Position==view.Participant.Position)?.Id.Value??view.Inventories.FirstOrDefault(i=>i.Id!=view.Participant.InventoryId)?.Id.Value??view.Participant.InventoryId.Value;
            bool a=Fill(_source,view,_preferred>0?_preferred:view.Participant.InventoryId.Value),b=Fill(_destination,view,_preferred>0?view.Participant.InventoryId.Value:other);if(a&&b)_inventoryKey=key;
        }
        Render();
    }
    private ManualTransferChoice? Choice(long? quantity)
    {
        if(_view is null||_source.Selected<0||_destination.Selected<0||_item.Selected<0)return null;
        long source=Id(_source),destination=Id(_destination);if(!_view.Inventories.Any(i=>i.Id.Value==source)||!_view.Inventories.Any(i=>i.Id.Value==destination))return null;
        return ManualTransferChoice.Read(_scenario,_view,new(source),new(destination),_item.GetItemMetadata(_item.Selected).AsString(),_access,_pending,quantity);
    }
    private void Render()
    {
        if(_view is null)return;
        var source=_view.Inventories.FirstOrDefault(i=>i.Id.Value==Id(_source));
        var items=(source?.Stock.Select(s=>s.ItemId)??[]).Distinct().Order(StringComparer.Ordinal).ToArray();string key=string.Join(',',items);
        if(key!=_itemKey&&!_item.GetPopup().Visible)
        {string old=_item.Selected>=0?_item.GetItemMetadata(_item.Selected).AsString():"";_itemKey=key;_item.Clear();foreach(string id in items){_item.AddItem(PlayerText.Item(_language,id));_item.SetItemMetadata(_item.ItemCount-1,id);if(id==old)_item.Select(_item.ItemCount-1);}}
        var all=Choice(null);var one=Choice(1);var selected=Choice((long)_quantity.Value);
        string verb=Id(_source)==_view.Participant.InventoryId.Value?L("Положить","Store"):Id(_destination)==_view.Participant.InventoryId.Value?L("Забрать","Take"):L("Перенести","Transfer");
        _one.Text=verb+" 1";_selected.Text=verb+" "+(long)_quantity.Value;_all.Text=verb+L(" всё ("," all (")+(all?.Maximum??0)+")";
        _one.Disabled=one?.Enabled!=true;_selected.Disabled=selected?.Enabled!=true;_all.Disabled=all?.Enabled!=true;
        _summary.Text=source is null?L("Выберите источник и получателя.","Choose a source and recipient."):
            GameUi.InventoryName(_view,Id(_source),_language)+" → "+(_item.Selected<0?L("нет товара","no goods"):PlayerText.Item(_language,_item.GetItemMetadata(_item.Selected).AsString()))+" → "+GameUi.InventoryName(_view,Id(_destination),_language);
        _why.Text=_item.Selected<0?L("В выбранном источнике пока нет товаров.","No goods in the selected source yet."):_all.Disabled?all?.Explain(_language)??L("Выберите разные точки переноса.","Choose different transfer endpoints."):_selected.Disabled?selected?.Explain(_language)??"":L("Можно перенести до ","You can move up to ")+all!.Value.Maximum+L(" ед. с учётом места и резервов."," units, allowing for capacity and holds.");
        _one.TooltipText=one?.Explain(_language)??_why.Text;_selected.TooltipText=selected?.Explain(_language)??_why.Text;_all.TooltipText=all?.Explain(_language)??_why.Text;
        var destination=_view.Inventories.FirstOrDefault(i=>i.Id.Value==Id(_destination));_go.Disabled=_pending||_view.Participant.Busy||destination is null||destination.Position==_view.Participant.Position;
        var transfer=_view.Transfers.FirstOrDefault();_stop.Visible=_progress.Visible=transfer is not null;_stop.Disabled=_pending||transfer is null;
        if(transfer is not null){_progress.MaxValue=Math.Max(1,transfer.Quantity);_progress.Value=transfer.Completed;}
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
    private void Swap(){long source=Id(_source),destination=Id(_destination);Select(_source,destination);Select(_destination,source);Render();}
    private void Send(long? quantity)
    {var choice=Choice(quantity);if(choice is not ManualTransferChoice {Enabled:true} selected)return;_pending=true;Render();_submit(selected.Action);}
}

public partial class BackpackPanel : VBoxContainer
{
    private readonly UiResourceList _stock=new();private readonly Label _capacity=GameUi.Text();private readonly ProgressBar _fill=new(){ShowPercentage=false,CustomMinimumSize=new(0,10)};
    private readonly InventoryTransferPanel _transfer;private readonly string _language;
    public BackpackPanel(ScenarioDefinition scenario,string language,Action<GameAction> submit,Func<GameAction,ActionAccess> access)
    {
        Name="BackpackPanel";_language=language;AddChild(GameUi.Intro(GameUi.L(language,"Рюкзак","Backpack"),GameUi.L(language,"Здесь ваши личные ресурсы. Переносите их в собственный склад, кузов или предприятие рядом с вами.","Your personal resources. Move them to your storage, cargo or nearby enterprise."),"bag",64));
        var box=new VBoxContainer();box.AddChild(_capacity);box.AddChild(_fill);box.AddChild(_stock);AddChild(GameUi.Card(box));
        _transfer=new(scenario,language,"BackpackTransfer",submit,access);AddChild(GameUi.Card(_transfer));
    }
    public void FocusRoute(long source,long destination)=>_transfer.FocusRoute(source,destination);
    public void ShowData(PlayerView view,bool pending)
    {
        var pack=view.Inventories.Single(i=>i.Id==view.Participant.InventoryId);_capacity.Text=GameUi.L(_language,"Занято ","Used ")+pack.OccupiedVolume+" / "+pack.Capacity+(pack.ReservedCapacity>0?GameUi.L(_language," · место под ожидаемый груз "," · held for incoming cargo ")+pack.ReservedCapacity:"");
        _fill.MaxValue=Math.Max(1,pack.Capacity);_fill.Value=pack.OccupiedVolume+pack.ReservedCapacity;_stock.ShowStock(pack,_language);_transfer.ShowData(view,pending);
    }
}

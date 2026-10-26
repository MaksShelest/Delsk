using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class ImportPanel:VBoxContainer
{
    private readonly string _language;
    private readonly System.Action<GameAction> _submit;
    private readonly Dictionary<string,(Label Label,SpinBox Quantity,Button Buy,Button Take)> _rows=[];
    private readonly Label _status=new(){Name="ImportStatus",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private PlayerView? _view;
    public ImportPanel(string language,System.Action<GameAction> submit)
    {
        Name="ImportPanel";_language=language;_submit=submit;AddChild(TabletDesign.Heading(L("Ограниченный импорт","Limited import")));AddChild(_status);
    }
    private string L(string ru,string en)=>_language=="ru"?ru:en;
    public void ShowView(PlayerView view)
    {
        _view=view;if(view.ProductionContent is not {} content){_status.Text=L("Импорт не предусмотрен этим сохранением.","This saved profile has no import supply.");return;}
        bool at=view.Participant.Position==content.Terminal;
        _status.Text=L("Покупка и выдача у терминала. Товар после оплаты остаётся в вашем пункте выдачи. Заберите его в рюкзак и перевезите обычным способом. Время включается прежней кнопкой планшета. Стартовый асфальт не пополняется после первого комбината стройматериалов IV.","Buy and collect at the terminal. Paid goods stay in your pickup section. Collect into your backpack and transport normally. Use the tablet's existing time control. Bootstrap asphalt stops replenishing after the first tier IV materials plant.");
        foreach(var stock in content.Imports)
        {
            string item=stock.ItemId;var spec=content.Rules.Imports.Single(i=>i.ItemId==item);
            if(!_rows.TryGetValue(item,out var row))
            {
                var box=new VBoxContainer();var buttons=new HFlowContainer();
                row=(new Label{Name="ImportStock_"+item,AutowrapMode=TextServer.AutowrapMode.WordSmart},new SpinBox{Name="ImportQuantity_"+item,MinValue=1,MaxValue=1000000,Value=1,Step=1,UpdateOnTextChanged=true},new Button{Name="ImportBuy_"+item,Text=L("Купить","Buy")},new Button{Name="ImportTake_"+item,Text=L("Забрать","Collect")});
                box.AddChild(row.Label);box.AddChild(buttons);buttons.AddChild(row.Quantity);buttons.AddChild(row.Buy);buttons.AddChild(row.Take);AddChild(TabletDesign.Wrap(box));_rows.Add(item,row);
                var quantity=row.Quantity;
                row.Buy.Pressed+=()=>_submit(new(ActionKind.BuyImport,ItemId:item,Quantity:(long)quantity.Value));
                row.Take.Pressed+=()=>{if(_view?.ProductionContent?.Pickup is {} source)_submit(new(ActionKind.Transfer,Source:source,Destination:_view.Participant.InventoryId,ItemId:item,Quantity:(long)quantity.Value));};
            }
            long owned=content.Pickup is {} pickup?view.Inventories.Single(i=>i.Id==pickup).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved):0;
            row.Label.Text=PlayerText.Item(_language,item)+" · "+PlayerText.Money(spec.UnitPriceMinor)+L("/шт · осталось ","/unit · remaining ")+stock.Remaining+L(" · проверка поставок через "," · supply review in ")+Math.Max(0,stock.NextRefreshTick-view.Tick)+L(" тиков · оплачено к вывозу "," ticks · paid for pickup ")+owned;
            row.Buy.Disabled=!at||view.Participant.Busy||stock.Remaining==0;row.Take.Disabled=!at||view.Participant.Busy||owned==0;
        }
    }
}

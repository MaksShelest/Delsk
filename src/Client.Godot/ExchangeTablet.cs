using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Five visual pages. Existing capital/market presenters keep command authority.</summary>
public partial class ExchangeTablet : VBoxContainer
{
    public CapitalTablet Capital{get;}
    public ParticipantMarketTablet Market{get;}
    private readonly ImportPanel _imports;
    public int SelectedPage{get;private set;}
    private readonly Label _cash=new();
    private readonly List<Button> _tabs=[];
    private readonly string _language;
    public ExchangeTablet(ScenarioDefinition scenario,string language,CapitalTablePreferences capital,CapitalTablePreferences market,long shop,System.Action<GameAction> submit,bool atMarket,System.Func<GameAction,ActionAccess>? access=null)
    {
        Name="ExchangePages";_language=language;Theme=CompactTabletDesign.Theme;
        _cash.AddThemeFontSizeOverride("font_size",20);AddChild(_cash);var nav=TabletDesign.Flow();AddChild(nav);
        foreach(var pair in new[]{("Заказы столицы","Capital orders"),("Рынок","Market"),("Получить покупки","Collect purchases"),("Мои предложения","My offers"),("История","History"),("Импорт","Import")})
        {
            int page=_tabs.Count;var b=new Button{Name="ExchangePage"+page,Text=PlayerText.Local(language,pair.Item1,pair.Item2),ToggleMode=true,FocusMode=FocusModeEnum.None,SizeFlagsHorizontal=SizeFlags.ExpandFill};b.Pressed+=()=>SelectPage(page);_tabs.Add(b);nav.AddChild(b);
        }
        Capital=new(scenario,language,capital,submit,access);Market=new(scenario,language,market,shop,submit,access);_imports=new(language,submit,scenario,access);AddChild(Capital);AddChild(Market);AddChild(_imports);SelectPage(atMarket?1:0);Resized+=()=>{if(Size.X>0)GameUi.FitControls(this,Size.X);};
    }
    public void SelectPage(int page)
    {
        SelectedPage=Math.Clamp(page,0,5);for(int i=0;i<_tabs.Count;i++)_tabs[i].SetPressedNoSignal(i==SelectedPage);
        Capital.Visible=SelectedPage is 0 or 4;Market.Visible=SelectedPage is >0 and <5;_imports.Visible=SelectedPage==5;
        Capital.ShowPage(SelectedPage==4);Market.ShowPage(SelectedPage switch{1=>0,2=>1,3=>2,_=>3});
    }
    public void ShowHeader(PlayerView view,bool pending=false){_imports.ShowView(view,pending);_cash.Text=PlayerText.Local(_language,"Доступно: ","Available: ")+PlayerText.Money(view.AvailableCashMinor)+PlayerText.Local(_language," · баланс "," · balance ")+PlayerText.Money(view.CashMinor);if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);}
}

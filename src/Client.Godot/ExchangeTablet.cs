using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Five visual pages. Existing capital/market presenters keep command authority.</summary>
public partial class ExchangeTablet : VBoxContainer
{
    public CapitalTablet Capital{get;}
    public ParticipantMarketTablet Market{get;}
    public int SelectedPage{get;private set;}
    private readonly Label _cash=new();
    private readonly List<Button> _tabs=[];
    private readonly string _language;
    public ExchangeTablet(ScenarioDefinition scenario,string language,CapitalTablePreferences capital,CapitalTablePreferences market,long shop,System.Action<GameAction> submit,bool atMarket,System.Func<GameAction,ActionAccess>? access=null)
    {
        Name="ExchangePages";_language=language;
        _cash.HorizontalAlignment=HorizontalAlignment.Right;AddChild(_cash);var nav=TabletDesign.Flow();AddChild(nav);
        foreach(var pair in new[]{("Столица","Capital"),("Лоты участников","Participant lots"),("Мои покупки","My purchases"),("Мои предложения","My offers"),("История","History")})
        {
            int page=_tabs.Count;var b=new Button{Name="ExchangePage"+page,Text=PlayerText.Local(language,pair.Item1,pair.Item2),ToggleMode=true,FocusMode=FocusModeEnum.None,SizeFlagsHorizontal=SizeFlags.ExpandFill};b.Pressed+=()=>SelectPage(page);_tabs.Add(b);nav.AddChild(b);
        }
        Capital=new(scenario,language,capital,submit);Market=new(scenario,language,market,shop,submit,access);AddChild(Capital);AddChild(Market);SelectPage(atMarket?1:0);
    }
    public void SelectPage(int page)
    {
        SelectedPage=Math.Clamp(page,0,4);for(int i=0;i<_tabs.Count;i++)_tabs[i].SetPressedNoSignal(i==SelectedPage);
        Capital.Visible=SelectedPage is 0 or 4;Market.Visible=SelectedPage!=0;
        Capital.ShowPage(SelectedPage==4);Market.ShowPage(SelectedPage switch{1=>0,2=>1,3=>2,_=>3});
    }
    public void ShowHeader(PlayerView view)=>_cash.Text=PlayerText.Local(_language,"Деньги: ","Cash: ")+PlayerText.Money(view.CashMinor)+PlayerText.Local(_language," · доступно: "," · available: ")+PlayerText.Money(view.AvailableCashMinor);
}

using System.Text.Json.Serialization;
namespace Simulation.Core;

public sealed record DemolitionRules(long RecoveryBasisPoints=6000,long CellPriceMinor=3000,long ExtraLevelPriceMinor=1500)
{
    public void Validate(){if(RecoveryBasisPoints is <0 or >10000||CellPriceMinor<0||ExtraLevelPriceMinor<0)throw new ArgumentException("Invalid demolition rules");}
}
public sealed record DemolitionCell(Cell Position,int Level);
public sealed record DemolitionQuote(long Target,bool Construction,long Owner,long PriceMinor,DemolitionCell[] Cells,MaterialCost[] Invested,MaterialCost[] Recovery,MaterialCost[] Stock,string[] Blockers,string Token);
public sealed record DemolitionReceipt(long Target,bool Construction,long Owner,long Tick,long PriceMinor,DemolitionCell[] Cells,MaterialCost[] Invested,MaterialCost[] Recovery,long[] GroundInventories);
public sealed record DemolitionState(long[] Preparing,DemolitionReceipt[] Receipts);

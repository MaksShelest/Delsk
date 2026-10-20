using Simulation.Core;

namespace Simulation.Hosting;

public record VisibleStock(string ItemId,long Volume);
public record WarehouseVisual(bool Known,long Capacity,VisibleStock[] Goods);
public record StockPile(string ItemId,int Slot,int Layers);

/// <summary>Bounded cosmetic approximation of physical volume. Holds for future receipts are not goods.</summary>
public static class WarehouseVisuals
{
    public const int MaximumPiles=24;
    public static StockPile[] Layout(WarehouseVisual view)
    {
        if(!view.Known || view.Capacity<=0)return [];
        var goods=view.Goods.Where(g=>g.Volume>0).OrderBy(g=>g.ItemId,StringComparer.Ordinal).ToArray();
        if(goods.Length==0)return [];
        // Current catalog fits24. For a larger catalog an explicit mixed marker avoids mislabelling stock.
        if(goods.Length>MaximumPiles)goods=[..goods.OrderByDescending(g=>g.Volume).Take(MaximumPiles-1),new("other",goods.OrderByDescending(g=>g.Volume).Skip(MaximumPiles-1).Sum(g=>g.Volume))];
        long total=goods.Sum(g=>g.Volume);
        int count=Math.Clamp((int)Int128.Min(MaximumPiles,((Int128)total*MaximumPiles+view.Capacity-1)/view.Capacity),goods.Length,MaximumPiles);
        var allocations=Enumerable.Repeat(1,goods.Length).ToArray();
        for(int n=goods.Length;n<count;n++)
        {
            int best=Enumerable.Range(0,goods.Length).OrderByDescending(i=>(decimal)goods[i].Volume/allocations[i]).ThenBy(i=>goods[i].ItemId,StringComparer.Ordinal).First();allocations[best]++;
        }
        var result=new List<StockPile>();
        for(int i=0;i<goods.Length;i++)
        {
            int layers=(int)Int128.Clamp(((Int128)goods[i].Volume*MaximumPiles*4+(Int128)view.Capacity*allocations[i]-1)/((Int128)view.Capacity*allocations[i]),1,4);
            for(int n=0;n<allocations[i];n++)result.Add(new(goods[i].ItemId,result.Count,layers));
        }
        return result.ToArray();
    }
}

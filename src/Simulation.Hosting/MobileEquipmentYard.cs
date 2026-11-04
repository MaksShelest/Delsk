using Simulation.Core;

namespace Simulation.Hosting;

internal static class MobileEquipmentYard
{
    // A new city annex, resolved once. No historical layout or loaded game is regenerated.
    internal static ScenarioDefinition Prepare(ScenarioDefinition s)
    {
        if (s.Economy?.Fleet is not { EquipmentDealerVersion:1 } || s.Layout is not {} layout) return s;
        var dealer=layout.CivicObjects.Single(c=>c.Kind=="dealership");
        if(dealer.EquipmentParking is not null)return s;
        var forbidden=layout.IndustrialLots.SelectMany(l=>l.Cells).Concat(layout.ResourceDistricts.SelectMany(r=>r.Cells))
            .Concat(layout.CivicObjects.SelectMany(c=>c.Footprint)).Concat(layout.MarketStalls.Select(m=>m.Cell)).ToHashSet();
        var navigation=new ScenarioNavigation(s);var queue=new Queue<Cell>();var previous=new Dictionary<Cell,Cell?>();
        foreach(var road in layout.InitialCityDirtCells.OrderBy(c=>Math.Abs(c.X-dealer.Entry.X)+Math.Abs(c.Z-dealer.Entry.Z)).ThenBy(c=>c.Z).ThenBy(c=>c.X))
        {queue.Enqueue(road);previous.Add(road,null);}
        Cell? exit=null;
        while(queue.TryDequeue(out var cell))
        {
            if(cell.Z==s.MapHeight-1&&cell.X>=2&&cell.X<s.MapWidth-2){exit=cell;break;}
            foreach(var next in Neighbors(cell))
                if(navigation.Walkable(next)&&!forbidden.Contains(next)&&previous.TryAdd(next,cell))queue.Enqueue(next);
        }
        if(exit is not {} gate)throw new ArgumentException("City equipment yard has no public approach");
        var roads=layout.InitialCityDirtCells.ToHashSet();var additions=new HashSet<Cell>();
        for(Cell? cell=gate;cell is {} at;cell=previous[at])additions.Add(at);
        int left=Math.Clamp(dealer.Entry.X-8,0,s.MapWidth-19),spine=left+17,top=s.MapHeight;
        // Eight separated bays in each row. Every machine faces its own north-side street.
        // A one-cell outer margin and empty space between bays preserve the shared wide-body swept footprint.
        var parking=Enumerable.Range(0,16).Select(n=>new Cell(left+1+2*(n%8),top+2+4*(n/8))).ToArray();
        for(int x=Math.Min(gate.X,spine);x<=Math.Max(gate.X,spine);x++)additions.Add(new(x,top));
        for(int z=top;z<=top+5;z++)additions.Add(new(spine,z));
        foreach(int z in new[]{top+1,top+5})for(int x=left+1;x<=spine;x++)additions.Add(new(x,z));
        roads.UnionWith(additions);
        Cell[] Sorted(IEnumerable<Cell> cells)=>cells.Distinct().OrderBy(c=>c.Z).ThenBy(c=>c.X).ToArray();
        return s with {MapHeight=checked(top+8),Layout=layout with {
            InitialCityDirtCells=Sorted(roads),RoadCenterCells=Sorted(layout.RoadCenterCells.Concat(additions)),
            RoadCorridorCells=Sorted(layout.RoadCorridorCells.Concat(additions)),
            CivicObjects=layout.CivicObjects.Select(c=>c==dealer?c with {EquipmentParking=parking}:c).ToArray()}};
    }
    private static IEnumerable<Cell> Neighbors(Cell c)=>[new(c.X-1,c.Z),new(c.X+1,c.Z),new(c.X,c.Z-1),new(c.X,c.Z+1)];
}

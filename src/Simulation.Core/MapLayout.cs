namespace Simulation.Core;

/// <summary>Resolved initial geometry, not road ownership tiers or future construction rights.</summary>
public sealed record IndustrialLot(string Id,Cell[] Cells,Cell Entry);
public sealed record ResourceDistrict(string Id,string Item,string Profile,Cell[] Cells,Cell[] PublicCells);
public sealed record MarketStall(string Id,Cell Cell);
public sealed record CivicSite(string Id,string Kind,Cell[] Footprint,Cell[]? SourceBuilding,Cell[]? Parking,Cell[] Body,Cell Entry);
public sealed record MapLayout(string Version,string SourceId,string SourceName,string SourceSha256,int MaxParticipants,
    Cell[] RoadCenterCells,Cell[] RoadCorridorCells,Cell[] InitialCityDirtCells,IndustrialLot[] IndustrialLots,
    ResourceDistrict[] ResourceDistricts,CivicSite[] CivicObjects,MarketStall[] MarketStalls,Cell[] SpawnCells,
    Cell[] TurnCells,string PassingBays,string Assignment);

public static partial class ScenarioValidation
{
    public static void ValidateMapLayout(ScenarioDefinition s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if(s.Layout is not {} l)return;
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool valid){if(!valid)throw new ArgumentException("Invalid resolved map layout");}
        Require((l.Version is "map-adapter-1" or "map-adapter-2") && !string.IsNullOrWhiteSpace(l.SourceId) && l.SourceName is {Length:>0 and <=180} &&
            System.Text.RegularExpressions.Regex.IsMatch(l.SourceSha256??"","\\A[0-9a-f]{64}\\z") && l.MaxParticipants is >=4 and <=64 && s.Participants.Length<=l.MaxParticipants);
        HashSet<Cell> Cells(Cell[] cells,bool nonempty=true)
        {Require(cells is not null && (!nonempty || cells.Length>0));var set=cells!.ToHashSet();Require(set.Count==cells.Length && set.All(c=>c.X>=0&&c.Z>=0&&c.X<s.MapWidth&&c.Z<s.MapHeight));return set;}
        var corridor=Cells(l.RoadCorridorCells);Require(Cells(l.RoadCenterCells).IsSubsetOf(corridor));Require(Cells(l.InitialCityDirtCells).IsSubsetOf(corridor));Require(Cells(l.TurnCells).IsSubsetOf(corridor));
        var spawns=Cells(l.SpawnCells);Require(spawns.Count==l.MaxParticipants && spawns.IsSubsetOf(corridor));
        Require(l.IndustrialLots is {Length:>0} && l.ResourceDistricts is {Length:>0} && l.CivicObjects is {Length:>0} && l.MarketStalls is {Length:>0});
        Require(l.IndustrialLots.All(x=>x is not null)&&l.ResourceDistricts.All(x=>x is not null)&&l.CivicObjects.All(x=>x is not null)&&l.MarketStalls.All(x=>x is not null));
        var ids=new HashSet<string>(StringComparer.Ordinal);var occupied=new HashSet<Cell>();
        void Id(string id){Require(!string.IsNullOrWhiteSpace(id)&&id.Length<=80&&ids.Add(id));}
        void Reserve(HashSet<Cell> cells){Require(!cells.Overlaps(corridor)&&!cells.Overlaps(occupied));occupied.UnionWith(cells);}
        foreach(var lot in l.IndustrialLots)
        {Id(lot.Id);var c=Cells(lot.Cells);Require(c.Count==6 && c.Max(p=>p.X)-c.Min(p=>p.X)==1 && c.Max(p=>p.Z)-c.Min(p=>p.Z)==2 && corridor.Contains(lot.Entry) && c.Any(p=>Math.Abs(p.X-lot.Entry.X)+Math.Abs(p.Z-lot.Entry.Z)==1));Reserve(c);}
        foreach(var d in l.ResourceDistricts)
        {Id(d.Id);var c=Cells(d.Cells);Require(Cells(d.PublicCells).IsSubsetOf(c) && d.Profile==d.Item && s.Items.Any(i=>i.Id==d.Item));Reserve(c);Require(c.All(p=>s.ResourceNodes.Count(n=>n.Position==p&&n.ItemId==d.Item&&n.Rules?.Public==d.PublicCells.Contains(p))==1));}
        Require(s.ResourceNodes.Length==l.ResourceDistricts.Sum(d=>d.Cells.Length));
        var bodies=new HashSet<Cell>();
        foreach(var site in l.CivicObjects)
        {Id(site.Id);var c=Cells(site.Footprint);var body=Cells(site.Body);
            if(site.Kind=="town_hall")Require(body.Count==4 && body.Max(p=>p.X)-body.Min(p=>p.X)==1 && body.Max(p=>p.Z)-body.Min(p=>p.Z)==1);
            var shape=site.Kind switch{"town_hall"=>(3,3),"terminal"=>(2,3),"dealership"=>(5,5),_=>(0,0)};Require(c.Count==shape.Item1*shape.Item2 && c.Max(p=>p.X)-c.Min(p=>p.X)+1==shape.Item1 && c.Max(p=>p.Z)-c.Min(p=>p.Z)+1==shape.Item2);
            Require(body.IsSubsetOf(c)&&c.Contains(site.Entry)&&!body.Contains(site.Entry)&&body.Any(p=>Math.Abs(p.X-site.Entry.X)+Math.Abs(p.Z-site.Entry.Z)==1));if(site.SourceBuilding is {} source)Require(Cells(source).SetEquals(body));if(site.Parking is {} parking)Require(Cells(parking).IsSubsetOf(c));if(l.Version=="map-adapter-2")
            {
                // New starts only. Never reinterpret the body/entry of a resolved adapter-1 save.
                Require(site.SourceBuilding is not null && body.Count==4 && body.Max(p=>p.X)-body.Min(p=>p.X)==1 && body.Max(p=>p.Z)-body.Min(p=>p.Z)==1);
                Require(body.Min(p=>p.X)==c.Min(p=>p.X) && body.Min(p=>p.Z)==c.Min(p=>p.Z));
                Require(site.Entry==new Cell(c.Min(p=>p.X)+(site.Kind=="town_hall"?1:0),c.Min(p=>p.Z)+2));
                if(site.Kind=="dealership")Require(site.Parking is {Length:4} && site.Parking.SequenceEqual(Enumerable.Range(0,4).Select(i=>new Cell(c.Min(p=>p.X)+i,c.Min(p=>p.Z)+3))));
                Require(site.Parking is null || !site.Parking.Any(body.Contains));
            }
            Reserve(c);bodies.UnionWith(body);}
        Require(l.CivicObjects.Select(c=>c.Kind).Order(StringComparer.Ordinal).SequenceEqual(new[]{"dealership","terminal","town_hall"}));
        Require(s.BlockedCells.ToHashSet().SetEquals(bodies));Require(s.CapitalDemand?.Terminal==l.CivicObjects.Single(c=>c.Kind=="terminal").Entry && s.Support?.TradingPoint==s.CapitalDemand.Terminal);
        var stalls=Cells(l.MarketStalls.Select(m=>m.Cell).ToArray());foreach(var stall in l.MarketStalls)Id(stall.Id);Reserve(stalls);
        var allowed=l.IndustrialLots.SelectMany(t=>t.Cells).Concat(stalls).ToHashSet();
        Require((s.Plots??[]).Length==allowed.Count && (s.Plots??[]).All(p=>allowed.Contains(p.Position)&&!p.IsCorridor&&p.IsShopSite==stalls.Contains(p.Position)));
        Require(!string.IsNullOrWhiteSpace(l.Assignment)&&!string.IsNullOrWhiteSpace(l.PassingBays));
    }
}

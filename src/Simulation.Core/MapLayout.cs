namespace Simulation.Core;

/// <summary>Resolved initial geometry, not road ownership tiers or future construction rights.</summary>
public sealed record IndustrialLot(string Id,Cell[] Cells,Cell Entry);
public sealed record ResourceDistrict(string Id,string Item,string Profile,Cell[] Cells,Cell[] PublicCells);
public sealed record MarketStall(string Id,Cell Cell);
public sealed record CivicSite(string Id,string Kind,Cell[] Footprint,Cell[]? SourceBuilding,Cell[]? Parking,Cell[] Body,Cell Entry,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Cell[]? EquipmentParking=null);
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
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool valid,string because){if(!valid)throw new ArgumentException("Invalid resolved map layout: "+because);}
        static string At(Cell cell)=>FormattableString.Invariant($"at {cell.X},{cell.Z}");
        Require((l.Version is "map-adapter-1" or "map-adapter-2" or "map-adapter-3" or "map-adapter-4") && !string.IsNullOrWhiteSpace(l.SourceId) && l.SourceName is {Length:>0 and <=180} &&
            System.Text.RegularExpressions.Regex.IsMatch(l.SourceSha256??"","\\A[0-9a-f]{64}\\z") && l.MaxParticipants is >=4 and <=64 && s.Participants.Length<=l.MaxParticipants,"map header "+l.SourceId);
        HashSet<Cell> Cells(Cell[] cells,string what,bool nonempty=true)
        {
            Require(cells is not null && (!nonempty || cells.Length>0),"empty "+what);
            var set=cells!.ToHashSet();
            if(set.Count!=cells.Length || cells.Any(c=>c.X<0||c.Z<0||c.X>=s.MapWidth||c.Z>=s.MapHeight))
            {var bad=cells.First(c=>c.X<0||c.Z<0||c.X>=s.MapWidth||c.Z>=s.MapHeight||cells.Count(p=>p==c)>1);Require(false,"duplicate or outside "+what+" "+At(bad));}
            return set;
        }
        var corridor=Cells(l.RoadCorridorCells,"road corridor");Require(Cells(l.RoadCenterCells,"road center").IsSubsetOf(corridor),"road center leaves the corridor");Require(Cells(l.InitialCityDirtCells,"city dirt").IsSubsetOf(corridor),"city dirt leaves the corridor");Require(Cells(l.TurnCells,"turn").IsSubsetOf(corridor),"turn leaves the corridor");
        var spawns=Cells(l.SpawnCells,"spawn");Require(spawns.Count==l.MaxParticipants && spawns.IsSubsetOf(corridor),"spawns "+spawns.Count+"/"+l.MaxParticipants);
        Require(l.IndustrialLots is {Length:>0} && l.ResourceDistricts is {Length:>0} && l.CivicObjects is {Length:>0} && l.MarketStalls is {Length:>0},"missing lots, resources, civic objects or stalls");
        Require(l.IndustrialLots.All(x=>x is not null)&&l.ResourceDistricts.All(x=>x is not null)&&l.CivicObjects.All(x=>x is not null)&&l.MarketStalls.All(x=>x is not null),"null map object");
        var ids=new HashSet<string>(StringComparer.Ordinal);var occupied=new HashSet<Cell>();
        void Id(string id){Require(!string.IsNullOrWhiteSpace(id)&&id.Length<=80&&ids.Add(id),"duplicate id "+id);}
        void Reserve(HashSet<Cell> cells,string what)
        {
            var overlap=false;var hit=default(Cell);
            foreach(var cell in cells)if(corridor.Contains(cell)||occupied.Contains(cell)){overlap=true;hit=cell;break;}
            Require(!overlap,what+" overlaps "+At(hit));occupied.UnionWith(cells);
        }
        // adapter-1 и adapter-2 оставляют ровно 2×3. adapter-3 — связный участок 1…64 клеток; старые сохранения не перечитываются этой веткой.
        bool Linked(HashSet<Cell> cells)
        {
            if(cells.Count==0)return false;
            var seen=new HashSet<Cell>();var queue=new Queue<Cell>();var start=cells.First();queue.Enqueue(start);seen.Add(start);
            while(queue.TryDequeue(out var cell))foreach(var next in new[]{new Cell(cell.X-1,cell.Z),new(cell.X+1,cell.Z),new(cell.X,cell.Z-1),new(cell.X,cell.Z+1)})if(cells.Contains(next)&&seen.Add(next))queue.Enqueue(next);
            return seen.Count==cells.Count;
        }
        foreach(var lot in l.IndustrialLots)
        {
            Id(lot.Id);var c=Cells(lot.Cells,"lot "+lot.Id);
            bool entry=corridor.Contains(lot.Entry) && c.Any(p=>Math.Abs(p.X-lot.Entry.X)+Math.Abs(p.Z-lot.Entry.Z)==1);
            if(l.Version=="map-adapter-3")Require(c.Count is >=1 and <=64 && Linked(c) && entry,"lot "+lot.Id+" "+At(lot.Entry));
            else if(l.Version=="map-adapter-4")
            {
                var frame=IndustrialParcels.TryFrame(lot);
                Require(frame is not null && entry && IndustrialParcels.ReachesNetwork(l,lot.Entry),"lot "+lot.Id+" standard parcel "+At(lot.Entry));
                foreach(var type in (s.BuildingTypes??[]).Where(t=>t.Kind==BuildingKind.Production))
                {
                    var top=type.Levels.MaxBy(level=>level.Level);
                    var local=top is null||top.Footprint.IsDefault?type.Footprint:top.Footprint.ToArray();
                    Require(local.All(offset=>c.Contains(frame.Value.Map(offset.X,offset.Z))),"lot "+lot.Id+" footprint "+type.Id);
                }
            }
            else Require(c.Count==6 && c.Max(p=>p.X)-c.Min(p=>p.X)==1 && c.Max(p=>p.Z)-c.Min(p=>p.Z)==2 && entry,"lot "+lot.Id+" must be 2x3 "+At(lot.Entry));
            Reserve(c,"lot "+lot.Id);
        }
        foreach(var d in l.ResourceDistricts)
        {Id(d.Id);var c=Cells(d.Cells,"resource "+d.Id);Require(Cells(d.PublicCells,"public "+d.Id).IsSubsetOf(c) && d.Profile==d.Item && s.Items.Any(i=>i.Id==d.Item),"resource "+d.Id+" "+d.Item);Reserve(c,"resource "+d.Id);Require(c.All(p=>s.ResourceNodes.Count(n=>n.Position==p&&n.ItemId==d.Item&&n.Rules?.Public==d.PublicCells.Contains(p))==1),"resource node "+d.Id+" "+At(c.First()));}
        Require(s.ResourceNodes.Length==l.ResourceDistricts.Sum(d=>d.Cells.Length),"resource node count");
        var bodies=new HashSet<Cell>();
        foreach(var site in l.CivicObjects)
        {Id(site.Id);var c=Cells(site.Footprint,site.Kind+" "+site.Id);var body=Cells(site.Body,site.Kind+" body "+site.Id);
            Require(site.EquipmentParking is null || site.Kind=="dealership"&&s.Economy?.Fleet?.EquipmentDealerVersion is 1 or 2,"equipment parking on "+site.Id);
            if(site.Kind=="town_hall")Require(body.Count==4 && body.Max(p=>p.X)-body.Min(p=>p.X)==1 && body.Max(p=>p.Z)-body.Min(p=>p.Z)==1,"town hall body "+site.Id+" "+At(site.Entry));
            int spanX=c.Max(p=>p.X)-c.Min(p=>p.X)+1,spanZ=c.Max(p=>p.Z)-c.Min(p=>p.Z)+1;
            var shape=site.Kind switch{"town_hall"=>(3,3),"terminal"=>(2,3),"dealership"=>(5,5),_=>(0,0)};
            bool upright=c.Count==shape.Item1*shape.Item2 && spanX==shape.Item1 && spanZ==shape.Item2;
            // Поворот содержимого меняет терминал 2×3 на 3×2. Квадратные ратуша и автосалон не меняют габарит.
            bool turned=l.Version is "map-adapter-3" or "map-adapter-4" && site.Kind=="terminal" && c.Count==6 && spanX==3 && spanZ==2;
            Require(upright||turned,site.Kind+" "+site.Id+" footprint "+spanX+"x"+spanZ+" "+At(site.Entry));
            Require(body.IsSubsetOf(c)&&c.Contains(site.Entry)&&!body.Contains(site.Entry)&&body.Any(p=>Math.Abs(p.X-site.Entry.X)+Math.Abs(p.Z-site.Entry.Z)==1),site.Kind+" entry "+site.Id+" "+At(site.Entry));if(site.SourceBuilding is {} source)Require(Cells(source,site.Kind+" source "+site.Id).SetEquals(body),site.Kind+" source "+site.Id);if(site.Parking is {} parking)Require(Cells(parking,site.Kind+" apron "+site.Id).IsSubsetOf(c),site.Kind+" apron "+site.Id+" "+At(site.Entry));if(l.Version=="map-adapter-2")
            {
                // New starts only. Never reinterpret the body/entry of a resolved adapter-1 save.
                Require(site.SourceBuilding is not null && body.Count==4 && body.Max(p=>p.X)-body.Min(p=>p.X)==1 && body.Max(p=>p.Z)-body.Min(p=>p.Z)==1,site.Kind+" body "+site.Id+" "+At(site.Entry));
                Require(body.Min(p=>p.X)==c.Min(p=>p.X) && body.Min(p=>p.Z)==c.Min(p=>p.Z),site.Kind+" body corner "+site.Id+" "+At(site.Entry));
                Require(site.Entry==new Cell(c.Min(p=>p.X)+(site.Kind=="town_hall"?1:0),c.Min(p=>p.Z)+2),site.Kind+" south entry "+site.Id+" "+At(site.Entry));
                if(site.Kind=="dealership")Require(site.Parking is {Length:4} && site.Parking.SequenceEqual(Enumerable.Range(0,4).Select(i=>new Cell(c.Min(p=>p.X)+i,c.Min(p=>p.Z)+3))),"dealership apron "+site.Id+" "+At(site.Entry));
                Require(site.Parking is null || !site.Parking.Any(body.Contains),site.Kind+" apron overlaps body "+site.Id);
            }
            if(l.Version is "map-adapter-3" or "map-adapter-4")
            {
                Require(site.SourceBuilding is not null && body.Count==4 && body.Max(p=>p.X)-body.Min(p=>p.X)==1 && body.Max(p=>p.Z)-body.Min(p=>p.Z)==1,site.Kind+" body "+site.Id+" "+At(site.Entry));
                Require(site.Parking is null || !site.Parking.Any(body.Contains),site.Kind+" apron overlaps body "+site.Id);
                if(site.Kind=="dealership")Require(site.Parking is {Length:4},"dealership apron "+site.Id+" "+At(site.Entry));
            }
            Reserve(c,site.Kind+" "+site.Id);bodies.UnionWith(body);}
        Require(l.CivicObjects.Select(c=>c.Kind).Order(StringComparer.Ordinal).SequenceEqual(new[]{"dealership","terminal","town_hall"}),"civic set");
        Require(s.BlockedCells.ToHashSet().SetEquals(bodies),"blocked cells");Require(s.CapitalDemand?.Terminal==l.CivicObjects.Single(c=>c.Kind=="terminal").Entry && s.Support?.TradingPoint==s.CapitalDemand.Terminal,"terminal entry");
        var stalls=Cells(l.MarketStalls.Select(m=>m.Cell).ToArray(),"stall");foreach(var stall in l.MarketStalls)Id(stall.Id);Reserve(stalls,"stall");
        var allowed=l.IndustrialLots.SelectMany(t=>t.Cells).Concat(stalls).ToHashSet();
        Require((s.Plots??[]).Length==allowed.Count && (s.Plots??[]).All(p=>allowed.Contains(p.Position)&&!p.IsCorridor&&p.IsShopSite==stalls.Contains(p.Position)),"plots");
        Require(!string.IsNullOrWhiteSpace(l.Assignment)&&!string.IsNullOrWhiteSpace(l.PassingBays),"assignment");
    }
}

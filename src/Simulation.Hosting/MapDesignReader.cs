using System.Text.Json;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Strict design geometry to the existing runtime. Does not create a world or simulate routes.</summary>
public static class MapDesignReader
{
    public const int MaximumBytes=2*1024*1024;
    private sealed record Lot([property:JsonRequired]string Id,[property:JsonRequired]int[][] Cells,[property:JsonRequired]int[] Entry);
    private sealed record District([property:JsonRequired]string Id,[property:JsonRequired]string Item,[property:JsonRequired]int[][] Cells,[property:JsonRequired]int[][] PublicCells,[property:JsonRequired]string Profile);
    private sealed record Civic([property:JsonRequired]string Id,[property:JsonRequired]string Kind,[property:JsonRequired]int[][] Footprint,int[][]? Building=null,int[][]? Parking=null);
    private sealed record Stall([property:JsonRequired]string Id,[property:JsonRequired]int[] Cell);
    private sealed record Design([property:JsonRequired]string SchemaVersion,[property:JsonRequired]string Id,[property:JsonRequired]string Name,[property:JsonRequired]int Width,[property:JsonRequired]int Height,
        [property:JsonRequired]int MaxParticipants,[property:JsonRequired]int[][] RoadCenterCells,[property:JsonRequired]int[][] RoadCorridorCells,[property:JsonRequired]int[][] InitialCityDirtCells,
        [property:JsonRequired]Lot[] IndustrialLots,[property:JsonRequired]District[] ResourceDistricts,[property:JsonRequired]Civic[] CivicObjects,[property:JsonRequired]Stall[] MarketStalls,
        [property:JsonRequired]int[][] SpawnCells,[property:JsonRequired]int[][] TurnCells,[property:JsonRequired]string PassingBays,[property:JsonRequired]string Assignment);
    private static readonly JsonSerializerOptions Options=new(){PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=16};
    public static bool IsDesign(ReadOnlyMemory<byte> json)
    {using var d=JsonDocument.Parse(json);return d.RootElement.TryGetProperty("schema_version",out _);}
    public static ScenarioDefinition Read(ReadOnlyMemory<byte> bytes,EconomyProfile economy)
    {
        if(bytes.Length>MaximumBytes)throw new ArgumentException("Map design too large");
        using(var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16}))CheckFields(document.RootElement);
        var d=JsonSerializer.Deserialize<Design>(bytes.Span,Options)??throw new ArgumentException("Empty map design");
        if(d.SchemaVersion!="delsk-map-design-1" || d.Width is <1 or >128 || d.Height is <1 or >128 || d.MaxParticipants is <4 or >64 ||
            !System.Text.RegularExpressions.Regex.IsMatch(d.Id,"\\A[a-z0-9_]{1,60}\\z") || string.IsNullOrWhiteSpace(d.Name))throw new ArgumentException("Invalid map version/id/dimensions");
        if(d.IndustrialLots.Length==0 || d.ResourceDistricts.Length==0 || d.MarketStalls.Length<d.MaxParticipants || d.SpawnCells.Length!=d.MaxParticipants || d.CivicObjects.Length!=3 || !d.CivicObjects.Select(c=>c.Kind).Order(StringComparer.Ordinal).SequenceEqual(new[]{"dealership","terminal","town_hall"}))throw new ArgumentException("Missing map geometry or civic objects");
        if(!VNextStorageProfile.MapCompatible(economy))throw new ArgumentException("Designed maps require the explicit A2 supported profile");
        Cell Point(int[] p)=>p.Length==2?new(p[0],p[1]):throw new ArgumentException("Map cell must contain exactly x,z");
        Cell[] Cells(int[][] p)=>p.Select(Point).ToArray();
        var civic=d.CivicObjects.Select(c=>
        {
            var footprint=Cells(c.Footprint);if(footprint.Length==0)throw new ArgumentException("Empty civic footprint");
            if(c.Kind=="town_hall"&&c.Building is null || c.Parking is {Length:0})throw new ArgumentException("Missing civic body/parking");
            var source=c.Building is null?null:Cells(c.Building);var parking=c.Parking is null?null:Cells(c.Parking);
            // Source has no entry/terminal/dealer body. Versioned interpretation keeps their exact platforms;
            // the southern row(s) is an open loading/parking apron, with the body to its north.
            int apron=c.Kind=="dealership"?2:1;int edge=footprint.Max(p=>p.Z)-apron;
            var body=source??footprint.Where(p=>p.Z<=edge).ToArray();
            var entry=(parking??footprint.Where(p=>p.Z==footprint.Max(q=>q.Z)).ToArray()).OrderBy(p=>p.X).ThenBy(p=>p.Z).First();
            return new CivicSite(c.Id,c.Kind,footprint,source,parking,body,entry);
        }).ToArray();
        var layout=new MapLayout("map-adapter-1",d.Id,d.Name,VNextBalanceReader.Digest(bytes.Span),d.MaxParticipants,Cells(d.RoadCenterCells),Cells(d.RoadCorridorCells),Cells(d.InitialCityDirtCells),
            d.IndustrialLots.Select(l=>new IndustrialLot(l.Id,Cells(l.Cells),Point(l.Entry))).ToArray(),
            d.ResourceDistricts.Select(r=>new ResourceDistrict(r.Id,r.Item,r.Profile,Cells(r.Cells),Cells(r.PublicCells))).ToArray(),civic,
            d.MarketStalls.Select(s=>new MarketStall(s.Id,Point(s.Cell))).ToArray(),Cells(d.SpawnCells),Cells(d.TurnCells),d.PassingBays,d.Assignment);
        var resources=(economy.World.Resources??[]).ToDictionary(r=>r.ItemId,StringComparer.Ordinal);
        var nodes=layout.ResourceDistricts.SelectMany(r=>
        {
            if(r.Item!=r.Profile || !resources.TryGetValue(r.Profile,out var profile))throw new ArgumentException("Unknown/mismatched resource profile");
            return r.Cells.Select(p=>new ResourceNodeDefinition(r.Item,p,r.PublicCells.Contains(p)?profile.Public:profile.Private));
        }).ToArray();
        long id=0;var plots=layout.IndustrialLots.SelectMany(l=>l.Cells).Select(c=>new PlotDefinition(++id,c,economy.World.PlotPriceMinor))
            .Concat(layout.MarketStalls.Select(s=>new PlotDefinition(++id,s.Cell,economy.World.ShopSitePriceMinor,IsShopSite:true))).ToArray();
        if(layout.SpawnCells.Length<4)throw new ArgumentException("Insufficient spawn template");
        var terminal=civic.Single(c=>c.Kind=="terminal").Entry;var w=economy.World;
        var scenario=new ScenarioDefinition(d.Id,17,d.Width,d.Height,civic.SelectMany(c=>c.Body).ToArray(),w.Items,w.Recipes,
            Enumerable.Range(1,4).Select(i=>new ParticipantDefinition(i,w.StartingCashMinor,layout.SpawnCells[i-1],w.PersonalInventoryCapacity,i==1?"":layout.ResourceDistricts[(i-2)%layout.ResourceDistricts.Length].Item)).ToArray(),
            [],[],nodes,Plots:plots,Buildings:[],BuildingTypes:w.BuildingTypes,CapitalDemand:w.CapitalDemand with{Terminal=terminal},Support:w.Support with{TradingPoint=terminal},Layout:layout);
        // Full profile values and controllers are applied once by the ordinary resolver/caller.
        ScenarioValidation.ValidateMapLayout(scenario);return scenario;
    }
    private static void CheckFields(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.Null)throw new ArgumentException("Null map field");
        if(node.ValueKind==JsonValueKind.Object)
        {var names=new HashSet<string>(StringComparer.Ordinal);foreach(var f in node.EnumerateObject()){if(!names.Add(f.Name))throw new ArgumentException("Duplicate map field");CheckFields(f.Value);}}
        else if(node.ValueKind==JsonValueKind.Array)foreach(var v in node.EnumerateArray())CheckFields(v);
    }
}

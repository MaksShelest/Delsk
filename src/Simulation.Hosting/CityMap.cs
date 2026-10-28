using Simulation.Core;

namespace Simulation.Hosting;

[Flags] public enum CityLayer { Resources=1, Industry=2, Property=4, Roads=8, Services=16, Deliveries=32, People=64, All=127 }
public sealed record CityMapObject(string Key,CityLayer Layer,string Kind,long Id,long Owner,Cell Entry,Cell[] Footprint,
    string Label,int Level=0,string Status="",long Quantity=0,long Deadline=0,RoadContribution[]? Contributions=null,Cell[]? Route=null,long Holder=0);
public sealed record CityMapFrame(long Tick,int Width,int Height,long Viewer,CityMapObject[] Objects,long[] Participants)
{public Cell[] Blocked {get;init;}=[];}
public sealed record CityMapMemory(int Width,int Height,double Zoom,double CenterX,double CenterZ,CityLayer Layers,long Participant,string SelectedKey);

/// <summary>Explicit allowlist over public presentation and the viewer's own/bilateral desk.
/// No engine/WorldState, balances, inventories, private prices or foreign contracts in this DTO.</summary>
public static class CityMap
{
    public static CityMapFrame Read(PresentationFrame publicFrame,PlayerView own)
    {
        long actor=own.Participant.Id.Value;var objects=new List<CityMapObject>();
        foreach(var m in publicFrame.Markers)
        {
            CityLayer layer;string kind=m.Kind;
            if(kind.StartsWith("resource",StringComparison.Ordinal))layer=CityLayer.Resources;
            else if(kind is "production" or "warehouse" or "shop" or "equipment" or "civic" or "capital")layer=CityLayer.Industry;
            else if(kind.StartsWith("plot",StringComparison.Ordinal)||kind=="shop-plot")layer=CityLayer.Property;
            else continue;
            var at=new Cell(m.X,m.Z);long owner=m.OwnerId;
            if(layer==CityLayer.Resources)owner=own.Resources.FirstOrDefault(r=>r.Position==at&&r.ItemId==m.Label)?.Owner?.Value??0;
            // Marker footprint is relative to its entry. Do not copy WarehouseVisual or foreign stock.
            var footprint=m.Footprint.Length==0?new[]{at}:m.Footprint.Select(c=>new Cell(m.X+c.X,m.Z+c.Z)).ToArray();
            if(kind=="civic"&&publicFrame.Layout?.CivicObjects.FirstOrDefault(c=>c.Entry==at&&c.Kind==m.Label) is {} civic)footprint=[..civic.Footprint];
            var plot=layer==CityLayer.Property?own.Plots.FirstOrDefault(p=>p.Id.Value==m.EntityId):null;
            var site=layer==CityLayer.Resources?own.Resources.FirstOrDefault(r=>r.Position==at&&r.ItemId==m.Label):null;
            long holder=plot?.ReservedBy?.Value??site?.Tenant?.Value??0;
            string key=layer==CityLayer.Property?"plot:"+m.EntityId:layer==CityLayer.Resources?$"resource:{m.X}:{m.Z}:{m.Label}":kind=="capital"?$"capital:{m.X}:{m.Z}":$"{kind}:{m.EntityId}:{m.X}:{m.Z}:{m.Label}";
            objects.Add(new(key,layer,kind,m.EntityId,owner,at,footprint,m.Label,m.Level,
                layer==CityLayer.Resources?(m.ResourceStock==0?"Depleted":"Available"):holder>0?"Reserved":"",Deadline:plot?.ReservationUntilTick??site?.LeaseUntil??0,Holder:holder));
        }
        foreach(var r in own.Roads?.Cells??[])
            objects.Add(new($"road:{r.Cell.X}:{r.Cell.Z}",CityLayer.Roads,"road",0,0,r.Cell,[r.Cell],"",r.Level,Contributions:[..r.Contributions]));
        foreach(var bypass in own.Roads?.Bypasses??[])
            objects.Add(new($"bypass:{bypass.VehicleId}",CityLayer.Roads,"bypass",bypass.VehicleId,0,bypass.WorkCell,[..bypass.Path],"",Status:bypass.Closing?"Closing":"Working",Route:[..bypass.Path]));
        foreach(var v in publicFrame.Vehicles.Where(v=>!v.ForSale))
            objects.Add(new($"vehicle:{v.Id}",CityLayer.People,"vehicle",v.Id,v.Owner,new(v.X,v.Z),[new(v.X,v.Z)],v.ModelId));
        foreach(var p in publicFrame.Actors)
            objects.Add(new($"actor:{p.Id}",CityLayer.People,"actor",p.Id,p.Id,new(p.X,p.Z),[new(p.X,p.Z)],""));
        foreach(var s in own.Services?.Catalog??[])
            objects.Add(new($"service:{s.VehicleId}",CityLayer.Services,"service",s.VehicleId,s.Carrier.Value,s.Position,[s.Position],s.ModelId,Status:s.Available?"Available":"Busy",Quantity:s.CapacityVolume));
        foreach(var s in own.Services?.OwnOrders.Where(s=>s.Active)??[])
        {
            // Even a malformed caller cannot turn a third party's order into a map layer.
            if(s.Customer.Value!=actor&&s.Carrier.Value!=actor)continue;
            var q=s.Quote;objects.Add(new($"delivery:{s.Id}",CityLayer.Deliveries,"delivery",s.Id,actor,q.DestinationPosition,[q.SourcePosition,q.DestinationPosition],q.Terms.ItemId,
                Status:s.State,Quantity:q.Terms.Quantity,Deadline:s.State=="Accepted"?s.DeadlineTick:s.RespondByTick,Route:[..q.Loaded]));
        }
        foreach(var l in own.Logistics.Where(l=>l.OwnerId.Value==actor&&l.Trip is not null))
        {
            var t=l.Trip!;var end=t.Path.IsEmpty?t.Position:t.Path[^1];
            objects.Add(new($"logistics:{l.Id.Value}",CityLayer.Deliveries,"logistics",l.Id.Value,actor,end,[end],t.ItemId,Status:t.State,Quantity:t.Quantity,Deadline:t.ArrivalTick,Route:[..t.Path]));
        }
        foreach(var purchase in own.Market?.Allocations.Where(p=>p.Buyer.Value==actor&&p.ClosedTick is null)??[])
        {
            var guide=own.PurchaseGuides.FirstOrDefault(g=>g.PurchaseId==purchase.Id);if(guide is null)continue;
            objects.Add(new("pickup:"+purchase.Id,CityLayer.Deliveries,"pickup",purchase.Id,actor,guide.Position,[guide.Position],purchase.ItemId,Status:"AwaitingPickup",Quantity:purchase.RemainingQuantity,Deadline:purchase.DeadlineTick,Route:[..guide.Path]));
        }
        foreach(var c in own.Contracts??[])
            if(c.Buyer?.Value==actor&&c.ClosedTick is null)
                objects.Add(new($"incoming:{c.Id}",CityLayer.Deliveries,"incoming",c.Id,actor,c.Position,[c.Position],c.ItemId,Status:c.Status,Quantity:c.RemainingQuantity,Deadline:c.DeadlineTick));
        foreach(var p in own.Development?.Projects??[])
            objects.Add(new($"project:{p.Id}",CityLayer.Industry,"project",p.Id,actor,p.Position,[..p.Plan.Contour],p.TypeId,p.Level,"Building",Deadline:p.CompletesTick));
        return new(own.Tick,publicFrame.MapWidth,publicFrame.MapHeight,actor,[..objects],publicFrame.Actors.Select(p=>p.Id).Distinct().Order().ToArray()){Blocked=[..publicFrame.Markers.Where(m=>m.Kind=="blocked").Select(m=>new Cell(m.X,m.Z))]};
    }
    public static CityMapObject? Selected(CityMapFrame map,string key)
    {
        var found=map.Objects.FirstOrDefault(o=>o.Key==key);if(found is not null)return found;
        var parts=key.Split(':');if(parts.Length==3&&parts[0]=="cell"&&int.TryParse(parts[1],out int x)&&int.TryParse(parts[2],out int z)&&x>=0&&z>=0&&x<map.Width&&z<map.Height)
            return new(key,0,"cell",0,0,new(x,z),[new(x,z)],"");
        return null;
    }
    public static bool Visible(CityMapObject o,CityLayer layers,long participant)=>
        (layers&o.Layer)!=0&&(participant==0||o.Owner==participant||o.Holder==participant||o.Kind=="road"&&o.Contributions?.Any(c=>c.Builder.Value==participant)==true);
    public static CityMapObject[] At(CityMapFrame map,Cell cell,CityLayer layers,long participant)=>
        map.Objects.Where(o=>Visible(o,layers,participant)&&(o.Entry==cell||o.Footprint.Contains(cell)))
        .OrderBy(o=>o.Layer==CityLayer.Property?10:o.Kind=="road"?8:0).ThenBy(o=>o.Key,StringComparer.Ordinal).ToArray();
}

/// <summary>Cosmetic viewport. Pixel-to-cell transforms are shared by mouse, touch and keyboard.</summary>
public sealed class CityMapPreferences
{
    public CityLayer Layers {get;set;}=CityLayer.All;
    public long Participant {get;set;}
    public string SelectedKey {get;set;}="";
    public double Zoom {get;private set;}=1;
    public double CenterX {get;private set;}=double.NaN;
    public double CenterZ {get;private set;}=double.NaN;
    private int _mapWidth,_mapHeight;
    public void Reset(){Layers=CityLayer.All;Participant=0;SelectedKey="";Fit();}
    public CityMapMemory? Capture()=>_mapWidth==0?null:new(_mapWidth,_mapHeight,Zoom,CenterX,CenterZ,Layers,Participant,SelectedKey);
    public void Restore(CityMapMemory memory)
    {
        if(memory.Width!=_mapWidth||memory.Height!=_mapHeight||!double.IsFinite(memory.Zoom)||memory.Zoom is <1 or >32||!double.IsFinite(memory.CenterX)||!double.IsFinite(memory.CenterZ)||(memory.Layers&~CityLayer.All)!=0||memory.Participant<0||memory.SelectedKey is null||memory.SelectedKey.Length>200)throw new ArgumentException("Invalid map UI memory");
        Zoom=memory.Zoom;Center(memory.CenterX,memory.CenterZ);Layers=memory.Layers;Participant=memory.Participant;SelectedKey=memory.SelectedKey;
    }
    public void Bind(int width,int height)
    {if(width<1||height<1)throw new ArgumentOutOfRangeException(nameof(width));if(_mapWidth==width&&_mapHeight==height)return;_mapWidth=width;_mapHeight=height;Fit();}
    public void Fit(){Zoom=1;CenterX=_mapWidth/2d;CenterZ=_mapHeight/2d;}
    public double Unit(double width,double height)=>Math.Max(.001,Math.Min(width/_mapWidth,height/_mapHeight)*Zoom);
    public (double X,double Z) World(double x,double y,double width,double height)=>
        (CenterX+(x-width/2)/Unit(width,height),CenterZ+(y-height/2)/Unit(width,height));
    public Cell? CellAt(double x,double y,double width,double height)
    {var p=World(x,y,width,height);int cx=(int)Math.Floor(p.X),cz=(int)Math.Floor(p.Z);return cx>=0&&cz>=0&&cx<_mapWidth&&cz<_mapHeight?new(cx,cz):null;}
    public void Pan(double dx,double dz,double width,double height)=>Center(CenterX-dx/Unit(width,height),CenterZ-dz/Unit(width,height));
    public void Center(double x,double z){CenterX=Math.Clamp(x,0,_mapWidth);CenterZ=Math.Clamp(z,0,_mapHeight);}
    public void ZoomAt(double factor,double x,double y,double width,double height)
    {
        if(!double.IsFinite(factor)||factor<=0)return;var before=World(x,y,width,height);Zoom=Math.Clamp(Zoom*factor,1,32);var after=World(x,y,width,height);
        Center(before.X-after.X+CenterX,before.Z-after.Z+CenterZ);
    }
}

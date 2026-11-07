using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

[Flags] public enum CityLayer { Resources=1, Industry=2, Property=4, Roads=8, Services=16, Deliveries=32, People=64, All=127 }

/// <summary>Режимы обзора. Это состояние интерфейса, не правило экономики.</summary>
public enum CityMapFocus { All, Production, Transport, Orders, Property }

/// <summary>Разрешённые показатели одного объекта. Чужие рецепты, запасы, цены и счета сюда не входят.</summary>
public sealed record CityMapFacts(string State,string Detail,long Done,long Need,long Finished,string Reason,long Room,long Used,long Ready,long Haul,long? Arrives,bool Soon,bool Late,string Family)
{
    public const long Absent=-1;
    public int? Percent()=>Family switch
    {
        "production" or "harvest"=>CityMap.BatchPercent(Done>=0&&Need>0?1:0,Done,Need),
        "cargo" or "storage"=>CityMap.VolumePercent(Used,Room),
        "contract" or "service"=>Done<0?null:CityMap.ObligationPercent(Done,Need),
        _=>null
    };
}

public sealed record CityMapObject(string Key,CityLayer Layer,string Kind,long Id,long Owner,Cell Entry,Cell[] Footprint,
    string Label,int Level=0,string Status="",long Quantity=0,long Deadline=0,RoadContribution[]? Contributions=null,Cell[]? Route=null,long Holder=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CityMapFacts? Facts=null);
public sealed record CityMapFrame(long Tick,int Width,int Height,long Viewer,CityMapObject[] Objects,long[] Participants)
{public Cell[] Blocked {get;init;}=[];}
public sealed record CityMapMemory(int Width,int Height,double Zoom,double CenterX,double CenterZ,CityLayer Layers,long Participant,string SelectedKey,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] CityMapFocus Focus=CityMapFocus.All);

/// <summary>Явный allowlist публичной картинки и собственного/двустороннего планшета.
/// Нет WorldState, чужих запасов, цен, счетов и закрытых договоров.</summary>
public static class CityMap
{
    public static int? BatchPercent(long batchId,long done,long need)
    {
        if(batchId<=0||need<=0||done<0||done>need)return null;
        return (int)(done*100L/need);
    }
    public static int? VolumePercent(long used,long room)
    {
        if(room<=0||used<0||used>room)return null;
        return (int)(used*100L/room);
    }
    public static int? ObligationPercent(long delivered,long required)
    {
        if(required<=0||delivered<0||delivered>required)return null;
        return (int)(delivered*100L/required);
    }
    /// <summary>Близкий срок только внутри уже заданного правилами окна. Нулевое окно не считается предупреждением.</summary>
    public static bool DeadlineSoon(long tick,long deadline,long warningTicks)
    {
        if(warningTicks<=0||deadline<=0)return false;
        long left=deadline-tick;return left>0&&left<=warningTicks;
    }
    public static bool DeadlineLate(long tick,long deadline,bool open)=>open&&deadline>0&&tick>deadline;

    public static string ProductionState(EnterpriseView e)
    {
        if(!e.Enabled&&e.BatchId==0)return "Stopped";
        if(e.BatchId>0&&e.WorkRequired>0&&(e.ReasonCode=="Working"||e.Enabled))return "Working";
        if(!e.Enabled)return "Stopped";
        if(e.ReasonCode is "NoStaff" or "NoInputs" or "NoOutputSpace" or "NoMoney" or "NoEnergy" or "ConstructionInProgress" or "PreparingDemolition" or "PreparingDevelopment" or "IndustryTechnologyRequired")return "Blocked";
        if(e.Enabled&&e.BatchId==0)return e.ReasonCode is "" or "Working" or "UserPaused"?"Idle":"Blocked";
        return "Idle";
    }
    public static string EquipmentState(EquipmentView e)
    {
        if(e.Motion is not null||e.ReasonCode is "Moving" or "MovingEquipment")return "Moving";
        if(e.CycleId>0&&e.WorkRequired>0&&e.ReasonCode=="Working")return "Harvesting";
        if(e.Mode==EquipmentMode.Off||e.ReasonCode=="UserPaused"||e.Deployed==false)return "Stopped";
        if(e.ReasonCode is "NoOutputSpace" or "ResourceDepleted" or "BelowMinimumCrew" or "NoStaff" or "NoMoney" or "NoResourceRights" or "EquipmentPacked" or "EquipmentNotPlaced")return "Blocked";
        return "Waiting";
    }
    public static CityMapFacts Production(EnterpriseView e)
    {
        bool active=e.BatchId>0&&e.WorkRequired>0;
        return new(ProductionState(e),e.RecipeId,active?e.WorkDone:CityMapFacts.Absent,active?e.WorkRequired:CityMapFacts.Absent,e.CompletedBatches,Safe(e.ReasonCode),CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,null,false,false,"production");
    }
    public static CityMapFacts Volume(InventoryView inventory,string family,string state)
        =>new(state,"",CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,"",inventory.Capacity,inventory.OccupiedVolume,inventory.ReservedCapacity,CityMapFacts.Absent,null,false,false,family);
    public static CityMapFacts Obligation(string state,string item,long delivered,long required,long ready,long haul,long deadline,long tick,long warningTicks,string family,bool open)
        =>new(state,item,delivered,required,CityMapFacts.Absent,"",CityMapFacts.Absent,CityMapFacts.Absent,ready,haul,null,DeadlineSoon(tick,deadline,warningTicks),DeadlineLate(tick,deadline,open)||state=="Expired",family);

    public static (string Kind,long Id)? Card(CityMapObject o,long viewer)
    {
        if(o.Owner==viewer&&o.Kind is "production" or "warehouse" or "shop")return ("building",o.Id);
        if(o.Owner==viewer&&o.Kind=="equipment")return ("equipment",o.Id);
        return null;
    }
    public static string? Section(CityMapObject o,long viewer)=>o.Kind switch
    {
        "vehicle"=>"fleet","service"=>"service-vehicle","delivery"=>"services","pickup"=>"purchases","incoming" or "outgoing"=>"holdings",_=>o.Owner==viewer&&o.Layer==CityLayer.Deliveries?"holdings":null
    };

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
            var footprint=m.Footprint.Length==0?new[]{at}:m.Footprint.Select(c=>new Cell(m.X+c.X,m.Z+c.Z)).ToArray();
            if(kind=="civic"&&publicFrame.Layout?.CivicObjects.FirstOrDefault(c=>c.Entry==at&&c.Kind==m.Label) is {} civic)footprint=[..civic.Footprint];
            var plot=layer==CityLayer.Property?own.Plots.FirstOrDefault(p=>p.Id.Value==m.EntityId):null;
            var site=layer==CityLayer.Resources?own.Resources.FirstOrDefault(r=>r.Position==at&&r.ItemId==m.Label):null;
            long holder=plot?.ReservedBy?.Value??site?.Tenant?.Value??0;
            string key=layer==CityLayer.Property?"plot:"+m.EntityId:layer==CityLayer.Resources?$"resource:{m.X}:{m.Z}:{m.Label}":kind=="capital"?$"capital:{m.X}:{m.Z}":$"{kind}:{m.EntityId}:{m.X}:{m.Z}:{m.Label}";
            var facts=MarkerFacts(own,actor,kind,m.EntityId,owner,holder,m.Label,plot);
            objects.Add(new(key,layer,kind,m.EntityId,owner,at,footprint,m.Label,m.Level,
                layer==CityLayer.Resources?(m.ResourceStock==0?"Depleted":"Available"):holder>0?"Reserved":"",Deadline:plot?.ReservationUntilTick??site?.LeaseUntil??0,Holder:holder,Facts:facts));
        }
        foreach(var r in own.Roads?.Cells??[])
            objects.Add(new($"road:{r.Cell.X}:{r.Cell.Z}",CityLayer.Roads,"road",0,0,r.Cell,[r.Cell],"",r.Level,Contributions:[..r.Contributions]));
        foreach(var bypass in own.Roads?.Bypasses??[])
            objects.Add(new($"bypass:{bypass.VehicleId}",CityLayer.Roads,"bypass",bypass.VehicleId,0,bypass.WorkCell,[..bypass.Path],"",Status:bypass.Closing?"Closing":"Working",Route:[..bypass.Path]));
        foreach(var v in publicFrame.Vehicles.Where(v=>!v.ForSale))
            objects.Add(VehicleObject(own,actor,v));
        foreach(var p in publicFrame.Actors)
            objects.Add(new($"actor:{p.Id}",CityLayer.People,"actor",p.Id,p.Id,new(p.X,p.Z),[new(p.X,p.Z)],""));
        foreach(var s in own.Services?.Catalog??[])
            objects.Add(new($"service:{s.VehicleId}",CityLayer.Services,"service",s.VehicleId,s.Carrier.Value,s.Position,[s.Position],s.ModelId,Status:s.Available?"Available":"Busy",Quantity:s.CapacityVolume));
        foreach(var s in own.Services?.OwnOrders.Where(s=>s.Active)??[])
        {
            if(s.Customer.Value!=actor&&s.Carrier.Value!=actor)continue;
            var q=s.Quote;var run=own.Services?.Execution?.Runs.FirstOrDefault(r=>r.Run.Order==s.Id);
            long window=s.State is "Pending" or "Requote"?own.Services?.Rules.ResponseTicks??0:0;
            long deadline=s.State=="Accepted"?s.DeadlineTick:s.RespondByTick;
            var facts=Obligation(s.State,q.Terms.ItemId,run is null?CityMapFacts.Absent:run.Run.Delivered,q.Terms.Quantity,run?.SourceRemaining??CityMapFacts.Absent,run?.Run.Cargo??CityMapFacts.Absent,deadline,own.Tick,window,"service",s.Active);
            objects.Add(new($"delivery:{s.Id}",CityLayer.Deliveries,"delivery",s.Id,actor,q.DestinationPosition,[q.SourcePosition,q.DestinationPosition],q.Terms.ItemId,
                Status:s.State,Quantity:q.Terms.Quantity,Deadline:deadline,Route:[..q.Loaded],Facts:facts));
        }
        foreach(var l in own.Logistics.Where(l=>l.OwnerId.Value==actor&&l.Trip is not null))
        {
            var t=l.Trip!;var end=t.Path.IsEmpty?t.Position:t.Path[^1];
            var facts=new CityMapFacts(t.State,t.ItemId,CityMapFacts.Absent,t.Quantity,l.CompletedTrips,Safe(l.ReasonCode),CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,t.Quantity,t.ArrivalTick>0?t.ArrivalTick:null,false,false,"logistics");
            objects.Add(new($"logistics:{l.Id.Value}",CityLayer.Deliveries,"logistics",l.Id.Value,actor,end,[end],t.ItemId,Status:t.State,Quantity:t.Quantity,Deadline:t.ArrivalTick,Route:[..t.Path],Facts:facts));
        }
        foreach(var purchase in own.Market?.Allocations.Where(p=>p.Buyer.Value==actor&&p.ClosedTick is null)??[])
        {
            var guide=own.PurchaseGuides.FirstOrDefault(g=>g.PurchaseId==purchase.Id);if(guide is null)continue;
            var facts=new CityMapFacts("AwaitingPickup",purchase.ItemId,CityMapFacts.Absent,purchase.Quantity,CityMapFacts.Absent,"",CityMapFacts.Absent,CityMapFacts.Absent,purchase.RemainingQuantity,CityMapFacts.Absent,null,false,DeadlineLate(own.Tick,purchase.DeadlineTick,true),"pickup");
            objects.Add(new("pickup:"+purchase.Id,CityLayer.Deliveries,"pickup",purchase.Id,actor,guide.Position,[guide.Position],purchase.ItemId,Status:"AwaitingPickup",Quantity:purchase.RemainingQuantity,Deadline:purchase.DeadlineTick,Route:[..guide.Path],Facts:facts));
        }
        foreach(var c in own.Contracts??[])
        {
            if(c.ClosedTick is not null)continue;
            bool buyer=c.Buyer?.Value==actor,supplier=c.Supplier.Value==actor;if(!buyer&&!supplier)continue;
            var trip=own.Logistics.FirstOrDefault(l=>l.OwnerId.Value==actor&&l.Trip?.ContractId==c.Id)?.Trip;
            var facts=Obligation(c.Status,c.ItemId,c.DeliveredQuantity,c.Quantity,c.PreparedQuantity,c.InTransitQuantity,c.DeadlineTick,own.Tick,0,"contract",true);
            objects.Add(new((buyer?"incoming:":"outgoing:")+c.Id,CityLayer.Deliveries,buyer?"incoming":"outgoing",c.Id,actor,c.Position,[c.Position],c.ItemId,Status:c.Status,Quantity:c.RemainingQuantity,Deadline:c.DeadlineTick,Route:trip is null?null:[..trip.Path],Facts:facts));
        }
        foreach(var offer in own.BuyOffers??[])
        {
            if(offer.Buyer.Value!=actor||offer.Status!="Open")continue;
            var facts=new CityMapFacts(offer.Status,offer.ItemId,CityMapFacts.Absent,offer.Quantity,CityMapFacts.Absent,"",CityMapFacts.Absent,CityMapFacts.Absent,offer.AllocatedQuantity,CityMapFacts.Absent,null,false,DeadlineLate(own.Tick,offer.ExpiresAtTick,true),"offer");
            objects.Add(new($"offer:{offer.Id}",CityLayer.Deliveries,"offer",offer.Id,actor,offer.Position,[offer.Position],offer.ItemId,Status:offer.Status,Quantity:offer.RemainingQuantity,Deadline:offer.ExpiresAtTick,Facts:facts));
        }
        foreach(var p in own.Development?.Projects??[])
            objects.Add(new($"project:{p.Id}",CityLayer.Industry,"project",p.Id,actor,p.Position,[..p.Plan.Contour],p.TypeId,p.Level,"Building",Deadline:p.CompletesTick));
        return new(own.Tick,publicFrame.MapWidth,publicFrame.MapHeight,actor,[..objects],publicFrame.Actors.Select(p=>p.Id).Distinct().Order().ToArray()){Blocked=[..publicFrame.Markers.Where(m=>m.Kind=="blocked").Select(m=>new Cell(m.X,m.Z))]};
    }

    private static CityMapFacts? MarkerFacts(PlayerView own,long actor,string kind,long id,long owner,long holder,string label,PlotView? plot)
    {
        if(kind=="production"&&owner==actor&&own.Enterprises.FirstOrDefault(e=>e.Id.Value==id&&e.OwnerId.Value==actor) is {} enterprise)return Production(enterprise);
        if(kind is "warehouse" or "shop"&&owner==actor&&own.Buildings.FirstOrDefault(b=>b.Id.Value==id&&b.OwnerId.Value==actor) is {} building)
        {
            var inventory=own.Inventories.FirstOrDefault(i=>i.OwnerId.Value==actor&&i.Id==building.InputId);
            if(inventory is not null)return StorageFacts(own,inventory);
        }
        if(kind=="equipment"&&owner==actor&&own.Equipment?.FirstOrDefault(e=>e.Id.Value==id&&e.OwnerId.Value==actor) is {} machine)return Machine(own,actor,machine,null);
        if(kind.StartsWith("plot",StringComparison.Ordinal)||kind=="shop-plot")return new(PlotState(kind,owner,actor,holder),plot?.BuildingId is null?"":"Occupied",CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,"",CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,null,false,false,"land");
        if(kind.StartsWith("resource",StringComparison.Ordinal))return new(holder>0?"Leased":owner>0?"Owned":"Open",label,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,"",CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,null,false,false,"extraction");
        return null;
    }
    private static string PlotState(string kind,long owner,long actor,long holder)
        =>holder>0?"Reserved":kind=="shop-plot"?"Shop":owner==actor?"Own":owner>0?"Npc":"Free";
    private static CityMapFacts StorageFacts(PlayerView own,InventoryView inventory)
    {
        var facts=Volume(inventory,"storage","Storage");
        int window=own.Economy?.Storage?.WarningOffsets?.DefaultIfEmpty(0).Max()??0;
        bool soon=window>0&&(inventory.Overflow??[]).Any(lot=>lot.Owner==inventory.OwnerId.Value&&DeadlineSoon(own.Tick,lot.ExpiresTick,window));
        return soon?facts with{Soon=true}:facts;
    }
    private static CityMapFacts Machine(PlayerView own,long actor,EquipmentView machine,string? motion)
    {
        var storage=own.Inventories.FirstOrDefault(i=>i.OwnerId.Value==actor&&i.Id==machine.StorageId);
        bool cycle=machine.CycleId>0&&machine.WorkRequired>0;
        return new(motion??EquipmentState(machine),machine.ResourceItem??"",cycle?machine.WorkDone:CityMapFacts.Absent,cycle?machine.WorkRequired:CityMapFacts.Absent,machine.CompletedCycles,Safe(machine.ReasonCode),storage?.Capacity??CityMapFacts.Absent,storage?.OccupiedVolume??CityMapFacts.Absent,storage?.ReservedCapacity??CityMapFacts.Absent,CityMapFacts.Absent,null,false,false,"harvest");
    }
    private static CityMapObject VehicleObject(PlayerView own,long actor,PresentationVehicle v)
    {
        bool extractor=v.EquipmentItem is not null;
        var owned=own.Fleet?.Vehicles.FirstOrDefault(item=>item.Vehicle.Id==v.Id&&item.Vehicle.Owner.Value==actor);
        var cargo=owned is null?null:own.Inventories.FirstOrDefault(i=>i.OwnerId.Value==actor&&i.Id==owned.Vehicle.CargoInventory);
        var logistics=own.Logistics.FirstOrDefault(l=>l.OwnerId.Value==actor&&(l.VehicleId==v.Id||owned is not null&&owned.Logistics.Value!=0&&l.Id==owned.Logistics));
        var machine=extractor?own.Equipment?.FirstOrDefault(e=>e.Id.Value==v.Id&&e.OwnerId.Value==actor):null;
        CityMapFacts? facts=null;Cell[]? route=null;
        long? arrives=logistics?.Trip?.ArrivalTick is >0 and var eta?eta:null;
        if(machine is not null)
        {
            facts=Machine(own,actor,machine,null) with{Arrives=arrives};
            if(machine.Route is {Enabled:true} extraction&&!extraction.Stops.IsDefault&&extraction.Stops.Length>1)route=[..extraction.Stops];
            if(cargo is not null)facts=facts with{Room=cargo.Capacity,Used=cargo.OccupiedVolume,Ready=cargo.ReservedCapacity};
        }
        else if(owned is not null)
            facts=cargo is null
                ?new(owned.State,owned.Model.Name,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,Safe(owned.State),CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,CityMapFacts.Absent,arrives,false,false,"cargo")
                :Volume(cargo,"cargo",owned.State) with{Detail=owned.Model.Name,Arrives=arrives};
        if(logistics?.Trip is {Path.Length:>1} trip)route=[..trip.Path];
        string kind=extractor?"equipment":"vehicle";
        string key=extractor?$"equipment:{v.Id}":$"vehicle:{v.Id}";
        return new(key,extractor?CityLayer.People:CityLayer.People,kind,v.Id,v.Owner,new(v.X,v.Z),[new(v.X,v.Z)],extractor?v.EquipmentItem??v.ModelId:v.ModelId,extractor?v.EquipmentLevel:0,Facts:facts,Route:route);
    }
    private static string Safe(string reason)=>reason.Contains("stock",StringComparison.OrdinalIgnoreCase)||reason.Contains("escrow",StringComparison.OrdinalIgnoreCase)||reason.Contains("unitPrice",StringComparison.OrdinalIgnoreCase)?"":reason;

    public static CityMapObject? Selected(CityMapFrame map,string key)
    {
        var found=map.Objects.FirstOrDefault(o=>o.Key==key);if(found is not null)return found;
        var parts=key.Split(':');if(parts.Length==3&&parts[0]=="cell"&&int.TryParse(parts[1],out int x)&&int.TryParse(parts[2],out int z)&&x>=0&&z>=0&&x<map.Width&&z<map.Height)
            return new(key,0,"cell",0,0,new(x,z),[new(x,z)],"");
        return null;
    }
    public static bool Visible(CityMapObject o,CityLayer layers,long participant)=>
        (layers&o.Layer)!=0&&(participant==0||o.Owner==participant||o.Holder==participant||o.Kind=="road"&&o.Contributions?.Any(c=>c.Builder.Value==participant)==true);
    public static bool Shown(CityMapObject o,CityLayer layers,long participant,CityMapFocus focus)
    {
        if(focus==CityMapFocus.All)return Visible(o,layers,participant);
        bool mine=participant==0||o.Owner==participant||o.Holder==participant||o.Kind=="road"&&o.Contributions?.Any(c=>c.Builder.Value==participant)==true;
        if(!mine)return false;
        return focus switch
        {
            CityMapFocus.Production=>o.Kind is "production" or "project",
            CityMapFocus.Transport=>o.Kind is "vehicle" or "equipment" or "service" or "actor",
            CityMapFocus.Orders=>o.Layer==CityLayer.Deliveries,
            CityMapFocus.Property=>o.Layer is CityLayer.Property or CityLayer.Resources||o.Kind is "production" or "equipment",
            _=>Visible(o,layers,participant)
        };
    }
    public static CityMapObject[] At(CityMapFrame map,Cell cell,CityLayer layers,long participant)=>At(map,cell,layers,participant,CityMapFocus.All);
    public static CityMapObject[] At(CityMapFrame map,Cell cell,CityLayer layers,long participant,CityMapFocus focus)=>
        map.Objects.Where(o=>Shown(o,layers,participant,focus)&&(o.Entry==cell||o.Footprint.Contains(cell)))
        .OrderBy(o=>o.Layer==CityLayer.Property?10:o.Kind=="road"?8:0).ThenBy(o=>o.Key,StringComparer.Ordinal).ToArray();
}

/// <summary>Косметический вид. Преобразование пикселей общее для мыши, касания и клавиатуры.</summary>
public sealed class CityMapPreferences
{
    public CityLayer Layers {get;set;}=CityLayer.All;
    public CityMapFocus Focus {get;set;}=CityMapFocus.All;
    public long Participant {get;set;}
    public string SelectedKey {get;set;}="";
    public double Zoom {get;private set;}=1;
    public double CenterX {get;private set;}=double.NaN;
    public double CenterZ {get;private set;}=double.NaN;
    private int _mapWidth,_mapHeight;
    public void Reset(){Layers=CityLayer.All;Focus=CityMapFocus.All;Participant=0;SelectedKey="";Fit();}
    public void ApplyFocus(CityMapFocus focus)
    {
        Focus=focus;Layers=focus switch
        {
            CityMapFocus.Production=>CityLayer.Industry,
            CityMapFocus.Transport=>CityLayer.People|CityLayer.Services,
            CityMapFocus.Orders=>CityLayer.Deliveries,
            CityMapFocus.Property=>CityLayer.Property|CityLayer.Resources,
            _=>CityLayer.All
        };
    }
    public CityMapMemory? Capture()=>_mapWidth==0?null:new(_mapWidth,_mapHeight,Zoom,CenterX,CenterZ,Layers,Participant,SelectedKey,Focus);
    public void Restore(CityMapMemory memory)
    {
        if(memory.Width!=_mapWidth||memory.Height!=_mapHeight||!double.IsFinite(memory.Zoom)||memory.Zoom is <1 or >32||!double.IsFinite(memory.CenterX)||!double.IsFinite(memory.CenterZ)||(memory.Layers&~CityLayer.All)!=0||memory.Participant<0||memory.SelectedKey is null||memory.SelectedKey.Length>200||!Enum.IsDefined(memory.Focus))throw new ArgumentException("Invalid map UI memory");
        Zoom=memory.Zoom;Center(memory.CenterX,memory.CenterZ);Layers=memory.Layers;Participant=memory.Participant;SelectedKey=memory.SelectedKey;Focus=memory.Focus;
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

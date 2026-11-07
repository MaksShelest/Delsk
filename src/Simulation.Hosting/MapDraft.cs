using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Editable delsk-map-design-2 document. Drafts may be incomplete; ordinary play uses a checked export.</summary>
public sealed class MapDraft
{
    public const string ExhibitionToken="compact-exhibition-1";
    public static readonly string[] ResourceItems=["wood","grain","milk","beet","stone","sand","iron_ore","copper_ore"];
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull,
        Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented=true
    };
    private readonly List<string> _undo=[];
    private readonly List<string> _redo=[];
    public string SchemaVersion{get;set;}="delsk-map-design-2";
    public string Id{get;set;}="draft_map";
    public string Name{get;set;}="Новая карта";
    public int Width{get;set;}=32;
    public int Height{get;set;}=25;
    public int MaxParticipants{get;set;}=4;
    public bool CompactExhibition{get;set;}
    public string PassingBays{get;set;}="Single-cell roads. Showroom parking stays on the dealership apron.";
    public string Assignment{get;set;}="startup-rotation-1; full six-cell industrial lots; eight public resources; no initial private ownership.";
    public List<Cell> Roads{get;}=[];
    public List<DraftLot> Lots{get;}=[];
    public List<DraftDistrict> Districts{get;}=[];
    public List<DraftCivic> Civics{get;}=[];
    public List<DraftStall> Stalls{get;}=[];
    public List<Cell> Spawns{get;}=[];
    public List<Cell> Turns{get;}=[];
    public List<Cell> EquipmentParking{get;}=[];
    public string? ActiveDistrictId{get;private set;}
    public bool CanUndo=>_undo.Count>0;
    public bool CanRedo=>_redo.Count>0;
    public static MapDraft Create(int width=32,int height=25)
    {
        var draft=new MapDraft{Width=Math.Clamp(width,4,128),Height=Math.Clamp(height,4,128),MaxParticipants=4};
        draft.Spawns.Add(new(1,1));draft.Turns.Add(new(1,1));return draft;
    }
    public static MapDraft Parse(string json)
    {
        var file=JsonSerializer.Deserialize<DraftFile>(json,Json)??throw new ArgumentException("Пустой файл карты");
        var draft=new MapDraft
        {
            SchemaVersion=file.SchemaVersion,Id=file.Id,Name=file.Name,Width=file.Width,Height=file.Height,MaxParticipants=file.MaxParticipants,
            CompactExhibition=file.CompactExhibition||file.Assignment.Contains(ExhibitionToken,StringComparison.Ordinal),
            PassingBays=file.PassingBays,Assignment=file.Assignment
        };
        draft.Roads.AddRange(Points(file.RoadCenterCells));
        draft.Lots.AddRange(file.IndustrialLots.Select(l=>new DraftLot(l.Id,Points(l.Cells).ToList(),Point(l.Entry))));
        draft.Districts.AddRange(file.ResourceDistricts.Select(d=>new DraftDistrict(d.Id,d.Item,Points(d.Cells).ToList(),Points(d.PublicCells).ToList())));
        draft.Civics.AddRange(file.CivicObjects.Select(c=>new DraftCivic(c.Id,c.Kind,Points(c.Footprint).ToList(),c.Entry is null?Point(c.Footprint[0]):Point(c.Entry),c.Parking is null?[]:Points(c.Parking).ToList(),c.Building is null?[]:Points(c.Building).ToList())));
        draft.Stalls.AddRange(file.MarketStalls.Select(s=>new DraftStall(s.Id,Point(s.Cell))));
        draft.Spawns.AddRange(Points(file.SpawnCells));
        draft.Turns.AddRange(Points(file.TurnCells));
        draft.EquipmentParking.AddRange(file.CivicObjects.Where(c=>c.EquipmentParking is not null).SelectMany(c=>Points(c.EquipmentParking!)));
        return draft;
    }
    public string ToJson()
    {
        var roads=Distinct(Roads);
        var corridor=Distinct(roads.Concat(Spawns));
        var turns=Distinct(Turns.Where(corridor.Contains));
        if(turns.Count==0&&corridor.Count>0)turns.Add(corridor[0]);
        var parking=Distinct(EquipmentParking);
        var file=new DraftFile(SchemaVersion,Id,Name,Width,Height,MaxParticipants,CompactExhibition,Cells(roads),Cells(corridor),Cells(roads),
            Lots.Select(l=>new LotFile(l.Id,Cells(l.Cells),Pair(l.Entry))).ToArray(),
            Districts.Where(d=>d.Cells.Count>0).Select(d=>new DistrictFile(d.Id,d.Item,Cells(d.Cells),Cells(d.PublicCells.Count==0?[d.Cells[^1]]:d.PublicCells),d.Item)).ToArray(),
            Civics.Select(c=>new CivicFile(c.Id,c.Kind,Cells(c.Footprint),Cells(c.Building),c.Kind=="dealership"?Cells(c.Parking):null,Pair(c.Entry),c.Kind=="dealership"&&parking.Count>0?Cells(parking):null)).ToArray(),
            Stalls.Select(s=>new StallFile(s.Id,Pair(s.Cell))).ToArray(),Cells(Distinct(Spawns)),Cells(turns),PassingBays,AssignmentText());
        return JsonSerializer.Serialize(file,Json)+"\n";
    }
    private bool _stroke;
    public void BeginStroke(){if(_stroke)return;Remember();_stroke=true;}
    public void EndStroke()=>_stroke=false;
    public void Remember(){_undo.Add(ToJson());if(_undo.Count>100)_undo.RemoveAt(0);_redo.Clear();}
    private void Record(){if(!_stroke)Remember();}
    public void Undo(){if(!CanUndo)return;_stroke=false;_redo.Add(ToJson());Copy(Parse(_undo[^1]));_undo.RemoveAt(_undo.Count-1);}
    public void Redo(){if(!CanRedo)return;_stroke=false;_undo.Add(ToJson());Copy(Parse(_redo[^1]));_redo.RemoveAt(_redo.Count-1);}
    public void Resize(int width,int height)
    {
        Record();Width=Math.Clamp(width,4,128);Height=Math.Clamp(height,4,128);
        bool Inside(Cell c)=>c.X>=0&&c.Z>=0&&c.X<Width&&c.Z<Height;
        Roads.RemoveAll(c=>!Inside(c));Spawns.RemoveAll(c=>!Inside(c));Turns.RemoveAll(c=>!Inside(c));EquipmentParking.RemoveAll(c=>!Inside(c));
        foreach(var lot in Lots.ToArray())if(lot.Cells.Any(c=>!Inside(c))||!Inside(lot.Entry))Lots.Remove(lot);
        foreach(var d in Districts){d.Cells.RemoveAll(c=>!Inside(c));d.PublicCells.RemoveAll(c=>!d.Cells.Contains(c));}
        Districts.RemoveAll(d=>d.Cells.Count==0);
        foreach(var c in Civics.ToArray())if(c.Footprint.Any(p=>!Inside(p)))Civics.Remove(c);
        Stalls.RemoveAll(s=>!Inside(s.Cell));
    }
    public void PaintRoad(Cell cell){if(!Inside(cell)||Roads.Contains(cell))return;Record();Roads.Add(cell);if(Turns.Count==0)Turns.Add(cell);}
    public void EraseRoad(Cell cell){if(!Roads.Contains(cell))return;Record();Roads.Remove(cell);Turns.Remove(cell);}
    public void BeginResourceGroup(string item)
    {
        if(!ResourceItems.Contains(item))throw new ArgumentException("Неизвестный ресурс");
        Record();var district=new DraftDistrict(item+"_"+ (Districts.Count+1).ToString("00"),item,[],[]);
        Districts.Add(district);ActiveDistrictId=district.Id;
    }
    public void PaintResource(Cell cell,string item)
    {
        if(!Inside(cell)||!ResourceItems.Contains(item))return;
        var district=Districts.FirstOrDefault(d=>d.Id==ActiveDistrictId&&d.Item==item);
        if(district is null||district.Cells.Contains(cell)){if(district?.Cells.Contains(cell)==true)return;BeginResourceGroup(item);district=Districts[^1];}
        else Record();
        district.Cells.Add(cell);if(district.PublicCells.Count==0)district.PublicCells.Add(cell);
    }
    public void PlaceLot(Cell origin)
    {
        var cells=Rect(origin.X,origin.Z,2,3).Where(Inside).ToList();if(cells.Count!=6)return;
        Record();var entry=Inside(new(origin.X,origin.Z+3))?new Cell(origin.X,origin.Z+3):new Cell(origin.X,origin.Z-1);
        Lots.Add(new("lot_"+(Lots.Count+1).ToString("00"),cells,entry));
    }
    public void MoveLot(string id,Cell origin)
    {
        var lot=Lots.FirstOrDefault(l=>l.Id==id);if(lot is null)return;
        int dx=origin.X-lot.Cells.Min(c=>c.X),dz=origin.Z-lot.Cells.Min(c=>c.Z);if(dx==0&&dz==0)return;
        var moved=lot.Cells.Select(c=>new Cell(c.X+dx,c.Z+dz)).ToList();var entry=new Cell(lot.Entry.X+dx,lot.Entry.Z+dz);
        if(moved.Any(c=>!Inside(c))||!Inside(entry))return;
        Record();lot.Cells.Clear();lot.Cells.AddRange(moved);lot.Entry=entry;
    }
    public void PlaceCivic(string kind,Cell origin)
    {
        if(kind is not ("town_hall" or "terminal" or "dealership"))return;
        var civic=MakeCivic(kind,origin);if(civic.Footprint.Any(c=>!Inside(c)))return;
        Record();Civics.RemoveAll(c=>c.Kind==kind);Civics.Add(civic);
    }
    public void MoveCivic(string kind,Cell origin)
    {
        if(Civics.All(c=>c.Kind!=kind)){PlaceCivic(kind,origin);return;}
        var civic=MakeCivic(kind,origin);if(civic.Footprint.Any(c=>!Inside(c)))return;
        Record();Civics.RemoveAll(c=>c.Kind==kind);Civics.Add(civic);
    }
    public void ToggleStall(Cell cell)
    {
        if(!Inside(cell))return;Record();var old=Stalls.FirstOrDefault(s=>s.Cell==cell);
        if(old is null)Stalls.Add(new("stall_"+(Stalls.Count+1).ToString("00"),cell));else Stalls.Remove(old);
    }
    public void ToggleParking(Cell cell)
    {
        if(!Inside(cell))return;Record();
        if(!EquipmentParking.Remove(cell)){if(EquipmentParking.Count>=16)return;EquipmentParking.Add(cell);}
    }
    public void ToggleSpawn(Cell cell)
    {
        if(!Inside(cell))return;Record();
        if(!Spawns.Remove(cell)&&Spawns.Count<MaxParticipants)Spawns.Add(cell);
    }
    public void DeleteAt(Cell cell)
    {
        Record();
        Roads.Remove(cell);Turns.Remove(cell);EquipmentParking.Remove(cell);Spawns.Remove(cell);
        Stalls.RemoveAll(s=>s.Cell==cell);Lots.RemoveAll(l=>l.Cells.Contains(cell)||l.Entry==cell);Civics.RemoveAll(c=>c.Footprint.Contains(cell));
        foreach(var d in Districts){d.Cells.Remove(cell);d.PublicCells.Remove(cell);}Districts.RemoveAll(d=>d.Cells.Count==0);
    }
    public void SetPublic(Cell cell,bool Public)
    {
        var district=Districts.FirstOrDefault(d=>d.Cells.Contains(cell));if(district is null)return;
        Record();
        if(Public){if(!district.PublicCells.Contains(cell))district.PublicCells.Add(cell);}
        else district.PublicCells.Remove(cell);
        if(district.PublicCells.Count==0&&district.Cells.Count>0)district.PublicCells.Add(district.Cells[0]);
    }
    public IReadOnlyList<string> Problems()
    {
        var notes=new List<string>();
        void Add(string text){if(notes.Count<40)notes.Add(text);}
        if(Width is <4 or >128||Height is <4 or >128)Add($"Размер {Width}×{Height} вне 4…128.");
        if(string.IsNullOrWhiteSpace(Id)||Id.Length>60)Add("Нужен короткий латинский идентификатор карты.");
        var occupied=new Dictionary<Cell,string>();
        void Take(IEnumerable<Cell> cells,string label){foreach(var c in cells){if(!Inside(c))Add($"{label} выходит за карту: ({c.X}, {c.Z}).");else if(!occupied.TryAdd(c,label))Add($"Пересечение ({c.X}, {c.Z}): {occupied[c]} и {label}.");}}
        foreach(var lot in Lots)
        {
            Take(lot.Cells,$"участок {lot.Id}");
            int dx=lot.Cells.Count==0?0:lot.Cells.Max(c=>c.X)-lot.Cells.Min(c=>c.X),dz=lot.Cells.Count==0?0:lot.Cells.Max(c=>c.Z)-lot.Cells.Min(c=>c.Z);
            if(lot.Cells.Distinct().Count()!=6||dx!=1||dz!=2)Add($"Участок {lot.Id} должен быть 2×3, сейчас {lot.Cells.Count} клеток.");
            if(!Roads.Contains(lot.Entry))Add($"Въезд участка {lot.Id} ({lot.Entry.X}, {lot.Entry.Z}) не на дороге.");
            else if(!lot.Cells.Any(c=>Math.Abs(c.X-lot.Entry.X)+Math.Abs(c.Z-lot.Entry.Z)==1))Add($"Въезд участка {lot.Id} не примыкает к площадке.");
        }
        foreach(var d in Districts)
        {
            Take(d.Cells,$"ресурс {d.Item}");
            if(!ResourceItems.Contains(d.Item))Add($"Неизвестный ресурс {d.Item}.");
            if(d.PublicCells.Any(c=>!d.Cells.Contains(c)))Add($"Публичная клетка ресурса {d.Id} не входит в группу.");
        }
        foreach(var item in ResourceItems)
        {
            var cells=Districts.Where(d=>d.Item==item).SelectMany(d=>d.Cells).Distinct().ToArray();
            int open=Districts.Where(d=>d.Item==item).SelectMany(d=>d.PublicCells).Distinct().Count(cells.Contains);
            if(cells.Length>0&&(open==0||open==cells.Length))Add($"Ресурсу {item} нужны и общая, и покупаемая клетки.");
        }
        if(Districts.Select(d=>d.Item).Distinct().Count()<ResourceItems.Length)Add("Нужны все восемь видов ресурсов.");
        foreach(var c in Civics)Take(c.Footprint,$"{c.Kind}");
        foreach(var kind in new[]{"town_hall","terminal","dealership"})if(Civics.All(c=>c.Kind!=kind))Add($"Нет объекта: {kind}.");
        Take(Stalls.Select(s=>s.Cell), "лавка");
        if(Stalls.Count<MaxParticipants)Add($"Лавок {Stalls.Count}, нужно не меньше участников ({MaxParticipants}).");
        Take(EquipmentParking,"стоянка");
        var road=Roads.ToHashSet();
        foreach(var c in road)if(occupied.TryGetValue(c,out var label))Add($"Дорога пересекает {label}: ({c.X}, {c.Z}).");
        foreach(var c in road)if(road.Contains(new(c.X+1,c.Z))&&road.Contains(new(c.X,c.Z+1))&&road.Contains(new(c.X+1,c.Z+1)))
            Add($"Дорога шире одной клетки около ({c.X}, {c.Z}).");
        if(road.Count>0)
        {
            var seen=new HashSet<Cell>();var queue=new Queue<Cell>();queue.Enqueue(road.First());seen.Add(road.First());
            while(queue.TryDequeue(out var c))foreach(var n in new[]{new Cell(c.X-1,c.Z),new(c.X+1,c.Z),new(c.X,c.Z-1),new(c.X,c.Z+1)})if(road.Contains(n)&&seen.Add(n))queue.Enqueue(n);
            if(seen.Count!=road.Count)Add("Дороги не связаны между собой.");
        }
        else Add("Нет ни одной дороги.");
        foreach(var c in Spawns)if(!road.Contains(c))Add($"Точка появления ({c.X}, {c.Z}) не стоит на дороге.");
        if(Spawns.Distinct().Count()!=MaxParticipants)Add($"Точек появления {Spawns.Distinct().Count()}, а участников {MaxParticipants}.");
        if(EquipmentParking.Distinct().Count()!=16)Add($"Стоянок техники {EquipmentParking.Distinct().Count()}. Для обычной партии нужно ровно 16, иначе появится внешний двор.");
        foreach(var c in EquipmentParking.Distinct())if(!road.Any(r=>Math.Abs(r.X-c.X)+Math.Abs(r.Z-c.Z)==1))Add($"Стоянка ({c.X}, {c.Z}) не примыкает к дороге.");
        if(Spawns.Count>0)foreach(var lot in Lots)if(!road.Contains(lot.Entry)||!Path(road,Spawns[0],lot.Entry))Add($"Участок {lot.Id} недостижим по дороге.");
        return notes;
    }
    public string Check(EconomyProfile economy)
    {
        var notes=Problems().ToList();
        try
        {
            var scenario=economy.Apply(MapDesignReader.Read(System.Text.Encoding.UTF8.GetBytes(ToJson()),economy));
            if(scenario.MapWidth!=Width||scenario.MapHeight!=Height)notes.Add($"Профиль расширил карту до {scenario.MapWidth}×{scenario.MapHeight}.");
        }
        catch(Exception error){notes.Add(error.Message);}
        return string.Join('\n',notes.Distinct());
    }
    private void Copy(MapDraft other)
    {
        SchemaVersion=other.SchemaVersion;Id=other.Id;Name=other.Name;Width=other.Width;Height=other.Height;MaxParticipants=other.MaxParticipants;
        CompactExhibition=other.CompactExhibition;PassingBays=other.PassingBays;Assignment=other.Assignment;ActiveDistrictId=other.ActiveDistrictId;
        void Move<T>(List<T> target,IEnumerable<T> source){target.Clear();target.AddRange(source);}
        Move(Roads,other.Roads);Move(Lots,other.Lots);Move(Districts,other.Districts);Move(Civics,other.Civics);Move(Stalls,other.Stalls);Move(Spawns,other.Spawns);Move(Turns,other.Turns);Move(EquipmentParking,other.EquipmentParking);
    }
    private string AssignmentText()
    {
        var text=Assignment.Replace(" "+ExhibitionToken,"").Replace(ExhibitionToken,"").Trim();
        return CompactExhibition?text+" "+ExhibitionToken:text;
    }
    private bool Inside(Cell c)=>c.X>=0&&c.Z>=0&&c.X<Width&&c.Z<Height;
    private static bool Path(HashSet<Cell> road,Cell from,Cell to)
    {
        if(!road.Contains(from)||!road.Contains(to))return false;
        var seen=new HashSet<Cell>{from};var queue=new Queue<Cell>();queue.Enqueue(from);
        while(queue.TryDequeue(out var c))
        {
            if(c==to)return true;
            foreach(var n in new[]{new Cell(c.X-1,c.Z),new(c.X+1,c.Z),new(c.X,c.Z-1),new(c.X,c.Z+1)})if(road.Contains(n)&&seen.Add(n))queue.Enqueue(n);
        }
        return false;
    }
    private static DraftCivic MakeCivic(string kind,Cell origin)
    {
        var size=kind switch{"town_hall"=>(3,3),"terminal"=>(2,3),_=>(5,5)};
        var footprint=Rect(origin.X,origin.Z,size.Item1,size.Item2);
        var building=Rect(origin.X,origin.Z,2,2);
        var entry=new Cell(origin.X+(kind=="town_hall"?1:0),origin.Z+2);
        var parking=kind=="dealership"?Rect(origin.X,origin.Z+3,4,1):[];
        return new(kind,kind,footprint,entry,parking,building);
    }
    private static List<Cell> Rect(int x,int z,int w,int h)=>Enumerable.Range(z,h).SelectMany(row=>Enumerable.Range(x,w).Select(col=>new Cell(col,row))).ToList();
    private static List<Cell> Distinct(IEnumerable<Cell> cells)=>cells.Distinct().OrderBy(c=>c.X).ThenBy(c=>c.Z).ToList();
    private static int[][] Cells(IEnumerable<Cell> cells)=>Distinct(cells).Select(Pair).ToArray();
    private static int[] Pair(Cell c)=>[c.X,c.Z];
    private static Cell Point(int[] p)=>new(p[0],p[1]);
    private static IEnumerable<Cell> Points(int[][]? cells)=>cells?.Select(Point)??[];
    public sealed record DraftFile(string SchemaVersion,string Id,string Name,int Width,int Height,int MaxParticipants,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool CompactExhibition,int[][] RoadCenterCells,int[][] RoadCorridorCells,int[][] InitialCityDirtCells,LotFile[] IndustrialLots,DistrictFile[] ResourceDistricts,CivicFile[] CivicObjects,StallFile[] MarketStalls,int[][] SpawnCells,int[][] TurnCells,string PassingBays,string Assignment);
    public sealed record LotFile(string Id,int[][] Cells,int[] Entry);
    public sealed record DistrictFile(string Id,string Item,int[][] Cells,int[][] PublicCells,string Profile);
    public sealed record CivicFile(string Id,string Kind,int[][] Footprint,int[][] Building,int[][]? Parking,int[] Entry,int[][]? EquipmentParking);
    public sealed record StallFile(string Id,int[] Cell);
}
public sealed class DraftLot(string id,List<Cell> cells,Cell entry){public string Id{get;}=id;public List<Cell> Cells{get;}=cells;public Cell Entry{get;set;}=entry;}
public sealed class DraftDistrict(string id,string item,List<Cell> cells,List<Cell> Public){public string Id{get;}=id;public string Item{get;}=item;public List<Cell> Cells{get;}=cells;public List<Cell> PublicCells{get;}=Public;}
public sealed class DraftCivic(string id,string kind,List<Cell> footprint,Cell entry,List<Cell> parking,List<Cell> building)
{public string Id{get;}=id;public string Kind{get;}=kind;public List<Cell> Footprint{get;}=footprint;public Cell Entry{get;}=entry;public List<Cell> Parking{get;}=parking;public List<Cell> Building{get;}=building;}
public sealed class DraftStall(string id,Cell cell){public string Id{get;}=id;public Cell Cell{get;}=cell;}

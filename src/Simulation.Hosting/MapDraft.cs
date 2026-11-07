using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Черновик карты. Незаконченный файл можно сохранить; обычная партия требует проверку без ошибок.</summary>
public sealed class MapDraft
{
    public const string ExhibitionToken="compact-exhibition-1";
    public const string FlexibleSchema="delsk-map-design-3";
    public const int MaxLotCells=MapLots.MaximumCells;
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
    public bool Flexible=>SchemaVersion==FlexibleSchema;
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
        Record();var district=new DraftDistrict(NextId(item),item,[],[]);
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
        Lots.Add(new(NextId("lot"),cells,entry));
    }
    public void UseFlexible(){if(!Flexible)SchemaVersion=FlexibleSchema;}
    public string? AddLotCell(Cell cell,string? preferredId=null)
    {
        if(!Inside(cell)||Lots.Any(l=>l.Cells.Contains(cell))||Occupied(cell))return null;
        var lot=Lots.FirstOrDefault(l=>l.Id==preferredId&&l.Cells.Any(c=>Adjacent(c,cell)))??Lots.FirstOrDefault(l=>l.Cells.Any(c=>Adjacent(c,cell)));
        Record();
        if(lot is null)
        {
            lot=new(NextId("lot"),[cell],EntryNear([cell]));
            Lots.Add(lot);
        }
        else lot.Cells.Add(cell);
        if(!MapLots.LegacyRectangle(lot.Cells))UseFlexible();
        return lot.Id;
    }
    public void RemoveLotCell(Cell cell)
    {
        var lot=Lots.FirstOrDefault(l=>l.Cells.Contains(cell));if(lot is null)return;
        Record();lot.Cells.Remove(cell);
        if(lot.Cells.Count==0)Lots.Remove(lot);
        else if(!MapLots.LegacyRectangle(lot.Cells))UseFlexible();
    }
    public string? PlaceLotRectangle(Cell a,Cell b)
    {
        int x0=Math.Min(a.X,b.X),x1=Math.Max(a.X,b.X),z0=Math.Min(a.Z,b.Z),z1=Math.Max(a.Z,b.Z);
        var cells=Rect(x0,z0,x1-x0+1,z1-z0+1);
        if(cells.Count is <1 or >MaxLotCells||cells.Any(c=>!Inside(c)||Occupied(c)))return null;
        Record();
        var lot=new DraftLot(NextId("lot"),cells,EntryNear(cells));
        Lots.Add(lot);
        if(!MapLots.LegacyRectangle(cells))UseFlexible();
        return lot.Id;
    }
    public void SetEntry(string id,Cell cell)
    {
        var lot=Lots.FirstOrDefault(l=>l.Id==id);if(lot is null||lot.Entry==cell)return;
        Record();lot.Entry=cell;
    }
    public void SetParticipants(int count)
    {
        count=Math.Clamp(count,4,64);if(count==MaxParticipants)return;
        Record();MaxParticipants=count;while(Spawns.Count>count)Spawns.RemoveAt(Spawns.Count-1);
    }
    public void ToggleTurn(Cell cell)
    {
        if(!Roads.Contains(cell))return;Record();if(!Turns.Remove(cell))Turns.Add(cell);
    }
    /// <summary>Один шаг истории поворачивает всю схему на 90° по часовой стрелке и переводит её на design-3.</summary>
    public void RotateContent(int quarter=1)
    {
        quarter=((quarter%4)+4)%4;if(quarter==0)return;
        Record();
        for(int step=0;step<quarter;step++)TurnClockwise();
        UseFlexible();
    }
    public bool Reaches(Cell cell)=>Spawns.Count>0&&Path(Roads.ToHashSet(),Spawns[0],cell);
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
        if(old is null)Stalls.Add(new(NextId("stall"),cell));else Stalls.Remove(old);
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
        Stalls.RemoveAll(s=>s.Cell==cell);
        var lot=Lots.FirstOrDefault(l=>l.Cells.Contains(cell));
        if(lot is not null)
        {
            lot.Cells.Remove(cell);
            if(lot.Cells.Count==0)Lots.Remove(lot);
            else if(!MapLots.LegacyRectangle(lot.Cells))UseFlexible();
        }
        Civics.RemoveAll(c=>c.Footprint.Contains(cell));
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
    public IReadOnlyList<string> Problems()=>Notes().Where(n=>!n.Warning).Select(n=>n.Text).ToArray();
    public IReadOnlyList<MapNote> Review(EconomyProfile economy,string language="ru")
    {
        int version=economy.World.Rules.Fleet?.EquipmentDealerVersion??0;
        var notes=Notes(language,version).ToList();
        if(notes.Any(n=>!n.Warning))return notes;
        try
        {
            var scenario=economy.Apply(MapDesignReader.Read(System.Text.Encoding.UTF8.GetBytes(ToJson()),economy));
            if(scenario.MapWidth!=Width||scenario.MapHeight!=Height)
                notes.Add(new(language=="ru"?$"Профиль расширил карту до {scenario.MapWidth}×{scenario.MapHeight}. Карта игрока остаётся {Width}×{Height}.":$"The profile expanded the map to {scenario.MapWidth}×{scenario.MapHeight}. The authored map stays {Width}×{Height}.",null,false));
        }
        catch(Exception error){notes.Add(ExplainNote(error,language));}
        return notes;
    }
    public string? DescribeFix(string fix,string language="ru")
    {
        if(fix=="ids")return language=="ru"?"Повторяющиеся идентификаторы получат свободные номера. Карта не меняет координаты.":"Duplicate identifiers get free numbers. Coordinates stay the same.";
        if(fix=="bays"){var cells=BayCells();int missing=Math.Max(0,2-EquipmentParking.Distinct().Count());if(missing==0||cells.Count<missing)return null;var list=string.Join(language=="ru"?" и ":" and ",cells.Select(c=>$"({c.X}, {c.Z})"));return language=="ru"?$"Места техники автосалона: {list}. Это не городская парковка.":$"Dealership machine bays: {list}. This is not a town car park.";}
        if(fix.StartsWith("entry:",StringComparison.Ordinal)){var lot=Lots.FirstOrDefault(l=>l.Id==fix[6..]);if(lot is null)return null;var cell=EntryNear(lot.Cells);if(!Roads.Contains(cell))return null;return language=="ru"?$"Въезд участка {lot.Id} встанет на дорогу ({cell.X}, {cell.Z}).":$"Lot {lot.Id} entrance moves to the road ({cell.X}, {cell.Z}).";}
        return null;
    }
    public bool ApplyFix(string fix)
    {
        if(fix=="ids"){Record();RenameDuplicates();return true;}
        if(fix=="bays"){var cells=BayCells();int missing=Math.Max(0,2-EquipmentParking.Distinct().Count());if(missing==0||cells.Count<missing)return false;Record();foreach(var cell in cells)if(!EquipmentParking.Contains(cell)&&EquipmentParking.Count<16)EquipmentParking.Add(cell);return EquipmentParking.Distinct().Count()>=2;}
        if(fix.StartsWith("entry:",StringComparison.Ordinal)){var lot=Lots.FirstOrDefault(l=>l.Id==fix[6..]);if(lot is null)return false;var cell=EntryNear(lot.Cells);if(!Roads.Contains(cell))return false;Record();lot.Entry=cell;return true;}
        return false;
    }
    public IReadOnlyList<Cell> FixPreview(string fix)
    {
        if(fix=="bays")return BayCells();
        if(fix.StartsWith("entry:",StringComparison.Ordinal)){var lot=Lots.FirstOrDefault(l=>l.Id==fix[6..]);return lot is null?[]:[EntryNear(lot.Cells)];}
        return [];
    }
    public IReadOnlyList<MapNote> Notes(string language="ru",int dealerVersion=2)
    {
        string T(string ru,string en)=>language=="ru"?ru:en;
        var notes=new List<MapNote>();
        void Add(string text,Cell? at=null,bool warning=false,string? fix=null)=>notes.Add(new(text,at,warning,fix));
        if(Width is <4 or >128||Height is <4 or >128)Add(T($"Размер {Width}×{Height} вне 4…128.",$"Size {Width}×{Height} is outside 4…128."));
        if(string.IsNullOrWhiteSpace(Id)||Id.Length>60||!System.Text.RegularExpressions.Regex.IsMatch(Id,"\\A[a-z0-9_]{1,60}\\z"))
            Add(T("Нужен короткий латинский идентификатор карты.","The map needs a short latin identifier."));
        var occupied=new Dictionary<Cell,string>();
        void Take(IEnumerable<Cell> cells,string label){foreach(var c in cells){if(!Inside(c))Add(T($"{label} выходит за карту: ({c.X}, {c.Z}).",$"{label} is outside the map: ({c.X}, {c.Z})."),c);else if(!occupied.TryAdd(c,label))Add(T($"Пересечение ({c.X}, {c.Z}): {occupied[c]} и {label}.",$"Overlap ({c.X}, {c.Z}): {occupied[c]} and {label}."),c);}}
        foreach(var lot in Lots)
        {
            Take(lot.Cells,T($"участок {lot.Id}",$"lot {lot.Id}"));
            if(!Flexible&&!MapLots.LegacyRectangle(lot.Cells))Add(T($"Участок {lot.Id} должен быть 2×3, сейчас {lot.Cells.Count} клеток.",$"Lot {lot.Id} must be 2×3, it has {lot.Cells.Count} cells."),lot.Cells.FirstOrDefault());
            else if(lot.Cells.Count is <1 or >MaxLotCells)Add(T($"Участок {lot.Id} должен занимать от 1 до {MaxLotCells} клеток.",$"Lot {lot.Id} must cover 1 to {MaxLotCells} cells."),lot.Cells.FirstOrDefault());
            else if(!MapLots.Connected(lot.Cells))Add(T($"Участок {lot.Id} разорван: клетки должны соприкасаться сторонами.",$"Lot {lot.Id} is disconnected: cells must share a side."),lot.Cells.FirstOrDefault());
            else if(Flexible&&lot.Cells.Count<6)Add(T($"Участок {lot.Id} занимает {MapLots.Cells(lot.Cells.Count,true)}. Крупное предприятие может не поместиться.",$"Lot {lot.Id} has {lot.Cells.Count} cells. A large building may not fit."),lot.Cells[0],true);
            bool beside=lot.Cells.Any(c=>Math.Abs(c.X-lot.Entry.X)+Math.Abs(c.Z-lot.Entry.Z)==1);
            if(!Roads.Contains(lot.Entry)||!beside)Add(T($"Промышленный участок {lot.Id} не имеет подъезда: ({lot.Entry.X}, {lot.Entry.Z}).",$"Industrial lot {lot.Id} has no entrance: ({lot.Entry.X}, {lot.Entry.Z})."),lot.Entry,fix:"entry:"+lot.Id);
        }
        foreach(var d in Districts)
        {
            Take(d.Cells,T($"ресурс {ResourceTitle(d.Item,language)}",$"resource {ResourceTitle(d.Item,language)}"));
            if(!ResourceItems.Contains(d.Item))Add(T($"Неизвестный ресурс {d.Item}.",$"Unknown resource {d.Item}."));
            if(d.PublicCells.Any(c=>!d.Cells.Contains(c)))Add(T($"Публичная клетка ресурса {d.Id} не входит в группу.",$"A public cell of {d.Id} is outside its group."));
        }
        foreach(var item in ResourceItems)
        {
            var cells=Districts.Where(d=>d.Item==item).SelectMany(d=>d.Cells).Distinct().ToArray();
            int open=Districts.Where(d=>d.Item==item).SelectMany(d=>d.PublicCells).Distinct().Count(cells.Contains);
            if(cells.Length==0)Add(T($"Не размещено месторождение {Deposit(item)}.",$"No {ResourceTitle(item,"en")} deposit is placed."));
            else if(open==0||open==cells.Length)Add(T($"Ресурсу {ResourceTitle(item,language)} нужны и общая, и покупаемая клетки.",$"{ResourceTitle(item,"en")} needs both a public and a purchasable cell."));
        }
        if(Lots.Count==0)Add(T("Нет ни одного промышленного участка.","There is no industrial lot."));
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        void Identity(string id,string label,Cell? at)
        {
            if(string.IsNullOrWhiteSpace(id)||id.Length>80)Add(T($"Пустой идентификатор: {label}.",$"Empty identifier: {label}."),at);
            else if(!names.TryAdd(id,label))Add(T($"Повторяется идентификатор {id}: {names[id]} и {label}.",$"Duplicate identifier {id}: {names[id]} and {label}."),at,fix:"ids");
        }
        foreach(var lot in Lots)Identity(lot.Id,T($"участок {lot.Id}",$"lot {lot.Id}"),lot.Cells.FirstOrDefault());
        foreach(var district in Districts)Identity(district.Id,T($"ресурс {district.Id}",$"resource {district.Id}"),district.Cells.FirstOrDefault());
        foreach(var civic in Civics){Identity(civic.Id,CivicTitle(civic.Kind,language)+" "+civic.Id,civic.Entry);CivicShape(civic);}
        foreach(var stall in Stalls)Identity(stall.Id,T($"лавка {stall.Id}",$"stall {stall.Id}"),stall.Cell);
        foreach(var c in Civics)Take(c.Footprint,CivicTitle(c.Kind,language));
        if(Civics.All(c=>c.Kind!="town_hall"))Add(T("Нет ратуши.","No town hall."));
        if(Civics.All(c=>c.Kind!="terminal"))Add(T("Нет биржи.","No exchange."));
        if(Civics.All(c=>c.Kind!="dealership"))Add(T("Нет автосалона.","No dealership."));
        Take(Stalls.Select(s=>s.Cell),T("лавка","stall"));
        if(Stalls.Count<MaxParticipants)Add(T($"Лавок {Stalls.Count}, нужно не меньше участников ({MaxParticipants}).",$"Stalls: {Stalls.Count}, participants need {MaxParticipants}."));
        Take(EquipmentParking,T("стоянка","parking"));
        var road=Roads.ToHashSet();
        foreach(var c in road)if(occupied.TryGetValue(c,out var label))Add(T($"Дорога пересекает {label}: ({c.X}, {c.Z}).",$"A road overlaps {label}: ({c.X}, {c.Z})."),c);
        foreach(var c in road)if(road.Contains(new(c.X+1,c.Z))&&road.Contains(new(c.X,c.Z+1))&&road.Contains(new(c.X+1,c.Z+1)))
            Add(T($"Дорога шире одной клетки около ({c.X}, {c.Z}).",$"The road is wider than one cell near ({c.X}, {c.Z})."),c);
        if(road.Count>0)
        {
            var seen=new HashSet<Cell>();var queue=new Queue<Cell>();queue.Enqueue(road.First());seen.Add(road.First());
            while(queue.TryDequeue(out var c))foreach(var n in new[]{new Cell(c.X-1,c.Z),new(c.X+1,c.Z),new(c.X,c.Z-1),new(c.X,c.Z+1)})if(road.Contains(n)&&seen.Add(n))queue.Enqueue(n);
            if(seen.Count!=road.Count)Add(T("Дороги не связаны между собой.","The roads are not connected."));
        }
        else Add(T("Нет ни одной дороги.","There is no road."));
        foreach(var c in Spawns)if(!road.Contains(c))Add(T($"Точка появления ({c.X}, {c.Z}) не стоит на дороге.",$"Spawn ({c.X}, {c.Z}) is not on a road."),c);
        int spawns=Spawns.Distinct().Count();
        if(spawns<MaxParticipants)Add(T($"Недостаточно мест появления для {MaxParticipants} участников.",$"Not enough spawn points for {MaxParticipants} participants."),Spawns.FirstOrDefault());
        else if(spawns!=MaxParticipants)Add(T($"Точек появления {spawns}, а участников {MaxParticipants}.",$"Spawn points: {spawns}, participants: {MaxParticipants}."));
        int bays=EquipmentParking.Distinct().Count();
        if(dealerVersion==1&&bays!=16)Add(T($"Стоянок техники {bays}. Исторический профиль требует ровно 16, иначе появится внешний двор.",$"Equipment bays: {bays}. The historical profile needs exactly 16, otherwise an outside yard is added."));
        else if(dealerVersion>=2&&bays<2)Add(T($"Нужны два отдельных места техники у автосалона. Сейчас {bays}. Это не городская парковка, и карта из-за них не расширяется.",$"The dealership needs two separate machine bays. It has {bays}. This is not a town car park, and the map is not enlarged for them."),Civics.FirstOrDefault(c=>c.Kind=="dealership")?.Entry,fix:"bays");
        else if(dealerVersion>=2&&bays>16)Add(T($"Мест техники {bays}. Для четырёх классов достаточно двух и не больше шестнадцати, по одной машине на клетку.",$"Machine bays: {bays}. Four extractor classes need two and at most sixteen, one machine per cell."));
        foreach(var c in EquipmentParking.Distinct())
        {
            if(road.Contains(c))Add(T($"Парковка перекрывает дорогу: ({c.X}, {c.Z}).",$"Parking overlaps a road: ({c.X}, {c.Z})."),c);
            else if(!road.Any(r=>Math.Abs(r.X-c.X)+Math.Abs(r.Z-c.Z)==1))Add(T($"Стоянка ({c.X}, {c.Z}) не примыкает к дороге.",$"Parking ({c.X}, {c.Z}) does not touch a road."),c);
        }
        if(Spawns.Count>0)foreach(var lot in Lots)if(!road.Contains(lot.Entry)||!Path(road,Spawns[0],lot.Entry))Add(T($"Участок {lot.Id} недостижим по дороге.",$"Lot {lot.Id} is not reachable by road."),lot.Entry);
        return notes.Where(n=>!n.Warning).Take(40).Concat(notes.Where(n=>n.Warning).Take(12)).ToArray();
        void CivicShape(DraftCivic civic)
        {
            var cells=civic.Footprint.Distinct().ToArray();if(cells.Length==0){Add(T($"{CivicTitle(civic.Kind,language)} {civic.Id} не имеет клеток.",$"{civic.Kind} {civic.Id} has no cells."),civic.Entry);return;}
            int spanX=cells.Max(c=>c.X)-cells.Min(c=>c.X)+1,spanZ=cells.Max(c=>c.Z)-cells.Min(c=>c.Z)+1;
            var shape=civic.Kind switch{"town_hall"=>(3,3),"terminal"=>(2,3),"dealership"=>(5,5),_=>(0,0)};
            bool upright=cells.Length==shape.Item1*shape.Item2&&spanX==shape.Item1&&spanZ==shape.Item2;
            bool turned=Flexible&&civic.Kind=="terminal"&&cells.Length==6&&spanX==3&&spanZ==2;
            if(!upright&&!turned)Add(T($"{CivicTitle(civic.Kind,language)} {civic.Id} должна занимать {shape.Item1}×{shape.Item2}, сейчас {spanX}×{spanZ}.",$"{civic.Kind} {civic.Id} must be {shape.Item1}×{shape.Item2}, it is {spanX}×{spanZ}."),civic.Entry);
            var body=civic.Building.Distinct().ToArray();
            bool bodySquare=body.Length==4&&body.Max(c=>c.X)-body.Min(c=>c.X)==1&&body.Max(c=>c.Z)-body.Min(c=>c.Z)==1&&body.All(cells.Contains);
            if(!bodySquare)Add(T($"Корпус {CivicTitle(civic.Kind,language)} {civic.Id} должен быть квадратом 2×2 внутри здания.",$"The body of {civic.Kind} {civic.Id} must be a 2×2 square inside the building."),civic.Entry);
            bool entry=cells.Contains(civic.Entry)&&!civic.Building.Contains(civic.Entry)&&body.Any(c=>Math.Abs(c.X-civic.Entry.X)+Math.Abs(c.Z-civic.Entry.Z)==1);
            if(!entry)Add(T($"Въезд {CivicTitle(civic.Kind,language)} {civic.Id} должен быть у корпуса и внутри здания: ({civic.Entry.X}, {civic.Entry.Z}).",$"The entrance of {civic.Kind} {civic.Id} must touch the body and stay inside the building: ({civic.Entry.X}, {civic.Entry.Z})."),civic.Entry);
            if(civic.Kind=="dealership"&&civic.Parking.Distinct().Count()!=4)Add(T($"У автосалона {civic.Id} на площадке показа должно быть 4 клетки, сейчас {civic.Parking.Distinct().Count()}.",$"Dealership {civic.Id} needs 4 display cells, it has {civic.Parking.Distinct().Count()}."),civic.Entry);
        }
    }
    public string Check(EconomyProfile economy,string language="ru")=>string.Join('\n',Review(economy,language).Where(n=>!n.Warning).Select(n=>n.Text).Distinct());
    public static string Explain(Exception error,string language="ru")=>ExplainNote(error,language).Text;
    private static MapNote ExplainNote(Exception error,string language)
    {
        string message=error.Message;Cell? at=null;
        var where=System.Text.RegularExpressions.Regex.Match(message,@"at (\d+),(\d+)");
        if(where.Success)at=new(int.Parse(where.Groups[1].Value),int.Parse(where.Groups[2].Value));
        string text;
        if(message.Contains("Invalid resolved map layout",StringComparison.Ordinal))
        {
            string detail=message.Contains(':')?message[(message.IndexOf(':')+1)..].Trim():"";
            text=PlayerText.Local(language,"Карта не проходит проверку геометрии. "+Human(detail,language),"The map geometry did not pass. "+detail);
        }
        else if(message.Contains("Invalid fleet rules",StringComparison.Ordinal))text=PlayerText.Local(language,"Автосалон не принимает карту. Для текущих четырёх классов нужны 2–16 отдельных клеток техники у дороги, не на зданиях и не друг на друге. Исторический профиль I4 по-прежнему требует ровно 16.","The dealership rejected the map. The four current classes need 2–16 separate machine cells beside a road, not on buildings and not stacked. The historical I4 profile still needs exactly 16.");
        else if(message.Contains("Missing map geometry",StringComparison.Ordinal))text=PlayerText.Local(language,"Для обычной партии не хватает дорог, участков, ресурсов или городских зданий.","An ordinary game still needs roads, lots, resources or civic buildings.");
        else if(message.Contains("Map design too large",StringComparison.Ordinal))text=PlayerText.Local(language,"Файл карты больше допустимых 2 МБ.","The map file is larger than the 2 MB limit.");
        else if(message.Contains("Designed maps require",StringComparison.Ordinal))text=PlayerText.Local(language,"Карта требует современный экономический профиль.","The map requires the current economy profile.");
        else if(message.Contains("Map participant capacity",StringComparison.Ordinal))text=PlayerText.Local(language,"Участников больше, чем мест на карте.","There are more participants than places on the map.");
        else text=PlayerText.Local(language,"Карту нельзя запустить. ","The map cannot be started. ")+message;
        return new(text,at,false);
    }
    private static string Human(string detail,string language)
    {
        if(detail.Length==0)return language=="ru"?"Причина не названа.":"No detail was given.";
        if(detail.StartsWith("duplicate id ",StringComparison.Ordinal)){string id=detail["duplicate id ".Length..];return language=="ru"?"Повторяется идентификатор "+id+".":"Duplicate identifier "+id+".";}
        if(detail.StartsWith("lot ",StringComparison.Ordinal)){string id=detail[4..];return language=="ru"?"Участок "+id+".":"Lot "+id+".";}
        if(detail.Contains("entry",StringComparison.Ordinal))return language=="ru"?$"Въезд или корпус: {detail}.":$"Entrance or body: {detail}.";
        if(detail.Contains("footprint",StringComparison.Ordinal))return language=="ru"?$"Форма здания: {detail}.":$"Building shape: {detail}.";
        if(detail.StartsWith("missing ",StringComparison.Ordinal))return language=="ru"?"Не хватает участка, ресурса, городского здания или лавки.":"A lot, resource, civic building or stall is missing.";
        return detail;
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
    private void TurnClockwise()
    {
        int height=Height;
        Cell Map(Cell cell)=>new(height-1-cell.Z,cell.X);
        void MapList(List<Cell> cells){var next=cells.Select(Map).ToList();cells.Clear();cells.AddRange(next);}
        MapList(Roads);MapList(Spawns);MapList(Turns);MapList(EquipmentParking);
        foreach(var lot in Lots){MapList(lot.Cells);lot.Entry=Map(lot.Entry);}
        foreach(var district in Districts){MapList(district.Cells);MapList(district.PublicCells);}
        foreach(var civic in Civics){MapList(civic.Footprint);MapList(civic.Building);MapList(civic.Parking);civic.Entry=Map(civic.Entry);}
        foreach(var stall in Stalls)stall.Cell=Map(stall.Cell);
        (Width,Height)=(Height,Width);
    }
    private bool Occupied(Cell cell)=>Roads.Contains(cell)||Spawns.Contains(cell)||EquipmentParking.Contains(cell)||Turns.Contains(cell)||Stalls.Any(s=>s.Cell==cell)||Civics.Any(c=>c.Footprint.Contains(cell))||Districts.Any(d=>d.Cells.Contains(cell))||Lots.Any(l=>l.Cells.Contains(cell));
    private Cell EntryNear(IReadOnlyList<Cell> cells)
    {
        foreach(var cell in cells)foreach(var next in new[]{new Cell(cell.X,cell.Z+1),new(cell.X,cell.Z-1),new(cell.X+1,cell.Z),new(cell.X-1,cell.Z)})if(Inside(next)&&Roads.Contains(next)&&!cells.Contains(next))return next;
        var origin=cells[0];var south=new Cell(origin.X,origin.Z+1);return Inside(south)?south:origin;
    }
    private static bool Adjacent(Cell a,Cell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z)==1;
    private bool Inside(Cell c)=>c.X>=0&&c.Z>=0&&c.X<Width&&c.Z<Height;
    private static string ResourceTitle(string item,string language)=>item switch
    {
        "wood"=>PlayerText.Local(language,"лес","wood"),
        "grain"=>PlayerText.Local(language,"зерно","grain"),
        "milk"=>PlayerText.Local(language,"молоко","milk"),
        "beet"=>PlayerText.Local(language,"свёкла","beet"),
        "stone"=>PlayerText.Local(language,"камень","stone"),
        "sand"=>PlayerText.Local(language,"песок","sand"),
        "iron_ore"=>PlayerText.Local(language,"железная руда","iron ore"),
        "copper_ore"=>PlayerText.Local(language,"медная руда","copper ore"),
        _=>item
    };
    private static string Deposit(string item)=>item switch{"wood"=>"леса","grain"=>"зерна","milk"=>"молока","beet"=>"свёклы","stone"=>"камня","sand"=>"песка","iron_ore"=>"железной руды","copper_ore"=>"медной руды",_=>item};
    private static string CivicTitle(string kind,string language)=>kind switch{"town_hall"=>PlayerText.Local(language,"ратуша","town hall"),"terminal"=>PlayerText.Local(language,"биржа","exchange"),"dealership"=>PlayerText.Local(language,"автосалон","dealership"),_=>kind};
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
    private string NextId(string prefix)
    {
        var used=Lots.Select(l=>l.Id).Concat(Districts.Select(d=>d.Id)).Concat(Civics.Select(c=>c.Id)).Concat(Stalls.Select(s=>s.Id)).ToHashSet(StringComparer.Ordinal);
        for(int n=1;n<1000;n++){string id=prefix+"_"+n.ToString("00");if(used.Add(id))return id;}
        throw new InvalidOperationException(prefix);
    }
    private List<Cell> BayCells()
    {
        int missing=2-EquipmentParking.Distinct().Count();if(missing<=0)return [];
        var dealer=Civics.FirstOrDefault(c=>c.Kind=="dealership");
        var blocked=Roads.Concat(Spawns).Concat(EquipmentParking).Concat(Turns).Concat(Lots.SelectMany(l=>l.Cells)).Concat(Districts.SelectMany(d=>d.Cells)).Concat(Civics.SelectMany(c=>c.Footprint)).Concat(Stalls.Select(s=>s.Cell)).ToHashSet();
        var found=new List<Cell>();
        foreach(var road in Roads.OrderBy(c=>dealer is null?0:Math.Abs(c.X-dealer.Entry.X)+Math.Abs(c.Z-dealer.Entry.Z)).ThenBy(c=>c.Z).ThenBy(c=>c.X))
            foreach(var next in new[]{new Cell(road.X+1,road.Z),new(road.X-1,road.Z),new(road.X,road.Z+1),new(road.X,road.Z-1)})
                if(Inside(next)&&!blocked.Contains(next)&&!found.Contains(next)){found.Add(next);blocked.Add(next);if(found.Count==missing)return found;}
        return found;
    }
    private void RenameDuplicates()
    {
        var used=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<Lots.Count;i++)if(!used.Add(Lots[i].Id)){string id=Fresh("lot",used);Lots[i]=new(id,Lots[i].Cells,Lots[i].Entry);used.Add(id);}
        for(int i=0;i<Districts.Count;i++)if(!used.Add(Districts[i].Id)){string id=Fresh(Districts[i].Item,used);Districts[i]=new(id,Districts[i].Item,Districts[i].Cells,Districts[i].PublicCells);used.Add(id);}
        for(int i=0;i<Civics.Count;i++)if(!used.Add(Civics[i].Id)){string id=Fresh(Civics[i].Kind,used);Civics[i]=new(id,Civics[i].Kind,Civics[i].Footprint,Civics[i].Entry,Civics[i].Parking,Civics[i].Building);used.Add(id);}
        for(int i=0;i<Stalls.Count;i++)if(!used.Add(Stalls[i].Id)){string id=Fresh("stall",used);Stalls[i]=new(id,Stalls[i].Cell);used.Add(id);}
        static string Fresh(string prefix,HashSet<string> used){for(int n=1;n<1000;n++){string id=prefix+"_"+n.ToString("00");if(!used.Contains(id))return id;}throw new InvalidOperationException(prefix);}
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
{public string Id{get;}=id;public string Kind{get;}=kind;public List<Cell> Footprint{get;}=footprint;public Cell Entry{get;set;}=entry;public List<Cell> Parking{get;}=parking;public List<Cell> Building{get;}=building;}
public sealed class DraftStall(string id,Cell cell){public string Id{get;}=id;public Cell Cell{get;set;}=cell;}
public readonly record struct MapNote(string Text,Cell? At,bool Warning,string? Fix=null);

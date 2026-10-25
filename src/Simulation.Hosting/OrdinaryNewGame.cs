using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record OrdinaryGameOptions(int NpcCount=3,string Difficulty="Medium",ulong? Seed=null,long HumanActorId=1)
{
    public static OrdinaryGameOptions Read(string json)
    {
        if(json.Length>8192)throw new ArgumentException("New-game options too large");
        using var d=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});
        if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected options object");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in d.RootElement.EnumerateObject())if(!seen.Add(p.Name) || p.Value.ValueKind==JsonValueKind.Null)throw new ArgumentException("Duplicate/null new-game option");
        var result=JsonSerializer.Deserialize<OrdinaryGameOptions>(json,new JsonSerializerOptions(SimulationJson.Options){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new ArgumentException("Empty options");
        result.Validate();return result;
    }
    public void Validate()
    {if(NpcCount is <3 or >10 || Difficulty is not ("Easy" or "Medium" or "Hard") || HumanActorId<=0)throw new ArgumentException("Ordinary game requires 3..10 NPCs, Easy/Medium/Hard, and a known human actor");}
}
public sealed record OrdinaryGameResolution(ScenarioDefinition Scenario,long HumanActorId,ImmutableArray<ControllerAssignment> Controllers);

/// <summary>Initial data preparation only. No world, concrete policies, HTTP, or runtime PRNG.</summary>
public static class OrdinaryNewGame
{
    public static OrdinaryGameResolution Resolve(ScenarioDefinition map,EconomyProfile economy,OrdinaryGameOptions options,IReadOnlyList<Cell>? spawnPool=null,VNextBalance? vnextBalance=null)
    {
        options.Validate();
        spawnPool??=map.Layout?.SpawnCells;
        if(map.Layout is not null && !VNextStorageProfile.MapCompatible(economy))throw new ArgumentException("Designed maps require the explicit A2 supported profile");
        if(!map.Participants.Any(p=>p.Id==options.HumanActorId))throw new ArgumentException("Unknown human actor");
        // A2's exact fixed-four projection is applied once, then only the named aggregates are scaled.
        var baseScenario=economy.Apply(map);int count=checked(options.NpcCount+1);ulong seed=options.Seed??map.Seed;
        if(options.HumanActorId>count)throw new ArgumentException("Human actor outside resolved roster");
        var navigation=new ScenarioNavigation(baseScenario);
        var origin=map.Participants.Single(p=>p.Id==options.HumanActorId).Position;
        var distances=navigation.DistancesFrom(origin);
        var forbidden=new HashSet<Cell>((baseScenario.Plots??[]).Where(p=>p.OwnerId.HasValue).Select(p=>p.Position));
        foreach(var node in baseScenario.ResourceNodes.Where(n=>n.Rules?.InitialOwner is not null))forbidden.Add(node.Position);
        foreach(var b in baseScenario.Buildings??[])foreach(var c in baseScenario.BuildingTypes!.Single(t=>t.Id==b.TypeId).Footprint)forbidden.Add(new(b.Position.X+c.X,b.Position.Z+c.Z));
        var pool=(spawnPool??map.Participants.Select(p=>p.Position).ToArray()).ToList();
        if(pool.Distinct().Count()!=pool.Count || pool.Any(c=>!distances.ContainsKey(c)||forbidden.Contains(c)))throw new ArgumentException("Invalid spawn pool");
        if(spawnPool is null)
            foreach(var c in distances.Keys.Where(c=>!forbidden.Contains(c)).OrderBy(c=>distances[c]).ThenBy(c=>c.Z).ThenBy(c=>c.X))
                if(!pool.Contains(c)) {pool.Add(c);if(pool.Count>=11)break;}
        if(pool.Count<count)throw new ArgumentException("Not enough reachable unique spawn cells");
        bool legacy=spawnPool is null && options.NpcCount==3 && seed==map.Seed && map.Participants.Length==4;
        int offset=legacy?0:(int)(StartupSeed(seed,map.Id)%(ulong)pool.Count);
        var inputs=baseScenario.ResourceNodes.Select(r=>r.ItemId).Distinct(StringComparer.Ordinal).ToArray();
        if(inputs.Length==0)throw new ArgumentException("Map has no resource input");
        var actors=Enumerable.Range(1,count).Select(id=>
        {
            var old=map.Participants.FirstOrDefault(p=>p.Id==id);
            string preference=id==options.HumanActorId?"":old is not null&&inputs.Contains(old.PublicPreferenceItem,StringComparer.Ordinal)?old.PublicPreferenceItem:inputs[(id-2+inputs.Length)%inputs.Length];
            return new ParticipantDefinition(id,economy.World.StartingCashMinor,pool[(id-1+offset)%pool.Count],economy.World.PersonalInventoryCapacity,preference);
        }).ToArray();
        var plots=(baseScenario.Plots??[]).ToList();
        if(plots.Any(p=>p.IsShopSite) && plots.Count(p=>p.IsShopSite)<count)
        {
            var used=plots.Select(p=>p.Position).Concat(baseScenario.ResourceNodes.Select(r=>r.Position)).Concat(baseScenario.CapitalDemand is {} d?[d.Terminal]:[]).ToHashSet();
            Cell center=plots.First(p=>p.IsShopSite).Position;long next=plots.Max(p=>p.Id)+1;
            foreach(var c in distances.Keys.Where(c=>!used.Contains(c)&&!forbidden.Contains(c)).OrderBy(c=>Math.Abs(c.X-center.X)+Math.Abs(c.Z-center.Z)).ThenBy(c=>c.Z).ThenBy(c=>c.X))
            {plots.Add(new(next++,c,economy.World.ShopSitePriceMinor,IsShopSite:true));if(plots.Count(p=>p.IsShopSite)>=count)break;}
            if(plots.Count(p=>p.IsShopSite)<count)throw new ArgumentException("Not enough market space");
        }
        bool vnext=economy.VNext is not null;
        if(vnext && (vnextBalance is null || vnextBalance.SourceSha256!=economy.VNext!.SourceSha256))throw new ArgumentException("A2 projection requires its verified full balance for new-game aggregates");
        var globals=vnextBalance?.EconomyGlobals;
        long population=vnext?checked(globals!.StartPopulationPerParticipant*count):Scale(baseScenario.Population,count);
        long? maximum=vnext?checked(globals!.MaxPopulationPerParticipant*count):baseScenario.Labor?.MaximumPopulation is {} max?Scale(max,count):null;
        long treasury=Scale(vnext?globals!.StartTreasuryMinorPer4Participants:baseScenario.StartingTreasuryMinor,count);
        var scenario=baseScenario with {Seed=seed,Participants=actors,Plots=baseScenario.Plots is null?null:plots.ToArray(),
            Population=population,Labor=(baseScenario.Labor??new()) with {MaximumPopulation=maximum},StartingTreasuryMinor=treasury,
            CapitalDemand=baseScenario.CapitalDemand is {} demand?demand with {BootstrapBaseBudgetMinor=Scale(demand.BootstrapBaseBudgetMinor,count)}:null,
            NewGame=new("ordinary-setup-1",options.HumanActorId,options.Difficulty,map.Id,VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(economy)),legacy?"legacy-default":"startup-rotation-1",
                vnext?checked(globals!.StartPopulationPerParticipant*4):baseScenario.Population,vnext?checked(globals!.MaxPopulationPerParticipant*4):baseScenario.Labor?.MaximumPopulation,vnext?globals!.StartTreasuryMinorPer4Participants:baseScenario.StartingTreasuryMinor,baseScenario.CapitalDemand?.BootstrapBaseBudgetMinor)};
        ScenarioValidation.Validate(scenario);
        return new(scenario,options.HumanActorId,[..actors.Where(p=>p.Id!=options.HumanActorId).Select(p=>new ControllerAssignment(p.Id,p.PublicPreferenceItem,options.Difficulty))]);
    }
    public static long Scale(long baseValue,int participants)
    {if(baseValue<0 || participants<1)throw new ArgumentException("Nonnegative base and positive roster required");return checked((long)(((Int128)baseValue*participants+3)/4));}
    private static ulong StartupSeed(ulong seed,string map)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(FormattableString.Invariant($"ordinary-startup-1:{map}:{seed}"))));
}

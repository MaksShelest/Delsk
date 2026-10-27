using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>C1 only, derived from the checked A2 projection and the typed full balance.</summary>
public static class VNextStorageProfile
{
    public const string ProfileId="vnext-c1-storage-1";
    public static bool MapCompatible(EconomyProfile p)=>p.VNext is not null && p.Id is VNextSupportedProfile.ProfileId or ProfileId or VNextWorkshopProfile.ProfileId or VNextDevelopmentProfile.ProfileId or VNextProductionProfile.ProfileId or VNextFleetProfile.ProfileId or VNextRoadProfile.ProfileId or VNextBillingProfile.ProfileId or VNextServiceProfile.ProfileId;
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        if(basis.Id!=VNextSupportedProfile.ProfileId || basis.VNext is not {} source || source.SourceSha256!=balance.SourceSha256)throw new ArgumentException("C1 requires the matching verified A2 basis");
        var levels=balance.Warehouses.OrderBy(w=>w.Level).Select(w=>new BuildingLevelDefinition(checked((int)w.Level),"",w.PriceMinor,w.Materials.Select(m=>new MaterialCost(m.Key,m.Value)).ToArray(),w.Capacity,0,0,0,ConstructionTicks:w.Ticks)).ToArray();
        var warehouse=new BuildingTypeDefinition("warehouse",BuildingKind.Warehouse,[new(0,0)],levels,[]);
        var rules=new StorageRules(balance.Rules.BareCellCapacity,balance.Rules.OverflowGraceTicks,balance.Rules.OverflowWarningsRemaining.Select(n=>checked((int)n)).ToArray());
        var p=basis with{Id=ProfileId,VNext=null,World=basis.World with{BuildingTypes=[..basis.World.BuildingTypes.Where(t=>t.Kind!=BuildingKind.Warehouse),warehouse],Rules=basis.World.Rules with{Storage=rules}}};
        return p with{VNext=source with{ProjectionVersion="storage-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"ground-storage","timed-warehouses","delivery-overflow"],
            Limitations=["C1 storage subset only; no C2 demolition or new industry/XP/fleet/services.","Warehouse II/III materials keep their real requirements. Full material production/import remains dependent on D; no free stock is injected."]}};
    }
}

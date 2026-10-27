using Simulation.Core;
using System.Collections.Immutable;

namespace Simulation.Hosting;

public static class VNextFleetProfile
{
    public const string ProfileId = "vnext-e1-fleet-1";
    public static EconomyProfile Create(EconomyProfile basis, VNextBalance balance)
    {
        var d3=VNextProductionProfile.Create(basis,balance);var source=d3.VNext!;
        var fleet=new FleetRules([..balance.Vehicles.Select(v=>new VehicleModel(v.Id,v.Name,checked((int)v.Speed),v.CapacityVolume,v.PriceMinor,v.HandlingMax,v.Id=="trakt_d1"))],
            [..balance.VehicleHandlingKits.Select(k=>new HandlingUpgrade(k.Id=="light"?1:2,k.Throughput,k.PriceMinor,[..k.Materials.Select(m=>new MaterialCost(m.Key,m.Value))],60,checked((int)k.RequiresStaff)))],
            [..balance.LoadingPlatforms.Select(p=>new HandlingUpgrade(checked((int)p.Level),p.Throughput,p.PriceMinor,[..p.Materials.Select(m=>new MaterialCost(m.Key,m.Value))],p.Ticks,checked((int)p.RequiresStaff),p.ServiceMinorPerActiveTick))],
            checked((int)balance.Rules.ShopUpdateTicks),checked((int)balance.Rules.ShopMinDisplayTicks),WageMinor:balance.Rules.WageMinor);
        var p=d3 with{Id=ProfileId,VNext=null,World=d3.World with{Rules=d3.World.Rules with{Fleet=fleet},Logistics=d3.World.Logistics with{WageMinor=balance.Rules.WageMinor}}};
        return p with{VNext=source with{ProjectionVersion="fleet-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"finite-vehicle-dealer","owned-fleet","shared-cargo-logistics","paid-handling-equipment"],
            Limitations=["E1 fleet and handling. Road construction, new movement assistant, invoices and third-party services are not included.","Existing roads are dirt (20); no road upgrades are silently applied.","Long-run balance and device acceptance remain separate."]}};
    }
}

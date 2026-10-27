using Simulation.Core;

namespace Simulation.Hosting;

public static class VNextRoadProfile
{
    public const string ProfileId="vnext-e2-roads-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var fleet=VNextFleetProfile.Create(basis,balance);var source=fleet.VNext!;
        var roads=new RoadRules([..balance.RoadProjects.Select(r=>new RoadStageRule(checked((int)r.Level),checked((int)balance.Rules.RoadSpeedCaps[(int)r.Level-1]),r.PriceMinor,checked((int)r.Ticks),[..r.Materials.Select(m=>new MaterialCost(m.Key,m.Value))]))],balance.Rules.WageMinor,
            checked((int)balance.Rules.RoadExitHoldTicks),checked((int)balance.Rules.RoadStopWarningTicks),checked((int)balance.Rules.RoadStopReleaseTicks));
        var p=fleet with{Id=ProfileId,VNext=null,World=fleet.World with{Rules=fleet.World.Rules with{Roads=roads}}};
        return p with{VNext=source with{ProjectionVersion="roads-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"sequential-road-construction","layer-contributions","physical-paver-bypass","shared-traffic-assistance"],
            Limitations=["E2 roads and traffic. E3 invoices/payments are not enabled.","Long-run traffic/ROI and device acceptance remain separate."]}};
    }
}

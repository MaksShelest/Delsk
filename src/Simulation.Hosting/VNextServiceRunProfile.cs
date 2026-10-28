using Simulation.Core;
namespace Simulation.Hosting;
public static class VNextServiceRunProfile
{
    public const string ProfileId="vnext-f2-delivery-runs-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var previous=VNextServiceProfile.Create(basis,balance);var source=previous.VNext!;
        var p=previous with{Id=ProfileId,VNext=null,World=previous.World with{Rules=previous.World.Rules with{ServiceExecution=new()},
            Market=previous.World.Market with{PickupTtlTicks=1200},Delivery=previous.World.Delivery with{ContractTtlTicks=2400}}};
        return p with{VNext=source with{ProjectionVersion="service-runs-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"physical-service-runs","per-receipt-service-settlement","paid-service-drivers","safe-service-returns"],
            Limitations=["Service ETA assumes clear roads and current handling. Existing goods contracts keep their signed deadlines.","Late service deductions use cumulative whole-minor rounding. Long-run balance and real-device acceptance remain separate."]}};
    }
}

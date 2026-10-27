using Simulation.Core;
namespace Simulation.Hosting;
public static class VNextServiceProfile
{
    public const string ProfileId="vnext-f1-delivery-offers-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var previous=VNextBillingProfile.Create(basis,balance);var b=balance.Rules;var source=previous.VNext!;
        var services=new ServiceRules(checked((int)b.DeliveryAcceptTicks),b.DeliveryMinFeeMinor,b.DeliveryFixedMinor,b.DeliveryBlockMinor,
            b.DeliveryFeeVolumeBlock,b.DeliveryDistanceBlock,12000,b.WageMinor,checked((int)b.DeliveryDeadlineMinBuffer),checked((int)b.DeliveryDeadlineBufferBp));
        var p=previous with{Id=ProfileId,VNext=null,World=previous.World with{Rules=previous.World.Rules with{Services=services}}};
        return p with{VNext=source with{ProjectionVersion="services-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"delivery-offers","multi-trip-service-quotes","service-reservations-and-acceptance"],
            Limitations=["F1 ends at acceptance. Pickup, trips and delivered-part settlement belong to F2.","Before pickup cancellation returns the full service escrow. ETA assumes clear roads and current handling resources."]}};
    }
}

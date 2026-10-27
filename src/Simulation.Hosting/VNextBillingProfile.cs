using System.Collections.Immutable;
using Simulation.Core;

namespace Simulation.Hosting;

public static class VNextBillingProfile
{
    public const string ProfileId="vnext-e3-accounts-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var road=VNextRoadProfile.Create(basis,balance);var source=road.VNext!;var b=balance.Rules;
        var billing=new BillingRules(checked((int)b.TransportInvoicePeriod),checked((int)b.InvoiceGrace),checked((int)b.PenaltyPeriod),
            checked((int)b.PenaltyBp),checked((int)b.CityPrivateRoadShareBp),b.RoadIncrementLightMinor)
            {ClassFactors=balance.Vehicles.ToImmutableSortedDictionary(v=>v.Id,v=>checked((int)v.ClassFactorBp),StringComparer.Ordinal)};
        var p=road with{Id=ProfileId,VNext=null,World=road.World with{Rules=road.World.Rules with{Billing=billing}}};
        return p with{VNext=source with{ProjectionVersion="billing-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"distance-road-accruals","principal-first-invoices","free-money-auto-pay","personal-account"],
            Limitations=["E3 road invoices only. Delivery services are not enabled.","Signed sub-minor rounding residues carry forward per payer and recipient. Long-run ROI and device acceptance remain separate."]}};
    }
}

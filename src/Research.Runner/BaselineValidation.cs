using Simulation.Core;
using Simulation.Hosting;

// Research assertions run outside the controller boundary, including command-only replay.
internal static class BaselineValidation
{
    public static void Check(GameSession session,ScenarioDefinition definition)
    {
        var w=session.Engine.Inspect();var events=session.Engine.EventsSince(0);
        void Require(bool condition,string detail){if(!condition)throw new InvalidOperationException("Baseline criterion: "+detail);}
        if(definition.Id=="baseline-economy-v2" && w.Tick>=1200)
        {
            foreach(long actor in new long[]{2,3,4})
            {
                Require(w.Enterprises.Any(e=>e.OwnerId.Value==actor && e.CompletedBatches>0),$"actor {actor} production");
                Require(w.Settlements.Any(s=>s.Seller.Value==actor && s.PaidMinor>0),$"actor {actor} trade");
            }
            Require(w.DeliveryContracts.Length>0 && w.DeliveryContracts.Where(c=>c.DeadlineTick<=w.Tick).All(c=>c.Status=="Fulfilled"),"due contracts");
            Require(events.Any(e=>e.Type=="WageChangeScheduled"),"wage management");
        }
        if(definition.Id.StartsWith("baseline-auction",StringComparison.Ordinal) && w.Tick>=180)
        {
            Require(events.Any(e=>e.Type=="MarketAuctionStarted") && events.Any(e=>e.Type=="MarketBidRaised"),"autonomous auction");
            Require(w.MarketAllocations.Any(a=>a.CollectedQuantity>0),"physical auction pickup");
        }
        if(definition.Id.StartsWith("baseline-politics",StringComparison.Ordinal) && w.Tick>=160)
        {
            var support=session.Engine.InspectSupport();
            Require(support.Proposals.Any(p=>p.Status==SupportProposalStatus.Rejected),"political refusal");
            Require(support.Commitments.Any(c=>c.Status==PoliticalCommitmentStatus.Executed),"signed and executed support");
        }
        if(definition.Id.StartsWith("baseline-logistics",StringComparison.Ordinal) && w.Tick>=360)
            foreach(long actor in new long[]{2,3,4})Require(w.Logistics.Any(l=>l.OwnerId.Value==actor && l.CompletedTrips>0),$"actor {actor} paid logistics");
    }
}

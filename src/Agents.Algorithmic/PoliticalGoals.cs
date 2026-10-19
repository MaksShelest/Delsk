using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private long VoteWeight(ControllerObservation o,ParticipantId actor)=>checked(o.Elections.Rules.BaseVote+o.LaborMarket.Where(g=>g.OwnerId==actor).Sum(g=>(long)g.Headcount));
    private long TotalVotes(ControllerObservation o)=>checked(o.Elections.Rules.BaseVote*o.Elections.Participants.Length+o.LaborMarket.Sum(g=>(long)g.Headcount));
    private long KnownVotes(ControllerObservation o,ParticipantId candidate)
    {
        bool sold=o.Support.Commitments.Any(c=>c.Seller==candidate && c.Election==o.Elections.NextElectionId && c.Candidate!=candidate);
        return checked((sold?0:VoteWeight(o,candidate))+o.Support.Commitments.Where(c=>c.Candidate==candidate && c.Election==o.Elections.NextElectionId).Sum(c=>VoteWeight(o,c.Seller)));
    }
    private bool AcceptableSupport(ControllerObservation o,SupportProposal p)
    {
        if(p.Seller!=Actor || p.Election!=o.Elections.NextElectionId || o.Support.Commitments.Any(c=>c.Seller==Actor && c.Election==p.Election))return false;
        long own=VoteWeight(o,Actor),total=TotalVotes(o);
        if(_state.Settings.OwnVictoryWeight>0 && ((Int128)2*KnownVotes(o,Actor)>total || (Int128)2*(KnownVotes(o,p.Candidate)+own)>total))return false;
        if(_state.Settings.HumanDefeatWeight>0 && p.Candidate.Value==_state.Settings.HumanActorId && (Int128)2*(KnownVotes(o,p.Candidate)+own)>total)return false;
        long trust=_state.Trust.FirstOrDefault(t=>t.ParticipantId==p.Candidate.Value)?.Value??0;
        long required=checked((long)((Int128)own*_state.Settings.SupportPricePerVoteMinor*Math.Max(10,_state.Settings.OwnVictoryWeight)*(200-trust)/20000));
        long value=p.Compensation.Sum(m=>checked(m.Quantity*BasePrice(o,m.ItemId)));
        var section=Inventory(o,o.Support.OwnSection);
        long volume=p.Compensation.Sum(m=>checked(m.Quantity*Volume(o,m.ItemId)));
        return value>=required && FreeVolume(section)>=volume;
    }
    private IEnumerable<Candidate> SupportResponses(ControllerObservation o)
    {
        if(!_state.Settings.PoliticsEnabled || o.Support.Rules is null)yield break;
        foreach(var p in o.Support.Proposals.Where(p=>p.Seller==Actor && p.Status==SupportProposalStatus.Proposed && p.Election==o.Elections.NextElectionId))
        {
            bool accept=CanTradeWith(o,p.Candidate) && AcceptableSupport(o,p);
            yield return One(accept?"AcceptSupport":"RejectSupport",accept?"CompensationAndRisk":"OwnVictoryOrPrice",850000,
                new(accept?ActionKind.AcceptSupport:ActionKind.RejectSupport,SupportProposalId:p.Id));
        }
    }
    private IEnumerable<Candidate> PoliticalCandidates(ControllerObservation o)
    {
        if(!_state.Settings.PoliticsEnabled || o.Support.Rules is not { } rules || o.Elections.NextElectionId is not { } election)yield break;
        foreach(var p in o.Support.Proposals.Where(p=>p.Election==election && p.Status==SupportProposalStatus.AcceptedAwaitingResources && !_state.FailedSupport.Contains(p.Id)))
        {
            if(o.Support.Commitments.Any(c=>c.Seller==p.Seller && c.Election==p.Election))continue;
            if(p.Reserved.SequenceEqual(p.Compensation))
            {
                var steps=new List<PlanStep>();Move(steps,o.Participant.Position,rules.TradingPoint);steps.Add(new(new(ActionKind.SignSupport,SupportProposalId:p.Id)));
                if(PlanTicks(o,steps)<=p.ExpiryTick-o.Tick)yield return new("SignSupport",p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),450000,steps.ToArray());
                continue;
            }
            if(p.Candidate!=Actor)continue;
            var carrier=Carrier(o);
            foreach(var term in p.Compensation)
            {
                long missing=term.Quantity-(p.Reserved.SingleOrDefault(r=>r.ItemId==term.ItemId)?.Quantity??0);if(missing<=0)continue;
                long carried=Free(carrier,term.ItemId);
                long q=Math.Min(missing,Math.Min(_state.Settings.BatchQuantity,checked(carried+FreeVolume(carrier)/Volume(o,term.ItemId))));if(q<=0)continue;
                var steps=new List<PlanStep>();Cell end=o.Participant.Position;
                if(carried<q){var fetch=Fetch(o,term.ItemId,q-carried,out end);if(fetch is null)continue;steps.AddRange(fetch);}
                Move(steps,end,rules.TradingPoint);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:p.Source,ItemId:term.ItemId,Quantity:q,SupportProposalId:p.Id)));
                if(PlanTicks(o,steps)+1<=p.ExpiryTick-o.Tick)yield return new("DeliverCompensation",p.Id+":"+term.ItemId,430000,steps.ToArray());
            }
        }
        if(HasObligations(o) || o.Account.AvailableMinor<_state.Settings.CashReserveMinor || _state.Settings.OwnVictoryWeight==0 ||
            o.Elections.NextVotingTick-o.Tick>_state.Settings.PoliticalHorizonTicks || (Int128)2*KnownVotes(o,Actor)>TotalVotes(o) ||
            o.Support.Proposals.Count(p=>p.Candidate==Actor && p.Election==election && p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)>=2)yield break;
        var raw=o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).Where(n=>Reachable(o,n.Position)).OrderBy(n=>Distance(o.Map,o.Participant.Position,n.Position)).ThenBy(n=>n.ItemId,StringComparer.Ordinal).FirstOrDefault();
        if(raw is null)yield break;
        foreach(var seller in o.Elections.Participants.Where(p=>p!=Actor && CanTradeWith(o,p)))
        {
            if(o.Negotiations.Any(n=>n.Current.Terms.Kind==TradeKind.Support && n.Current.Terms.Buyer==Actor && n.Current.Terms.Seller==seller && n.Current.Terms.Election==election) ||
                o.Support.Proposals.Any(p=>p.Candidate==Actor && p.Seller==seller && p.Election==election) ||
                o.Support.Commitments.Any(c=>c.Candidate==Actor && c.Seller==seller && c.Election==election))continue;
            long value=checked((long)((Int128)VoteWeight(o,seller)*_state.Settings.SupportPricePerVoteMinor*6/5));
            long q=checked((value-1)/BasePrice(o,raw.ItemId)+1);
            if(q*Volume(o,raw.ItemId)>rules.SectionCapacity)continue;
            var estimate=Fetch(o,raw.ItemId,Math.Min(q,_state.Settings.BatchQuantity),out var end);if(estimate is null)continue;
            Move(estimate,end,rules.TradingPoint);estimate.Add(new(new(ActionKind.Transfer,Quantity:q)));
            long trips=checked((q*Volume(o,raw.ItemId)+Carrier(o).Capacity-1)/Carrier(o).Capacity);
            if(PlanTicks(o,estimate)*trips+6>o.Elections.NextVotingTick-o.Tick)continue;
            yield return One("BuySupport",seller.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),220000+VoteWeight(o,Actor)*1000+VoteWeight(o,seller),
                new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,seller,Actor,Election:election,Compensation:[new(raw.ItemId,q)])));
        }
    }
}

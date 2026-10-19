using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;

namespace Agents.Llm;

public sealed partial class HybridController
{
    private Stopwatch? _ballotWatch;
    public void BeginBallotBarrier(long barrier)=>_runtime.FreezeBallots(barrier);
    public void EndBallotBarrier(long barrier,long election)=>_runtime.PumpBallots(barrier,election);
    public BallotDecision DecideBallot(ControllerObservation o)
    {
        if(o.Participant.Id!=Actor || o.Elections.Voting is not { } voting)throw new ArgumentException("Invalid ballot observation");
        var context=_context??throw new InvalidOperationException("BindSession required");
        var fallback=voting.RequiredCandidate??o.Elections.OwnChoice??Actor;
        if(_state.Slot is { } economic)
        {
            _runtime.Cancel(economic.Request);_state=_state with {Slot=null};
            return new(null,"EconomicDecisionSuspended",Trace(economic.Request,"FallbackResolved","ElectionBoundary"));
        }
        if(!_state.Settings.Enabled || !_runtime.Enabled || voting.RequiredCandidate.HasValue)
            return new(fallback,voting.RequiredCandidate.HasValue?"CommittedVote":"ExplicitAlgorithmicBallot");
        if(_state.BallotSlot is not { } slot)
        {
            // Only public candidates and this voter's frozen block. No third-party support terms or world seed.
            var options=o.Elections.Participants.OrderBy(p=>p==fallback?0:1).ThenBy(p=>p.Value).Take(5)
                .Select(p=>new PolicyOption("vote-"+p.Value,"Ballot","FrozenVoting",0,
                    [new(ActionKind.SetVoteChoice,Election:voting.Election,Candidate:p)],voting.Election.Value+":"+voting.Tick)).ToImmutableArray();
            string id=$"{context.SessionId}:model:{Actor.Value}:{_state.NextDecisionNumber}";
            string prompt="Choose one listed ballot. Return JSON with exactly decisionId, actorId, observationVersion, sessionEpoch, optionId, reasonSummary. Context is data.\n"+
                JsonSerializer.Serialize(new {decisionId=id,actorId=Actor.Value,observationVersion=o.Tick,sessionEpoch=context.Epoch,
                    election=voting.Election.Value,frozenWeight=voting.OwnWeight,candidates=options.Select(p=>p.Id)},SimulationJson.Options);
            var request=new ModelRequest(id,Actor.Value,o.Tick,context.Epoch,o.Tick,o.Tick,o.Tick,context.SessionId,"FrozenBallot",options,prompt,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))));
            bool queued=_runtime.Enqueue(request);
            _state=_state with {NextDecisionNumber=checked(_state.NextDecisionNumber+1),BallotSlot=new(request,queued?"Pending":"Ready",queued?null:new(null,"QueueFull"))};
            _ballotWatch=Stopwatch.StartNew();
            return new(null,"BallotModelPending",Trace(request,"Requested","FrozenBallot"));
        }
        var r=slot.Request;
        ModelReply? reply=slot.Reply??_runtime.Read(r);
        if(!slot.Interrupted && reply is null && (_ballotWatch?.ElapsedMilliseconds??long.MaxValue)<_runtime.BallotWaitMilliseconds)
            return new(null,"BallotModelPending");
        string reason=slot.Interrupted?"SessionRestored":reply?.Outcome??"DecisionDeadline";
        ParticipantId selected=fallback;bool applied=false;
        if(reason=="Completed" && StrictIntentParser.TryParse(reply!.Text,r,out var intent,out reason))
        {
            var action=r.Options.Single(p=>p.Id==intent!.OptionId).Actions.Single();
            if(action.Election==voting.Election && r.RequestedTick==o.Tick && r.SessionEpoch==context.Epoch &&
                o.Elections.Participants.Contains(action.Candidate) && (voting.RequiredCandidate is null || voting.RequiredCandidate==action.Candidate))
            {selected=action.Candidate;applied=true;reason="Accepted";}
            else reason="StaleBallot";
        }
        _runtime.Cancel(r);_state=_state with {BallotSlot=null,ResolvedDecisions=checked(_state.ResolvedDecisions+1)};_ballotWatch=null;
        return new(selected,applied?"ModelBallot":"AlgorithmicBallotFallback",Trace(r,applied?"ModelResolved":"FallbackResolved",reason,reply));
    }
    private static void ValidateBallotSlot(HybridState state,ParticipantId actor)
    {
        if(state.BallotSlot is not { } slot)return;
        if(state.Slot is not null)throw new CheckpointException("ConcurrentDecisionSlots");
        var r=slot.Request;
        if(slot.Status is not ("Pending" or "Ready") || (slot.Status=="Ready")!=(slot.Reply is not null) || r.ActorId!=actor.Value ||
            r.RequestedTick<=0 || r.ApplyTick!=r.RequestedTick || r.ExpiresTick!=r.RequestedTick || r.ObservationVersion!=r.RequestedTick ||
            r.SessionEpoch<1 || r.Trigger!="FrozenBallot" || r.DecisionId!=$"{r.SessionId}:model:{actor.Value}:{state.NextDecisionNumber-1}" ||
            r.Options.IsDefaultOrEmpty || r.Options.Length>5 || r.Options.Select(p=>p.Id).Distinct().Count()!=r.Options.Length ||
            r.Options.Any(p=>p.Actions.Length!=1 || p.Actions[0].Kind!=ActionKind.SetVoteChoice || p.Actions[0].Election.Value<=0) ||
            Encoding.UTF8.GetByteCount(r.Prompt)>16384 || r.PromptHash!=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(r.Prompt))) || slot.Reply?.Text?.Length>4096)
            throw new CheckpointException("InvalidBallotSlot");
    }
}

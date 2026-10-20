using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PresentationEffectsTests
{
    private static PublicSimulationEvent Event(long sequence,string type,long actor=2)=>new(sequence,1,4,type,new(actor),0,"",0,0,"secret-not-for-dialogue");
    [Fact]
    public void BurstHasTwoSpeakersTwoSoundsAndPriorityPreemptionWithoutDuplicatePlayback()
    {
        var effects=new PresentationEffects();effects.Advance(.1,false);
        effects.Consume(Enumerable.Range(1,1000).Select(i=>Event(i,i%2==0?"HarvestStarted":"HarvestCompleted",2+i%3)),1,new());
        Assert.InRange(effects.Speech.Count,1,2);Assert.InRange(effects.TakeSounds().Length,0,2);
        effects.Consume([Event(1001,"BuildingConstructed",2)],1,new());Assert.Contains(effects.Speech,c=>c.Actor==2 && c.Topic==SpeechTopic.Build);
        effects.Consume([Event(1001,"BuildingConstructed",2)],1,new());Assert.Empty(effects.TakeSounds());
        Assert.All(effects.Speech,c=>Assert.DoesNotContain("secret",PresentationEffects.Text(c,"en")));
    }
    [Fact]
    public void PauseCoverAndCooldownDropOldEffectsInsteadOfBurstingOnResume()
    {
        var effects=new PresentationEffects();effects.Advance(.1,false);effects.Consume([Event(1,"HarvestStarted")],1,new());Assert.Single(effects.Speech);
        for(int i=0;i<20;i++)effects.Advance(.2,false);Assert.Empty(effects.Speech);
        effects.Consume([Event(2,"HarvestStarted")],1,new());Assert.Empty(effects.Speech);
        effects.Advance(100,true);effects.Consume([Event(3,"BuildingConstructed")],1,new());Assert.Empty(effects.Speech);Assert.Empty(effects.TakeSounds());
        effects.Advance(.1,false);effects.Consume([Event(3,"BuildingConstructed")],1,new());Assert.Empty(effects.Speech);
        for(int i=0;i<20;i++)effects.Advance(.2,false);effects.Consume([Event(4,"HarvestStarted")],1,new());Assert.Single(effects.Speech);
        effects.Consume([],1,new(Speech:false));Assert.Empty(effects.Speech);
    }
    [Fact]
    public void OnlyPublicTransitionsProduceSpeechAndSecretTermsNeverBecomeText()
    {
        var d=SupportTests.Scenario();var s=Fixtures.Session(d);long election=s.Engine.Inspect().Elections.NextElectionId!.Value.Value;
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,new(ActionKind.ProposeSupport,SupportSeller:new(3),Election:new(election),Compensation:[new("wood",4)]),2));
        var effects=new PresentationEffects();effects.Consume(s.Engine.PublicEventsSince(0),1,new());Assert.Empty(effects.Speech);
        effects.Consume([Event(100,"SupportSigned"),Event(101,"NegotiationProposed")],1,new());Assert.Empty(effects.Speech);
        effects.Consume([Event(102,"HarvestStarted")],1,new());var cue=Assert.Single(effects.Speech);
        Assert.NotEqual(PresentationEffects.Text(cue,"ru"),PresentationEffects.Text(cue,"en"));
        Assert.DoesNotContain("secret",JsonSerializer.Serialize(effects.Speech));
    }
    [Fact]
    public void VacancyUsesPublicCurrentHeadcountNotPrivatePlansOrPendingWage()
    {
        var effects=new PresentationEffects();var settings=new FeedbackPreferences();var group=new PublicEmploymentView(new(42),new(2),EmploymentRole.Production,2,2,25);
        effects.ObserveLabor([group],1,settings);Assert.Empty(effects.Speech);
        effects.ObserveLabor([group with {Headcount=1}],1,settings);Assert.Equal(SpeechTopic.Vacancy,Assert.Single(effects.Speech).Topic);
        effects.ObserveLabor([group with {Headcount=1}],1,settings);Assert.Single(effects.Speech);
    }
    [Fact]
    public void PresentationHasNoInfluenceOnWorldSaveLoadReplayOrSeededOutcome()
    {
        var d=Fixtures.Cycle();var s=Fixtures.Session(d);Fixtures.Command(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:4),2);
        var effects=new PresentationEffects();long cursor=0;
        for(int i=0;i<35;i++)
        {
            string hash=s.Engine.ComputeStateHash();var events=s.Engine.PublicEventsSince(cursor);if(events.Length>0)cursor=events[^1].Sequence;
            effects.Advance(.1,i%5==0);effects.Consume(events,1,new());effects.ObserveLabor(s.Engine.Observe(new(1)).LaborMarket,1,new());effects.TakeSounds();Presentation.Read(d,s.Engine,1);
            Assert.Equal(hash,s.Engine.ComputeStateHash());s.Step();
        }
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));var replay=GameSession.Replay(d,s.CommandTape,s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        var other=new PresentationEffects();other.Reset(cursor);other.Consume(s.Engine.PublicEventsSince(0),1,new());Assert.Empty(other.Speech);
    }
    [Theory][InlineData(-1)][InlineData(101)]
    public void InvalidPreferencesCannotBecomeUnboundedVolume(int volume)=>Assert.Throws<ArgumentException>(()=>new FeedbackPreferences(EffectsVolume:volume).Validate());
}

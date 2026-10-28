using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcRecoveryG1Tests
{
    [Fact]public void NoCashRecoversByActualHarvestAndSaleWithoutGrants()
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("small_river"),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        d=d with{Participants=[..d.Participants.Select(p=>p.Id==2?p with{CashMinor=0}:p)]};var s=new GameSession(new(d));
        var c=NpcEconomyG1Tests.Controller(2,d.Participants[1].PublicPreferenceItem);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Observe(new(2)).Account.BalanceMinor>0,1200);
        Assert.Contains(s.CommandTape,r=>r.Command.ActorId.Value==2&&r.Command.Action.Kind==ActionKind.Harvest);Assert.Contains(s.Engine.Ledger(),r=>r.Reason=="CapitalSettlement");
        Assert.DoesNotContain(s.Engine.EventsSince(0),e=>e.Type.Contains("God"));
    }
    [Fact]public void PaidWorkshopIsOutfittedAndRunsWithConservedStaff()
    {
        var d=WorkshopD1Tests.Definition(ticks:10) with{CapitalOffers=[new(220,"plank",500,3000,new(15,2)),new(221,"bread",500,3000,new(15,2))]};d=d with{ResourceNodes=[..d.ResourceNodes.Where(n=>n.ItemId!="plank")]};var s=new GameSession(new(d));WorkshopD1Tests.Supply(s,50,0);var e=WorkshopD1Tests.Enterprise(s);
        WorkshopD1Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true));s.AdvanceTo(s.Engine.Tick+35);
        StorageC1Tests.Move(s,new(0,0));StorageC1Tests.Step(s,new(ActionKind.Transfer,Source:e.OutputId,Destination:StorageC1Tests.Carrier(s),ItemId:"plank",Quantity:6));StorageC1Tests.Idle(s);StorageC1Tests.Put(s,e.InputId,"plank",6);StorageC1Tests.Harvest(s,"steel",1);StorageC1Tests.Put(s,e.InputId,"steel",1);
        var c=new AlgorithmicController(new(1,"wood",Difficulty:NpcDifficulty.Medium,CoherentEconomy:true,FutureContractsEnabled:false),42,[1,2]);NpcEconomyG1Tests.Drive(s,c,()=>WorkshopD1Tests.Lines(s).Length==2&&WorkshopD1Tests.Lines(s).All(w=>w.CompletedBatches>0),1500);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.OutfitWorkshop);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.AssignWorkshopWorkers);
        Assert.Equal(s.Engine.Inspect().Population,s.Engine.Inspect().Unemployed+s.Engine.Inspect().EmploymentGroups.Sum(g=>g.Headcount));Assert.Contains(s.Engine.Ledger(),r=>r.Reason=="WorkshopOutfit");
    }
    [Fact]public void PrivateExtractorIsPurchasedAndActuallyWorksWithFinitePaidCrew()
    {
        var d=Fixtures.Cycle(cash:100000) with{Population=4,ResourceNodes=[new("wood",new(0,0),new(false,Capacity:100,RegenerationQuantity:2,RegenerationTicks:10,InitialOwner:1))],EquipmentTypes=[new("g1-wood", "wood",[new(1,1000,4,2,40,1,1,5)])],Labor=new(StepTicks:2,GrowthPerPeriod:0)};
        var s=new GameSession(new(d));Fixtures.Supply(s,20);Fixtures.UntilIdle(s);var c=new AlgorithmicController(new(1,"wood",Difficulty:NpcDifficulty.Medium,CoherentEconomy:true,FutureContractsEnabled:false),42,[1,2]);
        NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Inspect().Equipment.Any(e=>e.Mode==EquipmentMode.Automatic&&e.CompletedCycles>0),600);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.BuyEquipment);Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SetEquipmentMode);Assert.Equal(4,s.Engine.Inspect().Unemployed+s.Engine.Inspect().EmploymentGroups.Sum(g=>g.Headcount));
        Assert.DoesNotContain(s.Engine.EventsSince(0),e=>e.Type.Contains("God"));
    }
    [Fact]public void FullOutputIsPhysicallySoldAndTheNextBatchCanComplete()
    {
        var s=Fixtures.Session(Fixtures.Cycle(outputCapacity:1));Fixtures.Supply(s,20);Fixtures.UntilIdle(s);
        var c=new AlgorithmicController(new(1,"wood",Difficulty:NpcDifficulty.Medium,CoherentEconomy:true,FutureContractsEnabled:false),42,[1,2]);
        NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Inspect().Settlements.Any(x=>x.PaidMinor>0)&&s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches>=2,600);
        Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SellAvailableToCapital);s.Engine.ValidateInvariants();
    }
    [Fact]public void ActualBusyRejectionIsRememberedAndAnotherEarnedSaleRecovers()
    {
        var s=Fixtures.Session();var c=NpcEconomyG1Tests.Controller(2);var decision=c.Decide(s.Engine.ObserveController(new(2),true),[]);var action=Assert.Single(decision.Actions);
        string own=s.Send(new(2),new(ActionKind.Harvest,ItemId:"wood",Quantity:20));s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(own)!.Status);
        string id=s.Send(new(2),action);c.CommandSubmitted(id);s.Step();var rejection=s.Engine.Result(id)!;Assert.Equal(ResultStatus.Rejected,rejection.Status);
        c.Decide(s.Engine.ObserveController(new(2),true),[rejection]);Assert.Contains("retryTick",c.Capture().StateJson);NpcEconomyG1Tests.SameCheckpoint(c);
        NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.Observe(new(2)).Account.BalanceMinor>0,600);
        Assert.Single(s.CommandTape,r=>s.Engine.Result(r.Command.CommandId)?.Status==ResultStatus.Rejected);Assert.Contains(s.Engine.Ledger(),r=>r.Reason=="CapitalSettlement");
    }
    [Fact]public void HybridOffKeepsEconomicProjectionAndVersionFourMemoryAcrossLoad()
    {
        using var runtime=new ModelRuntime(new TestModelAdapter((_,_)=>throw new Exception("Off must not call model")),[2]);
        var hybrid=new HybridController(NpcEconomyG1Tests.Controller(2),runtime,new(Enabled:false));Assert.True(hybrid.UsesEconomicPlanning);
        var s=new GameSession(new(ServiceF2Tests.Definition()));s.AttachControllers([hybrid]);s.AdvanceTo(15);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.RestoreControllers(c=>HybridController.Restore(c,runtime));
        for(int i=0;i<10;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());}
        Assert.Contains("economyMemory",hybrid.Capture().StateJson);
    }
}

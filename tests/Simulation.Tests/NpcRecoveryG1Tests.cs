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
    [Fact]public void HybridOffKeepsEconomicProjectionAndVersionFourMemoryAcrossLoad()
    {
        using var runtime=new ModelRuntime(new TestModelAdapter((_,_)=>throw new Exception("Off must not call model")),[2]);
        var hybrid=new HybridController(NpcEconomyG1Tests.Controller(2),runtime,new(Enabled:false));Assert.True(hybrid.UsesEconomicPlanning);
        var s=new GameSession(new(ServiceF2Tests.Definition()));s.AttachControllers([hybrid]);s.AdvanceTo(15);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.RestoreControllers(c=>HybridController.Restore(c,runtime));
        for(int i=0;i<10;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());}
        Assert.Contains("economyMemory",hybrid.Capture().StateJson);
    }
}

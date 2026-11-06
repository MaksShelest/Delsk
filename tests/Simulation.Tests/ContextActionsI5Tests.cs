using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ContextActionsI5Tests
{
    private static readonly ParticipantId Human=new(1);
    private static ContextChoice Choice(string key,ActionKind kind=ActionKind.Transfer,int x=0)=>new(key,key,new(kind,Target:new(x,0)));
    [Fact] public void RefreshKeepsOpenMenuSelectionAndUsesKeysAfterReordering()
    {
        var state=new ContextSelection();var choices=Enumerable.Range(0,8).Select(n=>Choice("row:"+n)).ToArray();
        state.Update("same object",choices);state.Expanded=true;state.Select("row:7");
        state.Update("same object",choices.Reverse().Select(c=>c with{Label="new money/progress "+c.Label}).ToArray());
        Assert.True(state.Expanded);Assert.Equal("row:7",state.SelectedKey);
        Assert.True(state.TryChoose(choices[7],out var selected));Assert.Equal("row:7",selected!.Key);
    }
    [Fact] public void ScopeChangeClosesPanelAndRemovesTheOldTarget()
    {
        var state=new ContextSelection();state.Update("cell 1",[Choice("old")]);state.Expanded=true;
        state.Update("cell 2",[Choice("new")]);Assert.False(state.Expanded);Assert.Equal("new",state.SelectedKey);
        Assert.False(state.TryChoose(Choice("old"),out _));
    }
    [Fact] public void SameKeyWithChangedCommandCannotExecuteTheOldSnapshot()
    {
        var state=new ContextSelection();var old=Choice("buy",ActionKind.BuyPlot,1);
        state.Update("scope",[old]);state.Update("scope",[old with{Command=old.Command! with{Target=new(2,0)}}]);
        Assert.False(state.TryChoose(old,out _));Assert.False(state.Pending);
    }
    [Fact] public void OnePendingCommandBlocksRepeatedTransactionsButAllowsDeliberateNavigation()
    {
        var state=new ContextSelection();var buy=Choice("buy",ActionKind.BuyVehicle);var link=new ContextChoice("section:fleet","Fleet",null);
        state.Update("scope",[buy,link]);Assert.True(state.TryChoose(buy,out _));Assert.True(state.Pending);
        Assert.False(state.TryChoose(buy,out _));Assert.True(state.TryChoose(link,out _));
        state.Pending=false;Assert.True(state.TryChoose(buy,out _));
    }
    [Fact] public void DisabledSelectionIsNeverSubmitted()
    {
        var state=new ContextSelection();var disabled=Choice("buy") with{Enabled=false,Details="Not enough money"};
        state.Update("scope",[disabled]);Assert.False(state.TryChoose(disabled,out _));Assert.False(state.Pending);
    }
    [Fact] public void PrimaryPriorityIsSemanticAndDoesNotDependOnEnumOrInsertionOrder()
    {
        var state=new ContextSelection();var choices=new[]{new ContextChoice("section:fleet","Fleet",null),Choice("build",ActionKind.Build),Choice("stop",ActionKind.StopHarvest),Choice("board",ActionKind.BoardVehicle),Choice("harvest",ActionKind.StartHarvesting)};
        state.Update("scope",choices.OrderBy(ContextActions.Priority).ThenBy(c=>c.Key).ToArray());
        Assert.Equal(new[]{"stop","board","harvest"},state.PrimaryKeys());
        Assert.Equal("resources",ContextActions.Group(choices[2]));
    }
    [Theory][InlineData("ru")][InlineData("en")]
    public void OrdinaryContextContainsOnlyLocalChoicesWithoutChangingWorldOrTape(string language)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("compact_valley"),MobileEquipmentI4Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));string before=s.Engine.ComputeStateHash();int schema=s.Engine.ActiveCheckpointSchemaVersion;
        var choices=ContextActions.Read(s.Engine,Human,d,language);var state=new ContextSelection();
        state.Update(ContextActions.Scope(PlayerDesk.Read(s.Engine,Human)),choices);state.Expanded=true;
        for(int n=0;n<3;n++)ContextActions.Read(s.Engine,Human,d,language);
        Assert.DoesNotContain(choices,c=>c.Key.StartsWith("section:",StringComparison.Ordinal));
        Assert.All(choices.Where(c=>c.Command is not null&&c.Enabled),c=>Assert.True(s.Engine.AccessFor(Human,c.Command!).Allowed));
        Assert.Equal(choices.Length,choices.Select(c=>c.Key).Distinct().Count());Assert.Equal(before,s.Engine.ComputeStateHash());Assert.Empty(s.CommandTape);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(before,copy.Engine.ComputeStateHash());Assert.Equal(schema,copy.Engine.ActiveCheckpointSchemaVersion);
    }
    [Fact] public void LanguagesShareStableKeysAndEveryVisibleGroupHasALocalizedName()
    {
        var d=MobileEquipmentI4Tests.Prepared();var e=new SimulationEngine(d);
        var ru=ContextActions.Read(e,Human,d,"ru");var en=ContextActions.Read(e,Human,d,"en");
        Assert.Equal(ru.Select(c=>c.Key),en.Select(c=>c.Key));
        foreach(var group in ru.Select(ContextActions.Group).Distinct())
        {Assert.NotEqual(group,ContextActions.GroupName(group,"ru"));Assert.NotEqual(group,ContextActions.GroupName(group,"en"));}
    }
    [Fact] public void InsufficientCashHasASpecificReasonRatherThanAnEmptyDisabledAction()
    {
        using var stream=typeof(ContextActionsI5Tests).Assembly.GetManifestResourceStream("Fixture.baseline-economy.json")!;
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
        d=d with{Participants=d.Participants.Select(a=>a.Id==1?a with{CashMinor=0,Position=new(4,0)}:a).ToArray()};
        var e=new SimulationEngine(d);var buy=Assert.Single(ContextActions.Read(e,Human,d,"en"),c=>c.Key=="buy:1");
        Assert.False(buy.Enabled);Assert.Contains("money",buy.Details,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void MobileEquipmentEntriesShareCorePermissionsForBothParticipants()
    {
        var d=MobileEquipmentI4Tests.Prepared();var s=new GameSession(new(d));var vehicle=MobileEquipmentI4Tests.Buy(s);
        FleetE1Tests.Move(s,StorageC1Tests.Inventory(s,vehicle.CargoInventory).Position);
        var action=new GameAction(ActionKind.PlaceEquipment,Equipment:new(vehicle.Id));
        Assert.False(s.Engine.AccessFor(Human,action).Allowed);
        var place=new ContextChoice("machine:place:"+vehicle.Id,"Place",action);
        Assert.DoesNotContain(ContextActions.Read(s.Engine,Human,d,"en"),c=>c.Key==place.Key);
        Assert.DoesNotContain(ContextActions.Read(s.Engine,new(2),d,"en"),c=>c.Key==place.Key);
        Assert.False(s.Engine.AccessFor(new(2),place.Command!).Allowed);
        string id=s.Send(new(2),PlayerDesk.Check(place.Command!));s.Step();Assert.Equal(ResultStatus.Rejected,s.Engine.Result(id)!.Status);
        Assert.Equal(PlayerText.Reason("en",s.Engine.Result(id)!.ReasonCode),ContextActions.ResultText("en",s.Engine.Result(id)));
    }
    [Fact] public void RoadWorkRemainsThePrimaryActionForTheDrivenPaver()
    {
        var choices=new[]{Choice("vehicle:1",ActionKind.LeaveVehicle),new ContextChoice("road-work:1","Road work",null)}.OrderBy(ContextActions.Priority).ToArray();
        Assert.Equal("road-work:1",choices[0].Key);
    }
    [Theory][InlineData("ru")][InlineData("en")]
    public void DealerOffersUseRealPricesAndCountOnlyExecutableLocalActions(string language)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("compact_valley"),MobileEquipmentI4Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var dealer=d.Layout!.CivicObjects.Single(c=>c.Kind=="dealership").Entry;
        d=d with{Participants=d.Participants.Select(p=>p.Id==1?p with{Position=dealer,CashMinor=0}:p).ToArray()};
        var e=new SimulationEngine(d);var choices=ContextActions.Read(e,Human,d,language);var fleet=e.FleetOf(Human)!;
        var buys=choices.Where(c=>c.Command?.Kind==ActionKind.BuyVehicle).ToArray();Assert.InRange(buys.Length,1,3);
        foreach(var offer in buys)
        {
            var listing=fleet.Stock.Single(s=>s.Id==offer.Command!.VehicleId);var model=fleet.Models.Single(m=>m.Id==listing.ModelId);
            Assert.Contains(PlayerText.Money(model.PriceMinor),offer.Label);Assert.False(offer.Enabled);Assert.Equal(PlayerText.Reason(language,"NoMoney"),offer.Details);
            Assert.True(e.AccessFor(Human,offer.Command!).Allowed);
        }
        Assert.DoesNotContain(choices,c=>c.Key.StartsWith("section:",StringComparison.Ordinal));
        Assert.True(choices.Count(c=>c.Enabled)<choices.Length);
    }
    [Fact] public void UserCatalogDoesNotReplaceHistoricalScenarioResolution()
    {
        Assert.Equal(new[]{PlayMaps.CompactValley,PlayMaps.Second},PlayMaps.UserCatalog);
        Assert.NotEqual(PlayMaps.Second,PlayMaps.FourQuarters);
        Assert.Contains(PlayMaps.First,PlayMaps.All);Assert.Contains(PlayMaps.FourQuarters,PlayMaps.All);
        Assert.Equal(PlayMaps.Second,PlayMaps.KeyForScenario(PlayMaps.SecondScenarioId));
        Assert.Equal(PlayMaps.Second,PlayMaps.KeyForScenario(PlayMaps.CurrentSecondScenarioId));
        Assert.Equal(PlayMaps.First,PlayMaps.KeyForScenario(PlayMaps.FirstScenarioId));
    }
    [Fact] public void TheAvailabilityMatrixAccountsForEveryKindIncludingTheDedicatedInputPath()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"docs/verification/context-i5/action-availability.csv")))root=root.Parent;
        Assert.NotNull(root);var rows=File.ReadAllLines(Path.Combine(root!.FullName,"docs/verification/context-i5/action-availability.csv")).Skip(1).Select(s=>s.Split(',')[0]).Order().ToArray();
        Assert.Equal(Enum.GetNames<ActionKind>().Order(),rows);Assert.DoesNotContain(ActionKind.SetTravelHeading,PlayerDesk.Allowed);
    }
    [Fact] public void PhysicalTravelChangesScopeAndTheAvailableLocalActions()
    {
        var d=MobileEquipmentI4Tests.Prepared();var s=new GameSession(new(d));var before=ContextActions.Scope(PlayerDesk.Read(s.Engine,Human));
        var target=d.ResourceNodes.First(n=>n.Rules!.Public).Position;FleetE1Tests.Move(s,target);
        Assert.NotEqual(before,ContextActions.Scope(PlayerDesk.Read(s.Engine,Human)));
        Assert.Contains(ContextActions.Read(s.Engine,Human,d,"en"),c=>c.Command?.Kind==ActionKind.StartHarvesting);
    }
}

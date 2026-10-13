using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class LandAndContentTests
{
    [Fact]
    public void ReservedLandDoesNotPermitConstruction()
    {
        var s = LandContentFixtures.Session(); LandContentFixtures.Harvest(s, "wood", 10); LandContentFixtures.Move(s, new(8, 4));
        Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(5)));
        Assert.Equal("LandNotOwned", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(8, 4), BuildingTypeId: "bakery")));
        Assert.Equal(10, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        Assert.Equal(199000, s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void ManagementCannotPurchaseBeforeArrivalPhase_ButCanOnTheFollowingTick()
    {
        var cfg = LandContentFixtures.Scenario();
        cfg = cfg with { Participants = cfg.Participants.Select(p => p.Id == 1 ? p with { Position = new(7, 0) } : p).ToArray() };
        var s = LandContentFixtures.Session(cfg);
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(8, 0)));
        Assert.Equal("ParticipantBusy", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3))));
        Assert.Equal(new Cell(8, 0), s.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3))));
    }
    [Fact]
    public void ReservationIsAdditionalTenPercent_AndPurchasePaysFullPrice()
    {
        var s = LandContentFixtures.Session(); LandContentFixtures.Move(s, new(8, 0));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.ReservePlot, Plot: new(3))));
        Assert.Equal(199000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(s.Engine.Tick + 120, s.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).ReservationUntilTick);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3))));
        Assert.Equal(189000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(11000, s.Engine.Inspect().Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        var plot = s.Engine.Inspect().Plots.Single(p => p.Id.Value == 3);
        Assert.Equal(new ParticipantId(1), plot.OwnerId); Assert.Null(plot.ReservedBy); Assert.Null(plot.ReservationUntilTick);
    }
    [Fact]
    public void OwnerCanPurchaseAtInclusiveExpiry_ForeignBuyerCanOnlyPurchaseNextTick()
    {
        foreach (bool ownerBuys in new[] { true, false })
        {
            var cfg = LandContentFixtures.Scenario();
            cfg = cfg with { LandReservationTicks = 2, Participants = cfg.Participants.Select(p => p with { Position = new(8, 0) }).ToArray() };
            var s = LandContentFixtures.Session(cfg);
            Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(3))); s.Step();
            Assert.NotNull(s.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).ReservedBy);
            var result = LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3)), ownerBuys ? 1 : 2);
            Assert.Equal(3, s.Engine.Tick);
            if (ownerBuys)
            {
                Assert.Equal("Accepted", result);
                Assert.DoesNotContain(s.Engine.EventsSince(0), e => e.Type == "PlotReservationExpired");
            }
            else
            {
                Assert.Equal("PlotReserved", result);
                Assert.Equal(50000, s.Engine.Observe(new(2)).Account.BalanceMinor);
                Assert.Equal(7, Assert.Single(s.Engine.EventsSince(0), e => e.Type == "PlotReservationExpired").Phase);
                Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3)), 2));
                Assert.Equal(4, s.Engine.Tick);
            }
        }
    }
    [Fact]
    public void CancellationDoesNotRefund_OrPermitForeignCancellation()
    {
        var s = LandContentFixtures.Session(); LandContentFixtures.Move(s, new(8, 0));
        Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(3)));
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, new(ActionKind.CancelPlotReservation, Plot: new(3)), 2));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.CancelPlotReservation, Plot: new(3))));
        Assert.Equal(199000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Single(s.Engine.Ledger(), x => x.Reason == "PlotReservation");
    }
    [Fact]
    public void DuplicateReservationAndPurchaseDoNotChargeTwice_OrRenew()
    {
        var cfg = LandContentFixtures.Scenario();
        cfg = cfg with { Participants = cfg.Participants.Select(p => p.Id == 1 ? p with { Position = new(8, 0) } : p).ToArray() };
        var s = LandContentFixtures.Session(cfg);
        var c = new CommandEnvelope("reserve-once", new(1), 1, 0, 1, 20, new(ActionKind.ReservePlot, Plot: new(3)));
        s.Submit(new(1), c); s.Submit(new(1), c); s.Step(); s.Submit(new(1), c);
        Assert.Equal("PlotReserved", LandContentFixtures.Reason(s, new(ActionKind.ReservePlot, Plot: new(3))));
        Assert.Equal(121, s.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).ReservationUntilTick);
        Fixtures.Command(s, new(ActionKind.BuyPlot, Plot: new(3)));
        Assert.Equal("PlotOwned", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3))));
        Assert.Equal(189000, s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void PresenceCorridorsAssignedShopsAndReservationLimitAreEnforced()
    {
        var s = LandContentFixtures.Session(LandContentFixtures.Scenario() with { MaxActivePlotReservations = 1 });
        Assert.Equal("NotAtLocation", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(3))));
        Assert.Equal("ProtectedCorridor", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(7))));
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, new(ActionKind.BuyPlot, Plot: new(6))));
        LandContentFixtures.Move(s, new(8, 0)); Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(3)));
        LandContentFixtures.Move(s, new(8, 4));
        Assert.Equal("ReservationLimit", LandContentFixtures.Reason(s, new(ActionKind.ReservePlot, Plot: new(5))));
        Assert.Equal(199000, s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void FractionalMinorReservationFeeRoundsUp_WithoutOverflow()
    {
        var cfg = LandContentFixtures.Scenario();
        cfg = cfg with { Plots = cfg.Plots!.Select(p => p.Id == 3 ? p with { PriceMinor = 11 } : p).ToArray() };
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Move(s, new(8, 0));
        Fixtures.Command(s, new(ActionKind.ReservePlot, Plot: new(3)));
        Assert.Equal(199998, s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void LandConflictUsesSeededActorArbitration_IndependentOfSubmissionOrder()
    {
        var winners = new HashSet<long>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var cfg = LandContentFixtures.Scenario();
            cfg = cfg with { Seed = seed, Participants = cfg.Participants.Select(p => p with { Position = new(8, 0) }).ToArray() };
            var forward = LandContentFixtures.Session(cfg); var reverse = LandContentFixtures.Session(cfg);
            foreach (var actor in new long[] { 1, 2 }) forward.Send(new(actor), new(ActionKind.BuyPlot, Plot: new(3)));
            foreach (var actor in new long[] { 2, 1 }) reverse.Send(new(actor), new(ActionKind.BuyPlot, Plot: new(3)));
            forward.Step(); reverse.Step();
            Assert.Equal(forward.Engine.ComputeStateHash(), reverse.Engine.ComputeStateHash());
            winners.Add(forward.Engine.Inspect().Plots.Single(p => p.Id.Value == 3).OwnerId!.Value.Value);
            Assert.Single(forward.Engine.Ledger(), l => l.Reason == "PlotPurchase");
        }
        Assert.Equal(new long[] { 1, 2 }, winners.Order());
    }
    [Fact]
    public void MultiCellConstructionChecksEntireFootprintBeforeAnyDebit()
    {
        var cfg = LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), 3);
        cfg = cfg with { Plots = [.. cfg.Plots!, new(8, new(9, 0), 10000, OwnerId: 2)] };
        cfg = LandContentFixtures.Type(cfg, "warehouse", t => t with
        { Footprint = [new(0, 0), new(1, 0)], Levels = [t.Levels[0] with { Materials = [new("wood", 2)], PriceMinor = 100 }] });
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(8, 0));
        var before = s.Engine.Inspect();
        Assert.Equal("LandNotOwned", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(8, 0), BuildingTypeId: "warehouse")));
        var after = s.Engine.Inspect();
        Assert.Equal(before.NextId, after.NextId); Assert.Equal(before.Inventories.Length, after.Inventories.Length);
        Assert.Equal(2, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        Assert.Equal(200000, s.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Null(after.Plots.Single(p => p.Id.Value == 3).BuildingId);
    }
    [Fact]
    public void SuccessfulFootprintAndRepeatedBuildDoNotDuplicateInventoryOrConsumeAgain()
    {
        var cfg = LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), 3);
        cfg = cfg with { Plots = [.. cfg.Plots!, new(8, new(9, 0), 10000, OwnerId: 1)] };
        cfg = LandContentFixtures.Type(cfg, "warehouse", t => t with
        { Footprint = [new(0, 0), new(1, 0)], Levels = [t.Levels[0] with { Materials = [new("wood", 2)], PriceMinor = 100 }] });
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(8, 0));
        var id = Fixtures.Command(s, new(ActionKind.Build, Target: new(8, 0), BuildingTypeId: "warehouse"));
        var tape = s.CommandTape.Single(c => c.Command.CommandId == id); s.Submit(tape.AuthenticatedActor, tape.Command);
        Assert.Equal("LandOccupied", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(8, 0), BuildingTypeId: "warehouse")));
        var b = Assert.Single(s.Engine.Observe(new(1)).OwnBuildings, b => b.TypeId == "warehouse");
        Assert.Equal(2, b.Footprint.Length); Assert.All(s.Engine.Inspect().Plots.Where(p => p.Id.Value is 3 or 8), p => Assert.Equal(b.Id, p.BuildingId));
        Assert.Equal(199900, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(2, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").ConstructionInput);
        Assert.Equal(4, s.Engine.Inspect().Population); Assert.Equal(0, s.Engine.Inspect().Unemployed);
    }
    [Fact]
    public void NoMoneyAfterStagedMaterials_RollsBackEverything()
    {
        var cfg = LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), 3);
        cfg = LandContentFixtures.Type(cfg, "warehouse", t => t with
        { Levels = [t.Levels[0] with { Materials = [new("wood", 2)], PriceMinor = 300000 }] });
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(8, 0));
        var before = s.Engine.Inspect();
        Assert.Equal("NoMoney", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(8, 0), BuildingTypeId: "warehouse")));
        Assert.Equal(before.NextId, s.Engine.Inspect().NextId);
        Assert.Equal(2, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        Assert.Equal(0, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").ConstructionInput);
        Assert.DoesNotContain(s.Engine.Ledger(), x => x.Reason == "Construction");
    }
    [Theory]
    [InlineData("warehouse", 3, 8, 0)]
    [InlineData("shop", 4, 12, 0)]
    public void StorageHasFiniteCapacity_AndCannotTakeMoreThanItHolds(string type, long plot, int x, int z)
    {
        var cfg = LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), plot);
        cfg = LandContentFixtures.Type(cfg, type, t => t with
        { Levels = [t.Levels[0] with { Materials = [new("wood", 1)], InputCapacity = 3, PriceMinor = 100 }] });
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 5); LandContentFixtures.Move(s, new(x, z));
        Fixtures.Command(s, new(ActionKind.Build, Target: new(x, z), BuildingTypeId: type));
        var b = s.Engine.Observe(new(1)).OwnBuildings.Single(b => b.TypeId == type);
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Assert.Equal("NoCapacity", LandContentFixtures.Reason(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 4, Source: carrier, Destination: b.InputId)));
        Assert.Equal(4, Fixtures.Count(s, carrier, "wood")); Assert.Equal(0, Fixtures.Count(s, b.InputId, "wood"));
    }
    [Fact]
    public void NewProductionHasZeroStaffAndNoIdlePayroll()
    {
        var s = LandContentFixtures.Session(LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), 5));
        LandContentFixtures.Harvest(s, "wood", 10); LandContentFixtures.Move(s, new(8, 4));
        Fixtures.Command(s, new(ActionKind.Build, Target: new(8, 4), BuildingTypeId: "bakery"));
        var b = s.Engine.Observe(new(1)).OwnBuildings.Single(b => b.Position == new Cell(8, 4));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: new(b.Id.Value), Enabled: true));
        new TickScheduler(s).Headless(10);
        var e = s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == b.Id.Value);
        Assert.Equal(0, e.Staff); Assert.Equal(2, e.StaffCapacity); Assert.Equal("NoStaff", e.ReasonCode); Assert.Equal(0, e.CompletedBatches);
        Assert.Equal(4, s.Engine.Inspect().Population); Assert.Equal(0, s.Engine.Inspect().Unemployed);
        Assert.DoesNotContain(s.Engine.Ledger(), l => l.Reason is "Wages" or "Energy");
    }
    [Fact]
    public void UpgradeCannotUsePersonalInventory_AndKeepsIdentityFootprintAndHeadcount()
    {
        var s = LandContentFixtures.Session(LandContentFixtures.CheapUpgrade()); LandContentFixtures.OnePlank(s);
        var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: e.OutputId, Destination: o.Participant.InventoryId));
        Assert.Equal("NoStock", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
        Assert.Equal(1, Fixtures.Count(s, o.Participant.InventoryId, "plank")); Assert.Equal(199750, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: o.Participant.InventoryId, Destination: e.InputId));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
        var upgraded = s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1);
        Assert.Equal(e.InputId, upgraded.InputId); Assert.Equal(e.OutputId, upgraded.OutputId);
        Assert.Equal(2, upgraded.Level); Assert.Equal(4, upgraded.StaffCapacity); Assert.Equal(2, upgraded.Staff);
        Assert.Equal(199550, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(1, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
        Assert.Equal(80, s.Engine.Inspect().Inventories.Single(i => i.Id == e.InputId).Capacity);
        Assert.Equal("sawmill-2", upgraded.RecipeId);
        Assert.Equal("MaxLevel", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
    }
    [Fact]
    public void PauseWithWipCannotUpgradeOrChangeRecipe()
    {
        var s = LandContentFixtures.Session(); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(4, 0));
        var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false));
        Assert.Equal("ProductionNotSafe", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
        Assert.Equal("ProductionNotSafe", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: e.Id, RecipeId: "sawmill-1")));
        Assert.Equal(2, s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).WorkDone);
    }
    [Fact]
    public void UpgradeNoMoneyRollsBackMaterialsLevelAndCapacity()
    {
        var cfg = LandContentFixtures.CheapUpgrade();
        cfg = cfg with { Participants = cfg.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 250 } : p).ToArray() };
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.OnePlank(s);
        var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: e.OutputId, Destination: o.Participant.InventoryId));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: o.Participant.InventoryId, Destination: e.InputId));
        Assert.Equal("NoMoney", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
        Assert.Equal(1, s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).Level);
        Assert.Equal(1, Fixtures.Count(s, e.InputId, "plank"));
        Assert.Equal(40, s.Engine.Inspect().Inventories.Single(i => i.Id == e.InputId).Capacity);
        Assert.Equal(0, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
    }
    [Fact]
    public void UpgradeCombinesFreeMaterialsFromBuiltInInputAndOutputAtomically()
    {
        var cfg = LandContentFixtures.Type(LandContentFixtures.CheapUpgrade(), "sawmill", t => t with
        { Levels = t.Levels.Select(l => l.Level == 2 ? l with { Materials = [new("plank", 2)] } : l).ToArray() });
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 4); LandContentFixtures.Move(s, new(4, 0));
        var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 4, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        while (s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).CompletedBatches < 2) s.Step();
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: e.OutputId, Destination: o.Participant.InventoryId));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: o.Participant.InventoryId, Destination: e.InputId));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1))));
        Assert.Equal(0, Fixtures.Count(s, e.InputId, "plank")); Assert.Equal(0, Fixtures.Count(s, e.OutputId, "plank"));
        Assert.Equal(2, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
        Assert.Equal(199300, s.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void LockedAndForeignRecipesAreRejected_AndLowerRecipeDoesNotBypassTierStaffRequirement()
    {
        var s = LandContentFixtures.Session(LandContentFixtures.CheapUpgrade());
        Assert.Equal("RecipeLocked", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: new(1), RecipeId: "sawmill-2")));
        Assert.Equal("RecipeLocked", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: new(1), RecipeId: "bakery-1")));
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: new(1)), 2));
        LandContentFixtures.OnePlank(s); var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: e.OutputId, Destination: o.Participant.InventoryId));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "plank", Quantity: 1, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.UpgradeBuilding, Building: new(1)));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: e.Id, RecipeId: "sawmill-1")));
        LandContentFixtures.Move(s, new(0, 0)); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(4, 0));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        new TickScheduler(s).Headless(2);
        Assert.Equal(1, s.Engine.Observe(new(1)).OwnEnterprises.Single(x => x.Id == e.Id).CompletedBatches);
        s.Step(); Assert.Equal(2, s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).CompletedBatches);
    }
    [Fact]
    public void FullTierTwoTeamDoublesSawmillRate_WithoutCreatingWorkersDuringPlay()
    {
        var cfg = LandContentFixtures.Scenario();
        cfg = cfg with { Population = 6, Buildings = cfg.Buildings!.Select(b => b.Id == 1 ? b with { Level = 2, Staff = 4 } : b).ToArray() };
        var s = LandContentFixtures.Session(cfg); LandContentFixtures.Harvest(s, "wood", 2); LandContentFixtures.Move(s, new(4, 0));
        var o = s.Engine.Observe(new(1)); var e = o.OwnEnterprises.Single(e => e.Id.Value == 1);
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        Assert.Equal(0, s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).CompletedBatches);
        s.Step(); Assert.Equal(1, s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1).CompletedBatches);
        Assert.Equal(6, s.Engine.Inspect().Population); Assert.Equal(0, s.Engine.Inspect().Unemployed);
        Assert.Equal(200, s.Engine.Inspect().Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(50, s.Engine.Inspect().Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
    }
    [Fact]
    public void DevelopmentRunsTwoChainsBuildsAndUpgrade_AndReplaysExactly()
    {
        var cfg = LandContentFixtures.Scenario(); var s = LandContentFixtures.Session(cfg);
        ScriptedScenarios.Development(s, cfg); var state = s.Engine.Inspect();
        Assert.Equal(119375, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(5750, state.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(87375, state.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(20, state.Flows.Single(f => f.ItemId == "wood").ConstructionInput);
        Assert.Equal(20, state.Flows.Single(f => f.ItemId == "plank").ConstructionInput);
        Assert.Equal(25, state.Flows.Single(f => f.ItemId == "plank").Produced);
        Assert.Equal(5, state.Flows.Single(f => f.ItemId == "bread").Produced);
        Assert.Equal(5, state.Flows.Single(f => f.ItemId == "plank").Exported);
        Assert.Equal(5, state.Flows.Single(f => f.ItemId == "bread").Exported);
        Assert.Equal(5, state.Buildings.Length); Assert.All(state.Inventories, i => Assert.Empty(i.Stock));
        var replay = GameSession.Replay(cfg, s.CommandTape, state.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Equal(s.Samples, replay.Samples); Assert.Equal(s.Engine.Ledger(), replay.Engine.Ledger());
    }
    [Fact]
    public void CatalogObservationCannotMutateCostsOrFootprintOfRunningWorld()
    {
        var s = LandContentFixtures.Session(LandContentFixtures.OwnPlot(LandContentFixtures.Scenario(), 5));
        var t = s.Engine.Observe(new(1)).BuildingTypes.Single(t => t.Id == "bakery");
        t.Footprint[0] = new(50, 50); t.Levels[0].Materials[0] = new("wood", 1);
        LandContentFixtures.Harvest(s, "wood", 1); LandContentFixtures.Move(s, new(8, 4));
        Assert.Equal("NoStock", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(8, 4), BuildingTypeId: "bakery")));
    }
    [Theory]
    [InlineData("bad-recipe")]
    [InlineData("duplicate-plot")]
    [InlineData("disconnected")]
    [InlineData("overflow")]
    [InlineData("wrong-owner")]
    [InlineData("population")]
    [InlineData("shop-site")]
    public void InvalidContentFailsBeforeWorldCreation(string kind)
    {
        var cfg = LandContentFixtures.Scenario();
        cfg = kind switch
        {
            "bad-recipe" => LandContentFixtures.Type(cfg, "sawmill", t => t with { Levels = [t.Levels[0] with { RecipeId = "missing" }] }),
            "duplicate-plot" => cfg with { Plots = [.. cfg.Plots!, cfg.Plots![0]] },
            "disconnected" => LandContentFixtures.Type(cfg, "sawmill", t => t with { Footprint = [new(0, 0), new(2, 0)] }),
            "overflow" => cfg with { Items = cfg.Items.Select(i => i.Id == "wood" ? i with { UnitVolume = long.MaxValue } : i).ToArray() },
            "wrong-owner" => LandContentFixtures.OwnPlot(cfg, 1, 2),
            "population" => cfg with { Population = 3 },
            "shop-site" => cfg with { Buildings = [.. cfg.Buildings!, new(3, 1, new(8, 0), "shop")] },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(cfg));
    }
}

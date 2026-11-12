using System.Text;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ProvincialSupportI6C8CTests
{
    private static EconomyProfile Profile() => VNextProvincialSupportProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile());

    private static GameSession Open(string industry, string design = "compact_valley_i6c8c.json")
    {
        var profile = Profile();
        var source = MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text(design)), profile);
        var resolved = OrdinaryNewGame.Resolve(source, profile, new(3, StartingIndustry: industry), vnextBalance: MapDesignTests.Balance());
        return new(new(resolved.Scenario));
    }

    private static long Cash(GameSession s, long actor = 1) => s.Engine.Observe(new(actor)).Account.BalanceMinor;

    private static string Send(GameSession s, GameAction action, bool accepted = true, long actor = 1)
    {
        string id = s.Send(new(actor), action);
        s.Step();
        var result = s.Engine.Result(id)!;
        Assert.True(result.Status == (accepted ? ResultStatus.Accepted : ResultStatus.Rejected), action.Kind + " " + result.ReasonCode);
        return result.ReasonCode;
    }

    private static void RoundTrip(GameSession s)
    {
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    private static void Haul(GameSession s, InventoryId source, InventoryId destination, string item, long quantity)
    {
        long volume = s.Engine.Configuration().Items.Single(i => i.Id == item).UnitVolume;
        long chunk = Math.Max(1, 20 / volume);
        for (long left = quantity; left > 0; left -= chunk)
        {
            long take = Math.Min(chunk, left);
            StorageC1Tests.Move(s, s.Engine.Inspect().Inventories.Single(i => i.Id == source).Position);
            StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: source, Destination: StorageC1Tests.Carrier(s), ItemId: item, Quantity: take));
            StorageC1Tests.Idle(s);
            StorageC1Tests.Move(s, s.Engine.Inspect().Inventories.Single(i => i.Id == destination).Position);
            StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: StorageC1Tests.Carrier(s), Destination: destination, ItemId: item, Quantity: take));
            StorageC1Tests.Idle(s);
        }
    }

    [Theory]
    [InlineData("woodworking", 17000, 25375, 1232, 103607)]
    [InlineData("food", 17000, 25275, 1232, 103507)]
    [InlineData("dairy", 19000, 26200, 1848, 107048)]
    [InlineData("materials", 19000, 29400, 1540, 109940)]
    public void EachIndustryReservesTheCatalogCostAndMovesNoMoneyUntilItIsSpent(string industry, long construction, long supply, long operating, long total)
    {
        var s = Open(industry);
        Assert.Equal(20_000, Cash(s));
        Assert.Equal("Offered", s.Engine.ProvincialProgramOf(new(1))!.Status);
        var lot = s.Engine.Configuration().Layout!.IndustrialLots[0];
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        StorageC1Tests.Move(s, anchor);
        Assert.Equal("NoMoney", Send(s, new(ActionKind.BuyPlot, Plot: new(s.Engine.Configuration().Plots!.Single(p => p.Position == anchor).Id)), accepted: false));
        Assert.Equal(20_000, Cash(s));
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var program = s.Engine.ProvincialProgramOf(new(1))!;
        Assert.Equal("Open", program.Status);
        Assert.Equal(60_000, program.ParcelReserved);
        Assert.Equal(construction, program.ConstructionReserved);
        Assert.Equal(supply, program.SupplyValueMinor);
        Assert.Equal(operating, program.OperatingReserved);
        Assert.Equal(total, program.ParcelReserved + program.ConstructionReserved + program.SupplyValueMinor + program.OperatingReserved);
        Assert.Equal(0, s.Engine.SpecializationChoices().Single(c => c.ActorId == 1).SupportSlotsRemaining);
        Assert.Equal(20_000, Cash(s));
        Assert.DoesNotContain(s.Engine.Ledger(), l => l.Reason.StartsWith("Provincial", StringComparison.Ordinal));
        Assert.Equal("ProvincialSupportUsed", Send(s, new(ActionKind.AcceptProvincialSupport), accepted: false));
        Assert.DoesNotContain(s.Engine.PublicEventsSince(0), e => e.Type.StartsWith("Provincial", StringComparison.Ordinal));
        RoundTrip(s);
    }

    // Пища и стройматериалы держат те же суммы в проверке резерва. Здесь остаются два разных способа продажи: столичный заказ и выкуп провинции.
    [Theory]
    [InlineData("woodworking", "plank")]
    [InlineData("dairy", "cheese")]
    public void OrdinaryCashBuildsTheFirstEnterpriseAndSellsTheFirstBatch(string industry, string output)
    {
        var s = Open(industry);
        // Обычная пауза выборов. Бюллетени не дают экономического преимущества.
        s.AttachControllers(s.Engine.Inspect().Participants.Select(p => (IController)new SelfBallotController(p.Id)).ToArray());
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var lot = s.Engine.Configuration().Layout!.IndustrialLots.First(l => IndustrialParcels.TryFrame(l) is not null);
        var frame = IndustrialParcels.TryFrame(lot)!.Value;
        var anchorPlot = s.Engine.Configuration().Plots!.Single(p => p.Position == frame.Anchor);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.BuyPlot, Plot: new(anchorPlot.Id)));
        var owned = s.Engine.Observe(new(1)).Plots.Where(p => lot.Cells.Contains(p.Position)).ToArray();
        Assert.Equal(6, owned.Length);
        Assert.All(owned, p => Assert.Equal(new ParticipantId(1), p.OwnerId));
        Assert.Equal(20_000, Cash(s));
        Assert.Equal(60_000, s.Engine.Ledger().Where(l => l.Reason == "ProvincialParcelGrant").Sum(l => l.AmountMinor));
        Assert.Equal(60_000, s.Engine.Ledger().Where(l => l.Reason == "ParcelPurchase").Sum(l => l.AmountMinor));
        var other = s.Engine.Configuration().Layout!.IndustrialLots.First(l => l.Id != lot.Id);
        var otherAnchor = IndustrialParcels.TryFrame(other)!.Value.Anchor;
        StorageC1Tests.Move(s, otherAnchor);
        Send(s, new(ActionKind.BuyPlot, Plot: new(s.Engine.Configuration().Plots!.Single(p => p.Position == otherAnchor).Id)), accepted: false);
        Assert.Equal(20_000, Cash(s));
        var terminal = s.Engine.Configuration().CapitalDemand!.Terminal;
        StorageC1Tests.Move(s, terminal);
        Send(s, new(ActionKind.CollectProvincialSupply));
        Assert.Equal("ProvincialSupplyCollected", Send(s, new(ActionKind.CollectProvincialSupply), accepted: false));
        var supply = s.Engine.Inspect().Inventories.Single(i => i.OwnerId == new ParticipantId(1) && i.Position == terminal && i.Stock.Any(n => n.ItemId == "steel"));
        var direction = StartingSpecializations.Direction(industry)!;
        Assert.Equal(output, s.Engine.Configuration().Recipes.Single(r => r.Id == direction.RecipeId).OutputItem);
        var recipe = s.Engine.Configuration().Recipes.Single(r => r.Id == direction.RecipeId);
        var level = s.Engine.Configuration().BuildingTypes!.Single(t => t.Id == industry).Levels.Single(l => l.Level == 1);
        StorageC1Tests.Move(s, terminal);
        StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: supply.Id, Destination: StorageC1Tests.Carrier(s), ItemId: "wood", Quantity: 1));
        StorageC1Tests.Idle(s);
        var woodOffer = s.Engine.Inspect().CapitalOffers.First(o => o.ItemId == "wood");
        StorageC1Tests.Move(s, woodOffer.Position);
        Assert.Equal("SubsidizedGoods", Send(s, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: woodOffer.Id), accepted: false));
        StorageC1Tests.Move(s, terminal);
        StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: StorageC1Tests.Carrier(s), Destination: supply.Id, ItemId: "wood", Quantity: 1));
        StorageC1Tests.Idle(s);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Build, BuildingTypeId: industry, Target: frame.Anchor), accepted: false);
        Assert.Equal(level.PriceMinor, s.Engine.ProvincialProgramOf(new(1))!.ConstructionRemaining);
        var ground = new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g => g.PlotId == anchorPlot.Id).InventoryId);
        foreach (var line in level.Materials.Concat(recipe.Ingredients.Select(i => new MaterialCost(i.ItemId, i.Quantity))).GroupBy(x => x.ItemId))
            Haul(s, supply.Id, ground, line.Key, line.Sum(x => x.Quantity));
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Build, BuildingTypeId: industry, Target: frame.Anchor));
        Assert.Equal(20_000, Cash(s));
        Assert.Equal(level.PriceMinor, s.Engine.Ledger().Where(l => l.Reason == "ProvincialConstructionGrant").Sum(l => l.AmountMinor));
        var project = s.Engine.DevelopmentOf(new(1))!.Projects.Single();
        s.AdvanceTo(project.CompletesTick);
        Assert.Equal(20_000 + level.StaffCapacity * level.WageMinor * recipe.FullStaffTicks + recipe.EnergyPerBatchMinor, Cash(s));
        Assert.Contains(s.Engine.Ledger(), l => l.Reason == "ProvincialOperatingReserve");
        var enterprise = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        if (enterprise.RecipeId != direction.RecipeId)
        {
            StorageC1Tests.Move(s, frame.Anchor);
            Send(s, new(ActionKind.SetRecipe, Enterprise: enterprise.Id, RecipeId: direction.RecipeId));
            enterprise = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        }
        Assert.Equal(direction.RecipeId, enterprise.RecipeId);
        foreach (var ingredient in recipe.Ingredients)
            Haul(s, ground, enterprise.InputId, ingredient.ItemId, ingredient.Quantity);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: true));
        for (int i = 0; i < 800 && s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff == 0; i++) s.Step();
        Assert.True(s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff > 0);
        for (int i = 0; i < 800 && s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches == 0; i++) s.Step();
        Assert.Equal(1, s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);
        Send(s, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: false));
        Assert.True(Cash(s) >= 20_000);
        var made = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Transfer, Source: made.OutputId, Destination: StorageC1Tests.Carrier(s), ItemId: output, Quantity: recipe.OutputQuantity));
        StorageC1Tests.Idle(s);
        var offer = s.Engine.Inspect().CapitalOffers.FirstOrDefault(o => o.ItemId == output && o.RemainingQuantity > 0);
        long before = Cash(s);
        if (offer is not null)
        {
            long sold = Math.Min(recipe.OutputQuantity, offer.RemainingQuantity);
            StorageC1Tests.Move(s, offer.Position);
            Send(s, new(ActionKind.SellAvailableToCapital, ItemId: output, Quantity: sold, OfferId: offer.Id));
            s.AdvanceTo(s.Engine.Inspect().CapitalOffers.Single(o => o.Id == offer.Id).CutoffTick!.Value);
            Assert.Contains(s.Engine.Inspect().Settlements, row => row.ItemId == output);
        }
        else
        {
            // Столичный спрос округляет дорогой сыр до нуля. Провинция покупает первую партию по базовой цене.
            StorageC1Tests.Move(s, s.Engine.Configuration().CapitalDemand!.Terminal);
            Send(s, new(ActionKind.SellProvincialOutput, ItemId: output, Quantity: recipe.OutputQuantity));
            Assert.Equal("ProvincialSupportUsed", Send(s, new(ActionKind.SellProvincialOutput, ItemId: output, Quantity: recipe.OutputQuantity), accepted: false));
            Assert.Contains(s.Engine.Ledger(), row => row.Reason == "ProvincialOutputPurchase" && row.AmountMinor == recipe.OutputQuantity * s.Engine.Configuration().Items.Single(i => i.Id == output).BasePriceMinor);
        }
        Assert.True(Cash(s) > before);
        var quote = s.Engine.QuoteDemolition(new(1), made.Id.Value)!;
        Assert.All(quote.Recovery, row => Assert.True(row.Quantity == 0 || row.ItemId != "steel" && row.ItemId != "wood"));
        Assert.DoesNotContain(quote.Recovery, row => row.ItemId == "steel" && row.Quantity > 0);
        Assert.Equal(0, s.Engine.SpecializationChoices().Single(c => c.ActorId == 1).SupportSlotsRemaining);
        RoundTrip(s);
        Assert.Null(VNextStartingSpecializationProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile()).World.Rules.ProvincialSupport);
        Assert.Null(VNextStartingSpecializationProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile()).World.Rules.IndustrialParcels);
        Assert.Equal(15, Profile().World.Rules.Fleet!.WageMinor);
        Assert.Equal(2, Profile().World.Rules.PlayerWageStepMinor);
        Assert.Equal(10_000, Profile().World.PlotPriceMinor);
    }

    [Fact]
    public void CancellingTheSubsidizedBuildingDoesNotRestoreTheSlotOrPayTheReserveAgain()
    {
        var s = Open("woodworking");
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var lot = s.Engine.Configuration().Layout!.IndustrialLots[0];
        var frame = IndustrialParcels.TryFrame(lot)!.Value;
        var anchorPlot = s.Engine.Configuration().Plots!.Single(p => p.Position == frame.Anchor);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.BuyPlot, Plot: new(anchorPlot.Id)));
        StorageC1Tests.Move(s, s.Engine.Configuration().CapitalDemand!.Terminal);
        Send(s, new(ActionKind.CollectProvincialSupply));
        var supply = s.Engine.Inspect().Inventories.Single(i => i.OwnerId == new ParticipantId(1) && i.Stock.Any(n => n.ItemId == "steel"));
        var ground = new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g => g.PlotId == anchorPlot.Id).InventoryId);
        var level = s.Engine.Configuration().BuildingTypes!.Single(t => t.Id == "woodworking").Levels.Single(l => l.Level == 1);
        foreach (var material in level.Materials) Haul(s, supply.Id, ground, material.ItemId, material.Quantity);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: frame.Anchor));
        var project = s.Engine.StorageOf(new(1)).Construction.Single();
        StorageC1Tests.Move(s, frame.Anchor);
        var quote = s.Engine.QuoteDemolition(new(1), project.Id, true)!;
        Send(s, new(ActionKind.CancelConstruction, OfferId: project.Id, DemolitionQuote: quote.Token));
        Assert.Equal(0, s.Engine.SpecializationChoices().Single(c => c.ActorId == 1).SupportSlotsRemaining);
        Assert.Equal("Completed", s.Engine.ProvincialProgramOf(new(1))!.Status);
        Assert.DoesNotContain(s.Engine.Ledger(), l => l.Reason == "ProvincialOperatingReserve");
        Send(s, new(ActionKind.AcceptProvincialSupport), accepted: false);
        RoundTrip(s);
    }

    [Fact]
    public void ResearchCanTurnTheProgramOffAndOldProfilesStayUnchanged()
    {
        var disabled = Open("dairy").Engine.Configuration() with
        {
            Economy = Open("dairy").Engine.Configuration().Economy! with { ProvincialSupport = new(1, false) }
        };
        var s = new GameSession(new(disabled));
        Assert.Equal("ProvincialSupportDisabled", Send(s, new(ActionKind.AcceptProvincialSupport), accepted: false));
        Assert.Null(s.Engine.ProvincialProgramOf(new(1)));
        var historical = MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text("compact_valley_i6c21.json")), VNextStartingSpecializationProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile()));
        Assert.Null(historical.Economy!.ProvincialSupport);
        Assert.Null(historical.Economy.IndustrialParcels);
        var started = OrdinaryNewGame.Resolve(historical, VNextStartingSpecializationProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile()), new(3, StartingIndustry: "dairy"), vnextBalance: MapDesignTests.Balance());
        Assert.Equal(15, started.Scenario.Plots!.First(p => !p.IsShopSite).PriceMinor == 10_000 ? started.Scenario.Economy!.Fleet!.WageMinor : 0);
        Assert.Equal(10_000, started.Scenario.Plots!.First(p => !p.IsShopSite).PriceMinor);
    }

    [Fact]
    public void WoodlandParcelsFaceTheRingAndKeepTheRoad()
    {
        var s = Open("materials", "woodland_region_i6c8c.json");
        Assert.Equal(8, s.Engine.Configuration().Layout!.IndustrialLots.Length);
        Assert.Equal(46, s.Engine.Configuration().Layout!.InitialCityDirtCells.Length);
        Assert.All(s.Engine.Configuration().Layout!.IndustrialLots, lot => Assert.NotNull(IndustrialParcels.TryFrame(lot)));
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var lot = s.Engine.Configuration().Layout!.IndustrialLots[0];
        var frame = IndustrialParcels.TryFrame(lot)!.Value;
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.BuyPlot, Plot: new(s.Engine.Configuration().Plots!.Single(p => p.Position == frame.Anchor).Id)));
        Assert.Equal(6, s.Engine.Observe(new(1)).Plots.Count(p => lot.Cells.Contains(p.Position) && p.OwnerId == new ParticipantId(1)));
        Assert.Equal(20_000, Cash(s));
    }

    [Fact]
    public void SubsidizedInputsStayRestrictedThroughOwnTransferSupportAndASecondGrant()
    {
        var s = Open("woodworking");
        s.AttachControllers(s.Engine.Inspect().Participants.Select(p => (IController)new SelfBallotController(p.Id)).ToArray());
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var terminal = s.Engine.Configuration().CapitalDemand!.Terminal;
        StorageC1Tests.Move(s, terminal);
        Send(s, new(ActionKind.CollectProvincialSupply));
        var supply = s.Engine.Inspect().Inventories.Single(i => i.OwnerId == new ParticipantId(1) && i.Stock.Any(n => n.ItemId == "steel"));
        long wood = supply.Stock.Single(n => n.ItemId == "wood").Quantity;
        var section = s.Engine.ObserveController(new(1)).Support.OwnSection;
        Assert.NotEqual(0, section.Value);
        Haul(s, supply.Id, section, "wood", 1);
        var woodOffer = s.Engine.Inspect().CapitalOffers.First(o => o.ItemId == "wood" && o.RemainingQuantity > 0);
        StorageC1Tests.Move(s, woodOffer.Position);
        Assert.Equal("SubsidizedGoods", Send(s, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: woodOffer.Id), accepted: false));
        Assert.Equal("NotAvailable", Send(s, new(ActionKind.DeliverContract, ContractId: 1, Quantity: 1), accepted: false));
        long WoodOf(long actor) => s.Engine.Inspect().Inventories.Where(i => i.OwnerId == new ParticipantId(actor)).Sum(i => i.Stock.Where(n => n.ItemId == "wood").Sum(n => n.Quantity));
        Assert.Equal(wood, WoodOf(1));
        StorageC1Tests.Move(s, supply.Position);
        StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: supply.Id, Destination: StorageC1Tests.Carrier(s), ItemId: "wood", Quantity: 10));
        StorageC1Tests.Idle(s);
        var shop = s.Engine.Inspect().Plots.First(p => p.IsShopSite && p.OwnerId is null && p.ReservedBy is null);
        StorageC1Tests.Move(s, shop.Position);
        Send(s, new(ActionKind.BuyPlot, Plot: shop.Id));
        Send(s, new(ActionKind.Build, BuildingTypeId: "shop", Target: shop.Position));
        Assert.Equal(wood - 10, WoodOf(1));
        Assert.Equal("SubsidizedGoods", Send(s, new(ActionKind.CreateSellOffer, Building: s.Engine.Observe(new(1)).OwnBuildings.Single(b => b.Kind == BuildingKind.Shop).Id, ItemId: "wood", Quantity: 1, UnitPriceMinor: 100), accepted: false));
        var forest = s.Engine.Configuration().ResourceNodes.First(n => n.ItemId == "wood");
        StorageC1Tests.Move(s, forest.Position, 2);
        Send(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 10), actor: 2);
        StorageC1Tests.Idle(s, 2);
        var otherShop = s.Engine.Inspect().Plots.First(p => p.IsShopSite && p.OwnerId is null && p.ReservedBy is null);
        StorageC1Tests.Move(s, otherShop.Position, 2);
        Send(s, new(ActionKind.BuyPlot, Plot: otherShop.Id), actor: 2);
        Send(s, new(ActionKind.Build, BuildingTypeId: "shop", Target: otherShop.Position), actor: 2);
        Assert.Equal(0, WoodOf(2));
        var election = s.Engine.Inspect().Elections.NextElectionId!.Value;
        Send(s, new(ActionKind.ProposeSupport, SupportSeller: new(1), Election: election, Compensation: [new("wood", 1)]), actor: 2);
        long proposal = s.Engine.ObserveController(new(1)).Support.Proposals.Single().Id;
        Send(s, new(ActionKind.AcceptSupport, SupportProposalId: proposal));
        s.Step();
        Assert.Empty(s.Engine.InspectSupport().Proposals.Single().Reserved);
        StorageC1Tests.Move(s, s.Engine.Configuration().Support!.TradingPoint);
        Assert.Equal("IncompleteCompensation", Send(s, new(ActionKind.SignSupport, SupportProposalId: proposal), accepted: false));
        Assert.Equal(wood - 10, WoodOf(1));
        Assert.Equal(0, WoodOf(2));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        loaded.RestoreControllers(SelfBallotController.Restore);
        string again = loaded.Send(new(1), new(ActionKind.CollectProvincialSupply));
        loaded.Step();
        Assert.Equal("ProvincialSupplyCollected", loaded.Engine.Result(again)!.ReasonCode);
        var node = s.Engine.Configuration().ResourceNodes.First(n => n.ItemId is not "wood" and not "steel");
        StorageC1Tests.Move(s, node.Position);
        Send(s, new(ActionKind.Harvest, ItemId: node.ItemId, Quantity: 1));
        StorageC1Tests.Idle(s);
        var ordinary = s.Engine.Inspect().CapitalOffers.FirstOrDefault(o => o.ItemId == node.ItemId && o.RemainingQuantity > 0);
        Assert.NotNull(ordinary);
        StorageC1Tests.Move(s, ordinary!.Position);
        Send(s, new(ActionKind.SellAvailableToCapital, ItemId: node.ItemId, Quantity: 1, OfferId: ordinary.Id));
        var plank = s.Engine.Inspect().CapitalOffers.First(o => o.ItemId == "plank" && o.RemainingQuantity > 0);
        if (s.Engine.Observe(new(1)).Participant.Position != plank.Position) StorageC1Tests.Move(s, plank.Position);
        StorageC1Tests.Idle(s);
        Assert.Equal("NoStock", Send(s, new(ActionKind.SellAvailableToCapital, ItemId: "plank", Quantity: 1, OfferId: plank.Id), accepted: false));
        var first = s.Engine.Configuration().Layout!.IndustrialLots[0];
        var firstAnchor = IndustrialParcels.TryFrame(first)!.Value.Anchor;
        StorageC1Tests.Move(s, firstAnchor);
        long cash = Cash(s);
        Send(s, new(ActionKind.BuyPlot, Plot: new(s.Engine.Configuration().Plots!.Single(p => p.Position == firstAnchor).Id)));
        Assert.Equal(cash, Cash(s));
        Assert.Equal(1, s.Engine.Ledger().Count(l => l.Reason == "ProvincialParcelGrant"));
        var second = s.Engine.Configuration().Layout!.IndustrialLots[1];
        var secondAnchor = IndustrialParcels.TryFrame(second)!.Value.Anchor;
        StorageC1Tests.Move(s, secondAnchor);
        Assert.Equal("NoMoney", Send(s, new(ActionKind.BuyPlot, Plot: new(s.Engine.Configuration().Plots!.Single(p => p.Position == secondAnchor).Id)), accepted: false));
        Assert.Equal(1, s.Engine.Ledger().Count(l => l.Reason == "ProvincialParcelGrant"));
        Assert.Equal(0, s.Engine.Ledger().Count(l => l.Reason == "ProvincialConstructionGrant"));
        RoundTrip(s);
    }

    [Fact]
    public void DemolishingTheFinishedSubsidizedEnterpriseDoesNotRestoreEitherGrant()
    {
        var s = Open("woodworking");
        s.AttachControllers(s.Engine.Inspect().Participants.Select(p => (IController)new SelfBallotController(p.Id)).ToArray());
        Send(s, new(ActionKind.AcceptProvincialSupport));
        var lot = s.Engine.Configuration().Layout!.IndustrialLots[0];
        var frame = IndustrialParcels.TryFrame(lot)!.Value;
        var anchorPlot = s.Engine.Configuration().Plots!.Single(p => p.Position == frame.Anchor);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.BuyPlot, Plot: new(anchorPlot.Id)));
        StorageC1Tests.Move(s, s.Engine.Configuration().CapitalDemand!.Terminal);
        Send(s, new(ActionKind.CollectProvincialSupply));
        var supply = s.Engine.Inspect().Inventories.Single(i => i.OwnerId == new ParticipantId(1) && i.Stock.Any(n => n.ItemId == "steel"));
        var ground = new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single(g => g.PlotId == anchorPlot.Id).InventoryId);
        var level = s.Engine.Configuration().BuildingTypes!.Single(t => t.Id == "woodworking").Levels.Single(l => l.Level == 1);
        foreach (var material in level.Materials) Haul(s, supply.Id, ground, material.ItemId, material.Quantity);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: frame.Anchor));
        var project = s.Engine.DevelopmentOf(new(1))!.Projects.Single();
        s.AdvanceTo(project.CompletesTick);
        var building = s.Engine.Observe(new(1)).OwnBuildings.Single(b => b.Kind == BuildingKind.Production);
        var quote = s.Engine.QuoteDemolition(new(1), building.Id.Value)!;
        Assert.DoesNotContain(quote.Recovery, row => row.ItemId is "steel" or "wood" && row.Quantity > 0);
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.PrepareDemolition, Building: building.Id));
        quote = s.Engine.QuoteDemolition(new(1), building.Id.Value)!;
        Send(s, new(ActionKind.DemolishBuilding, Building: building.Id, DemolitionQuote: quote.Token));
        Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings, b => b.Kind == BuildingKind.Production);
        Assert.Equal(0, s.Engine.SpecializationChoices().Single(c => c.ActorId == 1).SupportSlotsRemaining);
        Assert.Equal("Completed", s.Engine.ProvincialProgramOf(new(1))!.Status);
        Assert.Equal(1, s.Engine.Ledger().Count(l => l.Reason == "ProvincialConstructionGrant"));
        Assert.Equal(1, s.Engine.Ledger().Count(l => l.Reason == "ProvincialOperatingReserve"));
        Assert.Equal("ProvincialSupportUsed", Send(s, new(ActionKind.AcceptProvincialSupport), accepted: false));
        StorageC1Tests.Move(s, frame.Anchor);
        Send(s, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: frame.Anchor), accepted: false);
        Assert.Equal(1, s.Engine.Ledger().Count(l => l.Reason == "ProvincialConstructionGrant"));
        RoundTrip(s);
    }

    [Fact]
    public void NpcCompletesTheProgramThroughTheFirstSaleWithoutAGiftedEnterprise()
    {
        var profile = Profile();
        var source = MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text("compact_valley_i6c8c.json")), profile);
        var resolved = OrdinaryNewGame.Resolve(source, profile, new(3, StartingIndustry: "woodworking"), vnextBalance: MapDesignTests.Balance());
        var config = OrdinaryNewGameTests.Config().ForOrdinary(resolved.Scenario, 1, resolved.Controllers);
        var s = new GameSession(new(resolved.Scenario));
        Assert.DoesNotContain(s.Engine.Inspect().Buildings, b => b.Kind == BuildingKind.Production);
        s.AttachControllers(config.Npcs.Select(n => (IController)new AlgorithmicController(n, AlgorithmicController.RuntimeSeed(resolved.Scenario.Seed, n.ActorId), resolved.Scenario.Participants.Select(p => p.Id))).Append(new SelfBallotController(new(1))).ToArray());
        string direction = StartingSpecializations.Direction("woodworking")!.RecipeId;
        long seller = 0; int staff = 0; string stage = "accept";
        for (int i = 0; i < 40000 && seller == 0; i++)
        {
            s.Step();
            foreach (var actor in resolved.Scenario.Participants.Where(p => p.Id != 1))
            {
                var program = s.Engine.ProvincialProgramOf(new(actor.Id));
                if (program is { Status: not "Offered" }) stage = "accepted";
                if (program?.SupplyCollected == true) stage = "supply";
                if (program?.ParcelRemaining == 0) stage = "parcel";
                var enterprise = s.Engine.Observe(new(actor.Id)).OwnEnterprises.FirstOrDefault();
                if (enterprise is not null)
                {
                    stage = "built";
                    staff = Math.Max(staff, enterprise.Staff);
                    if (enterprise.CompletedBatches > 0)
                    {
                        stage = "produced";
                        Assert.Equal(direction, enterprise.RecipeId);
                        bool sold = s.Engine.Inspect().Settlements.Any(row => row.Seller.Value == actor.Id && row.ItemId == "plank")
                            || s.Engine.Ledger().Any(row => row.Reason == "ProvincialOutputPurchase" && (row.CreditAccount.Value == s.Engine.Observe(new(actor.Id)).Account.Id.Value));
                        if (sold) { seller = actor.Id; break; }
                    }
                }
            }
        }
        Assert.True(seller != 0, "Остановились на «" + stage + "», штат " + staff + ". " + string.Join("; ", resolved.Scenario.Participants.Where(p => p.Id != 1).Select(p =>
        {
            var program = s.Engine.ProvincialProgramOf(new(p.Id));
            var enterprise = s.Engine.Observe(new(p.Id)).OwnEnterprises.FirstOrDefault();
            return p.Id + " " + program?.NextStep + " " + enterprise?.RecipeId + " batches " + enterprise?.CompletedBatches + " staff " + enterprise?.Staff;
        })));
        var made = s.Engine.Observe(new(seller)).OwnEnterprises.Single(e => e.CompletedBatches > 0);
        Assert.Equal(direction, made.RecipeId);
        Assert.True(staff > 0);
        var owned = s.Engine.Observe(new(seller)).Plots.Where(p => p.OwnerId == new ParticipantId(seller)).Select(p => p.Position).ToHashSet();
        Assert.Contains(s.Engine.Configuration().Layout!.IndustrialLots, lot => lot.Cells.Count(owned.Contains) == 6);
        long account = s.Engine.Observe(new(seller)).Account.Id.Value;
        var rows = s.Engine.Ledger().Where(l => l.DebitAccount.Value == account || l.CreditAccount.Value == account).ToArray();
        Assert.Equal(60_000, rows.Where(l => l.Reason == "ProvincialParcelGrant").Sum(l => l.AmountMinor));
        Assert.Equal(60_000, rows.Where(l => l.Reason == "ParcelPurchase").Sum(l => l.AmountMinor));
        Assert.Equal(17_000, rows.Where(l => l.Reason == "ProvincialConstructionGrant").Sum(l => l.AmountMinor));
        Assert.Equal(1_232, rows.Where(l => l.Reason == "ProvincialOperatingReserve").Sum(l => l.AmountMinor));
        Assert.DoesNotContain(rows, l => l.Reason == "ProvincialMaterialSupply");
        Assert.True(s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").ProvincialSupplied > 0);
        Assert.True(Cash(s, seller) < 20_000 + 60_000);
        foreach (var actor in resolved.Scenario.Participants.Where(p => p.Id != 1))
        {
            var program = s.Engine.ProvincialProgramOf(new(actor.Id));
            Assert.NotNull(program);
            Assert.NotEqual("Offered", program!.Status);
            Assert.Equal(0, s.Engine.SpecializationChoices().Single(c => c.ActorId == actor.Id).SupportSlotsRemaining);
            Assert.True(Cash(s, actor.Id) < 20_000 + program.ParcelReserved);
        }
        Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnBuildings, b => b.Kind == BuildingKind.Production);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        loaded.RestoreControllers(c => c.Kind == SelfBallotController.Kind ? SelfBallotController.Restore(c) : AlgorithmicController.Restore(c));
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(s.ComputeControllerStateHash(), loaded.ComputeControllerStateHash());
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }
}

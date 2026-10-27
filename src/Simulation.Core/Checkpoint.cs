using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simulation.Core;

public sealed class CheckpointException(string reason, Exception? inner = null) : Exception(reason, inner)
{
    public string ReasonCode { get; } = reason;
}

internal record SavedAccount(long Id, string Kind, long Balance, KeyValuePair<string, long>[] Holds);
internal record SavedStockHold(string Id, string Item, long Quantity);
internal record SavedInventory(long Id, long Owner, Cell Position, long Capacity,
    KeyValuePair<string, long>[] Stock, SavedStockHold[] Reservations, KeyValuePair<string, long>[] CapacityHolds,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] OverflowLot[]? Overflow=null);
internal record SavedJob(long Id, string Kind, long StartTick, long CompletionTick, Cell[] Path, int PathIndex,
    int SegmentTicks, string Item, long Quantity, string CapacityHold, string CommandId, long EquipmentId);
internal record SavedActor(long Id, long Account, long Inventory, Cell Position, SavedJob? Job,
    int OffsetX = SimulationEngine.TravelCenter, int OffsetZ = SimulationEngine.TravelCenter,
    int HeadingX = 0, int HeadingZ = 0, int FacingX = 0, int FacingZ = -1);
internal record SavedBatch(long Id, string Hold, long WorkRequired, long WorkDone, long EnergyPaid, string RecipeId="",
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] System.Collections.Immutable.ImmutableArray<MaterialCost> Inputs=default,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkshopBatchTerms? Terms=null);
internal record SavedEnterprise(EnterpriseDefinition Definition, long Input, long Output, string RecipeId,
    bool Enabled, string Reason, SavedBatch? Batch, long Completed, long EnergyPaid, EmploymentView Group);
internal record SavedEquipment(long Id, long Owner, Cell Position, long Storage, string TypeId, int Level,
    EquipmentMode Mode, string Reason, SavedBatch? Batch, long Completed, long PersonalJobId, EmploymentView Group, EquipmentMotion? Motion=null, ExtractionRoute? Route=null, long MovedAtTick=0);
internal record SavedSale(long ActorId, long InventoryId, long Quantity, string Reservation, string CommandId);
internal record SavedOffer(long Id, long Remaining, long? Cutoff, SavedSale[] Requests, SupplyRequest[] SupplyRequests, int Window);
internal record WorldCheckpoint(int SchemaVersion, string RulesVersion, ScenarioDefinition Config, string ConfigHash,
    string StateHash, long Tick, long NextId, long EventSequence, ulong AllocationRandom, ulong CommandRandom,
    ulong LaborRandom, long Population, long Unemployed, long LastGrowthPeriod,
    long InitialMoney, long ExternalIn, long ExternalOut, SavedAccount[] Accounts, SavedInventory[] Inventories,
    SavedActor[] Actors, SavedEnterprise[] Enterprises, SavedEquipment[] Equipment, PlotView[] Plots,
    BuildingView[] Buildings, SavedOffer[] Offers, FlowView[] Flows, SettlementView[] Settlements,
    long[] CompletedJobs, CommandEnvelope[] Commands, CommandResult[] Results, string[] Pending,
    LedgerRecord[] Ledger, SimulationEvent[] Events, ulong MarketRandom,
    SavedMarketOffer[] MarketOffers, MarketAllocationView[] MarketAllocations, SavedBuyOffer[] BuyOffers, DeliveryContractView[] DeliveryContracts, long CapitalCompensation, SavedLogistics[] Logistics, CapitalDemandView? CapitalDemand, ElectionStateView Elections, SupportResearchState Support,
    int TravelQuantum = 0, CityStageState? CityStage = null, ResearchModification[]? Research = null,bool DeveloperPackage=false, System.Collections.Immutable.ImmutableArray<ResourceSiteView> Resources=default, ResourceClaim[]? ResourceClaims=null,TransferOperation[]? Transfers=null,TradeNegotiation[]? Negotiations=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StorageState? Storage=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DemolitionState? Demolition=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WorkshopCheckpoint? Workshops=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DevelopmentState? Development=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ProductionContentState? ProductionContent=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] FleetState? Fleet=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] RoadsState? Roads=null);

public sealed partial class SimulationEngine
{
    public const int CheckpointSchemaVersion = 25;
    public ScenarioDefinition Configuration() => JsonSerializer.Deserialize<ScenarioDefinition>(
        JsonSerializer.Serialize(_config, SimulationJson.Options), SimulationJson.Options)!;

    public long MaximumAcceptedSequence(ParticipantId actor) => _seenCommands.Values
        .Where(c => c.ActorId == actor).Select(c => c.LocalSequence).DefaultIfEmpty(0).Max();

    public string CreateCheckpointJson()
    {
        if (_stepping || !HasCommittedBoundary())
            throw new CheckpointException("NotCommittedTick");
        ValidateInvariants();
        var world = Inspect();
        var saved = new WorldCheckpoint(CheckpointSchemaVersion, RulesVersion, Configuration(), ConfigHash,
            ComputeStateHash(), Tick, _nextId, _eventSequence, _allocationRandom.State, _commandRandom.State,
            _laborRandom.State, _population, _unemployed, _lastGrowthPeriod,
            _economy.InitialMoney, _economy.ExternalIn, _economy.ExternalOut,
            _economy.Accounts.Values.OrderBy(a => a.Id).Select(a => new SavedAccount(a.Id, a.Kind, a.Balance,
                Ordered(a.Holds))).ToArray(),
            _economy.Inventories.Values.OrderBy(i => i.Id).Select(i => new SavedInventory(i.Id, i.Owner, i.Position,
                i.Capacity, Ordered(i.Stock), i.Reservations.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new SavedStockHold(x.Key, x.Value.Item, x.Value.Quantity)).ToArray(), Ordered(i.CapacityHolds),i.Overflow.Count==0?null:i.Overflow.Values.OrderBy(l=>l.Id).ToArray())).ToArray(),
            _actors.Values.OrderBy(a => a.Id).Select(a => new SavedActor(a.Id, a.Account, a.Inventory, a.Position, SaveJob(a.Job),
                a.OffsetX, a.OffsetZ, a.HeadingX, a.HeadingZ, a.FacingX, a.FacingZ)).ToArray(),
            _enterprises.Values.OrderBy(e => e.Definition.Id).Select(e => new SavedEnterprise(e.Definition, e.Input,
                e.Output, e.Recipe.Id, e.Enabled, e.Reason, SaveBatch(e.Batch), e.Completed, e.EnergyPaid, e.Group.View())).ToArray(),
            _equipment.Values.OrderBy(e => e.Id).Select(e => new SavedEquipment(e.Id, e.Owner, e.Position, e.Storage,
                e.Type.Id, e.Level, e.Mode, e.Reason, SaveBatch(e.Batch), e.Completed, e.PersonalJobId, e.Group.View(),e.Motion,e.Route,e.MovedAtTick)).ToArray(),
            world.Plots, BuildingViews(true),
            _offers.Values.OrderBy(o => o.Definition.Id).Select(o => new SavedOffer(o.Definition.Id, o.Remaining, o.Cutoff,
                o.Requests.Values.OrderBy(r => r.ActorId).Select(r => new SavedSale(r.ActorId, r.InventoryId, r.Quantity,
                    r.Reservation, r.CommandId)).ToArray(), o.SupplyRequests.Values.OrderBy(r => r.Supplier.Value).ToArray(), o.Window)).ToArray(), world.Flows, world.Settlements,
            _completedPersonalJobs.Order().ToArray(), _seenCommands.Values.OrderBy(c => c.CommandId, StringComparer.Ordinal).ToArray(),
            _results.Values.OrderBy(c => c.CommandId, StringComparer.Ordinal).ToArray(), _pending.Select(c => c.CommandId).ToArray(),
            Ledger(), _events.ToArray(), _marketRandom.State, SaveMarketOffers(), _marketAllocations.ToArray(), SaveBuyOffers(), _deliveryContracts.ToArray(), _capitalCompensation, SaveLogistics(), CapitalDemandState(), ElectionState(), InspectSupport(), _travelQuantum, _cityStage,_researchModifications.ToArray(),IsDeveloperBuild,ResourceSites(),_resourceClaims.Values.OrderBy(c=>c.JobId).ToArray(),_transfers.Values.OrderBy(t=>t.Id).ToArray(),InspectNegotiations(),!HasGroundStorage?null:CaptureStorage(),CaptureDemolition(),CaptureWorkshops(),InspectDevelopment(),InspectProductionContent(),InspectFleet(),InspectRoads());
        return JsonSerializer.Serialize(saved, SimulationJson.Options);
    }
    private static string MigrateTravel(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new CheckpointException("EmptyCheckpoint");
        bool changed = root["travelQuantum"] is null;
        if (changed) root["travelQuantum"] = 0;
        if (root["actors"] is JsonArray actors)
            foreach (var node in actors)
                if (node is JsonObject actor && actor["offsetX"] is null)
                {
                    actor["offsetX"] = TravelCenter; actor["offsetZ"] = TravelCenter;
                    actor["headingX"] = 0; actor["headingZ"] = 0; actor["facingX"] = 0; actor["facingZ"] = -1;
                    changed = true;
                }
        return changed ? root.ToJsonString(SimulationJson.Options) : json;
    }

    private static KeyValuePair<string, long>[] Ordered(Dictionary<string, long> values)
        => values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
    private static SavedJob? SaveJob(PersonalJob? j) => j is null ? null : new(j.Id, j.Kind, j.StartTick,
        j.CompletionTick, j.Path.ToArray(), j.PathIndex, j.SegmentTicks, j.Item, j.Quantity, j.CapacityHold, j.CommandId, j.EquipmentId);
    private static SavedBatch? SaveBatch(BatchState? b) => b is null ? null : new(b.Id, b.Hold, b.WorkRequired, b.WorkDone, b.EnergyPaid,b.RecipeId,b.Inputs,b.Terms);
    private static BatchState? LoadBatch(SavedBatch? b) => b is null ? null : new(b.Id, b.Hold, b.WorkRequired,b.RecipeId,b.Inputs)
        { WorkDone = b.WorkDone, EnergyPaid = b.EnergyPaid, Terms=b.Terms };
    private static void LoadGroup(EmploymentGroup g, EmploymentView v)
    {
        if (g.Id != v.Id.Value || g.Owner != v.OwnerId.Value || g.Role != v.Role || g.Capacity != v.Capacity)
            throw new CheckpointException("InvalidEmploymentReference");
        g.Headcount = v.Headcount; g.Wage = v.WageMinor; g.AttractionWage = v.AttractionWageMinor;
        g.PendingWage = v.PendingWageMinor; g.PayEffectiveTick = v.PayEffectiveTick;
        g.PendingAttractionWage = v.PendingAttractionWageMinor; g.AttractionEffectiveTick = v.AttractionEffectiveTick;
        g.MobilityCredit = v.MobilityCredit; g.Target = v.TargetHeadcount; g.Ready = v.Ready;
        g.ReadinessReason = v.ReadinessReason; g.AssessedAtTick = v.AssessedAtTick;
    }

    public static SimulationEngine FromCheckpointJson(string json)
    {
        if (json is null || json.Length > 64 * 1024 * 1024) throw new CheckpointException("CheckpointTooLarge");
        try
        {
            var s = JsonSerializer.Deserialize<WorldCheckpoint>(MigrateTravel(json), SimulationJson.Options)
                ?? throw new CheckpointException("EmptyCheckpoint");
            if(s.SchemaVersion<25&&(s.Roads is not null||s.Config.Economy?.Roads is not null))throw new CheckpointException("UnknownWorldSchema");
            if(s.SchemaVersion<24&&(s.Fleet is not null||s.Config.Economy?.Fleet is not null||s.Logistics.Any(l=>l.View.VehicleId!=0)))throw new CheckpointException("UnknownWorldSchema");
            if(s.SchemaVersion<23&&(s.ProductionContent is not null||s.Config.Economy?.ProductionContent is not null))throw new CheckpointException("UnknownWorldSchema");
            if(s.SchemaVersion<22 && (s.Development is not null||s.Config.Economy?.Development is not null||s.Config.BuildingTypes?.Any(t=>t.Levels.Any(l=>!l.Footprint.IsDefault))==true||s.Storage?.Construction.Any(c=>c.Development is not null)==true))throw new CheckpointException("UnknownWorldSchema");
            if (s.SchemaVersion < 21 && (s.Workshops is not null || s.Config.Economy?.Workshops is not null || s.Enterprises.Any(e=>e.Batch?.Terms is not null) || s.Equipment.Any(e=>e.Batch?.Terms is not null))) throw new CheckpointException("UnknownWorldSchema");
            if (s.SchemaVersion != CheckpointSchemaVersion && s.SchemaVersion!=24 && s.SchemaVersion!=23 && s.SchemaVersion!=22 && s.SchemaVersion!=21 && s.SchemaVersion!=20 && !(s.SchemaVersion==19&&s.Demolition is null&&s.Config.Economy?.Demolition is null&&s.Flows.All(f=>f.ConstructionRecovered==0))&&!(s.SchemaVersion==18&&s.Demolition is null&&s.Config.Economy?.Storage is null&&s.Storage is null&&s.Inventories.All(i=>i.Overflow is null)&&s.Flows.All(f=>f.Lost==0&&f.ConstructionRecovered==0))) throw new CheckpointException("UnknownWorldSchema");
            if (s.RulesVersion != RulesVersion) throw new CheckpointException("UnknownRulesVersion");
            var e = new SimulationEngine(s.Config); // Only validates/configures a fresh, unpublished instance.
            if (e.ConfigHash != s.ConfigHash) throw new CheckpointException("ConfigurationHashMismatch");
            e.RestoreWorld(s);
            e.ValidateCheckpointState(); e.ValidateCityStage(); e.ValidateInvariants();
            if (e.ComputeStateHash() != s.StateHash) throw new CheckpointException("StateHashMismatch");
            return e;
        }
        catch (CheckpointException) { throw; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or OverflowException or NullReferenceException or IndexOutOfRangeException)
        { throw new CheckpointException("InvalidWorldCheckpoint", ex); }
    }

    private void RestoreWorld(WorldCheckpoint s)
    {
#if !DELSK_DEVELOPER
        if(s.DeveloperPackage)throw new CheckpointException("DeveloperSessionUnsupported");
#endif
        _researchModifications.AddRange(s.Research??[]);
        if (s.CityStage is not { } stage || stage.Stage is < 0 or > 3 || stage.Candidate is < 0 or > 3 || stage.SinceTick < 0 || stage.SinceTick > s.Tick)
            throw new CheckpointException("InvalidCityStage");
        _cityStage = stage;
        foreach(var n in s.Negotiations??[])_negotiations.Add(n.Id,n);
        Tick = s.Tick; _travelQuantum = s.TravelQuantum; _nextId = s.NextId; _eventSequence = s.EventSequence;
        _allocationRandom.RestoreState(s.AllocationRandom); _commandRandom.RestoreState(s.CommandRandom);
        _laborRandom.RestoreState(s.LaborRandom); _population = s.Population; _unemployed = s.Unemployed;
        _lastGrowthPeriod = s.LastGrowthPeriod;
        _economy.Accounts.Clear(); _economy.Inventories.Clear(); _economy.Ledger.Clear();
        _economy.RestoreTotals(s.InitialMoney, s.ExternalIn, s.ExternalOut);
        foreach (var a in s.Accounts)
        {
            var value = new MoneyAccount(a.Id, a.Kind, a.Balance);
            foreach (var h in a.Holds) value.Holds.Add(h.Key, h.Value);
            _economy.Accounts.Add(a.Id, value);
        }
        foreach (var i in s.Inventories)
        {
            var value = new Inventory(i.Id, i.Owner, i.Position, i.Capacity);
            foreach (var stock in i.Stock) value.Stock.Add(stock.Key, stock.Value);
            foreach (var h in i.CapacityHolds) value.CapacityHolds.Add(h.Key, h.Value);
            foreach (var h in i.Reservations) value.Reservations.Add(h.Id, new(h.Item, h.Quantity));
            foreach(var lot in i.Overflow??[])value.Overflow.Add(lot.Id,lot);
            _economy.Inventories.Add(i.Id, value);
        }
        _economy.Ledger.AddRange(s.Ledger.Select(l => new LedgerEntry(l.Tick, l.DebitAccount.Value, l.CreditAccount.Value, l.AmountMinor, l.Reason)));
        _actors.Clear();
        foreach (var a in s.Actors)
        {
            var value = new ActorState(a.Id, a.Account, a.Inventory, a.Position)
            {
                OffsetX = a.OffsetX, OffsetZ = a.OffsetZ, HeadingX = a.HeadingX, HeadingZ = a.HeadingZ,
                FacingX = a.FacingX, FacingZ = a.FacingZ
            };
            if (a.Job is { } j) value.Job = new(j.Id, j.Kind, j.StartTick, j.CompletionTick, j.Path.ToArray(), j.Item,
                j.Quantity, j.CapacityHold, j.CommandId, j.EquipmentId) { PathIndex = j.PathIndex, SegmentTicks = j.SegmentTicks };
            _actors.Add(a.Id, value);
        }
        var recipes = _config.Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);
        _enterprises.Clear();
        foreach (var v in s.Enterprises)
        {
            var value = new EnterpriseState(v.Definition, v.Input, v.Output, recipes[v.RecipeId])
                { Enabled = v.Enabled, Reason = v.Reason, Batch = LoadBatch(v.Batch), Completed = v.Completed, EnergyPaid = v.EnergyPaid };
            LoadGroup(value.Group, v.Group); _enterprises.Add(v.Definition.Id, value);
        }
        RestoreWorkshops(s.Workshops);
        _equipment.Clear();
        foreach (var v in s.Equipment)
        {
            var type = (_config.EquipmentTypes ?? []).Single(t => t.Id == v.TypeId);
            var value = new EquipmentState(v.Id, v.Owner, v.Position, v.Storage, type, v.Level)
                { Mode = v.Mode, Reason = v.Reason, Batch = LoadBatch(v.Batch), Completed = v.Completed, PersonalJobId = v.PersonalJobId, Motion=v.Motion, Route=v.Route,MovedAtTick=v.MovedAtTick };
            LoadGroup(value.Group, v.Group); _equipment.Add(v.Id, value);
        }
        if (s.Plots.Length != _plots.Count) throw new CheckpointException("InvalidPlotCount");
        var seenPlots = new HashSet<long>();
        foreach (var v in s.Plots)
        {
            if (!seenPlots.Add(v.Id.Value)) throw new CheckpointException("DuplicatePlot");
            var p = _plots[v.Id.Value];
            if (p.Definition.Position != v.Position || p.Definition.PriceMinor != v.PriceMinor || p.Definition.IsCorridor != v.IsCorridor ||
                p.Definition.ShopParticipantId != v.ShopParticipantId?.Value || p.Definition.IsShopSite != v.IsShopSite) throw new CheckpointException("InvalidPlotDefinition");
            p.Owner = v.OwnerId?.Value; p.ReservedBy = v.ReservedBy?.Value; p.Until = v.ReservationUntilTick; p.Building = v.BuildingId?.Value;
        }
        _preparingDemolition.Clear();foreach(long id in s.Demolition?.Preparing??[])_preparingDemolition.Add(id);
        _demolitionReceipts.Clear();_demolitionReceipts.AddRange(s.Demolition?.Receipts??[]);
        _buildings.Clear();
        foreach (var v in s.Buildings)
        {
            var type = _buildingTypes[v.TypeId];
            if (v.Kind != type.Kind) throw new CheckpointException("InvalidBuildingType");
            _buildings.Add(v.Id.Value, new(v.Id.Value, v.OwnerId.Value, v.Position, type, v.Level,
                v.InputId.Value, v.OutputId?.Value, v.Footprint.ToArray()));
        }
        _groundStorage.Clear();foreach(var g in s.Storage?.Ground??[])_groundStorage.Add(g.PlotId,g.InventoryId);
        _storageConstruction.Clear();foreach(var c in s.Storage?.Construction??[])_storageConstruction.Add(c.Id,c);
        RestoreDevelopment(s.Development);RestoreProductionContent(s.ProductionContent);
        if(s.Plots.Any(p=>p.ExpansionFor!=ExpansionClaim(p.Position)?.Id))throw new CheckpointException("InvalidExpansionClaim");
        ApplyBuildingObstacles();
        RestoreCapitalDemand(s.CapitalDemand);
        if (s.Offers.Length != _offers.Count) throw new CheckpointException("InvalidOfferCount");
        var seenOffers = new HashSet<long>();
        foreach (var v in s.Offers)
        {
            if (!seenOffers.Add(v.Id)) throw new CheckpointException("DuplicateOffer");
            var o = _offers[v.Id]; o.Remaining = v.Remaining; o.Cutoff = v.Cutoff; o.Window = v.Window; o.Requests.Clear(); o.SupplyRequests.Clear();
            foreach (var r in v.SupplyRequests) o.SupplyRequests.Add(r.Supplier.Value, r);
            foreach (var r in v.Requests) o.Requests.Add(r.ActorId, new(r.ActorId, r.InventoryId, r.Quantity, r.Reservation, r.CommandId));
        }
        _flows.Clear();
        foreach (var f in s.Flows) _flows.Add(f.ItemId, new(f.ItemId) { Initial = f.Initial, Harvested = f.Harvested,
            RecipeInput = f.RecipeInput, Produced = f.Produced, Exported = f.Exported, ConstructionInput = f.ConstructionInput,Lost=f.Lost,ConstructionRecovered=f.ConstructionRecovered });
        _settlements.Clear(); _settlements.AddRange(s.Settlements);
        _completedPersonalJobs.Clear(); foreach (long id in s.CompletedJobs)
            if (!_completedPersonalJobs.Add(id)) throw new CheckpointException("DuplicateCompletedJob");
        _seenCommands.Clear(); _actorSequences.Clear(); _pending.Clear(); _results.Clear();
        foreach (var c in s.Commands)
        {
            _seenCommands.Add(c.CommandId, c);
            if (!_actorSequences.Add((c.ActorId.Value, c.LocalSequence))) throw new CheckpointException("DuplicateCommandSequence");
        }
        foreach (var r in s.Results) _results.Add(r.CommandId, r);
        foreach (string id in s.Pending) _pending.Add(_seenCommands[id]);
        _events.Clear(); _events.AddRange(s.Events);IndexImports();
        if(s.Flows.Any(f=>f.Imported!=ImportQuantity(f.ItemId)))throw new CheckpointException("InvalidImportFlow");
        RestoreMarket(s.MarketOffers, s.MarketAllocations, s.MarketRandom);
        RestoreDelivery(s.BuyOffers, s.DeliveryContracts, s.CapitalCompensation);
        RestoreFleet(s.Fleet);RestoreLogistics(s.Logistics);RestoreRoads(s.Roads);
        RestoreElections(s.Elections);
        RestoreSupport(s.Support);
        RestoreResources(s.Resources,s.ResourceClaims??[]);
        _transfers.Clear();foreach(var t in s.Transfers??[])_transfers.Add(t.Id,t);
        _phase = Tick == 0 ? 0 : 9;
    }
}

using System.Text.Json;

namespace Simulation.Core;

/// <summary>Authoritative single-thread simulation. No renderer, files, model or wall clock.</summary>
public sealed partial class SimulationEngine
{
    public const string RulesVersion = "participant-trade-1";
    private readonly ScenarioDefinition _config;
    private readonly GridMap _map;
    private readonly Dictionary<string, ItemDefinition> _items;
    private readonly EconomyStore _economy;
    private readonly Dictionary<long, ActorState> _actors = [];
    private readonly Dictionary<long, EnterpriseState> _enterprises = [];
    private readonly Dictionary<long, CapitalOffer> _offers = [];
    private readonly Dictionary<string, ItemFlow> _flows;
    private readonly List<SimulationEvent> _events = [];
    private readonly List<SettlementView> _settlements = [];
    private readonly List<CommandEnvelope> _pending = [];
    private readonly Dictionary<string, CommandEnvelope> _seenCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandResult> _results = new(StringComparer.Ordinal);
    private readonly HashSet<(long Actor, long Sequence)> _actorSequences = [];
    private readonly HashSet<long> _completedPersonalJobs = [];
    private readonly DeterministicRandom _allocationRandom;
    private readonly DeterministicRandom _commandRandom;
    private long _nextId = 1000;
    private long _eventSequence;
    private int _phase;
    private bool _stepping;
    private readonly long _treasury;
    private readonly long _wages;
    private readonly long _capital;
    public long Tick { get; private set; }
    public string ConfigHash { get; }

    public SimulationEngine(ScenarioDefinition config)
    {
        ScenarioValidation.Validate(config);
        // Caller cannot change the running rules by mutating a configuration array.
        _config = JsonSerializer.Deserialize<ScenarioDefinition>(
            JsonSerializer.Serialize(config, SimulationJson.Options), SimulationJson.Options)!;
        ConfigHash = ScenarioValidation.Hash(_config);
        _map = new(_config);
        _items = _config.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        _flows = _items.Keys.ToDictionary(i => i, i => new ItemFlow(i), StringComparer.Ordinal);
        _economy = new(_items);
        _allocationRandom = new(unchecked(_config.Seed ^ 0xA110CA7EUL));
        _commandRandom = new(unchecked(_config.Seed ^ 0xC011A11DUL));
        _laborRandom = new(unchecked(_config.Seed ^ 0x1AB012UL));
        _marketRandom = new(unchecked(_config.Seed ^ 0xA0C710UL));
        _population = _config.Population;
        _treasury = CreateAccount("Treasury"); _wages = CreateAccount("WagesSink");
        _capital = CreateAccount("CapitalBudget");
        foreach (var p in _config.Participants.OrderBy(p => p.Id))
        {
            long account = CreateAccount("Participant", p.CashMinor);
            _economy.InitialMoney = checked(_economy.InitialMoney + p.CashMinor);
            long inv = _nextId++;
            _economy.Inventories.Add(inv, new(inv, p.Id, p.Position, p.InventoryCapacity));
            _actors.Add(p.Id, new(p.Id, account, inv, p.Position));
        }
        var recipes = _config.Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var e in _config.Enterprises.OrderBy(e => e.Id))
        {
            long input = _nextId++, output = _nextId++;
            _economy.Inventories.Add(input, new(input, e.OwnerId, e.Position, e.InputCapacity));
            _economy.Inventories.Add(output, new(output, e.OwnerId, e.Position, e.OutputCapacity));
            _enterprises.Add(e.Id, new(e, input, output, recipes[e.RecipeId]));
        }
        foreach (var node in _config.ResourceNodes) _resources.Add((node.Position,node.ItemId), new(node));
        InitializeLandAndBuildings();
        foreach (var o in _config.CapitalOffers.OrderBy(o => o.Id))
        {
            string hold = $"capital-offer:{o.Id}";
            long budget = checked(o.Quantity * o.UnitPriceMinor);
            var tx = _economy.Begin(0);
            tx.CreditExternal(_capital, budget, "CapitalOrderBudget");
            tx.HoldMoney(_capital, hold, budget); tx.Commit();
            _offers.Add(o.Id, new(o, hold));
        }
        _unemployed = checked(_population - EmploymentGroups().Sum(g => (long)g.Headcount));
        InitializeCapitalDemand();
        InitializeElections();
        InitializeSupport();
        Emit("WorldCreated");
        ValidateInvariants();
    }
    private long CreateAccount(string kind, long balance = 0)
    {
        long id = _nextId++;
        _economy.Accounts.Add(id, new(id, kind, balance)); return id;
    }
    private void Emit(string type, long? actor = null, long entity = 0, string item = "",
        long quantity = 0, long money = 0, string reason = "", string? command = null)
        => _events.Add(new(++_eventSequence, Tick, _phase, type,
            actor.HasValue ? new ParticipantId(actor.Value) : null, entity, item, quantity, money, reason, command));

    public SimulationEvent[] EventsSince(long afterSequence) => _events.Where(e => e.Sequence > afterSequence).ToArray();
    public LedgerRecord[] Ledger() => _economy.Ledger.Select(x => new LedgerRecord(x.Tick,
        new(x.DebitAccount), new(x.CreditAccount), x.AmountMinor, x.Reason)).ToArray();
    public CommandResult? Result(string commandId) => _results.GetValueOrDefault(commandId);
    public CommandResult? Enqueue(ParticipantId authenticatedActor, CommandEnvelope command)
    {
        if (command is null || command.Action is null || string.IsNullOrWhiteSpace(command.CommandId) ||
            command.CommandId.Length > 160 || command.Action.ItemId is null || command.Action.ItemId.Length > 50 ||
            command.Action.BuildingTypeId is null || command.Action.BuildingTypeId.Length > 50 ||
            command.Action.RecipeId is null || command.Action.RecipeId.Length > 50 ||
            command.Action.EquipmentTypeId is null || command.Action.EquipmentTypeId.Length > 50 ||
            (command.Action.Stops is { } stops && (stops.IsDefault || stops.Length > 32)) ||
            (command.Action.Terms is {} terms && (terms.ItemId is null || terms.ItemId.Length>50 ||
                !terms.Compensation.IsDefault && (terms.Compensation.Length>8 || terms.Compensation.Any(m=>m is null || m.ItemId is null || m.ItemId.Length>50)))) ||
            (command.Action.Compensation is { } compensation && (compensation.IsDefault || compensation.Length>8 || compensation.Any(m=>m is null || m.ItemId is null || m.ItemId.Length>50))))
            return new(command?.CommandId ?? "", ResultStatus.Rejected, "InvalidEnvelope", Tick);
        if (authenticatedActor != command.ActorId || !_actors.ContainsKey(authenticatedActor.Value))
            return new(command.CommandId, ResultStatus.Rejected, "NotAvailable", Tick);
        if (_seenCommands.TryGetValue(command.CommandId, out var previous))
            return previous == command ? _results.GetValueOrDefault(command.CommandId)
                : new(command.CommandId, ResultStatus.Rejected, "CommandIdConflict", Tick);
        if (command.LocalSequence <= 0 || command.IssuedAtTick < 0 || command.IssuedAtTick > Tick ||
            (command.NotBeforeTick <= command.IssuedAtTick && !IsImmediateBallot(command)) || command.ExpiresAtTick < command.NotBeforeTick ||
            command.NotBeforeTick > _config.MaxTicks || !Enum.IsDefined(command.Action.Kind))
            return new(command.CommandId, ResultStatus.Rejected, "InvalidEnvelope", Tick);
        if (WaitingForBallots && !IsImmediateBallot(command)) return new(command.CommandId, ResultStatus.Rejected, "ElectionInProgress", Tick);
        if (IsImmediateBallot(command) && (!WaitingForBallots || command.IssuedAtTick != Tick))
            return new(command.CommandId, ResultStatus.Rejected, "ElectionClosed", Tick);
        if (!_actorSequences.Add((command.ActorId.Value, command.LocalSequence)))
            return new(command.CommandId, ResultStatus.Rejected, "SequenceReused", Tick);
        _seenCommands.Add(command.CommandId, command);
        if (WaitingForBallots)
        {
            _phase = 9; Execute(command); ValidateInvariants(); return _results[command.CommandId];
        }
        _pending.Add(command); return null;
    }
    public void Step()
    {
        if (WaitingForBallots) return;
        if (_stepping) throw new InvalidOperationException("Step is not reentrant");
        if (Tick >= _config.MaxTicks) throw new InvalidOperationException("Scenario tick limit reached");
        _stepping = true;
        try
        {
            ++Tick; _phase = 0; AdvanceResourceIntervals(); ApplyWages();
            var due = _pending.Where(c => c.NotBeforeTick <= Tick).ToArray();
            foreach (var c in due) _pending.Remove(c);
            var actors = due.Select(c => c.ActorId.Value).Distinct().Order().ToArray();
            _commandRandom.Shuffle(actors);
            var rank = actors.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            var ordered = due.OrderBy(c => rank[c.ActorId.Value]).ThenBy(c => c.LocalSequence).ToArray();
            var allowed = new List<CommandEnvelope>();
            foreach (var group in ordered.GroupBy(c => c.ActorId.Value))
                foreach (var c in group.Select((c, n) => (c, n)))
                    if (c.n >= _config.MaxCommandsPerActorTick) Reject(c.c, "CommandBudgetExceeded");
                    else if (c.c.ExpiresAtTick < Tick) Reject(c.c, "CommandExpired");
                    else allowed.Add(c.c);
            _phase = 1;
            foreach (var c in allowed.Where(c => IsManagement(c.Action.Kind))) Execute(c);
            _phase = 2; AdvanceEquipmentMovements(); AdvanceMovements(); AdvanceLogisticsMovements();
            _phase = 3; AcceptLogisticsArrivals(); ReserveSupportResources();
            foreach (var c in allowed.Where(c => !IsManagement(c.Action.Kind))) Execute(c);
            AdvanceTransfers(); ReserveSupportResources();
            _phase = 4; AdvanceHarvests(); AdvanceWork();
            _phase = 5; AdvanceLabor();
            _phase = 6; CloseCapitalWindows(); AdvanceMarketWindows(); CloseBuyWindows(); AdvanceCapitalDemand();
            _phase = 7; ExpirePlotReservations(); ExpirePurchases(); ExpireDelivery(); ExpireCapitalOrders(); ExpireSupport(); ExpireNegotiations();
            _phase = 8; AdvanceElections();
            _phase = 9; AdvanceCityStage(); ValidateInvariants(); Emit("TickCommitted");
        }
        finally { _stepping = false; }
    }
    private void Reject(CommandEnvelope c, string reason)
    {
        _results[c.CommandId] = new(c.CommandId, ResultStatus.Rejected, reason, Tick);
        Emit("CommandRejected", c.ActorId.Value, reason: reason, command: c.CommandId);
    }
    private void Execute(CommandEnvelope c)
    {
        try
        {
            if (c.Action.Quantity < 0) throw new RuleViolation("InvalidQuantity");
            ActorState actor = _actors[c.ActorId.Value];
            var access = AccessFor(c.ActorId, c.Action);
            // Existing local handlers retain their established privacy/busy/resource error order.
            if (!access.Allowed && (c.Action.Kind == ActionKind.SetWage || NewTradingAction(c.Action.Kind)))
                throw new RuleViolation(access.ReasonCode);
            switch (c.Action.Kind)
            {
                case ActionKind.SetTransportRoute: SetTransportRoute(actor,c); break;
                case ActionKind.StopTransfer: StopTransfer(actor, c); break;
                case ActionKind.MoveEquipment: MoveEquipment(actor, c); break;
                case ActionKind.SetExtractionRoute: SetExtractionRoute(actor, c); break;
                case ActionKind.BuyResource: AcquireResource(actor, c, false); break;
                case ActionKind.LeaseResource: AcquireResource(actor, c, true); break;
                case ActionKind.MoveTo: StartMove(actor, c); break;
                case ActionKind.Harvest: StartHarvest(actor, c); break;
                case ActionKind.StartHarvesting: StartHarvesting(actor, c); break;
                case ActionKind.StopHarvest: StopHarvest(actor, c); break;
                case ActionKind.Transfer: Transfer(actor, c); break;
                case ActionKind.SetProductionEnabled: SetProduction(actor, c); break;
                case ActionKind.SellAvailableToCapital: StartSale(actor, c); break;
                case ActionKind.CancelCapitalSale: CancelSale(actor, c); break;
                case ActionKind.ReservePlot: ReservePlot(actor, c); break;
                case ActionKind.BuyPlot: BuyPlot(actor, c); break;
                case ActionKind.CancelPlotReservation: CancelPlotReservation(actor, c); break;
                case ActionKind.Build: Build(actor, c); break;
                case ActionKind.UpgradeBuilding: UpgradeBuilding(actor, c); break;
                case ActionKind.SetRecipe: SetRecipe(actor, c); break;
                case ActionKind.SetWage: SetWage(actor, c); break;
                case ActionKind.BuyEquipment: BuyEquipment(actor, c); break;
                case ActionKind.UpgradeEquipment: UpgradeEquipment(actor, c); break;
                case ActionKind.SetEquipmentMode: SetEquipmentMode(actor, c); break;
                case ActionKind.ProposeTrade: ProposeTrade(actor,c); break;
                case ActionKind.CounterTrade: CounterTrade(actor,c); break;
                case ActionKind.AcceptTrade: AcceptTrade(actor,c); break;
                case ActionKind.RejectTrade: RejectTrade(actor,c); break;
                case ActionKind.CreateSellOffer: CreateSellOffer(actor, c); break;
                case ActionKind.SubmitBuyRequest: SubmitBuyRequest(actor, c); break;
                case ActionKind.RaiseBid: RaiseBid(actor, c); break;
                case ActionKind.WithdrawBid: WithdrawBid(actor, c); break;
                case ActionKind.CollectPurchase: CollectPurchase(actor, c); break;
                case ActionKind.CreateBuyOffer: CreateBuyOffer(actor, c); break;
                case ActionKind.AcceptDeliveryQuantity: AcceptDeliveryQuantity(actor, c); break;
                case ActionKind.WithdrawSupplyRequest: WithdrawSupplyRequest(actor, c); break;
                case ActionKind.CancelBuyOffer: CancelBuyOffer(actor, c); break;
                case ActionKind.DeliverContract: DeliverContract(actor, c); break;
                case ActionKind.BuyLogistics: BuyLogistics(actor, c); break;
                case ActionKind.UpgradeLogistics: UpgradeLogistics(actor, c); break;
                case ActionKind.SetLogisticsContract: SetLogisticsContract(actor, c); break;
                case ActionKind.SetLogisticsStorage: SetLogisticsStorage(actor, c); break;
                case ActionKind.SetLogisticsProduction: SetLogisticsOwnDestination(actor, c, production: true); break;
                case ActionKind.SetLogisticsInbound: SetLogisticsInbound(actor, c); break;
                case ActionKind.QueueLogisticsJob: QueueLogisticsJob(actor, c); break;
                case ActionKind.SetLogisticsEnabled: SetLogisticsEnabled(actor, c); break;
                case ActionKind.ProposeSupport: ProposeSupport(actor, c); break;
                case ActionKind.AcceptSupport: AcceptSupport(actor, c); break;
                case ActionKind.RejectSupport: RejectSupport(actor, c); break;
                case ActionKind.SignSupport: SignSupport(actor, c); break;
                case ActionKind.SetVoteChoice: SetVoteChoice(actor, c); break;
                case ActionKind.ContinueAfterVictory: ContinueAfterVictory(actor, c); break;
                default: throw new RuleViolation("UnknownAction");
            }
            _results[c.CommandId] = new(c.CommandId, ResultStatus.Accepted, "Accepted", Tick);
            Emit("CommandAccepted", actor.Id, command: c.CommandId);
        }
        catch (RuleViolation ex) { Reject(c, ex.ReasonCode); }
        catch (OverflowException) { Reject(c, "ArithmeticOverflow"); }
    }
    private bool HasSale(long actor) => _offers.Values.Any(o => o.Requests.ContainsKey(actor));
    private void RequireIdle(ActorState actor)
    { if (actor.Job is not null || HasSale(actor.Id) || HasTransfer(actor.Id)) throw new RuleViolation("ParticipantBusy"); }
    private void StartMove(ActorState actor, CommandEnvelope c)
    {
        if (actor.Job is not null && !IsHarvest(actor.Job) || HasSale(actor.Id)) RequireIdle(actor);
        Cell[] path = _map.FindPath(actor.Position, c.Action.Target);
        long arrival = checked(Tick + checked((long)(path.Length - 1) * _config.MoveTicksPerCell));
        if (path.Length > 1) foreach (var t in _transfers.Values.Where(t=>t.Owner.Value==actor.Id).ToArray()) CancelTransfer(t,"MovementStarted");
        if (path.Length > 1 && IsHarvest(actor.Job)) CancelHarvest(actor, "MovementStarted");
        actor.HeadingX = 0; actor.HeadingZ = 0; actor.OffsetX = TravelCenter; actor.OffsetZ = TravelCenter;
        if (path.Length == 1) return;
        actor.FacingX = Math.Sign(path[1].X - path[0].X); actor.FacingZ = Math.Sign(path[1].Z - path[0].Z);
        actor.Job = new(_nextId++, "Move", Tick, arrival, path, "", 0, "", c.CommandId);
        Emit("MovementStarted", actor.Id, actor.Job.Id, reason: arrival.ToString(System.Globalization.CultureInfo.InvariantCulture), command: c.CommandId);
    }
    private void StartHarvest(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        if (c.Action.Equipment.Value != 0) { StartEquipmentHarvest(actor, c); return; }
        RequireActionLocation(actor, c.Action, "NoResourceNode");
        long end = checked(Tick + checked(c.Action.Quantity * _config.HarvestTicksPerUnit));
        RequireResource(actor.Id, actor.Position, c.Action.ItemId, c.Action.Quantity);
        string hold = $"harvest:{c.CommandId}";
        var tx = _economy.Begin(Tick);
        tx.ReserveCapacity(actor.Inventory, hold, c.Action.ItemId, c.Action.Quantity); tx.Commit();
        actor.Job = new(_nextId++, "Harvest", Tick, end, [], c.Action.ItemId, c.Action.Quantity, hold, c.CommandId);
        ClaimResource(actor.Job.Id, actor.Position, c.Action.Quantity);
        Emit("HarvestStarted", actor.Id, actor.Job.Id, c.Action.ItemId, c.Action.Quantity, command: c.CommandId);
    }
    private void Transfer(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        if (!_economy.Inventories.TryGetValue(c.Action.Source.Value, out var source) ||
            !_economy.Inventories.TryGetValue(c.Action.Destination.Value, out var destination) ||
            source.Owner != actor.Id || destination.Owner != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (_logistics.Values.Any(l => l.Cargo == source.Id || l.Cargo == destination.Id))
            throw new RuleViolation("ManagedCargo");
        RequireActionLocation(actor, c.Action);
        if (_equipment.Values.Any(e => (e.Storage==source.Id || e.Storage==destination.Id) && e.Motion is not null)) throw new RuleViolation("EquipmentBusy");
        BeginTransfer(actor,c,source.Id,destination.Id,c.Action.ItemId,c.Action.Quantity);
    }
    private void SetProduction(ActorState actor, CommandEnvelope c)
    {
        if (!_enterprises.TryGetValue(c.Action.Enterprise.Value, out var e) || e.Definition.OwnerId != actor.Id)
            throw new RuleViolation("NotAvailable");
        e.Enabled = c.Action.Enabled;
        if (!e.Enabled) e.Reason = "UserPaused";
        Emit("ProductionSettingChanged", actor.Id, e.Definition.Id, reason: e.Enabled ? "Enabled" : "UserPaused", command: c.CommandId);
    }
    private void AdvanceMovements()
    {
        foreach (var actor in _actors.Values.OrderBy(a => a.Id))
        {
            if (actor.Job is not { Kind: "Move" } job || job.StartTick >= Tick) continue;
            ++job.SegmentTicks;
            if (job.SegmentTicks < _config.MoveTicksPerCell) continue;
            job.SegmentTicks = 0; ++job.PathIndex; actor.Position = job.Path[job.PathIndex];
            actor.OffsetX = TravelCenter; actor.OffsetZ = TravelCenter;
            _economy.Inventories[actor.Inventory].Position = actor.Position;
            if (Tick >= job.CompletionTick) CompleteMovement(actor.Id, job.Id);
        }
    }
    internal bool CompleteMovement(long actorId, long jobId)
    {
        if (!_actors.TryGetValue(actorId, out var actor) || actor.Job is not { Kind: "Move" } job ||
            job.Id != jobId || Tick < job.CompletionTick || !_completedPersonalJobs.Add(jobId)) return false;
        actor.Position = job.Path[^1]; actor.OffsetX = TravelCenter; actor.OffsetZ = TravelCenter;
        _economy.Inventories[actor.Inventory].Position = actor.Position;
        actor.Job = null;
        Emit("MovementArrived", actor.Id, jobId, command: job.CommandId); return true;
    }
    private void AdvanceHarvests()
    {
        foreach (var actor in _actors.Values.OrderBy(a => a.Id))
        {
            if (actor.Job is not { } job || !IsHarvest(job) || Tick < job.CompletionTick) continue;
            long total = checked(_flows[job.Item].Harvested + job.Quantity);
            var tx = _economy.Begin(Tick);
            tx.ReleaseCapacity(actor.Inventory, job.CapacityHold); tx.AddStock(actor.Inventory, job.Item, job.Quantity); tx.Commit();
            ConsumeResource(job.Id);
            if (job.EquipmentId != 0)
            {
                _equipment[job.EquipmentId].PersonalJobId = 0;
                Emit("PersonalEquipmentHarvestCompleted", actor.Id, job.EquipmentId, job.Item, job.Quantity, command: job.CommandId);
            }
            _flows[job.Item].Harvested = total;
            _completedPersonalJobs.Add(job.Id); actor.Job = null;
            Emit("HarvestCompleted", actor.Id, job.Id, job.Item, job.Quantity, command: job.CommandId);
            if (job.Kind == "HarvestLoop")
            {
                try { BeginHarvestCycle(actor, job.Item, job.CommandId); }
                catch (RuleViolation ex) when (ex.ReasonCode is "NoCapacity" or "ResourceDepleted" or "NoResourceRights")
                { Emit("HarvestLoopStopped", actor.Id, job.Id, job.Item, reason: ex.ReasonCode, command: job.CommandId); }
            }
        }
    }
    public WorldView Inspect()
    {
        return new(Tick, _nextId, _allocationRandom.State, ConfigHash,
            _economy.Accounts.Values.OrderBy(a => a.Id).Select(a => new AccountView(new(a.Id), a.Kind, a.Balance, a.Reserved)).ToArray(),
            _economy.Inventories.Values.OrderBy(i => i.Id).Select(i => new InventoryView(new(i.Id), new(i.Owner), i.Position,
                i.Capacity, i.Occupied(_items), i.Incoming, i.Stock.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new StockView(x.Key, x.Value, i.Reserved(x.Key))).ToArray())).ToArray(),
            _actors.Values.OrderBy(a => a.Id).Select(a => new ParticipantView(new(a.Id), new(a.Account), new(a.Inventory), a.Position,
                a.Job is not null || HasSale(a.Id) || HasTransfer(a.Id), a.Job is { Kind: "Move" } move ? move.Path[^1] : null, a.Job?.CompletionTick)).ToArray(),
            _enterprises.Values.OrderBy(e => e.Definition.Id).Select(e => new EnterpriseView(new(e.Definition.Id), new(e.Definition.OwnerId),
                new(e.Input), new(e.Output), e.Enabled, e.Group.Headcount, e.Reason, e.Batch?.Id ?? 0,
                e.Batch?.WorkDone ?? 0, e.Batch?.WorkRequired ?? 0, e.Completed, e.EnergyPaid,
                e.Recipe.Id, e.Definition.Level, e.Definition.Capacity, e.Definition.WorkRateMultiplier)).ToArray(),
            _flows.Values.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => new FlowView(f.Id, f.Initial, f.Harvested,
                f.RecipeInput, f.Produced, f.Exported, f.ConstructionInput)).ToArray(),
            _offers.Values.OrderBy(o => o.Definition.Id).Select(o => new CapitalOfferView(o.Definition.Id, o.Definition.ItemId,
                o.Remaining, o.Definition.UnitPriceMinor, o.Definition.Position, o.Cutoff,
                (long)Int128.Min(long.MaxValue, o.Requests.Values.Aggregate((Int128)0, (n, r) => n + r.Quantity) +
                    o.SupplyRequests.Values.Aggregate((Int128)0, (n, r) => n + r.Quantity)), o.Window,
                o.Definition.Quantity, o.CreatedTick, o.ExpiresAtTick, o.ReleasedQuantity)).ToArray(),
            _settlements.ToArray(), _population, _unemployed,
            _economy.InitialMoney, _economy.ExternalIn, _economy.ExternalOut, LandViews(), BuildingViews(),
            EmploymentGroups().OrderBy(g => g.Id).Select(g => g.View()).ToArray(),
            checked(_population - _config.Population), _lastGrowthPeriod, EquipmentViews(), MarketOfferViews(), MarketBidViews(), _marketAllocations.ToArray(), BuyOfferViews(), SupplyRequestViews(), _deliveryContracts.ToArray(), LogisticsViews(), CapitalDemandState(), ElectionState());
    }
    public ParticipantObservation Observe(ParticipantId actorId)
    {
        if (!_actors.ContainsKey(actorId.Value)) throw new ArgumentException("Participant not available");
        var view = Inspect(); var actor = view.Participants.Single(a => a.Id == actorId);
        return new(Tick, actor, view.Accounts.Single(a => a.Id == actor.AccountId),
            view.Inventories.Where(i => i.OwnerId == actorId).ToArray(), view.Enterprises.Where(e => e.OwnerId == actorId).ToArray(), view.CapitalOffers,
            view.Plots, view.Buildings.Where(b => b.OwnerId == actorId).ToArray(), CopyBuildingCatalog(),
            view.EmploymentGroups.Where(g => g.OwnerId == actorId).ToArray(),
            view.EmploymentGroups.Select(g => new PublicEmploymentView(g.Id, g.OwnerId, g.Role, g.Headcount, g.Capacity, g.PendingWageMinor ?? g.WageMinor)).ToArray(),
            view.Population, view.Unemployed, view.Equipment.Where(e => e.OwnerId == actorId).ToArray(), CopyEquipmentCatalog(), view.MarketOffers.Where(o => o.Status != MarketOfferStatus.Closed || o.Seller == actorId || view.MarketAllocations.Any(a => a.OfferId == o.Id && a.Buyer == actorId && a.RemainingQuantity > 0)).ToArray(),
            view.MarketBids.Where(b => b.Buyer == actorId).ToArray(),
            view.MarketAllocations.Where(a => a.Buyer == actorId || a.Seller == actorId).ToArray(), Market, view.BuyOffers.Where(o => o.Status == "Open" || o.Buyer == actorId).ToArray(),
            view.SupplyRequests.Where(r => r.Supplier == actorId).ToArray(),
            view.DeliveryContracts.Where(d => d.Supplier == actorId || d.Buyer == actorId).ToArray(), Delivery, view.Logistics.Where(l => l.OwnerId == actorId).ToArray(), Logistics, PublicCapitalPrices(false), PublicCapitalPrices(true), ObserveElections(actorId), ObserveSupport(actorId), PublicEconomy()) { Resources = ResourceSites(),Negotiations=ObserveNegotiations(actorId),NegotiationRules=NegotiationRules,TradingParticipants=TradingParticipants() };
    }
    public string ComputeStateHash()
    {
        object payload = StatePayload();
        var travel = TravelDeviation();
        return ScenarioValidation.Hash(travel is null ? payload : new { Payload = payload, Travel = travel });
    }

    private object StatePayload() => new
    {
        Transfers = _transfers.Values.OrderBy(t=>t.Id).ToArray(), EquipmentMotionIntervals = _equipment.Values.OrderBy(e=>e.Id).Select(e=>new { e.Id,e.MovedAtTick }).ToArray(), Resources = ResourceSites(), ResourceClaims = _resourceClaims.Values.OrderBy(c => c.JobId).ToArray(), World = Inspect(), Research = ResearchModifications, CityStage = _cityStage, Support = InspectSupport(), Negotiations=InspectNegotiations(), CommandRandom = _commandRandom.State, LaborRandom = _laborRandom.State, MarketRandom = _marketRandom.State,
        MoneyHolds = _economy.Accounts.OrderBy(x => x.Key).Select(x => new { x.Key, Holds = x.Value.Holds.OrderBy(h => h.Key, StringComparer.Ordinal).ToArray() }).ToArray(),
        InventoryHolds = _economy.Inventories.OrderBy(x => x.Key).Select(x => new { x.Key,
            Stock = x.Value.Reservations.OrderBy(h => h.Key, StringComparer.Ordinal).ToArray(),
            Capacity = x.Value.CapacityHolds.OrderBy(h => h.Key, StringComparer.Ordinal).ToArray() }).ToArray(),
        Jobs = _actors.OrderBy(x => x.Key).Select(x => new { x.Key, x.Value.Job }).ToArray(),
        Batches = _enterprises.OrderBy(x => x.Key).Select(x => new { x.Key, x.Value.Batch }).ToArray(),
        ExtractionBatches = _equipment.OrderBy(x => x.Key).Select(x => new { x.Key, x.Value.Batch }).ToArray(),
        Requests = _offers.OrderBy(x => x.Key).Select(x => new { x.Key, Requests = x.Value.Requests.OrderBy(r => r.Key).ToArray() }).ToArray(),
        CompletedJobs = _completedPersonalJobs.Order().ToArray(),
        Commands = _seenCommands.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
        Results = _results.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
        Pending = _pending.OrderBy(c => c.ActorId.Value).ThenBy(c => c.LocalSequence).ToArray(),
        Ledger = Ledger(), EventSequence = _eventSequence
    };
}

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void AdvanceProduction(EnterpriseState e)
    {
        if((PreparingDemolition(ProductionParent(e.Definition.Id))||PreparingDevelopment(ProductionParent(e.Definition.Id)))&&e.Batch is null){e.Enabled=false;e.Reason=PreparingDemolition(ProductionParent(e.Definition.Id))?"PreparingDemolition":"PreparingDevelopment";return;}
        if(Modernizing(e.Definition.Id)){e.Reason="ConstructionInProgress";return;}
        if (!e.Enabled) { e.Reason = "UserPaused"; return; }
        if (e.Group.Headcount < (ParallelWorkshops ? 1 : e.Recipe.MinimumStaff)) { e.Reason = "NoStaff"; return; }
        try
        {
            bool starting = e.Batch is null;
            int fullStaff = e.Definition.BuildingTypeId == "" ? e.Recipe.RequiredStaff : e.Definition.Capacity;
            long required = ParallelWorkshops ? checked(e.Recipe.FullStaffTicks * 100) : checked(e.Recipe.FullStaffTicks * fullStaff);
            long id = e.Batch?.Id ?? _nextId;
            string hold = e.Batch?.Hold ?? $"batch:{id}";
            long previousWork = e.Batch?.WorkDone ?? 0;
            long work = ParallelWorkshops ? WorkshopRules.Rate(e.Group.Headcount) : checked((long)Math.Min(e.Group.Headcount, fullStaff) * e.Definition.WorkRateMultiplier);
            long nextWork = Math.Min(required, checked(previousWork + work));
            long service = e.Batch?.Terms?.ServiceMinorPerFullTick ?? _config.Economy?.Workshops?.ServiceMinorPerFullTick ?? 0;
            long nextEnergy = ParallelWorkshops ? checked((long)((Int128)service * nextWork / 100)) : checked((long)((Int128)e.Recipe.EnergyPerBatchMinor * nextWork / required));
            long energy = checked(nextEnergy - (e.Batch?.EnergyPaid ?? 0));
            long wageRate = ParallelWorkshops ? e.Batch?.Terms?.WageMinor ?? e.Group.Wage : e.Group.Wage;
            long wage = checked(e.Group.Headcount * wageRate);
            long newTotalEnergy = checked(e.EnergyPaid + energy);
            var consumed = starting ? e.Recipe.Ingredients.ToDictionary(i=>i.ItemId,i=>checked(_flows[i.ItemId].RecipeInput+i.Quantity),StringComparer.Ordinal) : null;
            bool complete = nextWork == required;
            long newProduced = complete ? checked(_flows[e.Recipe.OutputItem].Produced + e.Recipe.OutputQuantity)
                : _flows[e.Recipe.OutputItem].Produced;
            long newCompleted = complete ? checked(e.Completed + 1) : e.Completed;
            var experience=complete?PrepareProductionExperience(e,id):null;
            var tx = _economy.Begin(Tick);
            if (starting)
            {
                foreach(var ingredient in e.Recipe.Ingredients)tx.RemoveFreeStock(e.Input,ingredient.ItemId,ingredient.Quantity);
                tx.ReserveCapacity(e.Output, hold, e.Recipe.OutputItem, e.Recipe.OutputQuantity);
            }
            var actor = _actors[e.Definition.OwnerId];
            if (wage > 0) tx.Pay(actor.Account, _wages, wage, "Wages");
            if (energy > 0) tx.Pay(actor.Account, _treasury, energy, "Energy");
            if (complete)
            {
                tx.ReleaseCapacity(e.Output, hold);
                tx.AddStock(e.Output, e.Recipe.OutputItem, e.Recipe.OutputQuantity);
            }
            tx.Commit();
            if (starting)
            {
                ++_nextId; e.Batch = new(id, hold, required,e.Recipe.Id,e.Recipe.Ingredients);
                if (ParallelWorkshops)
                {
                    e.Batch.Terms = new(e.Recipe, wageRate, service);
                    _workshopTurns[e.Definition.Id] = _workshopStartSerial = checked(_workshopStartSerial + 1);
                    Emit("WorkshopBatchStarted", actor.Id, id, e.Recipe.Id, service, wageRate, e.Definition.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                foreach(var ingredient in e.Recipe.Ingredients)
                {
                    _flows[ingredient.ItemId].RecipeInput = consumed![ingredient.ItemId];
                    Emit("ProductionBatchStarted",actor.Id,id,ingredient.ItemId,ingredient.Quantity);
                }
            }
            e.Batch!.WorkDone = nextWork; e.Batch.EnergyPaid = nextEnergy;
            e.EnergyPaid = newTotalEnergy; e.Reason = "Working";
            if (wage > 0) Emit("WagesPaid", actor.Id, e.Definition.Id, money: wage);
            if (energy > 0) Emit("EnergyPaid", actor.Id, e.Definition.Id, money: energy);
            if (complete)
            {
                _flows[e.Recipe.OutputItem].Produced = newProduced; e.Completed = newCompleted;
                if(experience is not null)GrantExperience(experience);
                e.Batch = null;
                Emit("ProductionCompleted", actor.Id, id, e.Recipe.OutputItem, e.Recipe.OutputQuantity);
            }
        }
        catch (RuleViolation ex)
        {
            e.Reason = ex.ReasonCode switch { "NoStock" => "NoInputs", "NoCapacity" => "NoOutputSpace", _ => ex.ReasonCode };
        }
        // A rules/config overflow in autonomous work is a failed simulation, not silent waiting.
    }
    private void StartSale(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        if (_offers.TryGetValue(c.Action.OfferId, out var pending) && pending.SupplyRequests.ContainsKey(actor.Id))
            throw new RuleViolation("ExistingDeliveryRequest");
        if (!_offers.TryGetValue(c.Action.OfferId, out var offer) || offer.Remaining <= 0 || offer.CreatedTick >= Tick || offer.ExpiresAtTick < Tick)
            throw new RuleViolation("NotAvailable");
        RequireActionLocation(actor, c.Action);
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        if (c.Action.ItemId != offer.Definition.ItemId) throw new RuleViolation("WrongItem");
        // A per-actor request may exceed the remaining order: allocation happens only at closing.
        long cutoff = offer.Cutoff ?? Math.Min(offer.ExpiresAtTick ?? long.MaxValue, checked(Tick + _config.TradeWindowTicks));
        string hold = $"capital-sale:{c.CommandId}";
        var tx = _economy.Begin(Tick);
        tx.ReserveStock(actor.Inventory, hold, c.Action.ItemId, c.Action.Quantity); tx.Commit();
        offer.Requests.Add(actor.Id, new(actor.Id, actor.Inventory, c.Action.Quantity, hold, c.CommandId));
        offer.Cutoff = cutoff;
        Emit("CapitalSaleRequested", actor.Id, offer.Definition.Id, c.Action.ItemId, c.Action.Quantity, command: c.CommandId);
    }
    private void CancelSale(ActorState actor, CommandEnvelope c)
    {
        if (!_offers.TryGetValue(c.Action.OfferId, out var offer) || !offer.Requests.TryGetValue(actor.Id, out var request))
            throw new RuleViolation("NotAvailable");
        var tx = _economy.Begin(Tick); tx.ReleaseStock(actor.Inventory, request.Reservation); tx.Commit();
        offer.Requests.Remove(actor.Id);
        Emit("CapitalSaleCancelled", actor.Id, offer.Definition.Id, command: c.CommandId);
        // The original cutoff remains, even if the last request is withdrawn.
    }
    private void CloseCapitalWindows()
    {
        foreach (var offer in _offers.Values.OrderBy(o => o.Definition.Id))
        {
            if (offer.Cutoff is null || Tick < offer.Cutoff) continue;
            var requests = offer.Requests.Values.OrderBy(r => r.ActorId).ToArray();
            var future = offer.SupplyRequests.Values.OrderBy(r => r.Supplier.Value).ToArray();
            var allocated = AllocateFixedVolume(requests.Select(r => (r.ActorId, r.Quantity))
                .Concat(future.Select(r => (r.Supplier.Value, r.Quantity))).OrderBy(r => r.Item1).ToArray(), offer.Remaining);
            long volume = allocated.Values.Sum();
            // Stage the complete closing batch. Nothing is paid to an early processed request.
            var tx = _economy.Begin(Tick);
            long nextId = _nextId;
            var settlements = new List<SettlementView>();
            long exported = checked(_flows[offer.Definition.ItemId].Exported + requests.Sum(r => allocated[r.ActorId]));
            long remaining = checked(offer.Remaining - volume);
            foreach (var request in requests)
            {
                long q = allocated[request.ActorId];
                long payment = checked(q * offer.Definition.UnitPriceMinor);
                long contractId = nextId++;
                if (q > 0)
                {
                    long escrowAccount = nextId++;
                    tx.CreateAccount(escrowAccount, "ContractEscrow");
                    tx.Pay(_capital, escrowAccount, payment, "CapitalEscrow", offer.BudgetHold);
                    tx.ConsumeReserved(request.InventoryId, request.Reservation, q);
                    tx.Pay(escrowAccount, _actors[request.ActorId].Account, payment, "CapitalSettlement");
                }
                if (q < request.Quantity) tx.ReleaseStock(request.InventoryId, request.Reservation);
                settlements.Add(new(contractId, offer.Definition.Id, new(request.ActorId), offer.Definition.ItemId,
                    request.Quantity, q, payment, Tick, q > 0 ? "Fulfilled" : "NotAllocated"));
            }
            var contracts = new List<DeliveryContractView>(); long compensation = _capitalCompensation;
            foreach (var r in future)
            {
                long quantity = allocated[r.Supplier.Value];
                if (quantity > 0)
                {
                    if (compensation == 0) { compensation = nextId++; tx.CreateAccount(compensation, "CapitalCompensation"); }
                    contracts.Add(StageDelivery(tx, ref nextId, r, quantity, offer.Definition.ItemId,
                        offer.Definition.UnitPriceMinor, null, null, offer.Definition.Position, _capital, offer.BudgetHold));
                }
                else if (r.BondMinor > 0) tx.ReleaseMoney(_actors[r.Supplier.Value].Account, r.BondHold);
            }
            tx.Commit(); _capitalCompensation = compensation; _deliveryContracts.AddRange(contracts);
            foreach (var d in contracts) EmitDeliveryCreated(d);
            _nextId = nextId; _flows[offer.Definition.ItemId].Exported = exported;
            offer.Remaining = remaining; offer.Requests.Clear(); offer.SupplyRequests.Clear(); offer.Cutoff = null; ++offer.Window;
            _settlements.AddRange(settlements);
            foreach (var settlement in settlements)
                Emit("CapitalSaleSettled", settlement.Seller.Value, settlement.Id, settlement.ItemId,
                    settlement.Accepted, settlement.PaidMinor, settlement.Status);
            Emit("CapitalWindowClosed", entity: offer.Definition.Id, quantity: volume);
        }
    }
    public void ValidateInvariants()
    {
        ValidateProductionContent();ValidateStorage();ValidateDemolition();ValidateWorkshops();ValidateIndustryDevelopment();ValidateDevelopmentBuildings();
        ValidateFleet();ValidateRoads();ValidateBilling();ValidateServices();ValidateServiceExecution(); ValidateResearchAudit(); ValidateResources(); ValidateExtractionRoutes(); ValidateTransfers();
        if (_plots.Values.Where(p => p.Definition.IsShopSite).SelectMany(p => new[] { p.Owner, p.ReservedBy }.Where(a => a.HasValue))
            .GroupBy(a => a).Any(g => g.Count() > 1)) throw new InvalidOperationException("Shared shop site limit exceeded");
        _economy.Validate();
        ValidateLaborInvariants();
        foreach (var actor in _actors.Values)
        {
            if (!_map.Walkable(actor.Position) || _economy.Inventories[actor.Inventory].Position != actor.Position)
                throw new InvalidOperationException("Invalid carrier position");
            if (actor.OffsetX is < 0 or >= TravelUnitsPerCell || actor.OffsetZ is < 0 or >= TravelUnitsPerCell ||
                actor.HeadingX is < -1 or > 1 || actor.HeadingZ is < -1 or > 1 ||
                actor.FacingX is < -1 or > 1 || actor.FacingZ is < -1 or > 1 ||
                (actor.FacingX == 0 && actor.FacingZ == 0))
                throw new InvalidOperationException("Invalid travel position");
            if (actor.Job is { } job && _completedPersonalJobs.Contains(job.Id))
                throw new InvalidOperationException("I08: completed job still active");
        }
        foreach (var flow in _flows.Values)
        {
            long physical = _economy.Inventories.Values.Aggregate(0L, (n, inv) => checked(n + inv.Count(flow.Id)+inv.Overflow.Values.Where(l=>l.ItemId==flow.Id).Sum(l=>l.Quantity)));
            long expected = checked(flow.Initial + ImportQuantity(flow.Id) + flow.Harvested + flow.Produced + ResearchStockDelta(flow.Id) - flow.RecipeInput - flow.Exported - flow.ConstructionInput-flow.Lost+flow.ConstructionRecovered);
            if (physical != expected || expected < 0) throw new InvalidOperationException($"I05: {flow.Id} not conserved");
        }
        foreach (var e in ProductionUnits())
        {
            if (e.Batch is { } batch && (batch.WorkDone < 0 || batch.WorkDone >= batch.WorkRequired ||
                !_economy.Inventories[e.Output].CapacityHolds.ContainsKey(batch.Hold) || batch.RecipeId!=e.Recipe.Id ||
                batch.Inputs.IsDefault || !batch.Inputs.SequenceEqual(e.Recipe.Ingredients)))
                throw new InvalidOperationException("Invalid work in progress");
        }
        foreach (var offer in _offers.Values)
        {
            long budget = checked(offer.Remaining * offer.Definition.UnitPriceMinor);
            if (_economy.Accounts[_capital].Holds.GetValueOrDefault(offer.BudgetHold) != budget)
                throw new InvalidOperationException("Order budget differs from remaining quantity");
            foreach (var request in offer.Requests.Values)
                if (_actors[request.ActorId].Position != offer.Definition.Position ||
                    !_economy.Inventories[request.InventoryId].Reservations.TryGetValue(request.Reservation, out var reserved) ||
                    reserved.Quantity != request.Quantity || reserved.Item != offer.Definition.ItemId)
                    throw new InvalidOperationException("Reserved sale changed before closing");
        }
        ValidateLandInvariants();
        ValidateEquipmentInvariants();
        ValidateNegotiations();
        ValidateMarketInvariants();
        ValidateDeliveryInvariants();
        ValidateLogisticsInvariants();
        ValidateCapitalDemandInvariants();
        ValidateSupportInvariants();
        ValidateElectionInvariants();
    }
}

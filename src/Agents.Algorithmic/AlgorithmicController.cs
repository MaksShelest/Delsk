using System.Collections.Immutable;
using Simulation.Core;

namespace Agents.Algorithmic;

/// <summary>Bounded utility selection and sequential plan executor, using only the allowlist.</summary>
public sealed partial class AlgorithmicController
{
    private sealed record Candidate(string Goal, string Reason, long Score, PlanStep[] Steps);
    public ControllerDecision Decide(ControllerObservation o, ImmutableArray<CommandResult> ownResults)
    {
        if (o.Participant.Id != Actor) throw new ArgumentException("Observation actor mismatch");
        bool completed = false;
        if (_state.PendingCommandId is { } pending)
        {
            var result = ownResults.FirstOrDefault(r => r.CommandId == pending);
            if (result is null) return Wait("AwaitingValidation");
            if (result.Status == ResultStatus.Rejected)
            {
                var failed = _state.Plan[_state.NextStep].Action;
                if(failed.Kind==ActionKind.SignSupport && result.ReasonCode=="NotAvailable")
                    _state = _state with {FailedSupport=[.._state.FailedSupport.Where(id=>id!=failed.SupportProposalId),failed.SupportProposalId]};
                _state = _state with { PendingCommandId = null, Plan = [], NextStep = 0,
                    BlockedGoal = failed.Kind==ActionKind.SignSupport?"":_state.Goal, BlockedUntilTick = checked(o.Tick + _state.Settings.BlockedRetryTicks),
                    Rejections = _state.Rejections + 1, Reason = result.ReasonCode, NextDecisionTick = o.Tick };
            }
            else
            {
                var action = _state.Plan[_state.NextStep].Action;
                if (action.Kind == ActionKind.RaiseBid)
                    _state = _state with { BidReviews = [.. _state.BidReviews.Where(r => r.OfferId != action.MarketOfferId), new(action.MarketOfferId, action.MarketRound)] };
                _state = _state with { PendingCommandId = null, NextStep = _state.NextStep + 1 };
                completed = _state.NextStep == _state.Plan.Length;
            }
        }
        if(_state.PendingCommandId is null && _state.NextStep>=_state.Plan.Length && _state.SuspendedPlan.Length>0)
            _state = _state with {Plan=_state.SuspendedPlan,NextStep=_state.SuspendedNextStep,Goal=_state.SuspendedGoal,Reason=_state.SuspendedReason,
                SuspendedPlan=[],SuspendedNextStep=0,SuspendedGoal="",SuspendedReason=""};
        _state = _state with {FailedSupport=_state.FailedSupport.Where(id=>o.Support.Proposals.Any(p=>p.Id==id && p.Election==o.Elections.NextElectionId)).ToArray()};
        _state = _state with { BidReviews = _state.BidReviews.Where(r => o.MarketOffers.Any(m => m.Id == r.OfferId && m.Status != MarketOfferStatus.Closed)).ToArray() };
        // Preserve the remainder while a remote decision runs; then restore and revalidate it.
        // The physical job itself keeps advancing in the core.
        var remote = RemoteCandidates(o).Where(c => Feasible(c,o)).ToArray();
        if (remote.Length > 0) {
            if(_state.NextStep<_state.Plan.Length)
                _state=_state with {SuspendedPlan=_state.Plan,SuspendedNextStep=_state.NextStep,SuspendedGoal=_state.Goal,SuspendedReason=_state.Reason};
            return Choose(o,remote);
        }
        if (o.Participant.Busy || Carrier(o).Stock.Any(s => s.Reserved > 0)) return Wait("ActiveJobOrSaleHold");
        if (!PlanRelevant(o) || (HasObligations(o) && !ObligationGoal(_state.Goal)))
            _state = _state with { Plan = [], NextStep = 0, NextDecisionTick = o.Tick };
        if (_state.NextStep < _state.Plan.Length)
            return new(_state.Goal, _state.Reason, [_state.Plan[_state.NextStep].Action],PlanStep:_state.NextStep);
        if (!completed && o.Tick < _state.NextDecisionTick) return Wait("DecisionInterval");
        return Choose(o, DecisionCandidates(o));
    }
    private Candidate[] DecisionCandidates(ControllerObservation o) => Candidates(o).Concat(WageCandidates(o)).Concat(ContractCandidates(o)).Concat(MarketCandidates(o))
            .Concat(LogisticsCandidates(o)).Concat(RecipeCandidates(o)).Concat(PoliticalCandidates(o)).Concat(NegotiationCandidates(o)).Concat(NegotiationResponses(o))
            .Where(c => (!SimplePolicy || SimpleGoal(c) || HasObligations(o)) && Feasible(c,o) && (!HasObligations(o) || !ExpansionGoal(c.Goal)) &&
                (o.OwnSupplyRequests.Length==0 || c.Goal is not ("SellCarried" or "SellOutput" or "HarvestForSale")))
            .Select(c => HasObligations(o) && c.Goal is "SupplyProduction" or "ResumeProduction" or "AdjustWage"
                ? c with { Score = c.Score + 300000 } : c).Select(c=>ApplyProfile(o,c)).ToArray();
    private bool Feasible(Candidate c, ControllerObservation o) => c.Steps.Length > 0 && c.Score > 0 && (!ExpansionGoal(c.Goal) || _state.Settings.ExpansionWeight>0) &&
        !(c.Goal == _state.BlockedGoal && o.Tick < _state.BlockedUntilTick);
    private ControllerDecision Choose(ControllerObservation o, Candidate[] candidates)
    {
        var ranked = candidates.OrderBy(c => c.Goal, StringComparer.Ordinal).ThenBy(c => c.Reason, StringComparer.Ordinal)
            .Select(c => (Candidate: c, Tie: RandomTie())).OrderByDescending(c => c.Candidate.Score).ThenBy(c => c.Tie).ToArray();
        _state = _state with { Decisions = _state.Decisions + 1, NextDecisionTick = checked(o.Tick + _state.Settings.DecisionIntervalTicks) };
        if (ranked.Length == 0)
        {
            _state = _state with { Goal = "Idle", Reason = "NoFeasibleGoal", Plan = [], NextStep = 0 };
            return new(_state.Goal,_state.Reason,[]
#if DELSK_DEVELOPER
                ,Candidates:ImmutableArray<UtilityScore>.Empty
#endif
            );
        }
        var chosen=ranked[0].Candidate;
        _state = _state with { Goal=chosen.Goal,Reason=chosen.Reason,Plan=chosen.Steps,NextStep=0 };
        return new(chosen.Goal,Explain(chosen),[chosen.Steps[0].Action]
#if DELSK_DEVELOPER
            ,Candidates:ranked.Take(16).Select(r=>new UtilityScore(r.Candidate.Goal,r.Candidate.Reason,r.Candidate.Score,r.Candidate.Steps.Length)).ToImmutableArray()
#endif
        );
    }
    private ControllerDecision Wait(string reason) => new(_state.Goal, reason, [], Record: false);
    private IEnumerable<Candidate> Candidates(ControllerObservation o)
    {
        var settings = _state.Settings;
        var carrier = Carrier(o);
        long cash = o.Account.AvailableMinor;
        long freeVolume = FreeVolume(carrier);
        bool recovery = cash < settings.CashReserveMinor;
        // Liquidating carried goods also prevents a full personal inventory from deadlocking a new goal.
        foreach (var stock in carrier.Stock.Where(s => s.Quantity > s.Reserved))
        {
            var offer = BestOffer(o, stock.ItemId, stock.Quantity - stock.Reserved, o.Participant.Position);
            if (offer is null) continue;
            long q = Math.Min(stock.Quantity - stock.Reserved, offer.RemainingQuantity);
            var steps = new List<PlanStep>(); Move(steps, o.Participant.Position, offer.Position);
            steps.Add(new(new(ActionKind.SellAvailableToCapital, ItemId: stock.ItemId, Quantity: q, OfferId: offer.Id)));
            yield return new("SellCarried", stock.ItemId, 120000 + Rate(o, offer, q, o.Participant.Position) * settings.CashWeight, steps.ToArray());
        }
        foreach (var enterprise in o.Enterprises)
        {
            var recipe = o.Map.Recipes.Single(r => r.Id == enterprise.RecipeId);
            var input = Inventory(o, enterprise.InputId); var output = Inventory(o, enterprise.OutputId);
            var group = o.EmploymentGroups.Single(g => g.Id.Value == enterprise.Id.Value);
            long outputQ = Free(output, recipe.OutputItem);
            if (outputQ >= Math.Max(1, settings.BatchQuantity / 4) || enterprise.ReasonCode == "NoOutputSpace")
            {
                long q = Math.Min(outputQ, Math.Min(settings.BatchQuantity, freeVolume / Volume(o, recipe.OutputItem)));
                var offer = BestOffer(o, recipe.OutputItem, q, output.Position);
                if (q > 0 && offer is not null)
                {
                    q = Math.Min(q, offer.RemainingQuantity);
                    var steps = new List<PlanStep>(); Move(steps, o.Participant.Position, output.Position);
                    steps.Add(new(new(ActionKind.Transfer, ItemId: recipe.OutputItem, Quantity: q, Source: output.Id, Destination: carrier.Id)));
                    Move(steps, output.Position, offer.Position);
                    steps.Add(new(new(ActionKind.SellAvailableToCapital, ItemId: recipe.OutputItem, Quantity: q, OfferId: offer.Id)));
                    if (Reachable(o, output.Position) && Distance(o.Map, output.Position, offer.Position) >= 0)
                        yield return new("SellOutput", recipe.OutputItem, 90000 + Rate(o, offer, q, output.Position) * settings.CashWeight, steps.ToArray());
                }
            }
            // Stop expenses during cash recovery; the core retains WIP and gradually releases workers.
            if (recovery && enterprise.Enabled)
                yield return One("PreserveCash", "UnpaidPause", 200000, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: false));
            if (!recovery && !enterprise.Enabled)
                yield return One("ResumeProduction", recipe.Id, 110000*settings.ProductionWeight/100, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: true));
            long targetWage = settings.BaseWageMinor;
            if (group.Ready && group.Headcount < recipe.MinimumStaff && o.Tick >= _state.NextWageTick)
            {
                long competitor = o.LaborMarket.Where(g => g.OwnerId != Actor && g.Headcount > 0).Select(g => g.WageMinor).DefaultIfEmpty(targetWage).Max();
                targetWage = Math.Min(settings.MaximumWageMinor, Math.Max(targetWage, checked(competitor + settings.WageRaiseMinor)));
            }
            long announced = group.PendingWageMinor ?? group.WageMinor;
            if (!recovery && announced != targetWage && o.Tick >= _state.NextWageTick &&
                cash >= checked(targetWage * group.Capacity * settings.WageReviewTicks))
            {
                yield return WageCandidate(o, group, targetWage, 100000, recipe.Id);
            }
            foreach(var ingredient in recipe.Ingredients)
            if (!recovery && Free(input, ingredient.ItemId) < Math.Max(ingredient.Quantity,InputTarget(o,input,recipe,ingredient)/2) && Need(o,ingredient.ItemId)>0)
            {
                long q = Math.Min(InputTarget(o,input,recipe,ingredient), Math.Min(freeVolume/Volume(o,ingredient.ItemId), InputSpace(o,input,recipe,ingredient.ItemId)));
                var steps = Acquire(o, ingredient.ItemId, q);
                if (steps is not null && q >= (recipe.Ingredients.Length==1?ingredient.Quantity:1))
                {
                    var last = AcquisitionPosition(o, ingredient.ItemId, q);
                    Move(steps, last, input.Position);
                    steps.Add(new(new(ActionKind.Transfer, ItemId: ingredient.ItemId, Quantity: q, Source: carrier.Id, Destination: input.Id)));
                    if (Distance(o.Map, last, input.Position) >= 0)
                        yield return new("SupplyProduction", ingredient.ItemId, 80000 * settings.ProductionWeight / 100, steps.ToArray());
                }
            }
        }
        // One primary production site per policy in this first baseline; expansion of chains belongs to P21.
        if (!recovery && o.Enterprises.Length == 0 && carrier.Stock.All(s => s.Quantity == 0))
        foreach (var type in o.BuildingTypes.Where(t => t.Kind == BuildingKind.Production))
        {
            var level = type.Levels.Single(l => l.Level == 1);
            var recipe = o.Map.Recipes.Single(r => r.Id == level.RecipeId);
            if (!recipe.Uses(settings.PreferredInput)) continue;
            var offer = BestOffer(o, recipe.OutputItem, recipe.OutputQuantity, o.Participant.Position);
            long margin = offer is null ? 0 : checked(offer.UnitPriceMinor * recipe.OutputQuantity - RecipeInputValue(o,recipe) -
                recipe.EnergyPerBatchMinor - settings.BaseWageMinor * recipe.RequiredStaff * recipe.FullStaffTicks);
            if (margin <= 0) continue;
            foreach (var plot in o.Plots.Where(p => p.BuildingId is null && !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null &&
                (p.OwnerId is null || p.OwnerId == Actor) && (p.ReservedBy is null || p.ReservedBy == Actor)).OrderBy(p => p.Id.Value))
            {
                var footprint = type.Footprint.Select(offset => new Cell(plot.Position.X + offset.X, plot.Position.Z + offset.Z)).ToArray();
                var plots = footprint.Select(cell => o.Plots.FirstOrDefault(p => p.Position == cell && p.BuildingId is null &&
                    !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null && (p.OwnerId is null || p.OwnerId == Actor) &&
                    (p.ReservedBy is null || p.ReservedBy == Actor))).ToArray();
                if (plots.Any(p => p is null)) continue;
                long cost = checked(level.PriceMinor + plots.Sum(p => p!.OwnerId is null ? p.PriceMinor : 0));
                if (cash < checked(cost + settings.CashReserveMinor) ||
                    level.Materials.Sum(m => checked(m.Quantity * Volume(o, m.ItemId))) > freeVolume) continue;
                var steps = new List<PlanStep>(); Cell current = o.Participant.Position; bool feasible = true;
                foreach (var material in level.Materials)
                {
                    var node = o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).FirstOrDefault(n => n.ItemId == material.ItemId && AvailableResource(o,n,material.Quantity) && Distance(o.Map, current, n.Position) >= 0);
                    if (node is null) { feasible = false; break; }
                    Move(steps, current, node.Position); steps.Add(new(new(ActionKind.Harvest, ItemId: material.ItemId, Quantity: material.Quantity)));
                    current = node.Position;
                }
                foreach (var part in plots.Select(p => p!).Where(p => p.OwnerId is null))
                {
                    if (Distance(o.Map, current, part.Position) < 0) { feasible = false; break; }
                    Move(steps, current, part.Position); steps.Add(new(new(ActionKind.BuyPlot, Plot: part.Id))); current = part.Position;
                }
                if (!feasible || Distance(o.Map, current, plot.Position) < 0) continue;
                Move(steps, current, plot.Position);
                steps.Add(new(new(ActionKind.Build, Target: plot.Position, BuildingTypeId: type.Id)));
                long travel = Distance(o.Map, o.Participant.Position, plot.Position);
                yield return new("BuildProduction", type.Id + ":" + plot.Id.Value,
                    60000 * settings.ExpansionWeight / 100 + margin - Math.Max(0, travel), steps.ToArray());
            }
        }
        if (!recovery && o.Enterprises.Length > 0 && o.Equipment.Length == 0)
        {
            var type = o.EquipmentTypes.FirstOrDefault(t => t.ItemId == settings.PreferredInput);
            var level = type?.Levels.Single(l => l.Level == 1);
            var site = o.Resources.Where(r=>r.ItemId==settings.PreferredInput && !r.Public &&
                (r.Owner==Actor || r.Owner is null && r.Tenant is null) && Reachable(o,r.Position))
                .OrderBy(r=>r.Owner==Actor?0:1).ThenBy(r=>Distance(o.Map,o.Participant.Position,r.Position)).FirstOrDefault();
            var node = site is null ? null : o.Map.ResourceNodes.Single(n=>n.Position==site.Position);
            long landCost=site is { Owner:null }?site.PurchasePriceMinor:0;
            if (type is not null && level is not null && node is not null && cash >= checked(level.PriceMinor + landCost + settings.CashReserveMinor))
            {
                var steps = new List<PlanStep>(); Move(steps, o.Participant.Position, node.Position);
                if(landCost>0)steps.Add(new(new(ActionKind.BuyResource,Target:node.Position)));
                steps.Add(new(new(ActionKind.BuyEquipment, Target: node.Position, EquipmentTypeId: type.Id)));
                yield return new("BuyExtraction", type.Id, 40000 * settings.ExpansionWeight / 100, steps.ToArray());
            }
        }
        foreach (var machine in o.Equipment)
        {
            if (recovery && machine.Mode == EquipmentMode.Automatic)
                yield return One("PreserveCash", "ExtractionPause", 200000, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Off));
            var type = o.EquipmentTypes.Single(t => t.Id == machine.TypeId);
            var level = type.Levels.Single(l => l.Level == machine.Level);
            if (!recovery && machine.Mode != EquipmentMode.Automatic && machine.PersonalJobId == 0 &&
                cash >= checked(level.CrewCapacity * level.WageMinor * settings.WageReviewTicks) &&
                o.Population >= level.MinimumCrew + o.Enterprises.Sum(e => o.Map.Recipes.Single(r => r.Id == e.RecipeId).MinimumStaff))
                yield return One("AutomateExtraction", type.Id, 70000, new(ActionKind.SetEquipmentMode, Equipment: machine.Id, Mode: EquipmentMode.Automatic));
        }
        if (carrier.Stock.All(s => s.Quantity == 0))
        foreach (var node in o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)))
        {
            long natural=o.Resources.Single(r=>r.Position==node.Position && r.ItemId==node.ItemId).Stock-o.Resources.Single(r=>r.Position==node.Position && r.ItemId==node.ItemId).Reserved;
            long q = Math.Min(natural, Math.Min(settings.BatchQuantity, freeVolume / Volume(o, node.ItemId)));
            var offer = BestOffer(o, node.ItemId, q, node.Position);
            if (q <= 0 || offer is null || !Reachable(o, node.Position)) continue;
            q = Math.Min(q, offer.RemainingQuantity);
            var steps = Acquire(o, node.ItemId, q);
            if (steps is null) continue;
            var origin = AcquisitionPosition(o, node.ItemId, q);
            if (Distance(o.Map, origin, offer.Position) < 0) continue;
            Move(steps, origin, offer.Position);
            steps.Add(new(new(ActionKind.SellAvailableToCapital, ItemId: node.ItemId, Quantity: q, OfferId: offer.Id)));
            long score = (recovery ? 150000 : 10000) + Rate(o, offer, q, origin) * settings.CashWeight;
            if (node.ItemId == settings.PreferredInput) score += 100;
            yield return new(recovery ? "RecoverCash" : "HarvestForSale", node.ItemId, score, steps.ToArray());
        }
    }
    private static Candidate One(string goal, string reason, long score, GameAction action)
        => new(goal, reason, score, [new(action)]);
    private static ControllerInventory Carrier(ControllerObservation o) => Inventory(o, o.Participant.InventoryId);
    private static ControllerInventory Inventory(ControllerObservation o, InventoryId id) => o.Inventories.Single(i => i.Id == id);
    private static long Free(ControllerInventory i, string item) => i.Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity - s.Reserved);
    private static long FreeVolume(ControllerInventory i) => i.Capacity - i.OccupiedVolume - i.ReservedCapacity;
    private static long Volume(ControllerObservation o, string item) => o.Map.Items.Single(i => i.Id == item).UnitVolume;
    private static bool Reachable(ControllerObservation o, Cell target) => Distance(o.Map, o.Participant.Position, target) >= 0;
    private static void Move(List<PlanStep> steps, Cell source, Cell target)
    { if (source != target) steps.Add(new(new(ActionKind.MoveTo, Target: target))); }
    private static Cell AcquisitionPosition(ControllerObservation o, string item, long q)
    {
        var machine = o.Equipment.Where(m => o.EquipmentTypes.Single(t => t.Id == m.TypeId).ItemId == item &&
            Free(Inventory(o, m.StorageId), item) >= q && Reachable(o, m.Position)).OrderBy(m => m.Id.Value).FirstOrDefault();
        return machine?.Position ?? o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).First(n => n.ItemId == item && AvailableResource(o,n,q) && Reachable(o, n.Position)).Position;
    }
    private static List<PlanStep>? Acquire(ControllerObservation o, string item, long q)
    {
        if (q <= 0) return null;
        var machine = o.Equipment.Where(m => o.EquipmentTypes.Single(t => t.Id == m.TypeId).ItemId == item &&
            Free(Inventory(o, m.StorageId), item) >= q && Reachable(o, m.Position)).OrderBy(m => m.Id.Value).FirstOrDefault();
        var node = o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).FirstOrDefault(n => n.ItemId == item && AvailableResource(o,n,q) && Reachable(o, n.Position));
        if (machine is null && node is null) return null;
        Cell source = machine?.Position ?? node!.Position;
        var steps = new List<PlanStep>(); Move(steps, o.Participant.Position, source);
        if (machine is not null)
            steps.Add(new(new(ActionKind.Transfer, ItemId: item, Quantity: q, Source: machine.StorageId, Destination: o.Participant.InventoryId)));
        else
        {
            var personal = o.Equipment.FirstOrDefault(m => m.Position == source && m.Mode == EquipmentMode.Personal &&
                o.EquipmentTypes.Single(t => t.Id == m.TypeId).ItemId == item);
            steps.Add(new(new(ActionKind.Harvest, ItemId: item, Quantity: q, Equipment: personal?.Id ?? default)));
        }
        return steps;
    }
    private static CapitalOfferView? BestOffer(ControllerObservation o, string item, long q, Cell source)
        => o.CapitalOffers.Where(a => a.ItemId == item && a.RemainingQuantity > 0 &&
            Distance(o.Map, source, a.Position) >= 0 && (a.ExpiresAtTick is null ||
            a.ExpiresAtTick>=o.Tick+(long)Distance(o.Map,source,a.Position)*o.Map.MoveTicksPerCell+o.Map.TradeWindowTicks+2)).OrderByDescending(a => Rate(o, a, q, source)).ThenBy(a => a.Id).FirstOrDefault();
    private static long Rate(ControllerObservation o, CapitalOfferView offer, long q, Cell source)
    {
        long distance = Distance(o.Map, source, offer.Position);
        if (distance < 0 || q <= 0) return 0;
        long ticks = checked(distance * o.Map.MoveTicksPerCell + q * o.Map.HarvestTicksPerUnit + o.Map.TradeWindowTicks + 1);
        return checked(Math.Min(q, offer.RemainingQuantity) * offer.UnitPriceMinor / ticks);
    }
    internal static int Distance(ControllerMap map, Cell from, Cell to)
    {
        if (from == to) return 0;
        if (to.X < 0 || to.Z < 0 || to.X >= map.Width || to.Z >= map.Height || map.BlockedCells.Contains(to)) return -1;
        var blocked = map.BlockedCells.ToHashSet(); var visited = new HashSet<Cell> { from };
        var queue = new Queue<(Cell Cell, int Distance)>(); queue.Enqueue((from, 0));
        while (queue.TryDequeue(out var entry))
        foreach (var next in new[] { new Cell(entry.Cell.X + 1, entry.Cell.Z), new(entry.Cell.X, entry.Cell.Z + 1),
            new(entry.Cell.X - 1, entry.Cell.Z), new(entry.Cell.X, entry.Cell.Z - 1) })
        {
            if (next.X < 0 || next.Z < 0 || next.X >= map.Width || next.Z >= map.Height || blocked.Contains(next) || !visited.Add(next)) continue;
            if (next == to) return entry.Distance + 1;
            queue.Enqueue((next, entry.Distance + 1));
        }
        return -1;
    }
}

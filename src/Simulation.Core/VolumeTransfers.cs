using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record HandlingRules(long PersonalVolumePerTick = 10000, long WorkerVolumePerTick = 10000)
{
    public void Validate()
    {
        if (PersonalVolumePerTick is < 1 or > 1000000 || WorkerVolumePerTick is < 1 or > 1000000)
            throw new ArgumentException("Invalid handling productivity");
    }
}
/// <summary>Integer work, including an unfinished unit, shared by personal and freight handling.</summary>
public static class VolumeWork
{
    public static (long Units, long Remainder) Advance(long remainder, long productivity, long unitVolume, long remaining)
    {
        if (remainder < 0 || remainder >= unitVolume || productivity < 0 || unitVolume <= 0 || remaining <= 0)
            throw new ArgumentOutOfRangeException(nameof(remainder));
        Int128 work = (Int128)remainder + productivity;
        long units = (long)Int128.Min(remaining, work / unitVolume);
        return (units, units == remaining ? 0 : (long)(work % unitVolume));
    }
}
public sealed record TransferOperation(long Id, ParticipantId Owner, long Source, long Destination, string ItemId,
    long Quantity, long Completed, long WorkRemainder, long StartedTick, Cell Position, string CommandId,
    string Kind = "Stock", long Contract = 0)
{
    public long Remaining => checked(Quantity - Completed);
    public string StockHold => $"transfer-stock:{Id}";
    public string CapacityHold => $"transfer-capacity:{Id}";
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, TransferOperation> _transfers = [];
    private HandlingRules Handling => _config.Economy?.Handling ?? new();
    public ImmutableArray<TransferOperation> TransfersOf(ParticipantId actor) => [.. _transfers.Values.Where(t => t.Owner == actor).OrderBy(t => t.Id)];
    private bool HasTransfer(long actor) => _transfers.Values.Any(t => t.Owner.Value == actor);
    private void BeginTransfer(ActorState actor, CommandEnvelope c, long source, long destination, string item,
        long quantity, string kind = "Stock", long contract = 0)
    {
        RequireIdle(actor);
        if (quantity <= 0 || source == destination) throw new RuleViolation("InvalidQuantity");
        var operation = new TransferOperation(_nextId, new(actor.Id), source, destination, item, quantity, 0, 0,
            Tick, actor.Position, c.CommandId, kind, contract);
        long nextId = checked(_nextId + 1);
        var tx = _economy.Begin(Tick);
        if (kind is not ("Pickup" or "Overflow")) tx.ReserveStock(source, operation.StockHold, item, quantity);
        if (destination != 0 && kind != "Delivery") tx.ReserveCapacity(destination, operation.CapacityHold, item, quantity);
        tx.Commit();
        if (kind == "Delivery")
        {
            int index = _deliveryContracts.FindIndex(d => d.Id == contract);
            _deliveryContracts[index] = _deliveryContracts[index] with { PreparedQuantity = checked(_deliveryContracts[index].PreparedQuantity + quantity) };
        }
        _nextId = nextId; _transfers.Add(operation.Id, operation);
        Emit("TransferStarted", actor.Id, operation.Id, item, quantity, reason: kind, command: c.CommandId);
    }
    private void AdvanceTransfers()
    {
        foreach (var t in _transfers.Values.OrderBy(t => t.Id).ToArray())
        {
            var actor = _actors[t.Owner.Value];
            if (Fleet is not null && Tick <= t.StartedTick) continue;
            if (actor.Position != t.Position) { CancelTransfer(t, "LeftTransferLocation"); continue; }
            if (t.Kind == "Pickup" && !_marketAllocations.Any(a => a.Id == t.Contract && a.ClosedTick is null && Tick <= a.DeadlineTick) ||
                t.Kind == "Delivery" && !_deliveryContracts.Any(d => d.Id == t.Contract && d.ClosedTick is null && Tick <= d.DeadlineTick))
            { CancelTransfer(t, "ContractClosed"); continue; }
            var work = VolumeWork.Advance(t.WorkRemainder, TransferHandlingPower(t), _items[t.ItemId].UnitVolume, t.Remaining);
            if (work.Units > 0)
            {
                if (t.Kind == "Pickup") CompletePickupTransfer(actor, t, work.Units);
                else if(t.Kind=="Overflow")
                {var tx=_economy.Begin(Tick);tx.WithdrawOverflow(t.Source,t.Contract,work.Units);tx.ReceiveReservedCapacity(t.Destination,t.CapacityHold,t.ItemId,work.Units);tx.Commit();Emit("OverflowTaken",actor.Id,t.Contract,t.ItemId,work.Units,command:t.CommandId);}
                else if (t.Kind == "Delivery") ReceiveDelivery(_deliveryContracts.FindIndex(d => d.Id == t.Contract),
                    t.Source, work.Units, false, null, t.CommandId, t.StockHold);
                else
                {
                    var tx = _economy.Begin(Tick); tx.ConsumeReserved(t.Source, t.StockHold, work.Units);
                    tx.ReceiveReservedCapacity(t.Destination, t.CapacityHold, t.ItemId, work.Units); tx.Commit();
                    Emit("StockTransferred", actor.Id, t.Destination, t.ItemId, work.Units, command: t.CommandId);
                }
            }
            var next = t with { Completed = checked(t.Completed + work.Units), WorkRemainder = work.Remainder };
            if (next.Remaining == 0)
            { _transfers.Remove(t.Id); Emit("TransferCompleted", actor.Id, t.Id, t.ItemId, t.Quantity, command: t.CommandId); }
            else _transfers[t.Id] = next;
        }
    }
    private void StopTransfer(ActorState actor, CommandEnvelope command)
    {
        foreach (var t in _transfers.Values.Where(t => t.Owner.Value == actor.Id).ToArray()) CancelTransfer(t, "UserStopped");
    }
    private void CancelTransfer(TransferOperation t, string reason)
    {
        var tx = _economy.Begin(Tick);
        if (t.Kind is not ("Pickup" or "Overflow")) tx.ReleaseStock(t.Source, t.StockHold);
        if (t.Destination != 0 && t.Kind != "Delivery") tx.ReleaseCapacity(t.Destination, t.CapacityHold);
        tx.Commit();
        if (t.Kind == "Delivery")
        {
            int index = _deliveryContracts.FindIndex(d => d.Id == t.Contract); var d = _deliveryContracts[index];
            if (d.ClosedTick is null) _deliveryContracts[index] = d with { PreparedQuantity = checked(d.PreparedQuantity - t.Remaining) };
        }
        _transfers.Remove(t.Id);
        Emit("TransferCancelled", t.Owner.Value, t.Id, t.ItemId, t.Completed, reason: reason, command: t.CommandId);
    }
    private void AddExpectedTransferHolds(Dictionary<(long Inventory, string Hold), long> capacity,
        Dictionary<(long Inventory, string Hold), StockReservation> stock)
    {
        if(_transfers.Values.GroupBy(t=>t.Owner).Any(g=>g.Count()>1))throw new CheckpointException("DuplicateTransferOwner");
        foreach (var t in _transfers.Values)
        {
            if (t.Id < 1000 || t.StartedTick < 1 || t.StartedTick > Tick || t.Completed < 0 || t.Remaining <= 0 ||
                t.WorkRemainder < 0 || t.WorkRemainder >= _items[t.ItemId].UnitVolume ||
                !_actors.TryGetValue(t.Owner.Value, out var actor) || actor.Position != t.Position ||
                !_seenCommands.TryGetValue(t.CommandId, out var c) || c.ActorId != t.Owner ||
                !_results.TryGetValue(t.CommandId, out var r) || r.Status != ResultStatus.Accepted ||
                t.Kind is not ("Stock" or "Pickup" or "Delivery" or "Overflow") || actor.Job is not null ||
                c.Action.Quantity!=t.Quantity || r.AppliedTick!=t.StartedTick || !ValidTransferReferences(t,actor,c.Action)) throw new CheckpointException("InvalidTransferOperation");
            string receipt=t.Kind switch {"Pickup"=>"PurchaseCollected","Delivery"=>"DeliveryReceived","Overflow"=>"OverflowTaken",_=>"StockTransferred"};
            if(_events.Where(e=>e.CommandId==t.CommandId && e.Type==receipt).Sum(e=>e.Quantity)!=t.Completed)
                throw new CheckpointException("TransferReceiptMismatch");
            if (t.Kind is not ("Pickup" or "Overflow")) stock.Add((t.Source,t.StockHold),new(t.ItemId,t.Remaining));
            if (t.Destination != 0 && t.Kind != "Delivery") capacity.Add((t.Destination,t.CapacityHold),checked(t.Remaining*_items[t.ItemId].UnitVolume));
        }
    }
    private bool ValidTransferReferences(TransferOperation t,ActorState actor,GameAction action)
    {
        if(!_economy.Inventories.TryGetValue(t.Source,out var source) ||
            !_events.Any(e=>e.Type=="TransferStarted" && e.EntityId==t.Id && e.CommandId==t.CommandId && e.ItemId==t.ItemId && e.Quantity==t.Quantity))return false;
        if(t.Kind=="Stock")return action.Kind==ActionKind.Transfer && action.Source.Value==t.Source && action.Destination.Value==t.Destination &&
            action.ItemId==t.ItemId && source.Owner==actor.Id && _economy.Inventories.TryGetValue(t.Destination,out var target) && target.Owner==actor.Id &&
            source.Position==t.Position && target.Position==t.Position && (TransferCarrier(actor,source.Id) || TransferCarrier(actor,target.Id)) &&
            !_logistics.Values.Any(l=>l.VehicleId==0 && (l.Cargo==source.Id || l.Cargo==target.Id)) && CanTransferVehicle(actor,source.Id,t.ItemId) && CanTransferVehicle(actor,target.Id,t.ItemId);
        if(t.Kind=="Overflow")return action.Kind==ActionKind.TakeOverflow && action.Source.Value==t.Source && action.OfferId==t.Contract && action.ItemId==t.ItemId && source.Owner==actor.Id && source.Position==t.Position && t.Destination==actor.Inventory && source.Overflow.TryGetValue(t.Contract,out var lot)&&lot.Quantity>=t.Remaining;
        if(t.Kind=="Pickup")return action.Kind==ActionKind.CollectPurchase && action.PurchaseId==t.Contract && t.Destination==actor.Inventory &&
            _marketAllocations.Any(a=>a.Id==t.Contract && a.Buyer.Value==actor.Id && a.SourceInventory.Value==t.Source && a.ItemId==t.ItemId &&
                a.ClosedTick is null && a.DeadlineTick>Tick && a.RemainingQuantity>=t.Remaining);
        return action.Kind==ActionKind.DeliverContract && action.ContractId==t.Contract && t.Source==actor.Inventory &&
            _deliveryContracts.Any(d=>d.Id==t.Contract && d.Supplier.Value==actor.Id && (d.DestinationInventory?.Value??0)==t.Destination && d.ItemId==t.ItemId &&
                d.ClosedTick is null && d.DeadlineTick>Tick && d.PreparedQuantity>=t.Remaining);
    }
    private void ValidateTransfers()
    {
        var capacity=new Dictionary<(long Inventory,string Hold),long>();var stock=new Dictionary<(long Inventory,string Hold),StockReservation>();
        AddExpectedTransferHolds(capacity,stock);
        foreach(var pair in capacity)
            if(_economy.Inventories[pair.Key.Inventory].CapacityHolds.GetValueOrDefault(pair.Key.Hold)!=pair.Value)throw new InvalidOperationException("Missing transfer capacity");
        foreach(var pair in stock)
            if(!_economy.Inventories[pair.Key.Inventory].Reservations.TryGetValue(pair.Key.Hold,out var hold)||hold!=pair.Value)throw new InvalidOperationException("Missing transfer stock");
        foreach(var inventory in _economy.Inventories.Values)
            if(inventory.CapacityHolds.Any(h=>h.Key.StartsWith("transfer-capacity:",StringComparison.Ordinal)&&!capacity.ContainsKey((inventory.Id,h.Key))) ||
                inventory.Reservations.Any(h=>h.Key.StartsWith("transfer-stock:",StringComparison.Ordinal)&&!stock.ContainsKey((inventory.Id,h.Key))))
                throw new InvalidOperationException("Orphan transfer reservation");
    }
    private bool IsTransferControlEvent(SimulationEvent e) => e.Tick==Tick && e.Phase==9 && e.Type=="TransferCancelled" &&
        e.ReasonCode=="LeftTransferLocation" && e.EntityId>=1000 && e.Quantity>=0 && e.CommandId is {} id &&
        _seenCommands.TryGetValue(id,out var command) && command.ActorId==e.ActorId &&
        command.Action.Kind is ActionKind.Transfer or ActionKind.TakeOverflow or ActionKind.CollectPurchase or ActionKind.DeliverContract &&
        _results.TryGetValue(id,out var result) && result.Status==ResultStatus.Accepted &&
        _events.Any(start=>start.Type=="TransferStarted" && start.EntityId==e.EntityId && start.CommandId==id &&
            start.ActorId==e.ActorId && start.ItemId==e.ItemId && start.Quantity>=e.Quantity);
}

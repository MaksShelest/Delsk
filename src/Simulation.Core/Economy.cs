namespace Simulation.Core;

internal sealed class MoneyAccount(long id, string kind, long balance)
{
    public long Id { get; } = id;
    public string Kind { get; } = kind;
    public long Balance { get; set; } = balance;
    public Dictionary<string, long> Holds { get; } = new(StringComparer.Ordinal);
    public long Reserved => Holds.Values.Aggregate(0L, (s, n) => checked(s + n));
    public long Available => checked(Balance - Reserved);
    public MoneyAccount Copy()
    {
        var copy = new MoneyAccount(Id, Kind, Balance);
        foreach (var pair in Holds) copy.Holds.Add(pair.Key, pair.Value);
        return copy;
    }
}
internal record StockReservation(string Item, long Quantity);
internal sealed class Inventory(long id, long owner, Cell position, long capacity)
{
    public long Id { get; } = id;
    public long Owner { get; } = owner;
    public Cell Position { get; set; } = position;
    public long Capacity { get; set; } = capacity;
    public Dictionary<string, long> Stock { get; } = new(StringComparer.Ordinal);
    public Dictionary<long,OverflowLot> Overflow {get;}=[];
    public Dictionary<string, StockReservation> Reservations { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> CapacityHolds { get; } = new(StringComparer.Ordinal);
    public long Count(string item) => Stock.GetValueOrDefault(item);
    public long Reserved(string item) => Reservations.Values.Where(x => x.Item == item)
        .Aggregate(0L, (s, x) => checked(s + x.Quantity));
    public long Free(string item) => checked(Count(item) - Reserved(item));
    public long Occupied(IReadOnlyDictionary<string, ItemDefinition> items) => Stock.Aggregate(0L,
        (s, x) => checked(s + checked(x.Value * items[x.Key].UnitVolume)));
    public long Incoming => CapacityHolds.Values.Aggregate(0L, (s, x) => checked(s + x));
    public Inventory Copy()
    {
        var copy = new Inventory(Id, Owner, Position, Capacity);
        foreach(var p in Overflow)copy.Overflow.Add(p.Key,p.Value);
        foreach (var pair in Stock) copy.Stock.Add(pair.Key, pair.Value);
        foreach (var pair in Reservations) copy.Reservations.Add(pair.Key, pair.Value);
        foreach (var pair in CapacityHolds) copy.CapacityHolds.Add(pair.Key, pair.Value);
        return copy;
    }
}
internal record LedgerEntry(long Tick, long DebitAccount, long CreditAccount, long AmountMinor, string Reason);

/// <summary>Single writer. Transactions stage changed accounts/inventories before replacing them.</summary>
internal sealed class EconomyStore(IReadOnlyDictionary<string, ItemDefinition> items)
{
    public Dictionary<long, MoneyAccount> Accounts { get; } = [];
    public Dictionary<long, Inventory> Inventories { get; } = [];
    public List<LedgerEntry> Ledger { get; } = [];
    public long InitialMoney { get; set; }
    public long ExternalIn { get; private set; }
    public long ExternalOut { get; private set; }
    internal void RestoreTotals(long initial, long externalIn, long externalOut)
    { InitialMoney = initial; ExternalIn = externalIn; ExternalOut = externalOut; }
    public EconomyTransaction Begin(long tick) => new(this, items, tick);

    public void Validate()
    {
        long money = 0;
        foreach (var a in Accounts.Values)
        {
            if (a.Balance < 0 || a.Holds.Values.Any(x => x <= 0) || a.Available < 0)
                throw new InvalidOperationException("I02: invalid account or money hold");
            money = checked(money + a.Balance);
        }
        if (money != checked(InitialMoney + ExternalIn - ExternalOut))
            throw new InvalidOperationException("I03: money not conserved");
        foreach (var i in Inventories.Values)
        {
            if (i.Capacity < 0 || i.Stock.Any(x => x.Value < 0 || !items.ContainsKey(x.Key)) ||
                i.Reservations.Values.Any(x => x.Quantity <= 0 || !items.ContainsKey(x.Item)) ||
                i.CapacityHolds.Values.Any(x => x <= 0) ||
                i.Stock.Keys.Concat(i.Reservations.Values.Select(x => x.Item)).Distinct()
                    .Any(item => i.Reserved(item) > i.Count(item)) ||
                checked(i.Occupied(items) + i.Incoming) > i.Capacity)
                throw new InvalidOperationException("I04/I06: invalid inventory or reservation");
        }
    }

    internal void Commit(EconomyTransaction tx)
    {
        // All arithmetic, resource checks and ledger creation have already succeeded.
        foreach (var pair in tx.AccountChanges) Accounts[pair.Key] = pair.Value;
        foreach (var pair in tx.InventoryChanges) Inventories[pair.Key] = pair.Value;
        ExternalIn = tx.ExternalInAfter;
        ExternalOut = tx.ExternalOutAfter;
        Ledger.AddRange(tx.LedgerChanges);
    }
}

internal sealed class EconomyTransaction
{
    private readonly EconomyStore _store;
    private readonly IReadOnlyDictionary<string, ItemDefinition> _items;
    private readonly long _tick;
    private bool _committed;
    internal Dictionary<long, MoneyAccount> AccountChanges { get; } = [];
    internal Dictionary<long, Inventory> InventoryChanges { get; } = [];
    internal List<LedgerEntry> LedgerChanges { get; } = [];
    internal long ExternalInAfter { get; private set; }
    internal long ExternalOutAfter { get; private set; }
    public EconomyTransaction(EconomyStore store, IReadOnlyDictionary<string, ItemDefinition> items, long tick)
    {
        _store = store; _items = items; _tick = tick;
        ExternalInAfter = store.ExternalIn; ExternalOutAfter = store.ExternalOut;
    }
    private void EnsureOpen()
    { if (_committed) throw new InvalidOperationException("Transaction already committed"); }
    private MoneyAccount Account(long id)
    {
        EnsureOpen();
        if (!AccountChanges.TryGetValue(id, out var a))
        {
            if (!_store.Accounts.TryGetValue(id, out var original)) throw new RuleViolation("NotAvailable");
            AccountChanges.Add(id, a = original.Copy());
        }
        return a;
    }
    private Inventory Inventory(long id)
    {
        EnsureOpen();
        if (!InventoryChanges.TryGetValue(id, out var i))
        {
            if (!_store.Inventories.TryGetValue(id, out var original)) throw new RuleViolation("NotAvailable");
            InventoryChanges.Add(id, i = original.Copy());
        }
        return i;
    }
    private static void Positive(long quantity)
    { if (quantity <= 0) throw new RuleViolation("InvalidQuantity"); }
    private long Volume(string item, long quantity)
    {
        Positive(quantity);
        if (!_items.TryGetValue(item, out var definition)) throw new RuleViolation("UnknownItem");
        return checked(definition.UnitVolume * quantity);
    }
    private void CheckSpace(Inventory inv, long volume)
    {
        if (checked(inv.Occupied(_items) + inv.Incoming + volume) > inv.Capacity)
            throw new RuleViolation("NoCapacity");
    }
    public void CreateAccount(long id, string kind)
    {
        EnsureOpen();
        if (_store.Accounts.ContainsKey(id) || AccountChanges.ContainsKey(id)) throw new RuleViolation("DuplicateAccount");
        AccountChanges.Add(id, new MoneyAccount(id, kind, 0));
    }
    public void CreateInventory(long id, long owner, Cell position, long capacity)
    {
        EnsureOpen(); Positive(capacity);
        if (_store.Inventories.ContainsKey(id) || InventoryChanges.ContainsKey(id)) throw new RuleViolation("DuplicateInventory");
        InventoryChanges.Add(id, new Inventory(id, owner, position, capacity));
    }
    public (long Normal,long Excess) ReceiveForecast(long inventory,string item,long quantity,OverflowLot lot)
    {
        Positive(quantity);var i=Inventory(inventory);long normal=Math.Min(quantity,Math.Max(0,i.Capacity-i.Occupied(_items)-i.Incoming)/_items[item].UnitVolume);
        if(normal>0)AddStock(inventory,item,normal);
        long excess=quantity-normal;if(excess>0)i.Overflow.Add(lot.Id,lot with{Quantity=excess});return(normal,excess);
    }
    public void WithdrawOverflow(long inventory,long lotId,long quantity)
    {
        Positive(quantity);var i=Inventory(inventory);if(!i.Overflow.TryGetValue(lotId,out var lot)||quantity>lot.Quantity)throw new RuleViolation("OverflowUnavailable");
        if(quantity==lot.Quantity)i.Overflow.Remove(lotId);else i.Overflow[lotId]=lot with{Quantity=lot.Quantity-quantity};
    }
    public void UpdateOverflow(long inventory,OverflowLot lot)=>Inventory(inventory).Overflow[lot.Id]=lot;
    public void SetCapacity(long id, long capacity)
    {
        Positive(capacity); var inventory = Inventory(id);
        if (checked(inventory.Occupied(_items) + inventory.Incoming) > capacity) throw new RuleViolation("NoCapacity");
        inventory.Capacity = capacity;
    }
    public void CreditExternal(long id, long amount, string reason)
    {
        Positive(amount);
        var a = Account(id);
        a.Balance = checked(a.Balance + amount);
        ExternalInAfter = checked(ExternalInAfter + amount);
        LedgerChanges.Add(new(_tick, 0, id, amount, reason));
    }
    public void DebitExternal(long id, long amount, string reason)
    {
        Positive(amount); var a = Account(id);
        if (a.Available < amount) throw new RuleViolation("NoMoney");
        a.Balance = checked(a.Balance - amount);
        ExternalOutAfter = checked(ExternalOutAfter + amount);
        LedgerChanges.Add(new(_tick, id, 0, amount, reason));
    }
    public void HoldMoney(long id, string hold, long amount)
    {
        Positive(amount);
        var a = Account(id);
        if (a.Holds.ContainsKey(hold)) throw new RuleViolation("DuplicateReservation");
        if (a.Available < amount) throw new RuleViolation("NoMoney");
        a.Holds.Add(hold, amount);
    }
    public void ReleaseMoney(long id, string hold)
    { if (!Account(id).Holds.Remove(hold)) throw new RuleViolation("MissingReservation"); }
    public void Pay(long from, long to, long amount, string reason, string? hold = null)
    {
        Positive(amount);
        if (from == to) throw new RuleViolation("SameAccount");
        var debit = Account(from); var credit = Account(to);
        if (hold is null)
        { if (debit.Available < amount) throw new RuleViolation("NoMoney"); }
        else
        {
            if (!debit.Holds.TryGetValue(hold, out var held) || held < amount) throw new RuleViolation("NoMoneyReserved");
            if (held == amount) debit.Holds.Remove(hold); else debit.Holds[hold] = checked(held - amount);
        }
        debit.Balance = checked(debit.Balance - amount);
        credit.Balance = checked(credit.Balance + amount);
        LedgerChanges.Add(new(_tick, from, to, amount, reason));
    }
    public void AddStock(long id, string item, long quantity)
    {
        long volume = Volume(item, quantity); var inv = Inventory(id);
        CheckSpace(inv, volume);
        inv.Stock[item] = checked(inv.Count(item) + quantity);
    }
    public void RemoveFreeStock(long id, string item, long quantity)
    {
        _ = Volume(item, quantity); var inv = Inventory(id);
        if (inv.Free(item) < quantity) throw new RuleViolation("NoStock");
        inv.Stock[item] = checked(inv.Count(item) - quantity);
        if (inv.Stock[item] == 0) inv.Stock.Remove(item);
    }
    public void MoveStock(long source, long destination, string item, long quantity)
    {
        if (source == destination) throw new RuleViolation("SameInventory");
        RemoveFreeStock(source, item, quantity);
        AddStock(destination, item, quantity);
    }
    public void ReserveStock(long id, string reservation, string item, long quantity)
    {
        _ = Volume(item, quantity); var inv = Inventory(id);
        if (inv.Reservations.ContainsKey(reservation)) throw new RuleViolation("DuplicateReservation");
        if (inv.Free(item) < quantity) throw new RuleViolation("NoStock");
        inv.Reservations.Add(reservation, new(item, quantity));
    }
    public void ConsumeReserved(long id, string reservation, long quantity)
    {
        Positive(quantity); var inv = Inventory(id);
        if (!inv.Reservations.TryGetValue(reservation, out var reserved) || reserved.Quantity < quantity)
            throw new RuleViolation("NoStockReserved");
        inv.Stock[reserved.Item] = checked(inv.Count(reserved.Item) - quantity);
        if (inv.Stock[reserved.Item] == 0) inv.Stock.Remove(reserved.Item);
        if (reserved.Quantity == quantity) inv.Reservations.Remove(reservation);
        else inv.Reservations[reservation] = reserved with { Quantity = checked(reserved.Quantity - quantity) };
    }
    public void ReleaseStock(long id, string reservation)
    { if (!Inventory(id).Reservations.Remove(reservation)) throw new RuleViolation("MissingReservation"); }
    public void ReserveCapacity(long id, string hold, string item, long quantity)
    {
        long volume = Volume(item, quantity); var inv = Inventory(id);
        if (inv.CapacityHolds.ContainsKey(hold)) throw new RuleViolation("DuplicateReservation");
        CheckSpace(inv, volume); inv.CapacityHolds.Add(hold, volume);
    }
    public void ReleaseCapacity(long id, string hold)
    { if (!Inventory(id).CapacityHolds.Remove(hold)) throw new RuleViolation("MissingReservation"); }
    public void ReserveAdditionalStock(long id,string hold,string item,long quantity)
    {
        _=Volume(item,quantity);var inv=Inventory(id);
        if(inv.Free(item)<quantity)throw new RuleViolation("NoStock");
        if(inv.Reservations.TryGetValue(hold,out var old))
        {if(old.Item!=item)throw new RuleViolation("WrongItem");inv.Reservations[hold]=old with {Quantity=checked(old.Quantity+quantity)};}
        else inv.Reservations.Add(hold,new(item,quantity));
    }
    public void ReserveAdditionalCapacity(long id,string hold,string item,long quantity)
    {
        long volume=Volume(item,quantity);var inv=Inventory(id);CheckSpace(inv,volume);
        inv.CapacityHolds[hold]=checked(inv.CapacityHolds.GetValueOrDefault(hold)+volume);
    }
    public void ReleaseCapacityVolume(long id,string hold,string item,long quantity)
    {
        long volume=Volume(item,quantity);var inv=Inventory(id);
        if(!inv.CapacityHolds.TryGetValue(hold,out long old)||old<volume)throw new RuleViolation("NoCapacityReserved");
        if(old==volume)inv.CapacityHolds.Remove(hold);else inv.CapacityHolds[hold]=checked(old-volume);
    }
    public void ReceiveReservedCapacity(long id, string hold, string item, long quantity)
    {
        long volume = Volume(item, quantity); var inventory = Inventory(id);
        if (!inventory.CapacityHolds.TryGetValue(hold, out long reserved) || reserved < volume)
            throw new RuleViolation("NoCapacityReserved");
        if (reserved == volume) inventory.CapacityHolds.Remove(hold);
        else inventory.CapacityHolds[hold] = checked(reserved - volume);
        AddStock(id, item, quantity);
    }
    public void Commit()
    {
        if (_committed) throw new InvalidOperationException("Transaction already committed");
        _store.Commit(this); _committed = true;
    }
}

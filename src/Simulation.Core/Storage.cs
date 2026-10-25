namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long,long> _groundStorage=[];
    private readonly Dictionary<long,StorageConstruction> _storageConstruction=[];
    private StorageRules? Storage=>EconomyRules.Storage;
    public bool HasGroundStorage=>Storage is not null||_demolitionReceipts.Count>0;
    private StorageRules GroundRules=>Storage??new(70,1200,[600,120]);
    public StorageState StorageOf(ParticipantId actor)=>new(
        _groundStorage.Where(p=>_economy.Inventories[p.Value].Owner==actor.Value).OrderBy(p=>p.Key).Select(p=>new GroundStorage(p.Key,p.Value)).ToArray(),
        _storageConstruction.Values.Where(c=>c.Owner==actor.Value).OrderBy(c=>c.Id).Select(c=>c with{Materials=c.Materials.ToArray()}).ToArray());
    public SimulationEvent[] StorageEventsOf(ParticipantId actor,long after)=>_events.Where(e=>e.Sequence>after&&e.ActorId==actor&&e.Type.StartsWith("Overflow",StringComparison.Ordinal)).ToArray();
    private StorageState CaptureStorage()=>new(_groundStorage.OrderBy(p=>p.Key).Select(p=>new GroundStorage(p.Key,p.Value)).ToArray(),_storageConstruction.Values.OrderBy(c=>c.Id).ToArray());
    private bool IsGround(long inventory)=>_groundStorage.ContainsValue(inventory);
    private bool HasOverflow(long inventory)=>_economy.Inventories[inventory].Overflow.Count>0;
    private void InitializeStorage()
    {
        if(Storage is null)return;
        foreach(var p in _plots.Values.Where(p=>p.Owner.HasValue&&p.Building is null&&!p.Definition.IsCorridor).OrderBy(p=>p.Definition.Id))
        {var tx=_economy.Begin(0);tx.CreateInventory(_nextId,p.Owner!.Value,p.Definition.Position,Storage.GroundCapacity);tx.Commit();_groundStorage.Add(p.Definition.Id,_nextId++);}
    }
    private bool StorageWorkAt(Cell cell)=>_storageConstruction.Values.Any(c=>c.Position==cell);
    private void StartWarehouseConstruction(ActorState actor,CommandEnvelope c,BuildingState? upgrading=null)
    {
        if(Storage is null)throw new RuleViolation("NotAvailable");
        RequireActionLocation(actor,c.Action);
        if(StorageWorkAt(upgrading?.Position??c.Action.Target))throw new RuleViolation("ConstructionInProgress");
        var type=upgrading?.Type??(_buildingTypes.TryGetValue(c.Action.BuildingTypeId,out var t)?t:throw new RuleViolation("UnknownBuildingType"));
        var position=upgrading?.Position??c.Action.Target;
        if(type.Kind!=BuildingKind.Warehouse || type.Footprint.Length!=1)throw new RuleViolation("InvalidFootprint");
        var plot=_plotsByCell.GetValueOrDefault(position);
        if(plot is null||plot.Owner!=actor.Id||plot.Definition.IsCorridor||plot.Definition.IsShopSite||plot.Definition.ShopParticipantId.HasValue)throw new RuleViolation("LandNotOwned");
        if(upgrading is null&&plot.Building is not null)throw new RuleViolation("LandOccupied");
        var level=type.Levels.SingleOrDefault(l=>l.Level==(upgrading?.Level??0)+1)??throw new RuleViolation("MaxLevel");
        if(level.ConstructionTicks<=0)throw new RuleViolation("InvalidConstructionDuration");
        long inventory=upgrading?.Input??_groundStorage[plot.Definition.Id];
        if(_transfers.Values.Any(t=>t.Source==inventory || t.Destination==inventory))throw new RuleViolation("StorageBusy");
        long id=_nextId,next=checked(id+1),end=checked(Tick+level.ConstructionTicks);var flows=PrepareMaterialFlows(level.Materials);
        var tx=_economy.Begin(Tick);foreach(var material in level.Materials)tx.RemoveFreeStock(inventory,material.ItemId,material.Quantity);
        if(level.PriceMinor>0)tx.Pay(actor.Account,_treasury,level.PriceMinor,upgrading is null?"Construction":"BuildingUpgrade");tx.Commit();
        _nextId=next;_storageConstruction.Add(id,new(id,actor.Id,position,type.Id,level.Level,inventory,upgrading?.Id??0,Tick,end,level.Materials.ToArray(),level.PriceMinor,c.CommandId));
        foreach(var f in flows)_flows[f.Key].ConstructionInput=f.Value;
        Emit("StorageConstructionStarted",actor.Id,id,money:level.PriceMinor,reason:end.ToString(System.Globalization.CultureInfo.InvariantCulture),command:c.CommandId);
        EmitMaterialConsumption(actor.Id,id,level.Materials,c.CommandId);
    }
    private void AdvanceStorageConstruction()
    {
        foreach(var c in _storageConstruction.Values.Where(c=>c.CompletesTick<=Tick).OrderBy(c=>c.Id).ToArray())
        {
            if(c.Development is not null){CompleteDevelopmentConstruction(c);continue;}
            var type=_buildingTypes[c.TypeId];var level=type.Levels.Single(l=>l.Level==c.Level);
            var tx=_economy.Begin(Tick);tx.SetCapacity(c.InventoryId,level.InputCapacity);tx.Commit();
            if(c.BuildingId==0)
            {_groundStorage.Remove(_plotsByCell[c.Position].Definition.Id);var b=new BuildingState(c.Id,c.Owner,c.Position,type,1,c.InventoryId,null,[c.Position]);_buildings.Add(b.Id,b);_plotsByCell[c.Position].Building=b.Id;Emit("BuildingConstructed",c.Owner,b.Id,reason:type.Id,command:c.CommandId);}
            else{_buildings[c.BuildingId].Level=c.Level;Emit("BuildingUpgraded",c.Owner,c.BuildingId,reason:c.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),command:c.CommandId);}
            _storageConstruction.Remove(c.Id);Emit("StorageConstructionCompleted",c.Owner,c.Id,command:c.CommandId);
        }
    }
    private void TakeOverflow(ActorState actor,CommandEnvelope c)
    {
        RequireIdle(actor);RequireActionLocation(actor,c.Action);
        if(!_economy.Inventories.TryGetValue(c.Action.Source.Value,out var source)||source.Owner!=actor.Id||!source.Overflow.TryGetValue(c.Action.OfferId,out var lot))throw new RuleViolation("NotAvailable");
        if(c.Action.Quantity<=0||c.Action.Quantity>lot.Quantity||lot.ItemId!=c.Action.ItemId)throw new RuleViolation("InvalidQuantity");
        BeginTransfer(actor,c,source.Id,actor.Inventory,lot.ItemId,c.Action.Quantity,"Overflow",lot.Id);
    }
    private void AdvanceOverflow(bool expire=false)
    {
        if(!HasGroundStorage)return;
        foreach(var inv in _economy.Inventories.Values.OrderBy(i=>i.Id).ToArray())
        foreach(var original in inv.Overflow.Values.OrderBy(l=>l.ExpiresTick).ThenBy(l=>l.Id).ToArray())
        {
            if(!_economy.Inventories[inv.Id].Overflow.TryGetValue(original.Id,out var lot))continue;
            var current=_economy.Inventories[inv.Id];
            long room=Math.Max(0,current.Capacity-current.Occupied(_items)-current.Incoming)/_items[lot.ItemId].UnitVolume;
            // A manual rescue is only a local intent. Free normal room removes the risk first;
            // cancel its unfinished portion and release the backpack hold before promotion.
            if(room>0)foreach(var transfer in _transfers.Values.Where(t=>t.Kind=="Overflow"&&t.Contract==lot.Id).ToArray())CancelTransfer(transfer,"OverflowRecovered");
            long q=Math.Min(lot.Quantity,room);
            if(q>0)
            {var tx=_economy.Begin(Tick);tx.WithdrawOverflow(inv.Id,lot.Id,q);tx.AddStock(inv.Id,lot.ItemId,q);tx.Commit();Emit("OverflowRecovered",inv.Owner,lot.Id,lot.ItemId,q);}
            if(!_economy.Inventories[inv.Id].Overflow.TryGetValue(lot.Id,out lot)||!expire)continue;
            if(Tick>=lot.ExpiresTick)
            {
                foreach(var t in _transfers.Values.Where(t=>t.Kind=="Overflow"&&t.Contract==lot.Id).ToArray())CancelTransfer(t,"OverflowExpired");
                long lost=checked(_flows[lot.ItemId].Lost+lot.Quantity);var tx=_economy.Begin(Tick);tx.WithdrawOverflow(inv.Id,lot.Id,lot.Quantity);tx.Commit();_flows[lot.ItemId].Lost=lost;Emit("OverflowLost",inv.Owner,lot.Id,lot.ItemId,lot.Quantity);
            }
            else foreach(int warning in GroundRules.WarningOffsets!)if(lot.ExpiresTick-Tick<=warning&&!lot.Warned.Contains(warning))
            {lot=lot with{Warned=[..lot.Warned,warning]};var tx=_economy.Begin(Tick);tx.UpdateOverflow(inv.Id,lot);tx.Commit();Emit("OverflowWarning",inv.Owner,lot.Id,lot.ItemId,lot.Quantity,reason:warning.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        }
    }
    private long ReceiveForecast(EconomyTransaction tx,long inventory,string item,long quantity,string source)
    {
        if(Storage is null||!_buildings.Values.Any(b=>b.Input==inventory))throw new RuleViolation("NotAvailable");
        var inv=_economy.Inventories[inventory];var lot=new OverflowLot(_nextId,inv.Owner,item,quantity,source,Tick,checked(Tick+GroundRules.OverflowGraceTicks),[]);
        return tx.ReceiveForecast(inventory,item,quantity,lot).Excess;
    }
    private void ForecastReceived(long inventory,string item,long excess)
    {if(excess<=0)return;long id=_nextId++;Emit("OverflowCreated",_economy.Inventories[inventory].Owner,id,item,excess,reason:(Tick+Storage!.OverflowGraceTicks).ToString(System.Globalization.CultureInfo.InvariantCulture));}
    private void ValidateStorage()
    {
        if(!HasGroundStorage){if(_groundStorage.Count>0||_storageConstruction.Count>0||_economy.Inventories.Values.Any(i=>i.Overflow.Count>0))throw new CheckpointException("StorageDisabled");return;}
        var expected=_plots.Values.Where(p=>p.Owner.HasValue&&p.Building is null&&!p.Definition.IsCorridor).Select(p=>p.Definition.Id).Order();
        if(!expected.SequenceEqual(_groundStorage.Keys.Order())||_groundStorage.Values.Distinct().Count()!=_groundStorage.Count)throw new CheckpointException("InvalidGroundStorage");
        foreach(var p in _groundStorage)
        {var plot=_plots[p.Key];if(!_economy.Inventories.TryGetValue(p.Value,out var i)||i.Owner!=plot.Owner||i.Position!=plot.Definition.Position||i.Capacity!=GroundRules.GroundCapacity||i.Overflow.Count>0&&!DemolitionInventory(i.Id,false)&&!_demolitionReceipts.Any(r=>r.GroundInventories.Contains(i.Id))||i.Reservations.Keys.Any(h=>!h.StartsWith("transfer-stock:",StringComparison.Ordinal))||i.CapacityHolds.Keys.Any(h=>!h.StartsWith("transfer-capacity:",StringComparison.Ordinal)&&!h.StartsWith("development:",StringComparison.Ordinal)))throw new CheckpointException("InvalidGroundInventory");}
        var pending=_events.Where(e=>e.Type=="StorageConstructionStarted").Select(e=>e.EntityId).Except(_events.Where(e=>e.Type is "StorageConstructionCompleted" or "StorageConstructionCancelled").Select(e=>e.EntityId)).Order();
        if(!pending.SequenceEqual(_storageConstruction.Keys.Order()))throw new CheckpointException("MissingStorageConstruction");
        if(_storageConstruction.Values.Select(c=>c.Position).Distinct().Count()!=_storageConstruction.Count)throw new CheckpointException("DuplicateStorageConstruction");
        foreach(var c in _storageConstruction.Values)
        {
            var level=_buildingTypes[c.TypeId].Levels.Single(l=>l.Level==c.Level);
            if(c.Owner!=_economy.Inventories[c.InventoryId].Owner||c.Position!=_economy.Inventories[c.InventoryId].Position||c.StartedTick<1||c.StartedTick>Tick||c.CompletesTick!=checked(c.StartedTick+level.ConstructionTicks)||c.CompletesTick<=Tick||c.PaidMinor!=level.PriceMinor||!c.Materials.SequenceEqual(level.Materials)||
                !_seenCommands.TryGetValue(c.CommandId,out var command)||command.ActorId.Value!=c.Owner||!_results.TryGetValue(c.CommandId,out var result)||result.Status!=ResultStatus.Accepted||result.AppliedTick!=c.StartedTick||
                (c.BuildingId==0?command.Action.Kind!=ActionKind.Build||command.Action.Target!=c.Position||!_groundStorage.Values.Contains(c.InventoryId):command.Action.Kind!=ActionKind.UpgradeBuilding||command.Action.Building.Value!=c.BuildingId||_buildings[c.BuildingId].Level+1!=c.Level))throw new CheckpointException("InvalidStorageConstruction");
        }
        var allLots=_economy.Inventories.Values.SelectMany(i=>i.Overflow.Values).ToArray();if(allLots.Select(l=>l.Id).Distinct().Count()!=allLots.Length)throw new CheckpointException("DuplicateOverflowLot");
        foreach(var flow in _flows.Values)if(flow.Lost<0||flow.Lost!=_events.Where(e=>e.Type=="OverflowLost"&&e.ItemId==flow.Id).Sum(e=>e.Quantity))throw new CheckpointException("OverflowLossMismatch");
        foreach(var i in _economy.Inventories.Values)foreach(var lot in i.Overflow.Values)
        {if(lot.Id<1000||lot.Owner!=i.Owner||lot.Quantity<=0||!_items.ContainsKey(lot.ItemId)||string.IsNullOrWhiteSpace(lot.Source)||lot.CreatedTick<1||lot.CreatedTick>Tick||lot.ExpiresTick!=checked(lot.CreatedTick+GroundRules.OverflowGraceTicks)||lot.ExpiresTick<=Tick||lot.Warned is null||lot.Warned.Distinct().Count()!=lot.Warned.Length||lot.Warned.Any(w=>!GroundRules.WarningOffsets!.Contains(w)))throw new CheckpointException("InvalidOverflowLot");
            if(!lot.Source.StartsWith("demolition:",StringComparison.Ordinal)&&(!lot.Source.StartsWith("delivery:",StringComparison.Ordinal)||!long.TryParse(lot.Source[9..],out var delivery)||!_deliveryContracts.Any(d=>d.Id==delivery&&d.CapacityForecast&&d.Buyer?.Value==lot.Owner&&d.DestinationInventory?.Value==i.Id&&lot.CreatedTick>d.CreatedTick&&lot.CreatedTick<=d.DeadlineTick)||!_events.Any(e=>e.Type=="OverflowCreated"&&e.EntityId==lot.Id&&e.ItemId==lot.ItemId&&e.ActorId?.Value==lot.Owner&&e.Tick==lot.CreatedTick&&e.Quantity>=lot.Quantity)||!lot.Warned.ToHashSet().SetEquals(GroundRules.WarningOffsets!.Where(n=>lot.ExpiresTick-Tick<=n))))throw new CheckpointException("InvalidOverflowProvenance");
            if(lot.Source.StartsWith("demolition:",StringComparison.Ordinal)&&(!_demolitionReceipts.Any(r=>r.Owner==lot.Owner&&r.Tick==lot.CreatedTick&&r.GroundInventories.Contains(i.Id)&&lot.Source==$"demolition:{r.Target}")||!_events.Any(e=>e.Type=="OverflowCreated"&&e.EntityId==lot.Id&&e.Tick==lot.CreatedTick)))throw new CheckpointException("InvalidDemolitionOverflow");}
    }
}

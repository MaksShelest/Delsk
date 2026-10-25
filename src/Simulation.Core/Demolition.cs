using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly HashSet<long> _preparingDemolition=[];
    private readonly List<DemolitionReceipt> _demolitionReceipts=[];
    private DemolitionRules DemolitionRules=>EconomyRules.Demolition??new();
    private bool Demolished(long id)=>_demolitionReceipts.Any(r=>!r.Construction&&r.Target==id);
    private bool PreparingDemolition(long id)=>_preparingDemolition.Contains(id);
    private bool DemolitionInventory(long id,bool preparing=true)
    {var active=_buildings.Values.FirstOrDefault(b=>(b.Input==id||b.Output==id)&&!Demolished(b.Id));return active is not null?preparing&&PreparingDemolition(active.Id):_buildings.Values.Any(b=>(b.Input==id||b.Output==id)&&Demolished(b.Id));}
    public DemolitionState? InspectDemolition()=>CaptureDemolition();
    public DemolitionState? DemolitionOf(ParticipantId actor)=>Storage is null?null:new(_preparingDemolition.Where(id=>_buildings[id].Owner==actor.Value).Order().ToArray(),_demolitionReceipts.Where(r=>r.Owner==actor.Value).Select(CopyReceipt).ToArray());
    private static DemolitionReceipt CopyReceipt(DemolitionReceipt r)=>r with{Cells=r.Cells.ToArray(),Invested=r.Invested.ToArray(),Recovery=r.Recovery.ToArray(),GroundInventories=r.GroundInventories.ToArray()};
    private DemolitionState? CaptureDemolition()=>_preparingDemolition.Count+_demolitionReceipts.Count==0?null:new(_preparingDemolition.Order().ToArray(),_demolitionReceipts.Select(CopyReceipt).ToArray());
    private long[] BuildingInventories(BuildingState b)=>b.Output is {} output?[b.Input,output]:[b.Input];
    public DemolitionQuote? QuoteDemolition(ParticipantId actor,long target,bool construction=false)
    {
        if(!_actors.ContainsKey(actor.Value))return null;
        BuildingState? building=null;StorageConstruction? job=null;
        if(construction){if(!_storageConstruction.TryGetValue(target,out job)||job.Owner!=actor.Value)return null;}
        else if(!_buildings.TryGetValue(target,out building)||building.Owner!=actor.Value||Demolished(target))return null;
        var cells=building is null?new[]{new DemolitionCell(job!.Position,job.Level)}:building.Footprint.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,building.Level)).ToArray();
        // Journal entries describe actual committed investments; initial scenario assets have none.
        var commands=construction?new HashSet<string>([job!.CommandId],StringComparer.Ordinal):_events.Where(e=>e.EntityId==target&&e.Type is "BuildingConstructed" or "BuildingUpgraded").Select(e=>e.CommandId!).ToHashSet(StringComparer.Ordinal);
        var invested=_events.Where(e=>e.Type=="ConstructionMaterialConsumed"&&e.CommandId is {} id&&commands.Contains(id)).GroupBy(e=>e.ItemId,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new MaterialCost(g.Key,g.Sum(e=>e.Quantity))).ToArray();
        var recovery=invested.Select(m=>new MaterialCost(m.ItemId,checked((long)((Int128)m.Quantity*DemolitionRules.RecoveryBasisPoints/10000)))).Where(m=>m.Quantity>0).ToArray();
        long[] inventories=building is null?[job!.InventoryId]:BuildingInventories(building);
        var stock=inventories.SelectMany(id=>_economy.Inventories[id].Stock).GroupBy(s=>s.Key,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new MaterialCost(g.Key,g.Sum(s=>s.Value))).ToArray();
        var blockers=new List<string>();
        if(building is not null)
        {
            if(!PreparingDemolition(target))blockers.Add("PrepareFirst");
            if(_storageConstruction.Values.Any(c=>c.BuildingId==target))blockers.Add("ConstructionInProgress");
            if(_enterprises.TryGetValue(target,out var e)&&e.Batch is not null)blockers.Add("ProductionBatch");
        }
        if(_transfers.Values.Any(t=>inventories.Contains(t.Source)||inventories.Contains(t.Destination)))blockers.Add("ManualTransfer");
        if(inventories.Any(id=>_economy.Inventories[id].Reservations.Count+_economy.Inventories[id].CapacityHolds.Count>0))blockers.Add("ReservedGoodsOrSpace");
        if(_deliveryContracts.Any(d=>d.ClosedTick is null&&(d.DestinationInventory is {} i&&inventories.Contains(i.Value)||building is not null&&_logistics.Values.Any(l=>l.Enterprise==target&&l.Queue.Any(j=>j.ContractId==d.Id)))))blockers.Add("DeliveryCommitment");
        if(_marketAllocations.Any(a=>a.ClosedTick is null&&inventories.Contains(a.SourceInventory.Value)))blockers.Add("CustomerPickup");
        if(_marketOffers.Values.Any(o=>o.Status!=MarketOfferStatus.Closed&&(inventories.Contains(o.Inventory)||o.Shop==target&&!construction)))blockers.Add("MarketWindow");
        if(_buyOffers.Values.Any(o=>o.View.Status=="Open"&&inventories.Contains(o.View.DestinationInventory.Value)))blockers.Add("BuyOffer");
        if(_logistics.Values.Any(l=>l.Trip is {} trip&&(building is not null&&l.Enterprise==target||trip.Freight is {} f&&(inventories.Contains(f.GoodsSource.Value)||inventories.Contains(f.Destination.Value)))))blockers.Add("ActiveFreight");
        long fee=checked(cells.Sum(c=>checked(DemolitionRules.CellPriceMinor+(c.Level-1)*DemolitionRules.ExtraLevelPriceMinor)));
        var overflow=inventories.SelectMany(i=>_economy.Inventories[i].Overflow.Values).OrderBy(l=>l.Id).ToArray();
        string token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{target,construction,owner=actor.Value,fee,cells,invested,recovery,stock,overflow},SimulationJson.Options)))).ToLowerInvariant();
        return new(target,construction,actor.Value,fee,cells,invested,recovery,stock,blockers.Distinct().ToArray(),token);
    }
    private void PrepareDemolition(ActorState actor,CommandEnvelope command)
    {
        long id=command.Action.Building.Value;if(QuoteDemolition(new(actor.Id),id) is null)throw new RuleViolation("NotAvailable");
        if(!_preparingDemolition.Add(id))return;
        var b=_buildings[id];long[] inventories=BuildingInventories(b);
        if(_enterprises.TryGetValue(id,out var production))production.Enabled=production.Batch is not null;
        foreach(var offer in _buyOffers.Values.Where(o=>o.View.Status=="Open"&&inventories.Contains(o.View.DestinationInventory.Value)).ToArray())CloseBuyOffer(offer,"Cancelled");
        foreach(var l in _logistics.Values)
        {
            bool home=l.Enterprise==id;
            for(int n=0;n<l.Queue.Count;n++)
            {var j=l.Queue[n];if(j.ContractId==0&&(home||inventories.Contains(j.Source.Value)||inventories.Contains(j.Destination.Value)||inventories.Contains(j.OwnDestination.Value)||inventories.Contains(j.InboundSource.Value)))l.Queue[n]=j with{Enabled=false};}
            if(home){l.InboundEnabled=false;if(l.Contract==0)l.Enabled=false;}
        }
        Emit("DemolitionPrepared",actor.Id,id,command:command.CommandId);
    }
    private void CancelDemolitionPreparation(ActorState actor,CommandEnvelope command)
    {long id=command.Action.Building.Value;if(!_buildings.TryGetValue(id,out var b)||b.Owner!=actor.Id||Demolished(id))throw new RuleViolation("NotAvailable");_preparingDemolition.Remove(id);Emit("DemolitionPreparationCancelled",actor.Id,id,command:command.CommandId);}
    private void ExecuteDemolition(ActorState actor,CommandEnvelope command,bool construction)
    {
        long id=construction?command.Action.OfferId:command.Action.Building.Value;
        var quote=QuoteDemolition(new(actor.Id),id,construction)??throw new RuleViolation("NotAvailable");
        RequireIdle(actor);RequireActionLocation(actor,command.Action);
        if(command.Action.DemolitionQuote!=quote.Token)throw new RuleViolation("StaleDemolitionQuote");
        if(quote.Blockers.Length>0)throw new RuleViolation(quote.Blockers[0]);
        var recoveredFlows=quote.Recovery.ToDictionary(m=>m.ItemId,m=>checked(_flows[m.ItemId].ConstructionRecovered+m.Quantity));
        var job=construction?_storageConstruction[id]:null;var building=construction?null:_buildings[id];
        // Cancelling an upgrade returns only the unfinished project's materials; the existing building stays.
        if(job?.BuildingId>0)
        {
            var tx=_economy.Begin(Tick);if(quote.PriceMinor>0)tx.Pay(actor.Account,_treasury,quote.PriceMinor,"Demolition");long next=_nextId;
            var upgradeLots=new List<OverflowLot>();foreach(var m in quote.Recovery){var lot=new OverflowLot(next++,actor.Id,m.ItemId,m.Quantity,$"demolition:{id}",Tick,checked(Tick+GroundRules.OverflowGraceTicks),[]);long excess=tx.ReceiveForecast(job.InventoryId,m.ItemId,m.Quantity,lot).Excess;if(excess>0)upgradeLots.Add(lot with{Quantity=excess});}
            tx.Commit();_nextId=next;FinishCancellation(job,quote,[job.InventoryId],command);RecordDemolitionRecovery(quote,command);foreach(var lot in upgradeLots)Emit("OverflowCreated",actor.Id,lot.Id,lot.ItemId,lot.Quantity,reason:lot.ExpiresTick.ToString(System.Globalization.CultureInfo.InvariantCulture),command:command.CommandId);return;
        }
        Cell[] cells=quote.Cells.Select(c=>c.Position).ToArray();long nextId=_nextId;long input=building?.Input??job!.InventoryId;long[] grounds=cells.Select((_,n)=>n==0?input:nextId++).ToArray();
        var removed=building is null?new[]{input}:BuildingInventories(building);
        var tx2=_economy.Begin(Tick);if(quote.PriceMinor>0)tx2.Pay(actor.Account,_treasury,quote.PriceMinor,"Demolition");
        foreach(long inv in removed)foreach(var stock in _economy.Inventories[inv].Stock.Where(s=>s.Value>0))tx2.RemoveFreeStock(inv,stock.Key,stock.Value);
        tx2.SetCapacity(input,GroundRules.GroundCapacity);foreach(long inv in grounds.Skip(1))tx2.CreateInventory(inv,actor.Id,cells[Array.IndexOf(grounds,inv)],GroundRules.GroundCapacity);
        if(building?.Output is {} output)tx2.SetCapacity(output,0);
        var extraGround=new List<GroundStorage>();
        foreach(var plot in _plots.Values.Where(p=>p.Owner.HasValue&&p.Building is null&&!p.Definition.IsCorridor&&!_groundStorage.ContainsKey(p.Definition.Id)).OrderBy(p=>p.Definition.Id)){long inventory=nextId++;tx2.CreateInventory(inventory,plot.Owner!.Value,plot.Definition.Position,GroundRules.GroundCapacity);extraGround.Add(new(plot.Definition.Id,inventory));}
        var lots=new List<OverflowLot>();
        foreach(var item in quote.Stock.Concat(quote.Recovery).GroupBy(m=>m.ItemId,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            long remaining=item.Sum(m=>m.Quantity);string itemId=item.Key;
            // Fill normal capacity over all cells before assigning the excess at the anchor.
            foreach(long ground in grounds){long q=tx2.FreeVolume(ground,_items[itemId].UnitVolume,remaining);if(q>0){tx2.AddStock(ground,itemId,q);remaining-=q;}}
            if(remaining>0){var lot=new OverflowLot(nextId++,actor.Id,itemId,remaining,$"demolition:{id}",Tick,checked(Tick+GroundRules.OverflowGraceTicks),[]);tx2.ReceiveForecast(grounds[0],itemId,remaining,lot);lots.Add(lot);}
        }
        tx2.Commit();_nextId=nextId;foreach(var ground in extraGround)_groundStorage.Add(ground.PlotId,ground.InventoryId);
        for(int n=0;n<cells.Length;n++){var plot=_plotsByCell[cells[n]];plot.Building=null;_groundStorage[plot.Definition.Id]=grounds[n];}
        if(building is not null)
        {
            if(_enterprises.TryGetValue(id,out var e)){e.Enabled=false;ReleaseDemolitionWorkers(e.Group);}
            foreach(var l in _logistics.Values.Where(l=>l.Enterprise==id)){ReleaseDemolitionWorkers(l.Group);l.Enabled=false;l.InboundEnabled=false;l.Queue.Clear();l.Contract=0;l.Destination=0;l.InboundSource=0;l.Reason="ParkedAfterDemolition";}
            _preparingDemolition.Remove(id);
        }
        if(job is not null)FinishCancellation(job,quote,grounds,command);
        else{_demolitionReceipts.Add(new(id,false,actor.Id,Tick,quote.PriceMinor,quote.Cells,quote.Invested,quote.Recovery,grounds));Emit("BuildingDemolished",actor.Id,id,money:quote.PriceMinor,command:command.CommandId);}
        foreach(var lot in lots)Emit("OverflowCreated",actor.Id,lot.Id,lot.ItemId,lot.Quantity,reason:lot.ExpiresTick.ToString(System.Globalization.CultureInfo.InvariantCulture),command:command.CommandId);
        RecordDemolitionRecovery(quote,command);
        ApplyBuildingObstacles();
    }
    private void RecordDemolitionRecovery(DemolitionQuote quote,CommandEnvelope command)
    {foreach(var m in quote.Recovery){_flows[m.ItemId].ConstructionRecovered=checked(_flows[m.ItemId].ConstructionRecovered+m.Quantity);Emit("DemolitionMaterialRecovered",quote.Owner,quote.Target,m.ItemId,m.Quantity,command:command.CommandId);}foreach(var m in quote.Invested){long recovered=quote.Recovery.Where(r=>r.ItemId==m.ItemId).Sum(r=>r.Quantity);Emit("DemolitionMaterialDiscarded",quote.Owner,quote.Target,m.ItemId,m.Quantity-recovered,command:command.CommandId);}}
    private void FinishCancellation(StorageConstruction job,DemolitionQuote quote,long[] inventories,CommandEnvelope command)
    {_storageConstruction.Remove(job.Id);_demolitionReceipts.Add(new(job.Id,true,job.Owner,Tick,quote.PriceMinor,quote.Cells,quote.Invested,quote.Recovery,inventories));Emit("StorageConstructionCancelled",job.Owner,job.Id,money:quote.PriceMinor,command:command.CommandId);}
    private void ReleaseDemolitionWorkers(EmploymentGroup group)
    {int count=group.Headcount;_unemployed=checked(_unemployed+count);group.Headcount=0;group.Target=0;group.Ready=false;group.ReadinessReason="UserPaused";group.AssessedAtTick=Tick;if(count>0)Emit("EmploymentChanged",group.Owner,group.Id,quantity:-count);}
    private void GuardDemolition(GameAction a,long actor)
    {
        if(a.Kind is ActionKind.PrepareDemolition or ActionKind.CancelDemolitionPreparation or ActionKind.DemolishBuilding or ActionKind.CancelConstruction)return;
        bool RestrictedInventory(long id)=>_economy.Inventories.TryGetValue(id,out var inv)&&inv.Owner==actor&&DemolitionInventory(id);
        bool change=a.Kind is ActionKind.Build or ActionKind.UpgradeBuilding or ActionKind.SetRecipe or ActionKind.BuyLogistics||a.Kind==ActionKind.SetProductionEnabled&&a.Enabled;
        long target=a.Building.Value!=0?a.Building.Value:a.Enterprise.Value;
        if(change&&target!=0&&_buildings.TryGetValue(target,out var owned)&&owned.Owner==actor&&(Demolished(target)||PreparingDemolition(target)&&!(a.Kind==ActionKind.SetProductionEnabled&&_enterprises.TryGetValue(target,out var working)&&working.Batch is not null)))throw new RuleViolation("PreparingDemolition");
        if(a.Kind==ActionKind.Build&&_preparingDemolition.Any(id=>_buildings[id].Owner==actor&&_buildings[id].Footprint.Contains(a.Target)))throw new RuleViolation("PreparingDemolition");
        if(a.Kind is ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade){var terms=a.Terms??_negotiations.GetValueOrDefault(a.NegotiationId)?.Current.Terms;if(terms is not null&&(RestrictedInventory(terms.Source.Value)||RestrictedInventory(terms.Destination.Value)))throw new RuleViolation("PreparingDemolition");}
        if(a.Kind==ActionKind.CreateSellOffer&&_buildings.TryGetValue(a.Building.Value,out var marketShop)&&marketShop.Owner==actor&&(PreparingDemolition(marketShop.Id)||Demolished(marketShop.Id)))throw new RuleViolation("PreparingDemolition");
        bool incoming=a.Kind is ActionKind.CreateBuyOffer or ActionKind.Transfer or ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade;
        bool market=a.Kind is ActionKind.CreateSellOffer or ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade;
        if(incoming&&RestrictedInventory(a.Destination.Value)&&!(a.Kind==ActionKind.Transfer&&IsGround(a.Destination.Value))||market&&RestrictedInventory(a.Source.Value))throw new RuleViolation("PreparingDemolition");
        if(a.Kind==ActionKind.AcceptDeliveryQuantity&&_buyOffers.TryGetValue(a.BuyOfferId,out var o)&&RestrictedInventory(o.View.DestinationInventory.Value))throw new RuleViolation("PreparingDemolition");
        if(a.Enabled&&a.Logistics.Value!=0&&_logistics.TryGetValue(a.Logistics.Value,out var system)&&system.Owner==actor&&PreparingDemolition(system.Enterprise)&&a.Kind is ActionKind.SetLogisticsContract or ActionKind.SetLogisticsInbound or ActionKind.SetLogisticsStorage or ActionKind.SetLogisticsProduction or ActionKind.QueueLogisticsJob or ActionKind.SetTransportRoute or ActionKind.SetLogisticsEnabled)throw new RuleViolation("PreparingDemolition");
        if(a.Enabled&&a.Kind is ActionKind.SetLogisticsInbound or ActionKind.SetLogisticsStorage or ActionKind.SetLogisticsProduction or ActionKind.QueueLogisticsJob or ActionKind.SetTransportRoute)
            if(RestrictedInventory(a.Source.Value)||RestrictedInventory(a.Destination.Value))throw new RuleViolation("PreparingDemolition");
    }
    private void ValidateDemolition()
    {
        // Legacy ticks have no demolition journal to reconcile. Avoid an events×items
        // scan every tick; still reject impossible recovery when restoring a snapshot.
        if(_preparingDemolition.Count+_demolitionReceipts.Count==0)
        {
            if(_flows.Values.Any(f=>f.ConstructionRecovered!=0)||!_stepping&&_events.Any(e=>e.Type=="DemolitionMaterialRecovered"))throw new CheckpointException("InvalidConstructionRecovery");
            return;
        }

        if(_demolitionReceipts.Select(r=>(r.Target,r.Construction)).Distinct().Count()!=_demolitionReceipts.Count)throw new CheckpointException("DuplicateDemolition");
        foreach(long id in _preparingDemolition)if(!_buildings.ContainsKey(id)||Demolished(id)||!_events.Any(e=>e.Type=="DemolitionPrepared"&&e.EntityId==id))throw new CheckpointException("InvalidDemolitionPreparation");
        foreach(var flow in _flows.Values)if(flow.ConstructionRecovered<0||flow.ConstructionRecovered!=_events.Where(e=>e.Type=="DemolitionMaterialRecovered"&&e.ItemId==flow.Id).Sum(e=>e.Quantity))throw new CheckpointException("InvalidConstructionRecovery");
        foreach(var r in _demolitionReceipts)
        {
            var commands=r.Construction?_events.Where(e=>e.EntityId==r.Target&&e.Type=="StorageConstructionStarted").Select(e=>e.CommandId!).ToHashSet(StringComparer.Ordinal):_events.Where(e=>e.EntityId==r.Target&&e.Type is "BuildingConstructed" or "BuildingUpgraded").Select(e=>e.CommandId!).ToHashSet(StringComparer.Ordinal);
            var actual=_events.Where(e=>e.Type=="ConstructionMaterialConsumed"&&e.CommandId is {} id&&commands.Contains(id)).GroupBy(e=>e.ItemId,StringComparer.Ordinal).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new MaterialCost(g.Key,g.Sum(e=>e.Quantity))).ToArray();
            if(!actual.SequenceEqual(r.Invested))throw new CheckpointException("InvalidActualInvestment");
            if(r.Tick<1||r.Tick>Tick||r.GroundInventories.Length!=r.Cells.Length||r.Cells.Length==0||r.PriceMinor!=r.Cells.Sum(c=>checked(DemolitionRules.CellPriceMinor+(c.Level-1)*DemolitionRules.ExtraLevelPriceMinor))||!r.Recovery.SequenceEqual(r.Invested.Select(m=>new MaterialCost(m.ItemId,checked((long)((Int128)m.Quantity*DemolitionRules.RecoveryBasisPoints/10000)))).Where(m=>m.Quantity>0))||!_events.Any(e=>e.Type==(r.Construction?"StorageConstructionCancelled":"BuildingDemolished")&&e.EntityId==r.Target&&e.Tick==r.Tick&&e.ActorId?.Value==r.Owner&&e.AmountMinor==r.PriceMinor))throw new CheckpointException("InvalidDemolitionReceipt");
            if(!r.Construction){var b=_buildings[r.Target];if(b.Owner!=r.Owner||b.Input!=r.GroundInventories[0]||!r.Cells.SequenceEqual(b.Footprint.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,b.Level)))||b.Footprint.Any(c=>_plotsByCell[c].Building==r.Target)||_enterprises.TryGetValue(r.Target,out var e)&&(e.Enabled||e.Batch is not null||e.Group.Headcount!=0))throw new CheckpointException("InvalidDemolishedBuilding");}
        }
    }
}

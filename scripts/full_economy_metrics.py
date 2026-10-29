"""Observed F/G outcomes over actual runner exports, not a second simulation.
Cash settlement excludes escrow funding; game capital is the engine's own valuation.
Author: Шелестов Максим Евгеньевич.
"""
import collections,csv,gzip,json,statistics,itertools
from pathlib import Path

def identity(value):
    return int(value.get('value',0) if isinstance(value,dict) else value or 0)

def rows(folder,name):
    path=Path(folder)/name
    opener=path.open if path.exists() else lambda **kwargs:gzip.open(str(path)+'.gz',**kwargs)
    with opener(mode='rt',encoding='utf-8') as stream:
        for line in stream:
            if line.strip():yield json.loads(line)

def vehicle_returns(folder,vehicles,orders,actors,account_owners):
    """Observed delivery-fee recovery for identifiable vehicles, never fee*imagined future trips.
    Shared unallocated costs preclude a full per-vehicle result. Owner labour is unpriced.
    """
    by_owner=collections.Counter(identity(v['owner']) for v in vehicles)
    order_vehicle={int(o['id']):int(o['quote']['terms']['vehicleId']) for o in orders}
    escrow_vehicle={identity(o['escrowAccount']):order_vehicle[int(o['id'])] for o in orders}
    purchases={identity(e['entityId']):e for e in rows(folder,'events.jsonl') if e['type']=='VehiclePurchased'}
    result={}
    for v in vehicles:
        vid=int(v['id']);owner=identity(v['owner']);purchase=purchases.get(vid)
        if purchase is None:continue
        result[vid]=dict(vehicle=vid,owner=owner,model=v['modelId'],purchaseTick=purchase['tick'],purchaseMinor=purchase['amountMinor'],feeReceiptsMinor=0,driverWagesMinor=0,roadAndHandlingPaidMinor=0,paybackTick=None,
            allocation='Single owned vehicle: all paid road/handling costs included; driver wages by actual service order. Multiple vehicles: shared costs not allocated.',sharedCostsResolved=by_owner[owner]==1 and v.get('handlingLevel',0)==0 and not v.get('installation'))
    net={k:-r['purchaseMinor'] for k,r in result.items()}
    for e in rows(folder,'ledger.jsonl'):
        reason=e['reason'].split(':');kind=reason[0];amount=e['amountMinor'];vid=None;delta=0
        if kind=='ServicePayment':
            vid=escrow_vehicle.get(identity(e['debitAccount']))
            if vid in result:result[vid]['feeReceiptsMinor']+=amount;delta=amount
        elif kind=='ServiceDriverWages' and len(reason)>1:
            vid=order_vehicle.get(int(reason[1]))
            if vid in result:result[vid]['driverWagesMinor']+=amount;delta=-amount
        elif kind in {'RoadPrincipal','RoadPenalty','HandlingWages','HandlingService'}:
            owner=account_owners.get(identity(e['debitAccount']))
            if owner and by_owner[owner]==1:
                vid=next((k for k,r in result.items() if r['owner']==owner),None)
                if vid in result:result[vid]['roadAndHandlingPaidMinor']+=amount;delta=-amount
        if vid in result:
            if e['tick']<result[vid]['purchaseTick']:continue
            net[vid]+=delta;r=result[vid]
            if net[vid]>=0 and r['feeReceiptsMinor']>0 and r['sharedCostsResolved'] and r['paybackTick'] is None:r['paybackTick']=e['tick']
    for vid,r in result.items():
        r['feeCashNetAfterPurchaseMinor']=net[vid] if r['sharedCostsResolved'] else None
        r['remainingRoadDebtMinor']=actors[r['owner']].get('debtMinor')
        r['unbilledRoadAccrualMinor']=actors[r['owner']].get('unbilledAccruedMinor')
        r['scope']='Observed service-fee cash recovery only; owner opportunity cost, kits and other unallocated uses are not full economic ROI.'
    return list(result.values())

def measure(folder):
    folder=Path(folder)
    def load(name,default=None):
        p=folder/name
        return json.loads(p.read_text(encoding='utf-8')) if p.exists() else default
    state,scenario,city,summary=(load(n) for n in ['final-state.json','scenario.json','public-economy.json','summary.json'])
    if summary['status']!='succeeded':raise ValueError('Incomplete run is not an economic observation')
    initial={identity(p['id']):p['cashMinor'] for p in scenario['participants']}
    accounts={identity(p['accountId']):identity(p['id']) for p in state['participants']}
    actors={a:collections.Counter() for a in initial};unknown=collections.Counter();reason_totals=collections.Counter()
    escrow={}
    for a in state['marketAllocations']:escrow[identity(a['escrowAccount'])]=identity(a['buyer'])
    for c in state['deliveryContracts']:
        escrow[identity(c['escrowAccount'])]=identity(c.get('buyer'))
        escrow[identity(c['bondAccount'])]=identity(c['supplier'])
    services=load('services.json',{}) or {}
    for order in services.get('orders',[]):escrow[identity(order['escrowAccount'])]=identity(order['customer'])
    capex={'Construction','BuildingUpgrade','PlotPurchase','PlotReservation','VehiclePurchase','EquipmentPurchase','EquipmentUpgrade','LogisticsPurchase','LogisticsUpgrade','WorkshopOutfit','WorkshopModernization','RoadConstruction','ResourcePurchase','VehicleHandlingKit','LoadingPlatformUpgrade'}
    revenues={'CapitalSettlement','DeliveryPayment','PickupPayment','RoadRevenue','RoadOrderReward','QuestReward','IndustryQuestReward'}
    costs={'Wages','Energy','ResourceLease','HandlingService','HandlingWages','EquipmentTravelWages','LogisticsWages','RoadWages','ServiceDriverWages','RoadPrincipal','RoadPenalty','Demolition'}
    financial={'MarketEscrow','DeliveryFunding','DeliveryBond','DeliveryBondReturn','DeliveryRefund','PickupRefund','ServiceFunding','ServiceRefund','ServiceLateRefund','PlotReservationRefund'}
    for entry in rows(folder,'ledger.jsonl'):
        reason=entry['reason'].split(':')[0];amount=entry['amountMinor'];debit=identity(entry['debitAccount']);credit=identity(entry['creditAccount'])
        da,ca=accounts.get(debit),accounts.get(credit)
        if da:actors[da]['cashDebitsMinor']+=amount
        if ca:actors[ca]['cashCreditsMinor']+=amount
        reason_totals[reason]+=amount
        # Delivery receipts settle the buyer's reserved money, not a new second purchase.
        if reason in {'DeliveryPayment','PickupPayment','ServicePayment'}:
            buyer=escrow.get(debit,da)
            if buyer in actors:actors[buyer]['serviceCostMinor' if reason=='ServicePayment' else 'goodsCostMinor']+=amount
            if ca:actors[ca]['serviceRevenueMinor' if reason=='ServicePayment' else 'salesRevenueMinor']+=amount
            continue
        if reason in revenues:
            if ca:actors[ca]['roadRevenueMinor' if reason=='RoadRevenue' else 'workRewardMinor' if reason in {'RoadOrderReward','QuestReward','IndustryQuestReward'} else 'salesRevenueMinor']+=amount
            continue
        if reason in costs or reason.endswith('Wages'):
            if da:actors[da]['operatingCostsMinor']+=amount;actors[da]['roadPaidMinor' if reason in {'RoadPrincipal','RoadPenalty'} else 'wagesMinor' if reason.endswith('Wages') else 'otherOperatingMinor']+=amount
            continue
        if reason in {'PickupFine','DeliveryFine'}:
            payer=escrow.get(debit,da)
            if payer in actors:actors[payer]['operatingCostsMinor']+=amount;actors[payer]['finesMinor']+=amount
            if ca:actors[ca]['compensationMinor']+=amount
            continue
        if reason in capex:
            if da:actors[da]['cashCapitalSpendingMinor']+=amount
            continue
        if reason in financial or 'Funding' in reason or 'Escrow' in reason or 'Refund' in reason:continue
        if da or ca:unknown[reason]+=amount
    final_cash={identity(a['id']):a['balanceMinor'] for a in state['accounts']}
    capital={identity(p['actor']):p['capitalMinor'] for p in city['participants']}
    for account,actor in accounts.items():
        r=actors[actor];r['cashMinor']=final_cash[account];r['capitalMinor']=capital[actor]
        if initial[actor]+r['cashCreditsMinor']-r['cashDebitsMinor']!=r['cashMinor']:raise ValueError('Cash ledger reconciliation failed for '+str(actor))
        r['operatingReceiptResultMinor']=r['salesRevenueMinor']+r['serviceRevenueMinor']+r['roadRevenueMinor']+r['workRewardMinor']+r['compensationMinor']-r['goodsCostMinor']-r['serviceCostMinor']-r['operatingCostsMinor']
    types={t['id']:t['kind'] for t in scenario.get('buildingTypes',[])}
    building_types={identity(b['id']):b['typeId'] for b in state.get('buildings',[])}
    milestones={str(a):dict(firstProductionTick=None,physicalLevels={str(i):None for i in range(1,6)},rightCensoredAt=state['tick']) for a in actors}
    output=collections.Counter();event_counts=collections.Counter();rejections=collections.Counter();overflow_losses=collections.Counter()
    for e in rows(folder,'events.jsonl'):
        kind=e['type'];event_counts[kind]+=1;actor=identity(e.get('actorId'));entity=identity(e.get('entityId'))
        if kind=='CommandRejected':rejections[e.get('reasonCode') or 'Unknown']+=1
        if kind=='OverflowLost':overflow_losses[e['itemId']]+=e['quantity']
        if kind=='ProductionCompleted':output[e['itemId']]+=e['quantity']
        if kind=='BuildingConstructed':building_types[entity]=e['reasonCode']
        if actor not in actors or kind not in {'BuildingConstructed','BuildingUpgraded'} or types.get(building_types.get(entity))!='Production':continue
        level=1 if kind=='BuildingConstructed' else int(e['reasonCode']);m=milestones[str(actor)]
        m['physicalLevels'][str(level)]=m['physicalLevels'][str(level)] or e['tick']
        if level==1:m['firstProductionTick']=m['firstProductionTick'] or e['tick']
    development=load('development.json',{}) or {};xp=collections.defaultdict(collections.Counter);unlock={}
    thresholds=json.loads((Path(__file__).resolve().parents[1]/'docs/vnext/data/BALANCE_VNEXT.json').read_text())['enterprise_tiers']
    for grant in sorted(development.get('grants',[]),key=lambda g:g['tick']):
        key=str(grant['owner'])+':'+grant['industry'];xp[key][grant['source']]+=grant['xp'];total=sum(xp[key].values())
        levels=unlock.setdefault(key,{str(t['level']):0 if t['xp']==0 else None for t in thresholds})
        for tier in thresholds:
            if total>=tier['xp'] and levels[str(tier['level'])] is None:levels[str(tier['level'])]=grant['tick']
    ownership=collections.defaultdict(collections.Counter)
    for b in state.get('buildings',[]):
        if b.get('kind')=='Production':ownership[b['typeId']][str(identity(b['ownerId']))]+=1
    hhi={industry:sum((n/sum(counts.values()))**2 for n in counts.values()) for industry,counts in ownership.items()}
    personal_idle=collections.Counter();longest_wait=collections.Counter();wait_start={};last={};goals=collections.Counter();goal_reasons=collections.Counter();zero_cash=collections.Counter();minimum_cash={}
    with (folder/'agents.csv').open(encoding='utf-8',newline='') as stream:
        for r in csv.DictReader(stream):
            actor=int(r['actor_id']);tick=int(r['tick']);goals[r['goal']]+=1
            goal_reasons[r['goal']+':'+r['reason']]+=1
            cash=int(r['cash_minor']);zero_cash[actor]+=cash==0;minimum_cash[actor]=min(minimum_cash.get(actor,cash),cash)
            idle=r['busy']=='False';personal_idle[actor]+=idle
            waiting=idle and r['goal'].lower() in {'wait','idle'}
            if waiting:
                start=wait_start.get(actor,tick) if last.get(actor) in (tick-1,tick) else tick;wait_start[actor]=start
                longest_wait[actor]=max(longest_wait[actor],tick-start+1)
            else:wait_start.pop(actor,None)
            last[actor]=tick
    idle_workers=0;labor_ticks=set()
    with (folder/'labor.csv').open(encoding='utf-8',newline='') as stream:
        for tick,block in itertools.groupby(csv.DictReader(stream),key=lambda r:int(r['tick'])):
            latest={int(r['group_id']):r for r in block};labor_ticks.add(tick)
            idle_workers+=sum(int(r['headcount'])*(r['ready']!='True') for r in latest.values())
    fleet=load('fleet.json',{}) or {};roads=load('roads.json',{}) or {};billing=load('billing.json',{}) or {};physical=load('physical-state.json',{}) or {}
    for personal in load('personal-accounts.json',[]) or []:
        actor=identity(personal['actor']);value=personal['personal']
        if actor in actors:actors[actor].update(debtMinor=value['debtMinor'],unbilledAccruedMinor=value['accruedMinor'],escrowMinor=value['escrowMinor'])
        if actor in actors:
            actors[actor]['roadLiabilityMinor']=value['debtMinor']+value['accruedMinor']
            actors[actor]['operatingAfterRoadDebtMinor']=actors[actor]['operatingReceiptResultMinor']-actors[actor]['roadLiabilityMinor']
    imports=collections.Counter()
    for a in state['marketAllocations']:
        if identity(a['seller'])==0:imports[a['itemId']]+=a['collectedQuantity']
    overflow=collections.Counter();items={i['id']:i for i in scenario['items']}
    for inventory in state['inventories']:
        for lot in inventory.get('overflow') or []:overflow[lot['itemId']]+=lot['quantity']
    resources=collections.defaultdict(collections.Counter)
    for node in physical.get('resources',[]):
        r=resources[node['itemId']];r['stock']+=node['stock'];r['reserved']+=node['reserved'];r['capacity']+=node['capacity'];r['depletedNodes']+=node['stock']==0
        if node.get('public'):r['publicStock']+=node['stock'];r['publicReserved']+=node['reserved'];r['publicNodes']+=1;r['depletedPublicNodes']+=node['stock']==0
    pressure=collections.defaultdict(collections.Counter)
    for inv in state['inventories']:
        r=pressure[str(identity(inv.get('ownerId')))];r['capacityVolume']+=inv['capacity'];r['occupiedVolume']+=inv.get('occupiedVolume',0);r['reservedVolume']+=inv.get('reservedCapacity',0);r['fullInventories']+=inv.get('occupiedVolume',0)>=inv['capacity']>0
    demand=collections.Counter();sales=collections.defaultdict(collections.Counter)
    for offer in state.get('capitalOffers',[]):demand[offer['itemId']]+=offer['remainingQuantity']
    for sale in state.get('settlements',[]):
        r=sales[sale['itemId']];r['requested']+=sale['requested'];r['accepted']+=sale['accepted'];r['paidMinor']+=sale['paidMinor']
    content=load('production-content.json',{}) or {}
    runs=(services.get('execution') or {}).get('runs',[])
    npcs=sorted(a for a in actors if a!=1);natural=bool(scenario.get('newGame'))
    # Initial stock/buildings require a proper initial valuation; do not fabricate it for rich fixtures.
    changes=[capital[a]-initial[a] for a in npcs] if natural and not scenario.get('buildings') and not scenario.get('enterprises') else []
    result=dict(version=1,actors={str(a):dict(r) for a,r in actors.items()},milestones=milestones,experience={k:dict(v) for k,v in xp.items()},industryUnlockTicks=unlock,
        productionUnitsByItem=dict(output),productionOwnership={k:dict(v) for k,v in ownership.items()},industryOwnerHHI=hhi,importsCollectedByItem=dict(imports),overflowUnitsByItem=dict(overflow),overflowVolume=sum(q*items[i].get('volume',1) for i,q in overflow.items()),
        events=dict(event_counts),ledgerReasons=dict(reason_totals),unclassifiedActorCashMinor=dict(unknown),cashLedgerReconciled=True,
        vehicleCount=len(fleet.get('vehicles',[])),completedRoadStages=sum(identity(c['builder'])>0 for r in roads.get('cells',[]) for c in r['contributions']),
        roadDebtPrincipalMinor=sum(i['principalRemaining'] for i in billing.get('invoices',[])),roadDebtPenaltyMinor=sum(i['penaltyRemaining'] for i in billing.get('invoices',[])),
        resourceAvailabilitySnapshot={k:dict(v) for k,v in resources.items()},storagePressureSnapshot={k:dict(v) for k,v in pressure.items()},overflowLostUnitsByItem=dict(overflow_losses),commandRejectedReasons=dict(rejections),
        importAvailabilitySnapshot=content.get('imports',[]),capitalDemandRemainingSnapshot=dict(demand),capitalSettlementsByItem={k:dict(v) for k,v in sales.items()},
        trafficSnapshotReasons=dict(collections.Counter(t['reason'] for t in roads.get('traffic',[]))),maxCurrentTrafficWaitTicks=max((state['tick']-t['waitingSince'] for t in roads.get('traffic',[]) if t.get('waitingSince')),default=0),
        serviceStates=dict(collections.Counter(o['state'] for o in services.get('orders',[]))),serviceDeliveredQuantity=sum(r['delivered'] for r in runs),serviceLateRefundMinor=sum(r['lateRefundMinor'] for r in runs),
        serviceOrders=len(services.get('orders',[])),servicePaidMinor=sum(r['paidMinor'] for r in (services.get('execution') or {}).get('runs',[])),
        personalIdleSamples=dict(personal_idle),longestWaitSampleStreak=dict(longest_wait),goalSampleCounts=dict(goals),goalReasonSamples=dict(goal_reasons),zeroCashSamples=dict(zero_cash),minimumObservedCashMinor=minimum_cash,idleWorkerSamples=idle_workers,laborSampleTicks=len(labor_ticks),laborSampleDefinition='Headcount-weighted non-ready group samples, deduplicated within tick. Not full observed worker idle time.',
        roi=dict(observedCapitalCashMinor=sum(a['cashCapitalSpendingMinor'] for a in actors.values()),vehicles=vehicle_returns(folder,fleet.get('vehicles',[]),services.get('orders',[]),actors,accounts),paybackTick=None,reason='Mixed enterprise/road portfolios require attributable revenue and costs. Unobserved payback stays censored; no static-margin extrapolation.'),
        definition='Actual settled receipts minus all actual goods/service settlement costs, wages/energy/lease/road/fines. Escrow funding/refunds and cash capex separate; purchased construction materials are included among goods costs, not allocated to an entity. Unsold stock, depreciation and owner opportunity cost are not accrual net profit. Idle samples do not certify no feasible action.')
    progress=load('run-progress.json',{}) or {};interval=state['tick']-progress.get('startTick',0)
    return dict(fullEconomy=result,fullEconomyMetricsVersion=1,hostedMillisecondsPerActiveTick=(summary.get('elapsedMilliseconds',0)-(summary.get('startupMilliseconds') or 0))/interval if interval>0 else None,npcMedianCapitalChangeMinor=statistics.median(changes) if changes else None,
        npcOperatingReceiptResultMinor=sum(actors[a]['operatingReceiptResultMinor'] for a in npcs) if not unknown else None,
        npcOperatingAfterRoadDebtMinor=sum(actors[a]['operatingAfterRoadDebtMinor'] for a in npcs) if not unknown and all('operatingAfterRoadDebtMinor' in actors[a] for a in npcs) else None,
        firstNpcProductionTick=min((m['firstProductionTick'] for a,m in milestones.items() if a!='1' and m['firstProductionTick'] is not None),default=None))

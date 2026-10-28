"""Independent F2 integer settlement and physical conservation audit. Шелестов Максим Евгеньевич."""
def verify_run(config,state,services,o,r,ledger,events,vehicles,inventories,actors,accounts):
    t=o['quote']['terms'];fee=o['quote']['feeMinor'];quantity=t['quantity'];identity=o['id']
    cargo=r['loaded']-r['delivered']-r['returned'];remaining=quantity-r['loaded']-r['released']
    assert min(cargo,remaining,r['delivered'],r['returned'],r['released'])>=0
    assert r['returnInventory']==o['quote']['returnInventory']
    if r['returnInventory']==t['destination'] and not t['contractId']:assert r['returned']==0, 'Actual receipt at agreed destination must settle delivery fee'
    assert inventories[r['returnInventory']['value']]['ownerId']==o['customer']
    delivered=gross=paid=late_gross=late_refund=0;last_tick=0
    for receipt in r['receipts']:
        assert last_tick<=receipt['tick']<=state['tick'] and receipt['quantity']>0
        last_tick=receipt['tick'];delivered+=receipt['quantity'];part=fee*delivered//quantity-gross;gross+=part
        if receipt['tick']>o['deadlineTick']:late_gross+=part
        penalty=late_gross*config['economy']['serviceExecution']['lateBasisPoints']//10000-late_refund
        late_refund+=penalty;paid+=part-penalty
        assert receipt['grossMinor']==part and receipt['paidMinor']==part-penalty and receipt['lateRefundMinor']==penalty
        matches=[e for e in events if e['type']=='ServicePartDelivered' and e['entityId']==identity and e['tick']==receipt['tick']]
        assert len(matches)==1 and matches[0]['quantity']==receipt['quantity'] and matches[0]['amountMinor']==receipt['paidMinor']
    assert (delivered,gross,paid,late_gross,late_refund)==(r['delivered'],r['grossSettledMinor'],r['paidMinor'],r['lateGrossMinor'],r['lateRefundMinor'])
    assert r['loaded']==sum(e['quantity'] for e in events if e['type']=='ServiceCargoLoaded' and e['entityId']==identity)
    assert r['returned']==sum(e['quantity'] for e in events if e['type']=='ServiceCargoReturned' and e['entityId']==identity)
    payments=[l for l in ledger if l['reason']=='ServicePayment:'+str(identity)]
    refunds=[l for l in ledger if l['reason'] in ('ServiceRefund:'+str(identity),'ServiceLateRefund:'+str(identity))]
    assert sum(l['amountMinor'] for l in payments)==paid
    assert sum(l['amountMinor'] for l in refunds)==r['refundedMinor']
    for group,recipient in ((payments,o['carrier']),(refunds,o['customer'])):
        assert all(l['debitAccount']['value']==o['escrowAccount'] and l['creditAccount']==actors[recipient['value']]['accountId'] for l in group)
    assert all(l['reason'] in ('ServicePayment:'+str(identity),'ServiceRefund:'+str(identity),'ServiceLateRefund:'+str(identity)) for l in ledger if l['debitAccount']['value']==o['escrowAccount'])
    assert accounts[o['escrowAccount']]['balanceMinor']==fee-r['paidMinor']-r['refundedMinor']>=0
    if o['state']=='Accepted':
        vehicle=vehicles[t['vehicleId']];stock=inventories[vehicle['cargoInventory']['value']]['stock']
        assert sum(p['quantity'] for p in stock)==cargo
        assert all(p['itemId']==t['itemId'] and p['reserved']==p['quantity'] for p in stock)
        assert vehicle['driver'] in (0,o['carrier']['value'])
    else:
        assert o['state'] in ('Completed','Cancelled') and cargo==remaining==0
        assert r['paidMinor']+r['refundedMinor']==fee
    if o['state']=='Completed':assert delivered==quantity and r['returned']==r['released']==0

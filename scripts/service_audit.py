"""Independent F1 quote/reservation/escrow audit. Шелестов Максим Евгеньевич."""
import json
from pathlib import Path
from collections import Counter
SCALE=800*10000*10000

def verify(folder):
    folder=Path(folder);config=json.loads((folder/'scenario.json').read_text());rules=(config.get('economy') or {}).get('services');path=folder/'services.json'
    ledger=[json.loads(l) for l in (folder/'ledger.jsonl').read_text().splitlines()]
    if not rules:
        assert not path.exists()
        assert not any(l['reason'].startswith('Service') for l in ledger)
        return
    services=json.loads(path.read_text());state=json.loads((folder/'final-state.json').read_text());fleet=json.loads((folder/'fleet.json').read_text())
    vehicles={v['id']:v for v in fleet['vehicles']};models={m['id']:m for m in config['economy']['fleet']['models']};items={i['id']:i for i in config['items']}
    accounts={a['id']['value']:a for a in state['accounts']};actors={p['id']['value']:p for p in state['participants']};inventories={i['id']['value']:i for i in state['inventories']}
    events=[json.loads(l) for l in (folder/'events.jsonl').read_text().splitlines()];active=[]
    for identity in services['listedVehicles']:assert identity in vehicles and not models[vehicles[identity]['modelId']]['materialsOnly']
    for o in services['orders']:
        q=o['quote'];t=q['terms'];model=models[vehicles[t['vehicleId']]['modelId']];volume=t['quantity']*items[t['itemId']]['unitVolume'];per=model['capacityVolume']//items[t['itemId']]['unitVolume'];trips=(t['quantity']+per-1)//per
        assert q['volume']==volume and q['quantityPerTrip']==per and q['trips']==trips
        assert q['loadedDistance']==len(q['loaded'])-1>0 and q['etaTicks']==q['travelTicks']+q['handlingTicks']>0
        assert q['bufferTicks']==max(rules['minimumBufferTicks'],(q['etaTicks']*rules['bufferBasisPoints']+9999)//10000)
        blocks=((volume+rules['volumeBlock']-1)//rules['volumeBlock'])*((q['loadedDistance']+rules['distanceBlock']-1)//rules['distanceBlock'])
        numerator=(rules['fixedFeeMinor']+rules['blockFeeMinor']*blocks)*SCALE*10000+(rules['driverWageMinor']*(q['etaTicks']+q['extraStaffTicks'])*SCALE+q['roadUnits'])*rules['costMarkupBasisPoints']
        assert q['feeMinor']==max(rules['minimumFeeMinor'],(numerator+SCALE*10000-1)//(SCALE*10000))
        assert o['respondByTick']>=o['createdTick']+rules['responseTicks']
        assert len([e for e in events if e['type']=='ServiceRequested' and e['entityId']==o['id']])==1
        status=o['state'];is_active=status in ('Pending','Requote','Accepted')
        if is_active:
            active.append(t['vehicleId']);assert not o['closedTick']
            assert (o['deadlineTick'] if status=='Accepted' else o['respondByTick'])>state['tick']
            if not t['purchaseId']:
                stock=next(s for s in inventories[t['source']['value']]['stock'] if s['itemId']==t['itemId']);assert stock['reserved']>=t['quantity']
        accepted=[e for e in events if e['type']=='ServiceAccepted' and e['entityId']==o['id']]
        assert len(accepted)==bool(o['escrowAccount'])
        if accepted:assert accepted[0]['tick']==o['acceptedTick'] and accepted[0]['amountMinor']==q['feeMinor']
        funded=[l for l in ledger if l['reason']=='ServiceFunding:'+str(o['id'])];refunded=[l for l in ledger if l['reason']=='ServiceRefund:'+str(o['id'])]
        if o['escrowAccount']:
            assert len(funded)==1 and funded[0]['amountMinor']==q['feeMinor'] and funded[0]['creditAccount']['value']==o['escrowAccount']
            assert o['deadlineTick']==o['acceptedTick']+q['etaTicks']+q['bufferTicks']
            expected=q['feeMinor'] if status=='Accepted' else 0
            assert accounts[o['escrowAccount']]['kind']=='DeliveryServiceEscrow' and accounts[o['escrowAccount']]['balanceMinor']==expected
            assert len(refunded)==(status!='Accepted') and sum(l['amountMinor'] for l in refunded)==q['feeMinor']-expected
            assert all(l['creditAccount']['value']==actors[o['customer']['value']]['accountId']['value'] for l in refunded)
            assert all(l['reason']=='ServiceRefund:'+str(o['id']) for l in ledger if l['debitAccount']['value']==o['escrowAccount'])
        else:assert not funded and not refunded and status!='Accepted'
    assert len(active)==len(set(active))
    public=[json.loads(l) for l in (folder/'public-events.jsonl').read_text().splitlines()]
    assert not any(e['type'].startswith('Service') for e in public)
    print(f'{folder}: F1 quotes, exclusive leases, frozen terms, escrow/refunds and private events verified')

if __name__=='__main__':
    import sys
    for folder in sys.argv[1:]:verify(folder)

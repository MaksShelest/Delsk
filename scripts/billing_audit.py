"""Independent E3 road entitlement, invoice, principal/penalty and payment audit.
Author: Шелестов Максим Евгеньевич.
"""
import json
from collections import defaultdict
from pathlib import Path

SCALE=800*10000*10000

def split_units(units):
    total=sum(units.values())//SCALE
    assert total>=0
    allocation={r:max(0,n//SCALE) for r,n in units.items()}
    # Only positive values use floor here; negative residues start at zero.
    allocated=sum(allocation.values())
    while allocated>total:
        r=min((r for r in units if allocation[r]),key=lambda r:(units[r]-allocation[r]*SCALE,r));allocation[r]-=1;allocated-=1
    while allocated<total:
        r=min(units,key=lambda r:(-(units[r]-allocation[r]*SCALE),r));allocation[r]+=1;allocated+=1
    return {r:n for r,n in allocation.items() if n}

def verify(folder):
    folder=Path(folder)
    if not (folder/'billing.json').exists():return
    load=lambda name:json.loads((folder/name).read_text())
    config=load('scenario.json');state=load('billing.json');rules=config['economy']['billing']
    world=load('final-state.json');fleet=load('fleet.json')
    ledger=[json.loads(s) for s in (folder/'ledger.jsonl').read_text().splitlines()]
    events=[json.loads(s) for s in (folder/'events.jsonl').read_text().splitlines()]
    invoices={i['id']:i for i in state['invoices']};accrual=defaultdict(int);claims={};interest={};viewed=set()
    layers={tuple((c['x'],c['z'])):[0] for c in config['layout']['initialCityDirtCells']}
    vehicle={v['id']:v for v in fleet['vehicles']};machine={m['id']['value']:m for m in world['equipment']}
    accounts={p['id']['value']:p['accountId']['value'] for p in world['participants']}
    treasury=next(a['id']['value'] for a in world['accounts'] if a['kind']=='Treasury');accounts[0]=treasury
    clearing=state['clearingAccount'];assert next(a for a in world['accounts'] if a['id']['value']==clearing)['balanceMinor']==0
    for e in events:
        kind=e['type'];identity=e['entityId'];actor=(e.get('actorId') or {}).get('value')
        if kind=='RoadStageCompleted':
            cell=tuple(map(int,e['reasonCode'].split(',')));old=layers.setdefault(cell,[])
            assert len(old)+1==e['quantity'];old.append(actor)
        elif kind=='BillingDistance':
            cell=tuple(map(int,e['reasonCode'].split(',')));assert 0<e['quantity']<=80
            model=vehicle.get(identity);assert actor==(model['owner']['value'] if model else machine[identity]['ownerId']['value'])
            factor=rules['classFactors'][model['modelId']] if model else 10000
            units=e['quantity']*factor*rules['lightIncrementMinor']
            for builder in layers.get(cell,[]):
                if builder==0:accrual[actor,0]+=units*10000
                else:
                    accrual[actor,0]+=units*rules['cityShareBasisPoints']
                    accrual[actor,builder]+=units*(10000-rules['cityShareBasisPoints'])
        elif kind=='BillingInvoiceCreated':
            assert identity not in claims and e['tick']%rules['invoicePeriod']==0
            i=invoices[identity];assert i['payer']['value']==actor and i['createdTick']==e['tick']
            expected=split_units({r:n for (p,r),n in accrual.items() if p==actor})
            assert expected and expected=={c['recipient']['value']:c['principalMinor'] for c in i['claims']}
            assert sum(expected.values())==e['amountMinor']==i['principalMinor']
            for r,n in expected.items():accrual[actor,r]-=n*SCALE
            claims[identity]={r:[amount,0] for r,amount in expected.items()}
            assert i['dueTick']==e['tick']+rules['graceTicks'];interest[identity]=[0,0,i['dueTick']+1,0]
        elif kind=='BillingInvoiceViewed':viewed.add(identity)
        elif kind=='BillingPenaltyAccrued':
            i=invoices[identity];penalty,paid,next_tick,remainder=interest[identity]
            assert e['tick']==next_tick
            principal=sum(total-done for total,done in claims[identity].values());assert principal>0
            amount,remainder=divmod(principal*rules['penaltyBasisPoints']+remainder,10000)
            assert amount==e['amountMinor'];interest[identity]=[penalty+amount,paid,next_tick+rules['penaltyPeriod'],remainder]
        elif kind=='BillingInvoicePaid':
            i=invoices[identity];assert actor==i['payer']['value']
            principal=sum(total-done for total,done in claims[identity].values());p=min(principal,e['amountMinor']);fine=e['amountMinor']-p
            assert p==e['quantity'] and e['amountMinor']>0
            recipients=sorted(claims[identity]);open_claims={r:t-d for r,(t,d) in claims[identity].items()}
            shares={r:p*open_claims[r]//principal for r in recipients} if p else dict.fromkeys(recipients,0)
            if p:
                ranked=sorted(enumerate(recipients),key=lambda nr:(-(p*open_claims[nr[1]]%principal),(nr[0]+identity)%len(recipients)))
                for _,r in ranked[:p-sum(shares.values())]:shares[r]+=1
            for r,n in shares.items():assert n<=open_claims[r];claims[identity][r][1]+=n
            assert fine<=interest[identity][0]-interest[identity][1];interest[identity][1]+=fine
    assert set(claims)==set(invoices)
    assert {key:n for key,n in accrual.items() if n}=={(a['payer']['value'],a['recipient']['value']):a['units'] for a in state['accruals'] if a['units']}
    for identity,i in invoices.items():
        assert i['viewed']==(identity in viewed)
        assert {c['recipient']['value']:[c['principalMinor'],c['paidMinor']] for c in i['claims']}==claims[identity]
        assert [i['penaltyMinor'],i['penaltyPaidMinor'],i['nextPenaltyTick'],i['penaltyRemainder']]==interest[identity]
        principal=sum(done for total,done in claims[identity].values());payer=accounts[i['payer']['value']]
        debits=[l for l in ledger if l['reason']==f'RoadPrincipal:{identity}'];assert sum(l['amountMinor'] for l in debits)==principal
        assert all(l['debitAccount']['value']==payer and l['creditAccount']['value']==clearing for l in debits)
        revenue=[l for l in ledger if l['reason']==f'RoadRevenue:{identity}']
        assert all(l['debitAccount']['value']==clearing for l in revenue)
        for r,(_,paid) in claims[identity].items():assert sum(l['amountMinor'] for l in revenue if l['creditAccount']['value']==accounts[r])==paid
        penalties=[l for l in ledger if l['reason']==f'RoadPenalty:{identity}']
        assert sum(l['amountMinor'] for l in penalties)==i['penaltyPaidMinor']
        assert all(l['debitAccount']['value']==payer and l['creditAccount']['value']==treasury for l in penalties)
    public=[json.loads(s) for s in (folder/'public-events.jsonl').read_text().splitlines()]
    assert not any(e['type'].startswith('Billing') for e in public)
    print(str(folder)+': E3 distance, frozen rights, rounding, invoices, simple penalties, actual payouts and privacy verified')

def capital(folder):
    """Integer recognised claims/debt, including unbilled whole minor units, cancel in consolidation."""
    path=Path(folder)/'billing.json';assets=defaultdict(int);debt=defaultdict(int)
    if not path.exists():return assets,debt
    state=json.loads(path.read_text())
    by_payer=defaultdict(dict)
    for a in state['accruals']:by_payer[a['payer']['value']][a['recipient']['value']]=a['units']
    for payer,units in by_payer.items():
        for recipient,n in split_units(units).items():assets[recipient]+=n;debt[payer]+=n
    for i in state['invoices']:
        payer=i['payer']['value']
        for c in i['claims']:
            n=c['principalMinor']-c['paidMinor'];assets[c['recipient']['value']]+=n;debt[payer]+=n
        n=i['penaltyMinor']-i['penaltyPaidMinor'];assets[0]+=n;debt[payer]+=n
    return assets,debt

if __name__=='__main__':
    import sys
    for folder in sys.argv[1:]:verify(folder)

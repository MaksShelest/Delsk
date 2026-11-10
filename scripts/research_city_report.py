"""Чтение уже записанных экспортов Core. Новых экономических правил здесь нет.
Author: Шелестов Максим Евгеньевич.
"""
import csv
import json
from pathlib import Path

CANONICAL = 'tick,participant_cash_minor,wages_minor,treasury_minor,harvested,produced,exported,state_hash,population,unemployed'
LEGACY_ENERGY = 'tick,participant_cash_minor,wages_minor,energy_minor,harvested,produced,exported,state_hash,population,unemployed'


def ident(value):
    if isinstance(value, dict):
        return value.get('value', value.get('Value'))
    return value


def load_json(path):
    file = Path(path)
    if not file.is_file():
        return None
    return json.loads(file.read_text(encoding='utf-8-sig'))


def stream_jsonl(path):
    file = Path(path)
    if not file.is_file():
        return
    with file.open(encoding='utf-8') as stream:
        for line in stream:
            text = line.strip()
            if not text:
                continue
            try:
                yield json.loads(text)
            except json.JSONDecodeError:
                yield {'_invalid': True}


def thin(points, limit=240):
    if len(points) <= limit:
        return points
    step = max(1, (len(points) + limit - 1) // limit)
    chosen = points[::step]
    if points and points[-1] not in chosen:
        chosen.append(points[-1])
    return chosen


def read_metrics(folder):
    path = Path(folder)/'metrics.csv'
    if not path.is_file():
        return dict(kind='missing', rows=[], note='metrics.csv ещё нет.')
    with path.open(encoding='utf-8', newline='') as stream:
        header = stream.readline().strip()
        if header == LEGACY_ENERGY:
            return dict(kind='energy_minor', rows=[], note='Заголовок energy_minor — старая колонка расхода. Она не казна, график казны по ней не строится.')
        if header != CANONICAL:
            return dict(kind='unknown', rows=[], note='Неизвестный заголовок metrics.csv. Колонки не угадываются.')
        rows = []
        for raw in csv.DictReader(stream, fieldnames=header.split(',')):
            try:
                rows.append(dict(tick=int(raw['tick']), treasuryMinor=int(raw['treasury_minor']), wagesMinor=int(raw['wages_minor']),
                                 participantCashMinor=int(raw['participant_cash_minor']), harvested=int(raw['harvested']),
                                 produced=int(raw['produced']), population=int(raw['population']), unemployed=int(raw['unemployed'])))
            except (KeyError, TypeError, ValueError):
                continue
    document = load_json(Path(folder)/'metrics-format.json')
    confirmed = isinstance(document, dict) and document.get('version') == 1 and document.get('treasuryColumn') == 'treasury_minor'
    note = 'treasury_minor — баланс счёта казны. Подтверждено metrics-format.json.' if confirmed else 'Заголовок treasury_minor совпадает с форматом с P13: это баланс счёта казны, не расход энергии. Файла версии формата нет, поэтому это чтение исторического заголовка, а не миграция старых чисел.'
    return dict(kind='treasury', confirmed=confirmed, rows=thin(rows), sourceRows=len(rows), note=note)


def count_events(folder):
    built = {}
    demolished = 0
    recent = []
    invalid = 0
    total = 0
    interesting = {'BuildingConstructed', 'BuildingDemolished', 'BuildingUpgraded', 'ProductionCompleted', 'DeliveryReceived', 'CapitalSaleSettled'}
    for event in stream_jsonl(Path(folder)/'events.jsonl'):
        if event.get('_invalid'):
            invalid += 1
            continue
        total += 1
        kind = event.get('type')
        if kind == 'BuildingConstructed':
            key = event.get('reasonCode') or 'unknown'
            built[key] = built.get(key, 0) + 1
        elif kind == 'BuildingDemolished':
            demolished += 1
        if kind in interesting:
            recent.append(dict(tick=event.get('tick'), type=kind, actor=ident(event.get('actorId')), reason=event.get('reasonCode') or '', item=event.get('itemId') or ''))
            if len(recent) > 12:
                recent = recent[-12:]
    return dict(total=total, invalidLines=invalid, constructed=sum(built.values()), constructedByType=built, demolished=demolished, recent=recent)


def samples(folder):
    rows = []
    invalid = 0
    for line in stream_jsonl(Path(folder)/'economy-samples.jsonl'):
        if line.get('_invalid'):
            invalid += 1
            continue
        rows.append(line)
    return thin(rows), invalid


def participant_rows(final, city):
    accounts = {ident(item.get('id')): item for item in (final or {}).get('accounts') or []}
    people = {ident(item.get('actor')): item for item in (city or {}).get('participants') or []}
    buildings = (final or {}).get('buildings') or []
    enterprises = (final or {}).get('enterprises') or []
    rows = []
    for person in (final or {}).get('participants') or []:
        actor = ident(person.get('id'))
        account = accounts.get(ident(person.get('accountId'))) or {}
        capital = people.get(actor) or {}
        owned = [item for item in buildings if ident(item.get('ownerId')) == actor]
        staffed = [item for item in enterprises if ident(item.get('ownerId')) == actor and item.get('enabled') and (item.get('staff') or 0) > 0]
        rows.append(dict(actor=actor, human=actor == 1, cashMinor=account.get('balanceMinor'), capitalMinor=capital.get('capitalMinor'),
                         buildings=len(owned), productionBuildings=sum(1 for item in owned if item.get('kind') == 'Production'),
                         staffedProductions=len(staffed), busy=bool(person.get('busy'))))
    return rows


def contract_counts(contracts):
    counts = {}
    for item in contracts or []:
        status = item.get('status') or 'unknown'
        counts[status] = counts.get(status, 0) + 1
    return counts


def report(folder):
    folder = Path(folder)
    summary = load_json(folder/'summary.json') or {}
    final = load_json(folder/'final-state.json')
    city = load_json(folder/'public-economy.json')
    manifest = load_json(folder/'manifest.json') or {}
    metrics = read_metrics(folder)
    events = count_events(folder) if (folder/'events.jsonl').is_file() else None
    series, broken = samples(folder)
    status = summary.get('status')
    accounts = {item.get('kind'): item for item in (final or summary).get('accounts') or []} if final or summary else {}
    treasury = (accounts.get('Treasury') or {}).get('balanceMinor')
    wages = (accounts.get('WagesSink') or {}).get('balanceMinor')
    demand = (final or {}).get('capitalDemand') or summary.get('capitalDemand')
    periods = (demand or {}).get('periods') or []
    order_budget = periods[-1].get('monetaryBudgetMinor') if periods else None
    municipal = None if city is None else city.get('municipalMinor')
    flows = (final or {}).get('flows') or []
    buildings = (final or {}).get('buildings') or []
    limitations = []
    if status != 'succeeded':
        limitations.append('Прогон не завершён успешно. Эти числа не входят в сравнительную статистику.')
    if metrics['kind'] == 'energy_minor':
        limitations.append(metrics['note'])
    elif metrics['kind'] == 'treasury':
        limitations.append(metrics['note'])
        limitations.append('Колонка participant_cash_minor — деньги первого участника, в обычной партии это пассивный человек, не NPC.')
        limitations.append('Колонка wages_minor — баланс WagesSink, уже выплаченные зарплаты, а не ставка.')
    if treasury is None:
        limitations.append('Баланс счёта казны в итоговом снимке не найден.')
    if municipal is not None and treasury is not None and municipal != treasury:
        limitations.append('Муниципальный капитал больше кассы на дорожную дебиторку: municipalMinor = баланс казны + BillingReceivable города.')
    if order_budget is None:
        limitations.append('Бюджет заказов столицы в этом снимке не записан.')
    else:
        limitations.append('Бюджет заказов — monetaryBudgetMinor последнего периода спроса. Это не деньги казны.')
    limitations.append('Построено за исследование — события BuildingConstructed. Существует на конце — здания итогового снимка, без снесённых. Действующие производства — включённые линии со штатом больше нуля; public-economy.activeProductions считает то же для ступени города.')
    limitations.append('Добыто и произведено — накопленные потоки с начала партии, не прирост последнего тика.')
    limitations.append('Деньги NPC — баланс их счёта. Капитал — оценка активов PublicEconomy, не тот же остаток.')
    if broken:
        limitations.append('В economy-samples.jsonl есть оборванная строка. Она пропущена.')
    if not series and metrics['kind'] != 'treasury':
        limitations.append('Промежуточных срезов нет. Краткий режим показывает экономику после завершения, когда записаны metrics.csv и final-state.json.')
    commit = manifest.get('commit')
    verified = isinstance(commit, str) and len(commit) == 40 and all(ch in '0123456789abcdef' for ch in commit)
    return dict(
        complete=status == 'succeeded', status=status, failure=summary.get('failure'),
        tick=summary.get('tick') if summary else (series[-1].get('tick') if series else None),
        elapsedMilliseconds=summary.get('elapsedMilliseconds'),
        identity=dict(scenarioId=manifest.get('scenarioId'), seed=manifest.get('seed'), economyProfileId=manifest.get('economyProfileId'),
                      rulesVersion=manifest.get('rulesVersion'), commit=commit if verified else None,
                      commitNote=None if verified else 'Коммит Git в манифесте не подтверждён. Сравнение по коммиту не заявляется.'),
        treasuryBalanceMinor=treasury, municipalMinor=municipal, orderBudgetMinor=order_budget, wagesSinkMinor=wages,
        population=None if city is None else city.get('population'), employed=None if city is None else city.get('employed'),
        unemployed=None if city is None else city.get('unemployed'), activeProductions=None if city is None else city.get('activeProductions'),
        grossOutputMinor=None if city is None else city.get('grossOutputMinor'),
        buildings=dict(existing=None if final is None else len(buildings),
                       production=None if final is None else sum(1 for item in buildings if item.get('kind') == 'Production'),
                       warehouse=None if final is None else sum(1 for item in buildings if item.get('kind') == 'Warehouse'),
                       shop=None if final is None else sum(1 for item in buildings if item.get('kind') == 'Shop'),
                       constructed=None if events is None else events['constructed'], constructedByType=None if events is None else events['constructedByType'],
                       demolished=None if events is None else events['demolished']),
        flows=None if final is None else dict(harvested=sum(item.get('harvested') or 0 for item in flows), produced=sum(item.get('produced') or 0 for item in flows),
                   exported=sum(item.get('exported') or 0 for item in flows)),
        contracts=contract_counts((final or {}).get('deliveryContracts') or summary.get('deliveryContracts')),
        participants=participant_rows(final, city),
        metrics=metrics, samples=series, events=events, limitations=limitations)

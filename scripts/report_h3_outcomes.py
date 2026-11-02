#!/usr/bin/env python3
"""Render retained H3 observations; never runs simulation or treats NPCs as replicas.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,collections,gzip,json,statistics
from pathlib import Path
import analyze_h3

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--observations',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
with gzip.open(a.observations,'rt',encoding='utf-8') as stream:data=json.load(stream)
plan=json.loads(analyze_h3.PLAN.read_text(encoding='utf-8'))
if data['plan']!=plan:raise ValueError('Different preregistered cohort')
rows=[dict(r,frozen=data['frozen']) for r in data['rows']] if data.get('formatVersion')==2 else data['rows']
s=analyze_h3.summarize(rows,plan)
def latest(r):
    good=[v for v in r['segments'] if v['status']=='succeeded']
    return good[-1]['metrics']['fullEconomy'] if good else None
def number(value):
    return '—' if value is None else f'{value:.2f}'.rstrip('0').rstrip('.')
def median(values):return statistics.median(values) if values else None
def npc_milestones(f):return [v for k,v in f['milestones'].items() if k!='1']
lines=['# H3: фактические экономические результаты','',
    'Автор: Шелестов Максим Евгеньевич. Сформировано `scripts/report_h3_outcomes.py`.', '',
    f"Собрано **{len(rows)}/{s['expectedWorlds']}** исходных миров; отсутствует {len(s['missing'])}. "
    f"Обычных конечных точек t5000: **{sum(g['completeWorlds'] for g in s['groups'])}/180**.", '',
    'Это отдельная Movement1 когорта. Независимая единица — мир/seed. NPC одного мира коррелированы; '
    'продолжения, re-audit и checkpoint re-export не добавляют реплик. Обычные и extended результаты не объединяются. '
    'Тик равен секунде активной симуляции. First factory означает построенный производственный корпус, '
    'не первый проданный товар и не окупаемость.', '']
if any(g['completeWorlds']!=g['plannedWorlds'] for g in s['groups']):
    lines+=['**Предварительный срез:** часть ordinary endpoints неполна. Complete-case сравнения могут быть '
        'смещены отказами аудитора при развитии/стройке. Эти p-values не являются окончательным выводом о сложности.','']
lines+=['## Обычные партии: t5000','',
    'Число предприятий и цензурированные NPC ниже относятся только к полным конечным точкам. '
    'Медиана времени рассчитана среди наблюдавшихся построек, пропуски не заменены нулём. '
    'Изменение капитала — медиана по мирам, внутри мира используется медиана NPC; денежные значения в монетах.','',
    '| Карта | NPC | Сложность | Полных миров / план | Первых корпусов / NPC | Цензурировано | Медиана tick среди построивших | Медиана Δкапитала |',
    '|---|---:|---|---:|---:|---:|---:|---:|']
for g in s['groups']:
    selected=[r for r in rows if r['case']['phase']=='ordinary' and r['case']['map']==g['map'] and r['case']['bots']==g['bots'] and r['case']['difficulty']==g['difficulty'] and r['status']=='completed' and r['lastSavedTick']==5000]
    times=[m['firstProductionTick'] for r in selected for m in npc_milestones(latest(r)) if m['firstProductionTick'] is not None]
    delta=g['distributions']['npcMedianCapitalChangeMinor'].get('median')
    lines.append(f"| {g['map']} | {g['bots']} | {g['difficulty']} | {g['completeWorlds']}/{g['plannedWorlds']} | {g['firstFactoriesObserved']}/{g['npcMilestones']} | {g['censoredNpcMilestones']} | {number(median(times))} | {number(delta/100 if delta is not None else None)} |")
lines+=['','## Заранее объявленные парные сравнения','',
    'Hard−Medium и Easy−Medium, один seed на пару. Δ — разность медиан NPC capital change в монетах. '
    'Bootstrap95% и двусторонний sign test взяты из исходного H1 анализа; Holm применяется ко всем18 сравнениям. '
    'n10 при6NPC — primary, n5 при3/10NPC — exploratory. Это исход ранней экспозиции, не доказательство зрелого промышленного преимущества.','',
    '| Карта | NPC | Сравнение | Полных пар / план | Медиана Δ | Bootstrap95% Δ | Holm p |', '|---|---:|---|---:|---:|---|---:|']
for v in s['pairs']:
    ci=v['medianBootstrap95'];ci_text='—' if not ci else '…'.join(number(x/100) for x in ci)
    d=v['spread'].get('median')
    lines.append(f"| {v['map']} | {v['bots']} | {v['left']}−Medium | {v['completePairs']}/{v['plannedPairs']} | {number(d/100 if d is not None else None)} | {ci_text} | {v['signTestHolmAdjusted']:.6g} |")
lines+=['','## Длинные партии: последние принятые точки','',
    'Цель —126000, а не конец bounded batch. Показаны только прошедшие оба аудита checkpoints. '
    'Неуспешные/незавершённые попытки сохраняются в journals и observations. Статус ready означает необходимость следующего сегмента.','',
    '| Исходный мир | Статус | Принятый tick | Корпусов /6 | Физические II/III/IV/V | XP production / quest | Транспорт / дороги / service orders |',
    '|---|---|---:|---:|---|---:|---|']
for r in sorted((r for r in rows if r['case']['phase']=='extended'),key=lambda r:r['case']['id']):
    f=latest(r)
    if not f:
        lines.append(f"| {r['case']['id']} | {r['status']} | {r['lastSavedTick']} | — | — | — | — |")
        continue
    ms=npc_milestones(f);xp=collections.Counter()
    for actor,grants in f['experience'].items():
        if not actor.startswith('1:'):xp.update(grants)
    levels='/'.join(str(sum(m['physicalLevels'][str(i)] is not None for m in ms)) for i in range(2,6))
    lines.append(f"| {r['case']['id']} | {r['status']} | {r['lastSavedTick']} | {sum(m['firstProductionTick'] is not None for m in ms)}/6 | {levels} | {xp['Production']}/{xp['Quest']} | {f['vehicleCount']}/{f['completedRoadStages']}/{f['serviceOrders']} |")
lines+=['','## Реальные денежные потоки и ограничения ROI','',
    'Ниже — медиана **мировых сумм NPC** последних принятых extended checkpoints каждой карты. '
    'Разная длительность и censoring запрещают трактовать их как равный сравнительный горизонт. '
    'Goods включает импорт; импорт указан также отдельной подкатегорией и второй раз не прибавляется. '
    'Operating receipts исключают cash capex; они не означают прибыль/окупаемость конкретного предприятия.','',
    '| Карта | Миров | Сбыт, монеты | Goods / импорт | Зарплаты | Cash capex | Operating после road debt |',
    '|---|---:|---:|---:|---:|---:|---:|']
for map_key in plan['maps']:
    fs=[latest(r) for r in rows if r['case']['phase']=='extended' and r['case']['map']==map_key and latest(r)]
    def flow(key):return number(median([sum(v.get(key,0) for k,v in f['actors'].items() if k!='1')/100 for f in fs]))
    lines.append(f"| {map_key} | {len(fs)} | {flow('salesRevenueMinor')} | {flow('goodsCostMinor')} / {flow('importCostMinor')} | {flow('wagesMinor')} | {flow('cashCapitalSpendingMinor')} | {flow('operatingAfterRoadDebtMinor')} |")
lines+=['','Полные snapshots содержат imports/pickups/физические остатки, rejection reasons, worker-ready samples, '
    'production batches, фактические receipts и trace/ledger reconciliation. Worker/idle samples внутри мира зависимы. '
    'Наблюдаемая атрибутируемая окупаемость не подменяется статической маржой или экстраполяцией будущих продаж. '
    'Если paybackTick отсутствует, результат остаётся цензурированным. '
    'Prepared delivery/road fixtures и scripted human отсутствуют в этой независимой когорте.','',
    f"Источник: `{a.observations.as_posix()}`. Исходные артефакты, frozen fingerprints и audit errata находятся рядом в source-artifacts/observations/collection-verification.",'']
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text('\n'.join(lines),encoding='utf-8')
print(json.dumps(dict(worlds=len(rows),completeOrdinary=sum(g['completeWorlds'] for g in s['groups']),output=str(a.output))))

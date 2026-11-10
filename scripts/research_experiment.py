"""Конструктор научного опыта поверх существующей очереди лаборатории.
Author: Шелестов Максим Евгеньевич.

План проверяется expand(). Симуляцию выполняет прежний Research.Runner.
Этот модуль не придумывает экономические числа: пропуск остаётся пропуском.
"""
import copy
import csv
import hashlib
import json
import os
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path

from research_findings import findings
from research_lab import ROOT, integer, read, write
from research_report import write_bundle
from research_statistics import METHOD, METHOD_NOTES, describe, mean_interval, paired

SAMARA = timezone(timedelta(hours=4))
HUMAN = 1
KEEP_RUN = ('summary.json', 'public-economy.json', 'manifest.json', 'metrics.csv', 'metrics-format.json', 'run-progress.json', 'economy-samples.jsonl')
SKIP_RUN = ('decisions.jsonl', 'decisions.jsonl.gz', 'events.jsonl', 'world-frames.jsonl', 'world-index.jsonl', 'ledger.jsonl')


def _metric(**kwargs):
    return kwargs


METRICS = {item['id']: item for item in [
    _metric(id='cityCapitalMinor', title='Капитал города', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Сумма капитала участников и муниципального капитала по правилам PublicEconomy. Не равна наличным деньгам и не равна одной казне.',
             source='public-economy.json, поле capitalMinor'),
    _metric(id='municipalMinor', title='Муниципальный капитал', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=True, sample='municipalMinor',
             definition='Касса муниципалитета плюс дорожная дебиторка города. Это не сумма денег NPC.',
             source='public-economy.json, поле municipalMinor'),
    _metric(id='treasuryMinor', title='Казна города', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=True, sample='treasuryBalanceMinor',
             definition='Денежные средства счёта Treasury. Не являются суммой денег всех NPC и не включают дорожную дебиторку.',
             source='summary.json, счёт Treasury'),
    _metric(id='participantCashMinor', title='Деньги участников', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Сумма остатков счетов участников, включая пассивного человека обычной партии. Это не капитал и не казна.',
             source='summary.json, счета Participant'),
    _metric(id='operatingCashMinor', title='Операционный денежный результат', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Считается прежним research_metrics.measure только если экспорт полный и проводки классифицированы. Иначе показатель отсутствует, а не равен нулю. Это не бухгалтерская прибыль.',
             source='research_metrics.py по ledger.jsonl и итоговым файлам'),
    _metric(id='producedUnits', title='Производственный выпуск', category='Экономика', unit='count', unitLabel='единицы', money=False, trajectory=True, sample='produced',
             definition='Накопленное число произведённых единиц с начала партии. Не денежная оценка и не прирост последнего тика.',
             source='summary.json, сумма flows.produced'),
    _metric(id='harvestedUnits', title='Добыча ресурсов', category='Экономика', unit='count', unitLabel='единицы', money=False, trajectory=True, sample='harvested',
             definition='Накопленное число добытых единиц с начала партии.',
             source='summary.json, сумма flows.harvested'),
    _metric(id='tradeTurnoverMinor', title='Торговые обороты', category='Экономика', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Сумма фактически оплаченных minor по договорам поставки и рыночным выделениям. Пустые списки дают ноль. Отсутствующий список не заменяется нулём.',
             source='summary.json, paidMinor договоров и выделений'),
    _metric(id='contractsFulfilled', title='Выполненные договоры', category='Экономика', unit='count', unitLabel='договоры', money=False, trajectory=False,
             definition='Число договоров поставки со статусом Fulfilled в итоге прогона. Не доля и не число товарных единиц.',
             source='summary.json, deliveryContracts'),
    _metric(id='activeProductions', title='Действующие производства', category='Экономика', unit='count', unitLabel='линии', money=False, trajectory=True, sample='activeProductions',
             definition='Включённые производственные линии со штатом больше нуля. Это не число всех зданий.',
             source='public-economy.json, поле activeProductions'),
    _metric(id='population', title='Численность населения', category='Население', unit='count', unitLabel='жители', money=False, trajectory=True, sample='population',
             definition='Численность населения мира на конец прогона. В обычной партии она зависит от числа участников и не является независимым эффектом найма.',
             source='public-economy.json, поле population'),
    _metric(id='employed', title='Занятость', category='Население', unit='count', unitLabel='жители', money=False, trajectory=True, sample='employed',
             definition='Численность занятых на конец прогона: население минус безработные по PublicEconomy.',
             source='public-economy.json, поле employed'),
    _metric(id='unemployed', title='Безработица', category='Население', unit='count', unitLabel='жители', money=False, trajectory=True, sample='unemployed',
             definition='Численность незанятых на конец прогона.',
             source='public-economy.json, поле unemployed'),
    _metric(id='wagesSinkMinor', title='Выплаченные зарплаты', category='Население', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Баланс счёта WagesSink: уже выплаченные зарплаты. Это не ставка и не обещание найма.',
             source='summary.json, счёт WagesSink'),
    _metric(id='idleWorkerSamples', title='Простой в срезах труда', category='Население', unit='count', unitLabel='человеко-срезы', money=False, trajectory=False,
             definition='Сумма численности трудовых групп в срезах labor.csv, где ready не равно True. Это не число простаивающих людей на конец партии и не простой предприятия.',
             source='labor.csv, сумма headcount при ready != True'),
    _metric(id='npcCashMinor', title='Деньги NPC', category='NPC', unit='minor', unitLabel='монеты', money=True, trajectory=True, sample='npcCashMinor',
             definition='Сумма остатков счетов NPC в последнем срезе. Пассивный человек обычной партии не входит. В краткой записи срезов нет, поэтому показатель отсутствует.',
             source='economy-samples.jsonl, сумма cashMinor участников кроме человека'),
    _metric(id='npcCapitalMinor', title='Капитал NPC', category='NPC', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Сумма капитала NPC по PublicEconomy. Капитал — стоимость учитываемых активов минус обязательства. Он не равен наличным деньгам. Человек не входит.',
             source='public-economy.json, participants, кроме actor 1'),
    _metric(id='npcCashChangeMinor', title='Изменение денег NPC', category='NPC', unit='minor', unitLabel='монеты', money=True, trajectory=False,
             definition='Деньги NPC в последнем срезе минус деньги NPC в первом срезе. Без двух срезов показатель отсутствует.',
             source='economy-samples.jsonl, первый и последний срез'),
    _metric(id='constructedBuildings', title='Построенные здания', category='NPC', unit='count', unitLabel='здания', money=False, trajectory=True, sample='constructedBuildings',
             definition='Число событий строительства, накопленное к последнему срезу. Снесённые здания из этого счётчика не вычитаются.',
             source='economy-samples.jsonl, поле constructedBuildings'),
    _metric(id='controllerDecisionTurns', title='Выполненные решения NPC', category='NPC', unit='count', unitLabel='ходы', money=False, trajectory=False,
             definition='Число ходов контроллеров, записанное runner. Это не число достигнутых целей.',
             source='summary.json, controllerDecisionTurns'),
    _metric(id='controllerRejections', title='Отклонённые команды', category='NPC', unit='count', unitLabel='отказы', money=False, trajectory=False,
             definition='Число ответов контроллера со статусом Rejected. Достижение целей отдельным показателем не считается: в экспорте нет надёжного счётчика целей.',
             source='summary.json, controllerRejections'),
    _metric(id='elapsedMilliseconds', title='Время выполнения', category='Технические показатели', unit='milliseconds', unitLabel='секунды', money=False, trajectory=False,
             definition='Длительность прогона по часам runner. Это не игровое время.',
             source='summary.json, elapsedMilliseconds'),
    _metric(id='ticksPerSecond', title='Скорость тиков', category='Технические показатели', unit='rate', unitLabel='тики в секунду', money=False, trajectory=False,
             definition='Число тиков, делённое на длительность прогона в секундах. Не определено при нулевой длительности.',
             source='summary.json, tick и elapsedMilliseconds'),
    _metric(id='peakWorkingSetBytes', title='Пик памяти процесса', category='Технические показатели', unit='bytes', unitLabel='байты', money=False, trajectory=False,
             definition='Пик рабочего набора процесса runner, если run-progress.json его записал. summary.ramBytes по-прежнему не измеряется и здесь не подменяется.',
             source='run-progress.json, peakWorkingSetBytes'),
    _metric(id='folderBytes', title='Размер файлов прогона', category='Технические показатели', unit='bytes', unitLabel='байты', money=False, trajectory=False,
             definition='Сумма размеров файлов папки попытки. Содержимое журналов для этого числа не читается.',
             source='размеры файлов папки прогона'),
]}

TEMPLATES = {
    'wages': dict(title='Влияние зарплат на экономику', ordinary=True, factor='npc.baseWageMinor', kind='minor',
                  values=[15, 25], primary='employed', extras=['treasuryMinor', 'producedUnits', 'harvestedUnits', 'npcCashMinor'],
                  goal='Сравнить две целевые ставки политики NPC при одинаковых карте, seed и остальных настройках.',
                  hypothesis='Различие целевой ставки NPC может сопровождаться различием занятости. Направление заранее не утверждается.',
                  comparison='Группы отличаются только целевой ставкой политики NPC.',
                  fixed='Шкала мира остаётся 0,15 монеты, ручной шаг 0,02. Меняется целевая ставка NPC, а не ставка зданий и не цена участка.'),
    'difficulty': dict(title='Сравнение сложности алгоритмических NPC', ordinary=True, factor='npc.difficulty', kind='text',
                       values=['Easy', 'Hard'], primary='cityCapitalMinor', extras=['employed', 'producedUnits', 'controllerRejections'],
                       goal='Сравнить две настройки сложности политики NPC на одной карте и одних seeds.',
                       hypothesis='Сложности могут дать разные итоги. Превосходство Hard заранее не предполагается.',
                       comparison='Группы отличаются только сложностью алгоритмических NPC.',
                       fixed='Сложность — настройка политики, а не множитель выпуска и не доказанное преимущество.'),
    'npc-count': dict(title='Влияние количества NPC', ordinary=True, factor='ordinary.npcCount', kind='count',
                      values=[3, 6], primary='producedUnits', extras=['employed', 'treasuryMinor', 'harvestedUnits'],
                      goal='Сравнить обычные партии с разным числом алгоритмических NPC.',
                      hypothesis='Число NPC меняет состав мира. Население при этом масштабируется вместе с числом участников, поэтому оно не выбрано основным показателем.',
                      comparison='Группы отличаются только числом алгоритмических NPC.',
                      fixed='Стартовое население обычной партии зависит от размера состава. Это механическое следствие, а не отдельный найденный эффект.'),
    'starting-capital': dict(title='Влияние начального капитала', ordinary=True, factor='world.startingCashMinor', kind='minor',
                             values=[20000, 40000], primary='cityCapitalMinor', extras=['participantCashMinor', 'producedUnits', 'employed'],
                             goal='Сравнить две суммы стартовых денег участника.',
                             hypothesis='Разница стартовых денег участника может изменить капитал города. Формулы казны и населения этим шаблоном не меняются.',
                             comparison='Группы отличаются только стартовыми деньгами участника.',
                             fixed='Это стартовые деньги участника, не начальная казна и не капитал в смысле PublicEconomy.'),
    'negotiations': dict(title='Сравнение переговоров NPC', ordinary=False, factor='npc.negotiationsEnabled', kind='bool',
                         values=[True, False], primary='cityCapitalMinor', extras=['tradeTurnoverMinor', 'contractsFulfilled', 'controllerRejections'],
                         goal='Сравнить исторический профиль, когда переговоры NPC включены и выключены.',
                         hypothesis='Отключение переговоров меняет только эту настройку политики. Если переговорных событий нет, опыт не показывает эффект механизма.',
                         comparison='Группы отличаются только флагом переговоров NPC.',
                         fixed='Это историческая карта и профиль presentation-economy-v6, не Малая долина и не шкала 0,15.'),
    'production': dict(title='Производственная эффективность в различных условиях', ordinary=True, factor='npc.difficulty', kind='text',
                       values=['Easy', 'Hard'], primary='producedUnits', extras=['activeProductions', 'harvestedUnits', 'employed'],
                       goal='Сравнить производственный выпуск при двух сложностях политики NPC.',
                       hypothesis='Выпуск может различаться. Отдельного множителя производства у сложности нет.',
                       comparison='Группы отличаются только сложностью алгоритмических NPC.',
                       fixed='Нет отдельной ручки эффективности производства. Сравнивается уже существующая сложность политики.'),
    'custom': dict(title='Собственный эксперимент', ordinary=True, factor=None, kind=None, values=None, primary='cityCapitalMinor', extras=['employed'],
                   goal='', hypothesis='', comparison='Группы отличаются одним выбранным параметром.',
                   fixed='Остальные поля профиля не меняются.'),
}
CUSTOM_FACTORS = {
    'npc.difficulty': ('Сложность NPC', 'text', ['Easy', 'Medium', 'Hard']),
    'ordinary.npcCount': ('Количество NPC', 'count', [3, 6]),
    'npc.baseWageMinor': ('Целевая ставка NPC', 'minor', [15, 25]),
    'npc.maximumWageMinor': ('Предел ставки NPC', 'minor', [35, 50]),
    'npc.cashReserveMinor': ('Денежный резерв NPC', 'minor', [0, 6000]),
    'world.startingCashMinor': ('Стартовые деньги участника', 'minor', [20000, 40000]),
}
FACTOR_LABELS = {
    'npc.baseWageMinor': 'Целевая ставка NPC', 'npc.difficulty': 'Сложность NPC', 'ordinary.npcCount': 'Количество NPC',
    'world.startingCashMinor': 'Стартовые деньги участника', 'npc.negotiationsEnabled': 'Переговоры NPC',
    'npc.maximumWageMinor': 'Предел ставки NPC', 'npc.cashReserveMinor': 'Денежный резерв NPC',
}


def catalog():
    return dict(templates=[{'id': key, **{name: template[name] for name in ('title', 'goal', 'hypothesis', 'comparison', 'fixed', 'primary', 'ordinary', 'factor', 'kind', 'values', 'extras')}}
                           for key, template in TEMPLATES.items()],
                metrics=[{key: value[key] for key in ('id', 'title', 'category', 'unitLabel', 'money', 'definition', 'source')} for value in METRICS.values()],
                customFactors=[{'path': key, 'label': value[0], 'kind': value[1], 'examples': value[2]} for key, value in CUSTOM_FACTORS.items()],
                method=METHOD, methodNotes=list(METHOD_NOTES))


def _text(value, limit, label):
    if not isinstance(value, str) or not value.strip() or len(value.strip()) > limit:
        raise ValueError(f'{label}: от 1 до {limit} символов')
    return value.strip()


def _value_label(kind, value):
    if kind == 'minor':
        sign = '−' if value < 0 else ''
        return sign + f'{abs(Decimal(value)) / Decimal(100):.2f}'.replace('.', ',') + ' монеты'
    if kind == 'bool':
        return 'включены' if value else 'выключены'
    if kind == 'count':
        return str(value)
    return str(value)


def _groups(body, template):
    raw = body.get('groups')
    if raw is None:
        values = template['values']
        raw = [dict(label=chr(ord('A') + index), value=value) for index, value in enumerate(values)]
    if not isinstance(raw, list) or not 2 <= len(raw) <= 6:
        raise ValueError('Нужны от 2 до 6 групп')
    groups = []
    for index, group in enumerate(raw):
        if not isinstance(group, dict) or set(group) - {'label', 'value'} or 'value' not in group:
            raise ValueError('У группы нужны подпись и значение')
        label = group.get('label') or chr(ord('A') + index)
        if not isinstance(label, str) or not label.strip() or len(label.strip()) > 40:
            raise ValueError('Подпись группы: от 1 до 40 символов')
        groups.append(dict(label=label.strip(), value=group['value']))
    if len({group['label'] for group in groups}) != len(groups) or len({json.dumps(group['value']) for group in groups}) != len(groups):
        raise ValueError('Подписи и значения групп должны различаться')
    return groups


def _check_values(path, kind, groups):
    values = [group['value'] for group in groups]
    if kind == 'minor':
        if any(not integer(value, 0, 10**9) for value in values):
            raise ValueError('Денежное значение задаётся целым числом сотых монеты')
        if path == 'npc.baseWageMinor' and any(not integer(value, 1, 10000) for value in values):
            raise ValueError('Целевая ставка NPC: от 0,01 до 100,00 монеты')
    elif kind == 'count':
        if any(not integer(value, 3, 10) for value in values):
            raise ValueError('Количество NPC: целое от 3 до 10')
    elif kind == 'text':
        allowed = CUSTOM_FACTORS.get(path, (None, None, ['Easy', 'Medium', 'Hard']))[2]
        if any(value not in allowed for value in values):
            raise ValueError('Сложность: Easy, Medium или Hard')
    elif kind == 'bool':
        if any(type(value) is not bool for value in values):
            raise ValueError('Для переговоров нужны значения включено и выключено')
    else:
        raise ValueError('Неизвестный тип фактора')
    return values


def build_plan(body):
    """Короткий запрос мастера превращается в обычный план очереди."""
    if not isinstance(body, dict):
        raise ValueError('Запрос исследования должен быть объектом')
    template_id = body.get('template', 'custom')
    if template_id not in TEMPLATES:
        raise ValueError('Неизвестный шаблон исследования')
    template = TEMPLATES[template_id]
    factor = template['factor']
    kind = template['kind']
    ordinary = template['ordinary']
    if template_id == 'custom':
        factor = body.get('factor')
        if factor not in CUSTOM_FACTORS:
            raise ValueError('Для собственного опыта выберите один поддерживаемый параметр')
        kind = CUSTOM_FACTORS[factor][1]
        ordinary = True
    groups = _groups(body, template if template['values'] else dict(values=CUSTOM_FACTORS[factor][2][:2]))
    values = _check_values(factor, kind, groups)
    title = _text(body.get('title') or template['title'], 80, 'Название')
    goal = _text(body.get('goal') or template['goal'], 500, 'Цель')
    hypothesis = _text(body.get('hypothesis') or template['hypothesis'], 500, 'Гипотеза')
    comparison = _text(body.get('comparison') or template['comparison'], 300, 'Сравнение')
    requested = body.get('metrics') or [template['primary'], *template['extras']]
    if not isinstance(requested, list) or any(item not in METRICS for item in requested):
        raise ValueError('Выберите показатели из каталога')
    primary = body.get('primary') or template['primary']
    if primary not in requested:
        requested = [primary, *requested]
    if primary not in METRICS:
        raise ValueError('Основной показатель должен быть из каталога')
    from research_lab_presets import CITY_MAPS, city_timeout
    if ordinary:
        choice = next((item for item in CITY_MAPS if item['path'] == body.get('map')), CITY_MAPS[0])
        scenario, economy = choice['path'], choice['economy']
        npc_count = values[0] if factor == 'ordinary.npcCount' else body.get('npcs', 3)
        if not integer(npc_count, 3, 10):
            raise ValueError('Количество NPC: 3..10')
        tick_cap = 1_000_000
    else:
        historical = {
            'scenarios/presentation-map-one-v3.json': 'Карта 1 · исторический профиль',
            'scenarios/presentation-map-two-v3.json': 'Карта 2 · исторический профиль',
            'scenarios/presentation-map-three-v2.json': 'Карта 3 · исторический профиль',
        }
        scenario = body.get('map') or 'scenarios/presentation-map-one-v3.json'
        if scenario not in historical:
            raise ValueError('Шаблон переговоров использует исторические карты, не Малую долину')
        choice = dict(label=historical[scenario], path=scenario, economy='config/economy.json', profile='presentation-economy-v6')
        economy = 'config/economy.json'
        npc_count = None
        tick_cap = int(read(ROOT / economy)['world']['maxTicks'])
    ticks = int(body.get('ticks', 120))
    if not integer(ticks, 1, tick_cap):
        raise ValueError(f'Тики: 1..{tick_cap}')
    seed_count = int(body.get('seedCount', 4))
    first_seed = int(body.get('firstSeed', 42))
    if not integer(seed_count, 1, 500) or not integer(first_seed, 0, 2**64 - 1) or first_seed + seed_count - 1 > 2**64 - 1:
        raise ValueError('Количество seeds: 1..500, начальный seed — неотрицательное целое')
    repetitions = int(body.get('repetitions', 1))
    if not integer(repetitions, 1, 20):
        raise ValueError('Повторы одного seed в мастере: 1..20')
    recording = body.get('recording', 'normal')
    if recording not in ('brief', 'normal', 'detailed'):
        raise ValueError('Режим записи: brief, normal или detailed')
    profile = read(ROOT / choice['economy'])
    profile_id = profile.get('id', 'не указан')
    seeds = list(range(first_seed, first_seed + seed_count))
    slug = 'r2-' + template_id
    plan = dict(version=1, id=slug[:64], title=title, scenario=scenario, economy=economy, ticks=ticks, seeds=seeds,
                repetitions=repetitions, mode='algorithmic', recording=recording,
                factors=[dict(path=factor, values=values)],
                timeoutSeconds=city_timeout(ticks) if ordinary else min(14400, max(300, ticks * 4)))
    if ordinary:
        plan['ordinaryNpcCount'] = npc_count
    else:
        plan['controllers'] = 'scenarios/baseline-controllers.json'
    fixed = [template['fixed'], f'Профиль {profile_id}.', f'Карта: {choice["label"]}.',
             f'Тиков в прогоне: {ticks}. Seeds: {", ".join(map(str, seeds))}. Повторов seed: {repetitions}.',
             'Режим алгоритмический, без языковой модели.']
    if ordinary and factor != 'ordinary.npcCount':
        fixed.append(f'Алгоритмических NPC: {npc_count}.')
    if factor != 'npc.baseWageMinor' and ordinary:
        fixed.append('Целевая ставка NPC и шкала мира остаются как в профиле.')
    science = dict(version=1, template=template_id, title=title, goal=goal, hypothesis=hypothesis, comparison=comparison,
                   primaryMetric=primary, metrics=list(dict.fromkeys(requested)),
                   groups=[dict(label=group['label'], value=group['value'], valueLabel=_value_label(kind, group['value']),
                                factorLabel=FACTOR_LABELS[factor]) for group in groups],
                   fixed=fixed, factor=factor, kind=kind, mapLabel=choice['label'], profileId=profile_id,
                   profileNote='Хеш проекции пересчитывается только у группы, где изменено поле vNext-профиля. Контрольная группа копируется побайтно.' if profile.get('vNext') else 'Это исторический профиль без замка vNext. Сравниваемые группы используют один и тот же файл профиля, кроме выбранного поля.')
    plan['science'] = science
    validate_science(plan)
    runs = len(seeds) * repetitions * len(groups)
    # Оценка по измерению R1: Малая долина, 3 NPC, около 12 с и 46 МБ журнала решений на 1000 тиков.
    estimate = dict(runs=runs, independentSeeds=len(seeds),
                    seconds=round(runs * ticks * 12 / 1000, 1),
                    decisionBytes=runs * ticks * 46000,
                    note='Оценка по одному измерению Малой долины в R1, не обещание срока. Журнал решений на 1000 тиков занимал около 46 МБ. Подробные кадры мира в эту оценку не входят.')
    return dict(plan=plan, estimate=estimate)


def validate_science(plan):
    science = plan.get('science')
    if science is None:
        return
    if not isinstance(science, dict):
        raise ValueError('Описание исследования повреждено')
    allowed = {'version', 'template', 'title', 'goal', 'hypothesis', 'comparison', 'primaryMetric', 'metrics', 'groups', 'fixed', 'factor', 'kind', 'mapLabel', 'profileId', 'profileNote'}
    if set(science) - allowed or science.get('version') != 1:
        raise ValueError('Описание исследования содержит неизвестные поля')
    if science.get('template') not in TEMPLATES:
        raise ValueError('Неизвестный шаблон')
    _text(science.get('title', ''), 80, 'Название')
    _text(science.get('goal', ''), 500, 'Цель')
    _text(science.get('hypothesis', ''), 500, 'Гипотеза')
    _text(science.get('comparison', ''), 300, 'Сравнение')
    if plan.get('title') != science['title']:
        raise ValueError('Название плана и исследования должны совпадать')
    if len(plan.get('factors') or []) != 1 or plan['factors'][0].get('path') != science.get('factor'):
        raise ValueError('В этом выпуске сравнивается ровно один фактор')
    groups = science.get('groups')
    if not isinstance(groups, list) or [group.get('value') for group in groups] != plan['factors'][0].get('values'):
        raise ValueError('Группы должны совпадать со значениями фактора и идти в том же порядке')
    metrics = science.get('metrics')
    if not isinstance(metrics, list) or not metrics or any(item not in METRICS for item in metrics) or science.get('primaryMetric') not in metrics:
        raise ValueError('Список показателей не совпадает с каталогом')


def _ident(value):
    if isinstance(value, dict):
        return value.get('value', value.get('Value'))
    return value


def _load(path):
    if not path.is_file():
        return None
    return read(path)


def _account(summary, kind):
    accounts = (summary or {}).get('accounts')
    if not isinstance(accounts, list):
        return None
    found = [item.get('balanceMinor') for item in accounts if item.get('kind') == kind and isinstance(item.get('balanceMinor'), int)]
    if not found:
        return None
    return sum(found) if kind == 'Participant' else found[0]


def _flow(summary, key):
    flows = (summary or {}).get('flows')
    if not isinstance(flows, list):
        return None
    if any(not isinstance(item.get(key), int) for item in flows):
        return None
    return sum(item[key] for item in flows)


def _paid(summary):
    contracts, allocations = (summary or {}).get('deliveryContracts'), (summary or {}).get('marketAllocations')
    if not isinstance(contracts, list) or not isinstance(allocations, list):
        return None
    def total(items):
        if any(not isinstance(item.get('paidMinor', 0), int) for item in items):
            return None
        return sum(item.get('paidMinor', 0) for item in items)
    left, right = total(contracts), total(allocations)
    if left is None or right is None:
        return None
    return left + right


def _fulfilled(summary):
    contracts = (summary or {}).get('deliveryContracts')
    if not isinstance(contracts, list):
        return None
    return sum(1 for item in contracts if item.get('status') == 'Fulfilled')


def _samples(path):
    rows = []
    if not path.is_file():
        return rows
    with path.open(encoding='utf-8') as stream:
        for line in stream:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return rows


def _npc_cash(sample):
    people = sample.get('participants') if isinstance(sample, dict) else None
    if not isinstance(people, list):
        return None
    total = 0
    seen = False
    for person in people:
        actor = _ident(person.get('actor'))
        cash = person.get('cashMinor')
        if actor == HUMAN or not isinstance(cash, int):
            continue
        total += cash
        seen = True
    return total if seen else None


def _npc_capital(city):
    people = (city or {}).get('participants')
    if not isinstance(people, list):
        return None
    total = 0
    seen = False
    for person in people:
        if _ident(person.get('actor')) == HUMAN:
            continue
        if not isinstance(person.get('capitalMinor'), int):
            return None
        total += person['capitalMinor']
        seen = True
    return total if seen else None


def _thin(points, limit=240):
    if len(points) <= limit:
        return points
    step = (len(points) - 1) / (limit - 1)
    chosen = []
    for index in range(limit):
        chosen.append(points[round(index * step)])
    chosen[0], chosen[-1] = points[0], points[-1]
    return chosen


def _sample_value(sample, metric):
    key = metric.get('sample')
    if key == 'npcCashMinor':
        return _npc_cash(sample)
    value = sample.get(key)
    return value if isinstance(value, int) else None


def _idle(path):
    if not path.is_file():
        return None
    total = 0
    with path.open(encoding='utf-8', newline='') as stream:
        for row in csv.DictReader(stream):
            if 'headcount' not in row or 'ready' not in row:
                return None
            total += int(row['headcount']) if row['ready'] != 'True' else 0
    return total


def _folder_bytes(folder):
    total = 0
    for path in folder.rglob('*'):
        if path.is_file() and not path.is_symlink():
            total += path.stat().st_size
    return total


def _operating(folder):
    try:
        from research_metrics import measure
        value = measure(folder).get('operatingCashProfitMinor')
        return value if isinstance(value, int) else None, None if isinstance(value, int) else 'Операционный результат не определён: есть неклассифицированные проводки или нет выручки.'
    except (OSError, ValueError, KeyError, TypeError) as error:
        return None, 'Операционный результат не рассчитан: ' + str(error)[:300]


def extract_run(folder, metric_ids, with_trajectory=False):
    folder = Path(folder)
    summary, city = _load(folder / 'summary.json'), _load(folder / 'public-economy.json')
    progress = _load(folder / 'run-progress.json') or _load(folder / 'progress.json')
    samples = _samples(folder / 'economy-samples.jsonl') if any(METRICS[item].get('trajectory') or item in ('npcCashMinor', 'npcCashChangeMinor', 'constructedBuildings') for item in metric_ids) else []
    last, first = (samples[-1] if samples else None), (samples[0] if samples else None)
    elapsed = (summary or {}).get('elapsedMilliseconds')
    tick = (summary or {}).get('tick')
    values = {}
    warnings = []
    def put(key, value, warning=None):
        values[key] = value if key in metric_ids else None
        if key in metric_ids and value is None and warning:
            warnings.append(warning)
    put('cityCapitalMinor', (city or {}).get('capitalMinor') if isinstance((city or {}).get('capitalMinor'), int) else None, 'Капитал города в public-economy.json не найден.')
    put('municipalMinor', (city or {}).get('municipalMinor') if isinstance((city or {}).get('municipalMinor'), int) else None, 'Муниципальный капитал не найден.')
    put('treasuryMinor', _account(summary, 'Treasury'), 'Счёт казны в итоге не найден.')
    put('participantCashMinor', _account(summary, 'Participant'), 'Счета участников в итоге не найдены.')
    if 'operatingCashMinor' in metric_ids:
        amount, warning = _operating(folder)
        values['operatingCashMinor'] = amount
        if warning:
            warnings.append(warning)
    put('producedUnits', _flow(summary, 'produced'), 'Поток производства в итоге не найден.')
    put('harvestedUnits', _flow(summary, 'harvested'), 'Поток добычи в итоге не найден.')
    put('tradeTurnoverMinor', _paid(summary), 'Списки договоров или рыночных выделений в итоге отсутствуют.')
    put('contractsFulfilled', _fulfilled(summary), 'Список договоров в итоге отсутствует.')
    put('activeProductions', (city or {}).get('activeProductions') if isinstance((city or {}).get('activeProductions'), int) else None, 'Число действующих производств не найдено.')
    put('population', (city or {}).get('population') if isinstance((city or {}).get('population'), int) else None, 'Население в public-economy.json не найдено.')
    put('employed', (city or {}).get('employed') if isinstance((city or {}).get('employed'), int) else None, 'Занятость в public-economy.json не найдена.')
    put('unemployed', (city or {}).get('unemployed') if isinstance((city or {}).get('unemployed'), int) else None, 'Безработица в public-economy.json не найдена.')
    put('wagesSinkMinor', _account(summary, 'WagesSink'), 'Счёт выплаченных зарплат не найден.')
    if 'idleWorkerSamples' in metric_ids:
        try:
            values['idleWorkerSamples'] = _idle(folder / 'labor.csv')
        except (OSError, ValueError):
            values['idleWorkerSamples'] = None
        if values['idleWorkerSamples'] is None:
            warnings.append('Срезы труда не прочитаны, простой не заменён нулём.')
    put('npcCashMinor', _npc_cash(last) if last else None, 'Денег NPC нет: в краткой записи отсутствуют срезы economy-samples.jsonl.')
    put('npcCapitalMinor', _npc_capital(city), 'Капитал NPC в public-economy.json не найден.')
    if 'npcCashChangeMinor' in metric_ids:
        start, end = (_npc_cash(first) if first else None), (_npc_cash(last) if last else None)
        values['npcCashChangeMinor'] = None if start is None or end is None or len(samples) < 2 else end - start
        if values['npcCashChangeMinor'] is None:
            warnings.append('Изменение денег NPC не определено: нужны первый и последний срезы.')
    put('constructedBuildings', last.get('constructedBuildings') if last and isinstance(last.get('constructedBuildings'), int) else None, 'Счётчик построенных зданий есть только в срезах экономики.')
    put('controllerDecisionTurns', (summary or {}).get('controllerDecisionTurns') if isinstance((summary or {}).get('controllerDecisionTurns'), int) else None, 'Число ходов контроллера не записано.')
    put('controllerRejections', (summary or {}).get('controllerRejections') if isinstance((summary or {}).get('controllerRejections'), int) else None, 'Число отказов команд не записано.')
    put('elapsedMilliseconds', elapsed if isinstance(elapsed, int) else None, 'Длительность прогона не записана.')
    if 'ticksPerSecond' in metric_ids:
        values['ticksPerSecond'] = (Decimal(tick) / (Decimal(elapsed) / Decimal(1000))) if isinstance(tick, int) and isinstance(elapsed, int) and elapsed > 0 else None
        if values['ticksPerSecond'] is None:
            warnings.append('Скорость тиков не определена.')
    peak = (progress or {}).get('peakWorkingSetBytes')
    put('peakWorkingSetBytes', peak if isinstance(peak, int) else None, 'Пик памяти процесса в run-progress.json не измерен.')
    if 'folderBytes' in metric_ids:
        values['folderBytes'] = _folder_bytes(folder)
    trajectories = {}
    if with_trajectory:
        for metric_id in metric_ids:
            metric = METRICS[metric_id]
            if not metric.get('trajectory'):
                continue
            points = []
            for sample in samples:
                value = _sample_value(sample, metric)
                if isinstance(sample.get('tick'), int) and isinstance(value, int):
                    points.append(dict(tick=sample['tick'], value=value))
            trajectories[metric_id] = _thin(points)
    return dict(metrics={key: values.get(key) for key in metric_ids}, warnings=warnings, trajectories=trajectories)


def _sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def _fingerprint(workspace, series):
    lines = []
    for job in series['jobs']:
        lines.append('|'.join([job['id'], job['status'], str(job.get('attempt', 0))]))
        if job['status'] == 'succeeded' and job.get('result'):
            lines.append(_sha(workspace / job['result'] / 'summary.json') or 'missing')
    return hashlib.sha256('\n'.join(lines).encode()).hexdigest()


def _status(jobs):
    if any(job['status'] in ('queued', 'running') for job in jobs):
        return 'preliminary'
    if all(job['status'] == 'succeeded' for job in jobs):
        return 'complete'
    return 'incomplete'


def _paths(folder):
    absolute = str(folder.resolve())
    try:
        relative = folder.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        relative = 'artifacts/lab/' + folder.name
    return dict(absolute=absolute, relative=relative, report=str((folder / 'analysis' / 'report.html').resolve()),
                reportRelative=relative + '/analysis/report.html',
                windowsNote='Это абсолютный путь компьютера, где сформирован отчёт. На Windows у такого пути есть буква диска.' if os.name != 'nt' else 'Абсолютный путь Windows.')


def analyze_snapshot(workspace, series, force=False):
    workspace = Path(workspace)
    folder = workspace / series['id']
    if not folder.resolve().is_relative_to(workspace.resolve()):
        raise ValueError('Папка исследования вне рабочей области')
    science = series['plan'].get('science')
    if not science:
        return None
    fingerprint = _fingerprint(workspace, series)
    cached = folder / 'analysis' / 'statistics.json'
    if cached.is_file() and not force:
        try:
            previous = read(cached)
        except (OSError, ValueError):
            previous = None
        if previous and previous.get('fingerprint') == fingerprint and (folder / 'analysis' / 'report.html').is_file():
            return previous
    metric_ids = list(science['metrics'])
    groups = []
    for index, group in enumerate(science['groups']):
        groups.append(dict(arm=f'arm-{index}', label=group['label'], value=group['value'], valueLabel=group['valueLabel'], factorLabel=group['factorLabel']))
    by_arm = {group['arm']: group for group in groups}
    runs = []
    best = {}
    for job in series['jobs']:
        if job['status'] != 'succeeded':
            continue
        key = (job['arm'], job['seed'])
        current = best.get(key)
        if current is None or job['repeat'] < current['repeat']:
            best[key] = job
    warnings = []
    for job in series['jobs']:
        group = by_arm.get(job['arm'], dict(label=job['arm']))
        included = False
        exclusion = None
        metrics = {metric_id: None for metric_id in metric_ids}
        trajectories = {}
        if job['status'] != 'succeeded':
            exclusion = {'failed': 'ошибка', 'timeout': 'таймаут', 'cancelled': 'прерван пользователем', 'interrupted': 'прерван остановкой лаборатории'}.get(job['status'], job['status'])
        else:
            chosen = best.get((job['arm'], job['seed']))
            if chosen is not job and chosen['repeat'] != job['repeat']:
                exclusion = 'повтор того же seed; в сравнение вошёл первый успешный повтор'
            else:
                included = True
            result = job.get('result')
            target = workspace / result if result else None
            if target is None or not target.is_dir():
                included = False
                exclusion = 'файлы успешного прогона не найдены'
            else:
                extracted = extract_run(target, metric_ids, with_trajectory=True)
                metrics = extracted['metrics']
                trajectories = extracted['trajectories']
                warnings += extracted['warnings']
                if job['repeat'] != chosen['repeat']:
                    pass
                elif any(other['seed'] == job['seed'] and other['arm'] == job['arm'] and other['repeat'] != job['repeat'] and other['status'] == 'succeeded' for other in series['jobs']):
                    other_values = []
                    for other in series['jobs']:
                        if other['arm'] == job['arm'] and other['seed'] == job['seed'] and other is not job and other['status'] == 'succeeded' and other.get('result'):
                            other_values.append(extract_run(workspace / other['result'], metric_ids)['metrics'])
                    if any(item != metrics for item in other_values):
                        warnings.append(f'Seed {job["seed"]} в группе {group["label"]} дал разные числа в повторах. В сравнение вошёл только первый успешный повтор.')
        runs.append(dict(id=job['id'], arm=job['arm'], group=group['label'], seed=job['seed'], repeat=job['repeat'],
                         status=job['status'], attempt=job.get('attempt', 0), included=included, exclusion=exclusion,
                         metrics=metrics, trajectories=trajectories, error=job.get('error')))
    metric_documents = {}
    reference = groups[0]['arm']
    for metric_id in metric_ids:
        spec = METRICS[metric_id]
        described = []
        for group in groups:
            observed = [run['metrics'].get(metric_id) for run in runs if run['arm'] == group['arm'] and run['included']]
            summary = describe(observed)
            summary['interval'] = mean_interval(summary)
            summary.update(arm=group['arm'], label=group['label'])
            described.append(summary)
        contrasts = []
        for group in groups[1:]:
            left = {run['seed']: run['metrics'].get(metric_id) for run in runs if run['arm'] == reference and run['included']}
            right = {run['seed']: run['metrics'].get(metric_id) for run in runs if run['arm'] == group['arm'] and run['included']}
            contrast = paired(left, right)
            contrast.pop('differences', None)
            contrast.update(arm=group['arm'], label=f'{group["label"]} − {groups[0]["label"]}')
            contrasts.append(contrast)
        trajectories = []
        for run in runs:
            if run['included'] and run['trajectories'].get(metric_id):
                trajectories.append(dict(group=run['group'], seed=run['seed'], points=run['trajectories'][metric_id]))
        metric_documents[metric_id] = dict(**spec, role='primary' if metric_id == science['primaryMetric'] else 'exploratory',
                                           groups=described, paired=contrasts, trajectories=trajectories[:24])
    experiment = dict(title=science['title'], goal=science['goal'], hypothesis=science['hypothesis'], comparison=science['comparison'],
                      primaryMetric=science['primaryMetric'], groups=groups, fixed=science['fixed'], mapLabel=science['mapLabel'],
                      profileId=science['profileId'], profileNote=science['profileNote'], ticks=series['plan']['ticks'],
                      seeds=list(series['plan']['seeds']), repetitions=series['plan']['repetitions'], recording=series['plan'].get('recording') or 'прежний режим')
    reproduction = _reproduction(workspace, series)
    document = dict(version=1, method=METHOD, methodNotes=list(METHOD_NOTES), status=_status(series['jobs']),
                    generatedAt=datetime.now(SAMARA).isoformat(timespec='seconds'), fingerprint=fingerprint,
                    experiment=experiment, runs=[{key: value for key, value in run.items() if key != 'trajectories'} for run in runs],
                    metrics=metric_documents, findings=[], files=[], paths=_paths(folder),
                    limitations=_limitations(science, runs, warnings), reproduction=reproduction,
                    experimentPlan=dict(plan=series['plan'], reproduction=reproduction),
                    omittedNote='В архив не входят decisions.jsonl, events.jsonl, кадры мира и ledger.jsonl: они нужны для подробного просмотра прогона, но многократно повторили бы огромные журналы. Итоги, срезы экономики и методика в архиве есть.\n')
    document['findings'] = findings(experiment, document['runs'], metric_documents)
    write_bundle(folder, document, _sources(workspace, series))
    saved = read(folder / 'analysis' / 'statistics.json')
    if saved.get('fingerprint') != fingerprint or not (folder / 'analysis' / 'report.html').is_file():
        raise ValueError('Отчёт записан не полностью')
    return saved


def _limitations(science, runs, warnings):
    lines = [
        'Опыты проведены в модели Delsk. Они не доказывают такое же поведение реальной экономики.',
        'Основной показатель выбран до просмотра как гипотеза мастера. Остальные выбранные показатели разведочные.',
        'Отсутствие значимости не считается доказательством отсутствия эффекта.',
    ]
    if science.get('recording') == 'brief':
        lines.append('Краткая запись не содержит срезов экономики. Показатели, которым нужен economy-samples.jsonl, остаются пустыми.')
    if any(run['status'] != 'succeeded' for run in runs):
        lines.append('Неуспешные прогоны сохранены в runs.csv и исключены из средних и парных различий.')
    for warning in dict.fromkeys(warnings):
        lines.append(warning)
    return lines


def _reproduction(workspace, series):
    verified = bool(series.get('commitVerified')) and isinstance(series.get('commit'), str)
    commit = series.get('commit') if verified else None
    note = f'Git commit {commit} подтверждён чистым деревом на момент постановки в очередь.' if commit else 'Git commit не подтверждён: репозитория не было, дерево было грязным либо версия не записана. Сравнивать этот опыт с другим по номеру commit нельзя.'
    folder = workspace / series['id']
    economies = {}
    for path in sorted((folder / 'inputs').glob('arm-*/economy.json')):
        economies[path.parent.name] = _sha(path)
    profile_ids = []
    for path in sorted((folder / 'inputs').glob('arm-*/economy.json')):
        try:
            profile_ids.append(read(path).get('id'))
        except (OSError, ValueError):
            profile_ids.append(None)
    if len(set(profile_ids)) > 1:
        note += ' Профили рук различаются. Такое сравнение не смешивается автоматически.'
    return dict(commit=commit, commitVerified=verified, versionNote=note,
                binaries=series.get('binaries') or {}, profileIds=profile_ids, economySha256=economies,
                map=series['plan'].get('scenario'), seeds=list(series['plan'].get('seeds') or []),
                ticks=series['plan'].get('ticks'), method=METHOD,
                definitions={metric_id: METRICS[metric_id]['definition'] for metric_id in series['plan']['science']['metrics']},
                detail=note + ' Хеши профилей рук и сборки записаны в statistics.json.')


def _sources(workspace, series):
    folder = workspace / series['id']
    found = []
    for name in ('plan.json',):
        path = folder / name
        if path.is_file():
            found.append((path, name))
    inputs = folder / 'inputs'
    if inputs.is_dir():
        for path in sorted(inputs.rglob('*.json')):
            if path.is_file() and not path.is_symlink():
                found.append((path, path.relative_to(folder).as_posix()))
    for job in series['jobs']:
        if not job.get('result'):
            continue
        run = workspace / job['result']
        for name in KEEP_RUN:
            path = run / name
            if path.is_file() and not path.is_symlink() and path.stat().st_size <= 8 * 1024 * 1024:
                found.append((path, path.relative_to(folder).as_posix()))
    return found


def publish(lab, series_id):
    try:
        with lab.lock:
            series = next((item for item in lab.series if item['id'] == series_id), None)
            if series is None or 'science' not in series.get('plan', {}):
                return None
            snapshot = copy.deepcopy(series)
        return analyze_snapshot(lab.workspace, snapshot)
    except Exception as error:
        target = lab.workspace / series_id / 'analysis'
        target.mkdir(parents=True, exist_ok=True)
        with (target / 'report-error.log').open('a', encoding='utf-8') as stream:
            stream.write(datetime.now(SAMARA).isoformat(timespec='seconds') + ' ' + str(error) + '\n')
        return None


def open_folder(lab, series_id):
    folder = (lab.workspace / series_id).resolve()
    if not folder.is_dir() or not folder.is_relative_to(lab.workspace.resolve()):
        raise ValueError('Папка исследования не найдена')
    if os.name == 'nt':
        command = ['explorer', str(folder)]
    elif sys.platform == 'darwin':
        command = ['open', str(folder)]
    else:
        command = ['xdg-open', str(folder)]
    try:
        completed = subprocess.run(command, timeout=5, check=False)
        opened = completed.returncode == 0
    except (OSError, subprocess.SubprocessError):
        opened = False
    return dict(opened=opened, path=str(folder), note=None if opened else 'Открыть папку из лаборатории на этой системе не удалось. Скопируйте путь и откройте её в проводнике.')


def load_study(lab, series_id):
    with lab.lock:
        series = next((item for item in lab.series if item['id'] == series_id), None)
        if series is None:
            raise ValueError('Исследование не найдено')
        snapshot = copy.deepcopy(series)
    document = None
    path = lab.workspace / series_id / 'analysis' / 'statistics.json'
    if path.is_file():
        document = read(path)
        if document.get('fingerprint') != _fingerprint(lab.workspace, snapshot):
            document = None
    return dict(series=snapshot, study=document, paths=_paths(lab.workspace / series_id))


def chart(lab, series_id, metric_id):
    if metric_id not in METRICS or not METRICS[metric_id].get('trajectory'):
        raise ValueError('Для этого показателя нет хода по тикам')
    study = load_study(lab, series_id)
    document = study['study']
    if document and metric_id in document.get('metrics', {}) and document['metrics'][metric_id].get('trajectories'):
        metric = document['metrics'][metric_id]
        return dict(title=metric['title'], unitLabel=metric['unitLabel'], money=metric['money'], trajectories=metric['trajectories'],
                    note='Каждая линия — один мир. Это не среднее независимых миров.')
    raise ValueError('График появится после расчёта отчёта')

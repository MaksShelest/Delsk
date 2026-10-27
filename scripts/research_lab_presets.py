"""Editable experiment templates, over the existing plan schema and validators.
Author: Шелестов Максим Евгеньевич.
"""
import copy
from research_lab import DEFAULT

MAPS = [
    dict(label="Речные кварталы · E1", path="scenarios/vnext/small-river.json", economy="config/vnext/economy.e1-fleet.json"),
    dict(label="Большой округ · E1", path="scenarios/vnext/large-districts.json", economy="config/vnext/economy.e1-fleet.json"),
    dict(label="Четыре квартала", path="scenarios/four-corners-v1.json"),
    dict(label="Городской тракт", path="scenarios/city-corridor-v1.json"),
    dict(label='Карта 1 · базовая', path='scenarios/presentation-map-one-v3.json'),
    dict(label='Карта 2 · другой маршрут', path='scenarios/presentation-map-two-v3.json'),
    dict(label='Карта 3 · пищевые цепочки', path='scenarios/presentation-map-three-v2.json'),
]
FACTOR_LABELS = {
    'npc.difficulty': 'Сложность NPC', 'npc.negotiationsEnabled': 'Переговоры NPC',
    'npc.tradeEnabled': 'Торговля NPC', 'npc.politicsEnabled': 'Политика NPC',
    'npc.logisticsEnabled': 'Логистика NPC', 'npc.cashReserveMinor': 'Денежный резерв NPC, сотые монеты',
    'npc.maximumWageMinor': 'Предел зарплаты NPC, сотые монеты',
    'world.startingCashMinor': 'Стартовые деньги, сотые монеты', 'world.population': 'Население',
    'world.labor.mobilityNumerator': 'Скорость переходов работников: числитель',
    'world.rules.wageAttractionExponent': 'Чувствительность найма к зарплате',
    'world.capitalDemand.bootstrapBaseBudgetMinor': 'Начальный спрос столицы, сотые монеты',
    'world.capitalDemand.demandNumerator': 'Коэффициент спроса: числитель',
}


def presets():
    base = copy.deepcopy(DEFAULT)
    return [
        dict(id='quick', label='Быстрая проверка · 1 прогон', description='120 тиков, seed 42, алгоритмы без LLM. Проверка работы лаборатории, не вывод о балансе.',
             plan=base | dict(id='quick-check', ticks=120, seeds=[42], factors=[])),
        dict(id='baseline', label='Базовое исследование · 3 seed', description='600 тиков, три независимых seed. Одинаковые исходные настройки.',
             plan=base | dict(id='baseline', factors=[])),
        dict(id='difficulty', label='Сравнить сложности · 9 прогонов', description='Easy / Medium / Hard при одинаковых трёх seed. Превосходство одной сложности заранее не предполагается.',
             plan=base | dict(id='difficulty-paired', factors=[dict(path='npc.difficulty', values=['Easy', 'Medium', 'Hard'])])),
        dict(id='negotiations', label='Сравнить переговоры · 6 прогонов', description='Включены / выключены при одинаковых seed. При отсутствии переговорных событий этот опыт не доказывает эффект механизма.',
             plan=base),
    ]

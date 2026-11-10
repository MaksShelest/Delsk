"""Короткие выводы из уже посчитанной статистики. Шаблоны фиксированы, модели нет.
Author: Шелестов Максим Евгеньевич.
"""
from decimal import Decimal

DISCLAIMER = 'Опыты выполнены в модели Delsk и сами по себе не доказывают такое же поведение реальной экономики.'


def _decimal(value):
    if value is None:
        return None
    return value if isinstance(value, Decimal) else Decimal(str(value))


def _coins(minor):
    value = _decimal(minor)
    if value is None:
        return 'нет числа'
    sign = '−' if value < 0 else ''
    return sign + f'{abs(value) / Decimal(100):.2f}'.replace('.', ',') + ' монеты'


def _plain(value, metric):
    number = _decimal(value)
    if number is None:
        return 'нет числа'
    if metric.get('money'):
        return _coins(number)
    if metric.get('unit') == 'milliseconds':
        return f'{number / Decimal(1000):.1f}'.replace('.', ',') + ' с'
    if metric.get('unit') == 'bytes':
        return f'{number:,.0f}'.replace(',', ' ') + ' Б'
    if number == number.to_integral_value():
        return f'{int(number)}'
    return format(number, 'f').replace('.', ',')


def _interval_contains_zero(interval):
    if not interval or interval[0] is None:
        return None
    return _decimal(interval[0]) <= 0 <= _decimal(interval[1])


def findings(experiment, runs, metrics):
    """3–7 утверждений. Факт, сравнение и ограничение не смешиваются в одном предложении."""
    items = []
    failed = [run for run in runs if run['status'] != 'succeeded']
    excluded = [run for run in runs if not run['included']]
    if failed:
        items.append(dict(kind='limitation', metricId=None,
            text=f'{len(failed)} прогонов не завершились успешно и не заменены нулями. В сравнительную статистику они не входят.'))
    elif excluded:
        items.append(dict(kind='limitation', metricId=None,
            text=f'{len(excluded)} прогонов не вошли в независимую выборку: повтор того же seed не считается новым наблюдением.'))
    primary_id = experiment['primaryMetric']
    primary = metrics.get(primary_id)
    if primary and primary.get('paired'):
        contrast = primary['paired'][0]
        left = primary['groups'][0]
        right = next(group for group in primary['groups'] if group['arm'] == contrast['arm'])
        name = primary['title']
        if contrast['pairs'] == 0:
            items.append(dict(kind='limitation', metricId=primary_id,
                text=f'По показателю «{name}» нет ни одной полной пары. Сравнение групп не выполнено.'))
        else:
            items.append(dict(kind='fact', metricId=primary_id,
                text=f'«{name}»: среднее группы {right["label"]} — {_plain(right["mean"], primary)}, группы {left["label"]} — {_plain(left["mean"], primary)}. Парных seeds: {contrast["pairs"]}.'))
            change = contrast.get('absoluteChange')
            if change is not None:
                items.append(dict(kind='comparison', metricId=primary_id,
                    text=f'Среднее парное различие {right["label"]} − {left["label"]} по «{name}» равно {_plain(change, primary)}. Это описание выборки, а не доказанная причина.'))
            if contrast.get('relativeChangePercent') is not None:
                percent = _decimal(contrast['relativeChangePercent'])
                items.append(dict(kind='comparison', metricId=primary_id,
                    text=f'Относительное изменение средних по «{name}» — {format(percent, "f").replace(".", ",")} %. Это разность средних, делённая на среднее группы {left["label"]}, а не среднее попарных процентов.'))
            contains = _interval_contains_zero(contrast.get('confidenceInterval'))
            if contrast['pairs'] < 8:
                items.append(dict(kind='limitation', metricId=primary_id,
                    text=f'Независимых пар меньше 8. Интервал и критерий по «{name}» описывают маленькую выборку и не дают уверенного вывода о превосходстве группы.'))
            elif contains is True:
                items.append(dict(kind='limitation', metricId=primary_id,
                    text=f'95% интервал парного различия по «{name}» включает ноль. Это не доказательство отсутствия эффекта.'))
            elif contains is False:
                items.append(dict(kind='comparison', metricId=primary_id,
                    text=f'В этой выборке 95% интервал парного различия по «{name}» не включает ноль. Это свойство модели Delsk на данных seeds, а не доказательство для реальной экономики.'))
            if contrast.get('standardDeviation') not in (None, 0) and change not in (None, 0):
                spread = abs(_decimal(contrast['standardDeviation']))
                if spread > abs(_decimal(change)):
                    items.append(dict(kind='fact', metricId=primary_id,
                        text=f'Разброс парных различий по «{name}» больше самого среднего различия: миры с одним и тем же условием заметно отличаются.'))
    else:
        items.append(dict(kind='limitation', metricId=primary_id,
            text='Парное сравнение основного показателя недоступно: нужны хотя бы две группы и успешные прогоны с одинаковыми seeds.'))
    exploratory = [item for key, item in metrics.items() if key != primary_id and item.get('role') == 'exploratory']
    if exploratory:
        items.append(dict(kind='limitation', metricId=None,
            text='Кроме основного показателя посчитаны разведочные метрики. Их интервалы не поправлялись на число сравнений и не заменяют основную гипотезу.'))
    # Порядок уже идёт от качества данных к основному сравнению. Дисклеймер всегда последний.
    body = [item for item in items if item['text'] != DISCLAIMER][:6]
    return body + [dict(kind='limitation', metricId=None, text=DISCLAIMER)]

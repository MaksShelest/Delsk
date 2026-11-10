"""Проверяемая описательная статистика и парное сравнение. Это не экономическая модель.
Author: Шелестов Максим Евгеньевич.

Методика r2-paired-t-1 заранее фиксирует один способ сравнения:
для каждой метрики группы описываются отдельно, а основной контраст —
парные различия одинаковых seeds относительно первой группы.
Повтор одного seed не считается новым наблюдением.
Отсутствие значимости не объявляется отсутствием эффекта.
"""
from decimal import Decimal, getcontext
import math

getcontext().prec = 50

METHOD = 'r2-paired-t-1'
METHOD_NOTES = (
    'Независимая единица — один успешный прогон одного seed. Повтор того же seed исключается.',
    'Тики внутри партии не являются отдельными наблюдениями.',
    'Пропуск и неуспешный прогон не заменяются нулём.',
    'Для пар используется разность «группа минус первая группа» на том же seed.',
    'Среднее — арифметическое. Медиана — среднее двух центральных значений при чётном числе.',
    'Стандартное отклонение — выборочное, делитель n−1. При n<2 оно не определено.',
    '95% интервал среднего парного различия: среднее ± t(n−1, 0,975) · s / √n.',
    'Двусторонний p считается тем же критерием Стьюдента для парных различий. Он не выбирается после просмотра нескольких тестов.',
    'Величина эффекта — d = среднее парное различие / выборочное стандартное отклонение этих различий.',
    'Относительное изменение — разность средних, делённая на среднее первой группы. Это не среднее попарных процентов.',
    'Основная метрика — заранее выбранный показатель. Остальные метрики разведочные и не поправляются на число сравнений.',
    'Интервал и p описывают эту выборку модели Delsk. Они не доказывают поведение реальной экономики.',
)


def _regularized_incomplete_beta(a, b, x):
    """I_x(a, b). Симметрия используется, когда ряд сходится хуже."""
    if x <= 0:
        return 0.0
    if x >= 1:
        return 1.0
    switch = x > (a + 1) / (a + b + 2)
    if switch:
        return 1 - _regularized_incomplete_beta(b, a, 1 - x)
    log_beta = math.lgamma(a) + math.lgamma(b) - math.lgamma(a + b)
    front = math.exp(a * math.log(x) + b * math.log(1 - x) - log_beta) / a
    # Непрерывная дробь DLMF 8.17: d(2m) и d(2m+1).
    tiny = 1e-30
    fraction = 1.0
    numerator_c = 1.0
    denominator_d = 1.0 - (a + b) * x / (a + 1)
    if abs(denominator_d) < tiny:
        denominator_d = tiny
    denominator_d = 1 / denominator_d
    fraction = denominator_d
    for m in range(1, 200):
        even = m * (b - m) * x / ((a + 2 * m - 1) * (a + 2 * m))
        numerator_c = 1 + even / numerator_c
        if abs(numerator_c) < tiny:
            numerator_c = tiny
        denominator_d = 1 + even * denominator_d
        if abs(denominator_d) < tiny:
            denominator_d = tiny
        denominator_d = 1 / denominator_d
        fraction *= numerator_c * denominator_d
        odd = -(a + m) * (a + b + m) * x / ((a + 2 * m) * (a + 2 * m + 1))
        numerator_c = 1 + odd / numerator_c
        if abs(numerator_c) < tiny:
            numerator_c = tiny
        denominator_d = 1 + odd * denominator_d
        if abs(denominator_d) < tiny:
            denominator_d = tiny
        denominator_d = 1 / denominator_d
        delta = numerator_c * denominator_d
        fraction *= delta
        if abs(delta - 1) < 1e-12:
            break
    return front * fraction


def student_t_two_sided_p(statistic, degrees):
    """Двусторонний p парного критерия. При нулевой дисперсии не определён: вызывающий код не должен звать функцию."""
    if degrees < 1:
        raise ValueError('Для критерия Стьюдента нужно хотя бы два наблюдения')
    if statistic == 0:
        return 1.0
    x = degrees / (degrees + statistic * statistic)
    return _regularized_incomplete_beta(degrees / 2, 0.5, x)


def student_t_critical(degrees, probability=0.975):
    """Квантиль |T|, у которого двусторонний хвост равен 2·(1−probability)."""
    if degrees < 1 or not 0.5 < probability < 1:
        raise ValueError('Некорректные степень свободы или вероятность')
    target = 2 * (1 - probability)
    low, high = 0.0, 1_000_000.0
    for _ in range(80):
        mid = (low + high) / 2
        if student_t_two_sided_p(mid, degrees) > target:
            low = mid
        else:
            high = mid
    return (low + high) / 2


def _decimal(value):
    if isinstance(value, Decimal):
        return value
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError('В статистику попадает только число')
    if isinstance(value, float):
        if not math.isfinite(value):
            raise ValueError('Бесконечность и NaN не заменяют пропуск')
        return Decimal(str(value))
    return Decimal(value)


def _numbers(values):
    clean = []
    for value in values:
        if value is None:
            continue
        clean.append(_decimal(value))
    return clean


def _sqrt(value):
    return value.sqrt()


def describe(values):
    """Описание одной группы. Пропуски не входят в n и не становятся нулём."""
    present = _numbers(values)
    missing = len(values) - len(present)
    if not present:
        return dict(n=0, missing=missing, mean=None, median=None, minimum=None, maximum=None,
                    standardDeviation=None, range=None, warnings=['Нет чисел для описания.'])
    ordered = sorted(present)
    n = len(ordered)
    mean = sum(ordered, Decimal(0)) / n
    if n % 2:
        median = ordered[n // 2]
    else:
        median = (ordered[n // 2 - 1] + ordered[n // 2]) / 2
    warnings = []
    if n < 2:
        deviation = None
        warnings.append('Одно наблюдение: стандартное отклонение не определено.')
    else:
        variance = sum((item - mean) ** 2 for item in ordered) / (n - 1)
        deviation = _sqrt(variance)
    if n < 8:
        warnings.append('Независимых наблюдений меньше 8: оценка этой группы ненадёжна.')
    return dict(n=n, missing=missing, mean=mean, median=median, minimum=ordered[0], maximum=ordered[-1],
                standardDeviation=deviation, range=ordered[-1] - ordered[0], warnings=warnings)


def paired(left, right):
    """Парные различия right−left по общим ключам. Ключ — seed, не номер тика."""
    if set(left) != set(dict(left)) or set(right) != set(dict(right)):
        raise ValueError('Повторяющийся seed внутри группы')
    keys = sorted(set(left) & set(right))
    differences = []
    used = []
    for key in keys:
        a, b = left[key], right[key]
        if a is None or b is None:
            continue
        left_value, right_value = _decimal(a), _decimal(b)
        differences.append(right_value - left_value)
        used.append(key)
    summary = describe(differences)
    only_left = sorted(set(left) - set(right))
    only_right = sorted(set(right) - set(left))
    result = dict(
        method=METHOD, pairs=summary['n'], seeds=used, unpairedSeeds=dict(leftOnly=only_left, rightOnly=only_right),
        differences=differences, mean=summary['mean'], median=summary['median'], minimum=summary['minimum'],
        maximum=summary['maximum'], standardDeviation=summary['standardDeviation'], range=summary['range'],
        absoluteChange=summary['mean'], relativeChangePercent=None, confidenceInterval=None,
        effectSize=None, pValue=None, warnings=list(summary['warnings']))
    if only_left or only_right:
        result['warnings'].append('Часть seeds есть только в одной группе и не входит в парное сравнение.')
    if summary['n'] < 2:
        result['warnings'].append('Пар меньше двух: доверительный интервал, отклонение и критерий не рассчитываются.')
        return result
    mean_left = sum((_decimal(left[key]) for key in used), Decimal(0)) / len(used)
    mean_right = sum((_decimal(right[key]) for key in used), Decimal(0)) / len(used)
    if mean_left == 0:
        result['warnings'].append('Относительное изменение не определено: среднее первой группы равно нулю.')
    else:
        result['relativeChangePercent'] = (mean_right - mean_left) / mean_left * 100
        if mean_left < 0:
            result['warnings'].append('Среднее первой группы отрицательное: процент считается от этого знакового уровня.')
    deviation = summary['standardDeviation']
    if deviation == 0:
        result['confidenceInterval'] = [summary['mean'], summary['mean']]
        if summary['mean'] == 0:
            result['warnings'].append('Все парные различия равны нулю. Дисперсия не оценивается, p-значение не приводится.')
        else:
            result['warnings'].append('Все парные различия совпали и не равны нулю. Обычный критерий Стьюдента здесь не определён.')
        return result
    degrees = summary['n'] - 1
    critical = Decimal(str(student_t_critical(degrees)))
    margin = critical * deviation / Decimal(summary['n']).sqrt()
    result['confidenceInterval'] = [summary['mean'] - margin, summary['mean'] + margin]
    result['effectSize'] = summary['mean'] / deviation
    statistic = float(summary['mean'] / (deviation / Decimal(summary['n']).sqrt()))
    result['pValue'] = student_t_two_sided_p(statistic, degrees)
    if summary['n'] < 8:
        result['warnings'].append('Пар меньше 8: интервал и p описывают маленькую выборку и не являются устойчивым выводом.')
    return result


def mean_interval(summary):
    """95% интервал среднего одной группы. Это не интервал парного различия."""
    if not summary or summary.get('mean') is None or summary['n'] < 2 or summary.get('standardDeviation') is None:
        return None
    if summary['standardDeviation'] == 0:
        return [summary['mean'], summary['mean']]
    margin = Decimal(str(student_t_critical(summary['n'] - 1))) * summary['standardDeviation'] / Decimal(summary['n']).sqrt()
    return [summary['mean'] - margin, summary['mean'] + margin]

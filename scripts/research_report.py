"""Самодостаточные отчёты исследования. Без сети, CDN и запущенной лаборатории.
Author: Шелестов Максим Евгеньевич.
"""
import csv
import io
import json
import os
from decimal import Decimal
from html import escape
import zipfile

GLOSSARY = [
    ('Среднее', 'Сумма наблюдений, делённая на их число. Один необычный мир может сильно сдвинуть среднее.'),
    ('Медиана', 'Середина упорядоченного списка. При чётном числе это среднее двух центральных значений. Она устойчивее к одному крайнему миру, чем среднее.'),
    ('Стандартное отклонение', 'Типичный разброс вокруг среднего. Делитель — n−1. При одном наблюдении оно не определено.'),
    ('Доверительный интервал', 'Здесь это 95% интервал Стьюдента для среднего парного различия. Он описывает неопределённость этой выборки. Попадание нуля в интервал не доказывает отсутствие эффекта.'),
    ('Парные seeds', 'Один и тот же seed запускается в каждой группе. Сравнивается разность этих двух миров. Два запуска одного seed не считаются двумя независимыми наблюдениями.'),
    ('Величина эффекта', 'Среднее парное различие, делённое на стандартное отклонение этих различий. Это описание выборки, а не доказательство причины.'),
]


def convert(value):
    if isinstance(value, Decimal):
        if value == value.to_integral_value():
            number = int(value)
            return number if abs(number) <= 2**53 else str(number)
        return format(value, 'f')
    if isinstance(value, dict):
        return {key: convert(item) for key, item in value.items()}
    if isinstance(value, list):
        return [convert(item) for item in value]
    return value


def dumps(document):
    return json.dumps(convert(document), ensure_ascii=False, indent=2, allow_nan=False) + '\n'


def _decimal(value):
    if value is None or isinstance(value, bool):
        return None
    return value if isinstance(value, Decimal) else Decimal(str(value))


def csv_number(value, money=False):
    number = _decimal(value)
    if number is None:
        return ''
    if money:
        text = f'{number / Decimal(100):.2f}'
    elif number == number.to_integral_value():
        text = str(int(number))
    else:
        text = format(number, 'f')
    return text.replace('.', ',')


def _display(value, metric):
    if value is None:
        return '—'
    if metric.get('money'):
        return csv_number(value, money=True) + ' монеты'
    if metric.get('unit') == 'milliseconds':
        return csv_number(_decimal(value) / Decimal(1000)) + ' с'
    return csv_number(value)


def summary_csv(document):
    buffer = io.StringIO()
    writer = csv.writer(buffer, delimiter=';', lineterminator='\n')
    writer.writerow(['Показатель', 'Роль', 'Группа', 'Вид', 'Наблюдения', 'Среднее', 'Медиана', 'Минимум', 'Максимум',
                     'Стандартное отклонение', 'Размах', 'Граница 95% снизу', 'Граница 95% сверху',
                     'Относительное изменение, %', 'Величина эффекта', 'p', 'Единица', 'Предупреждения'])
    for metric in document['metrics'].values():
        money = metric.get('money', False)
        unit = 'монеты' if money else metric.get('unitLabel', metric.get('unit', ''))
        for group in metric['groups']:
            interval = group.get('interval') or [None, None]
            writer.writerow([metric['title'], metric['role'], group['label'], 'группа', group['n'],
                             csv_number(group['mean'], money), csv_number(group['median'], money),
                             csv_number(group['minimum'], money), csv_number(group['maximum'], money),
                             csv_number(group['standardDeviation'], money), csv_number(group['range'], money),
                             csv_number(interval[0], money), csv_number(interval[1], money),
                             '', '', '', unit, ' | '.join(group.get('warnings') or [])])
        for contrast in metric.get('paired') or []:
            interval = contrast.get('confidenceInterval') or [None, None]
            writer.writerow([metric['title'], metric['role'], contrast['label'], 'парное различие', contrast['pairs'],
                             csv_number(contrast['mean'], money), csv_number(contrast['median'], money),
                             csv_number(contrast['minimum'], money), csv_number(contrast['maximum'], money),
                             csv_number(contrast['standardDeviation'], money), csv_number(contrast['range'], money),
                             csv_number(interval[0], money), csv_number(interval[1], money),
                             csv_number(contrast.get('relativeChangePercent')), csv_number(contrast.get('effectSize')),
                             csv_number(contrast.get('pValue')), unit, ' | '.join(contrast.get('warnings') or [])])
    return '\ufeff' + buffer.getvalue()


def runs_csv(document):
    metrics = list(document['metrics'].values())
    buffer = io.StringIO()
    writer = csv.writer(buffer, delimiter=';', lineterminator='\n')
    writer.writerow(['Прогон', 'Группа', 'Seed', 'Повтор', 'Статус', 'В статистике', 'Причина исключения'] +
                    [item['title'] + (' , монеты' if item.get('money') else ' , ' + item.get('unitLabel', '')) for item in metrics])
    for run in document['runs']:
        writer.writerow([run['id'], run['group'], run['seed'], run['repeat'], run['status'],
                         'да' if run['included'] else 'нет', run.get('exclusion') or ''] +
                        [csv_number(run['metrics'].get(item['id']), item.get('money')) for item in metrics])
    return '\ufeff' + buffer.getvalue()


def _rows(headers, rows):
    head = '| ' + ' | '.join(headers) + ' |\n| ' + ' | '.join('---' for _ in headers) + ' |\n'
    body = ''.join('| ' + ' | '.join(str(cell) for cell in row) + ' |\n' for row in rows)
    return head + body


def markdown(document):
    experiment = document['experiment']
    lines = [f"# {experiment['title']}", '', '## Цель и гипотеза', '', experiment['goal'], '', experiment['hypothesis'], '',
             f"Что сравнивается: {experiment['comparison']}", '',
             '## Версия симулятора', '', document['reproduction']['versionNote'], '',
             '## Экспериментальная конфигурация', '',
             f"Карта: {experiment['mapLabel']}", '', f"Профиль: {experiment['profileId']}. {experiment['profileNote']}", '',
             f"Тиков в одном прогоне: {experiment['ticks']}. Независимые seeds: {', '.join(map(str, experiment['seeds']))}. Повторов seed: {experiment['repetitions']}.",
             '', 'Одинаковые условия:', '']
    lines += [f'- {item}' for item in experiment['fixed']]
    lines += ['', '## Сравниваемые условия', '']
    for group in experiment['groups']:
        lines.append(f"- Группа {group['label']}: {group['factorLabel']} = {group['valueLabel']}")
    lines += ['', '## Методика', '', document['method'], '']
    lines += [f'- {note}' for note in document['methodNotes']]
    included = sum(1 for run in document['runs'] if run['included'])
    lines += ['', '## Независимые эксперименты', '',
              f"Запланировано прогонов: {len(document['runs'])}. В статистику групп вошло успешных прогонов: {included}.",
              'Независимая единица сравнения — общий seed, а не сумма прогонов двух групп. Тики внутри одного прогона не считаются отдельными экспериментами.', '',
              '## Основные результаты', '']
    for item in document['findings']:
        lines.append(f"- {item['text']}")
    lines += ['', '## Таблицы', '']
    for metric in document['metrics'].values():
        lines += [f"### {metric['title']}", '', metric['definition'], '', f"Источник: {metric['source']}", '',
                  _rows(['Группа', 'n', 'Среднее', 'Медиана', 'Минимум', 'Максимум'],
                        [[group['label'], group['n'], _display(group['mean'], metric), _display(group['median'], metric),
                          _display(group['minimum'], metric), _display(group['maximum'], metric)] for group in metric['groups']]),
                  '']
        for contrast in metric.get('paired') or []:
            interval = contrast.get('confidenceInterval')
            interval_text = 'не рассчитан' if not interval else f"{_display(interval[0], metric)} … {_display(interval[1], metric)}"
            lines.append(f"Парное различие {contrast['label']}: среднее {_display(contrast['mean'], metric)}, 95% интервал {interval_text}, пар {contrast['pairs']}.")
            lines.append('')
    lines += ['## Краткие пояснения', '']
    for title, text in GLOSSARY:
        lines.append(f'- **{title}.** {text}')
    lines += ['', '## Ограничения', '']
    lines += [f'- {item}' for item in document['limitations']]
    lines += ['', '## Воспроизведение', '', document['reproduction']['detail'], '', '## Файлы', '']
    for item in document['files']:
        lines.append(f"- `{item['name']}` — {item['role']} ({item['bytes']} Б)")
    lines += ['', f"Папка: {document['paths']['absolute']}", '', f"Относительный путь: {document['paths']['relative']}", '']
    return '\n'.join(lines)


def _svg_bars(metric):
    groups = [group for group in metric['groups'] if group.get('mean') is not None]
    if not groups:
        return '<p>Столбцы не построены: нет чисел.</p>'
    values = []
    for group in groups:
        interval = group.get('interval') or [group['mean'], group['mean']]
        values += [Decimal(str(group['mean'])), Decimal(str(interval[0])), Decimal(str(interval[1]))]
    low = min(values + [Decimal(0)]) if min(Decimal(str(group['mean'])) for group in groups) >= 0 else min(values)
    high = max(values + [Decimal(0)])
    if high == low:
        high += 1
    width, height, left, bottom = 640, 280, 70, 40
    plot_h = height - bottom - 24

    def y(value):
        return 24 + (high - Decimal(str(value))) / (high - low) * plot_h

    zero = y(0)
    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" xmlns="http://www.w3.org/2000/svg">',
             f'<title>{escape(metric["title"])}: средние независимых миров</title>',
             f'<rect width="{width}" height="{height}" fill="#1c1f27"/>',
             f'<text x="{width/2}" y="18" text-anchor="middle" fill="#ebe7df" font-size="13">{escape(metric["title"])}</text>',
             f'<line x1="{left}" y1="{zero}" x2="{width-20}" y2="{zero}" stroke="#6d7270"/>']
    slot = (width - left - 20) / len(groups)
    for index, group in enumerate(groups):
        center = left + slot * index + slot / 2
        mean = Decimal(str(group['mean']))
        top, base = sorted((float(y(mean)), float(zero)))
        parts.append(f'<rect x="{center-18}" y="{top}" width="36" height="{max(1, base-top)}" fill="#d4b483"/>')
        interval = group.get('interval')
        if interval:
            parts.append(f'<line x1="{center}" y1="{y(interval[0])}" x2="{center}" y2="{y(interval[1])}" stroke="#ebe7df"/>')
        parts.append(f'<text x="{center}" y="{height-16}" text-anchor="middle" fill="#a39e94" font-size="12">{escape(group["label"])} · n={group["n"]}</text>')
    unit = 'монеты' if metric.get('money') else metric.get('unitLabel', '')
    axis = 'Ось начинается с нуля.' if low == 0 else 'Ось не начинается с нуля: среди значений есть отрицательные.'
    parts.append(f'<text x="16" y="{height/2}" fill="#a39e94" font-size="11" transform="rotate(-90 16 {height/2})">{escape(unit)}</text></svg>')
    return ''.join(parts) + f'<p>{escape(axis)} Показаны средние независимых миров, не ход одного мира. Усы — 95% интервал среднего группы.</p>'


def _svg_lines(metric):
    series = metric.get('trajectories') or []
    if not series:
        return ''
    points = [(point['tick'], Decimal(str(point['value']))) for run in series for point in run['points']]
    if not points:
        return ''
    ticks = [item[0] for item in points]
    values = [item[1] for item in points]
    low = min(values + [Decimal(0)]) if min(values) >= 0 else min(values)
    high = max(values + [Decimal(0)])
    if high == low:
        high += 1
    width, height, left, bottom = 640, 280, 56, 36
    plot_w, plot_h = width - left - 16, height - bottom - 24

    def x(tick):
        span = max(ticks) - min(ticks) or 1
        return left + (tick - min(ticks)) / span * plot_w

    def y(value):
        return 24 + float((high - value) / (high - low) * plot_h)

    colors = ['#d4b483', '#9db8a4', '#efa29a', '#8fb4d6', '#c7b2d6', '#e1cd9b']
    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" xmlns="http://www.w3.org/2000/svg">',
             f'<title>Ход показателя {escape(metric["title"])} в отдельных мирах</title>',
             f'<rect width="{width}" height="{height}" fill="#1c1f27"/>',
             f'<text x="{width/2}" y="18" text-anchor="middle" fill="#ebe7df" font-size="13">Ход в отдельных мирах · {escape(metric["title"])}</text>']
    for index, run in enumerate(series):
        if len(run['points']) < 2:
            continue
        path = ' '.join(f'{x(point["tick"]):.1f},{y(Decimal(str(point["value"]))):.1f}' for point in run['points'])
        parts.append(f'<polyline fill="none" stroke="{colors[index % len(colors)]}" stroke-width="1.5" points="{path}"/>')
    unit = 'монеты' if metric.get('money') else metric.get('unitLabel', '')
    axis = 'Ось значений начинается с нуля.' if low == 0 else 'Ось значений не начинается с нуля: есть отрицательные числа.'
    parts.append('</svg>')
    legend = ', '.join(f'{run["group"]} seed {run["seed"]}' for run in series[:8])
    return ''.join(parts) + f'<p>Каждая линия — один мир. Это не среднее независимых миров. Единица оси значений: {escape(unit)}. Ось тиков — шаги этой партии. {escape(axis)} Линии: {escape(legend)}.</p>'


def html(document):
    experiment = document['experiment']
    findings_html = ''.join(f'<li class="{escape(item["kind"])}">{escape(item["text"])}</li>' for item in document['findings'])
    groups = ''.join(f'<li>Группа {escape(group["label"])}: {escape(group["factorLabel"])} = {escape(str(group["valueLabel"]))}</li>' for group in experiment['groups'])
    fixed = ''.join(f'<li>{escape(item)}</li>' for item in experiment['fixed'])
    sections = []
    for metric in document['metrics'].values():
        head = '<tr><th>Группа</th><th>n</th><th>Среднее</th><th>Медиана</th><th>Минимум</th><th>Максимум</th></tr>'
        rows = ''.join('<tr>' + ''.join(f'<td>{escape(str(cell))}</td>' for cell in (
            group['label'], group['n'], _display(group['mean'], metric), _display(group['median'], metric),
            _display(group['minimum'], metric), _display(group['maximum'], metric))) + '</tr>' for group in metric['groups'])
        contrasts = []
        for contrast in metric.get('paired') or []:
            interval = contrast.get('confidenceInterval')
            text = 'не рассчитан' if not interval else f'{_display(interval[0], metric)} … {_display(interval[1], metric)}'
            contrasts.append(f'<p>Парное различие {escape(contrast["label"])}: среднее {_display(contrast["mean"], metric)}; 95% интервал {escape(text)}; пар {contrast["pairs"]}.</p>')
        sections.append(f'<section><h3>{escape(metric["title"])}</h3><p>{escape(metric["definition"])}</p><p>Источник: {escape(metric["source"])}. Роль: {escape(metric["role"])}.</p>{_svg_bars(metric)}{_svg_lines(metric)}<table>{head}{rows}</table>{"".join(contrasts)}</section>')
    glossary = ''.join(f'<dt>{escape(title)}</dt><dd>{escape(text)}</dd>' for title, text in GLOSSARY)
    files = ''.join(f'<li><code>{escape(item["name"])}</code> — {escape(item["role"])} ({item["bytes"]} Б)</li>' for item in document['files'])
    limits = ''.join(f'<li>{escape(item)}</li>' for item in document['limitations'])
    included = sum(1 for run in document['runs'] if run['included'])
    status = {'complete': 'Завершено', 'preliminary': 'Предварительно: серия ещё идёт', 'incomplete': 'Завершено с пропусками'}[document['status']]
    return f'''<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>{escape(experiment["title"])}</title>
<style>
body{{margin:0;background:#14161c;color:#ebe7df;font:16px/1.55 "Segoe UI",sans-serif}}main{{max-width:920px;margin:auto;padding:40px 24px 64px}}
h1{{font-weight:550;letter-spacing:-.4px}}h2{{margin-top:36px;font-weight:550;font-size:22px}}h3{{font-weight:550}}
p,li,dd{{color:#d9d4ca}}code{{color:#f3e6cc}}.status{{color:#d4b483}}table{{border-collapse:collapse;width:100%;margin:12px 0 20px}}
th,td{{text-align:left;padding:8px 10px;border-bottom:1px solid #343844;font-size:14px}}th{{color:#a39e94;font-weight:500}}
svg{{width:100%;height:auto;border:1px solid #343844;border-radius:8px}}dt{{margin-top:10px;color:#f3e6cc}}
</style></head><body><main>
<p class="status">{escape(status)} · методика {escape(document["method"])}</p>
<h1>{escape(experiment["title"])}</h1>
<h2>Цель и гипотеза</h2><p>{escape(experiment["goal"])}</p><p>{escape(experiment["hypothesis"])}</p><p>{escape(experiment["comparison"])}</p>
<h2>Версия симулятора</h2><p>{escape(document["reproduction"]["versionNote"])}</p>
<h2>Конфигурация</h2><p>Карта: {escape(experiment["mapLabel"])}. Профиль: {escape(experiment["profileId"])}. {escape(experiment["profileNote"])}</p>
<p>Тиков в прогоне: {experiment["ticks"]}. Seeds: {escape(", ".join(map(str, experiment["seeds"])))}. Повторов одного seed: {experiment["repetitions"]}. Запись: {escape(experiment["recording"])}.</p>
<ul>{fixed}</ul>
<h2>Условия</h2><ul>{groups}</ul>
<h2>Методика</h2><ul>{"".join(f"<li>{escape(note)}</li>" for note in document["methodNotes"])}</ul>
<p>Запланировано прогонов: {len(document["runs"])}. В статистику групп вошло успешных прогонов: {included}. Независимая единица сравнения — общий seed, а не сумма прогонов двух групп.</p>
<h2>Главные результаты</h2><ul>{findings_html}</ul>
<h2>Графики и таблицы</h2>{"".join(sections)}
<h2>Пояснения</h2><dl>{glossary}</dl>
<h2>Ограничения</h2><ul>{limits}</ul>
<h2>Воспроизведение</h2><p>{escape(document["reproduction"]["detail"])}</p>
<h2>Файлы</h2><ul>{files}</ul>
<p>Папка этого отчёта: {escape(document["paths"]["absolute"])}</p>
<p>Относительный путь: {escape(document["paths"]["relative"])}</p>
<p>Сформирован: {escape(document["generatedAt"])}.</p>
</main></body></html>
'''


def atomic_bytes(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_bytes(data)
    os.replace(temporary, path)


def atomic_text(path, text, encoding='utf-8'):
    atomic_bytes(path, text.encode(encoding))


def write_bundle(folder, document, sources):
    """Пишет отчёт в analysis/. Исходные прогоны не открывает на запись."""
    analysis = folder / 'analysis'
    analysis.mkdir(parents=True, exist_ok=True)
    roles = {
        'report.html': 'Основной отчёт. Открывается браузером без лаборатории.',
        'report.md': 'Текстовый отчёт для редактора и дальнейшей работы.',
        'summary.csv': 'Сводная таблица для Excel: UTF-8 с BOM, точка с запятой, десятичная запятая.',
        'runs.csv': 'Все прогоны, включая ошибки и причины исключения из статистики.',
        'experiment.json': 'Снимок плана: гипотеза, группы, seeds, метрики и версии.',
        'statistics.json': 'Рассчитанные показатели, методика и правила пропусков.',
        'results.zip': 'Отчёты, план и исходные итоги без огромных журналов.',
    }
    prepared = {
        'summary.csv': summary_csv(document).encode('utf-8'),
        'runs.csv': runs_csv(document).encode('utf-8'),
        'experiment.json': dumps(document['experimentPlan']).encode('utf-8'),
    }
    markdown_payload = markdown(document).encode('utf-8')
    for name, payload in prepared.items():
        atomic_bytes(analysis / name, payload)
    atomic_bytes(analysis / 'report.md', markdown_payload)
    document['files'] = [dict(name='analysis/' + name, role=role, bytes=0) for name, role in roles.items()]

    def assign(name, size):
        for item in document['files']:
            if item['name'] == 'analysis/' + name:
                item['bytes'] = size

    for name, payload in prepared.items():
        assign(name, len(payload))
    assign('report.md', len(markdown_payload))
    html_size = stats_size = zip_size = 0
    html_payload = stats_payload = b''
    archive_path = analysis / 'results.zip'
    temporary = archive_path.with_suffix('.zip.tmp')
    omitted_text = document.get('omittedNote', '')
    for _ in range(8):
        assign('report.html', html_size)
        assign('statistics.json', stats_size)
        assign('results.zip', zip_size)
        html_payload = html(document).encode('utf-8')
        stats_payload = dumps(document).encode('utf-8')
        with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('analysis/report.html', html_payload)
            archive.writestr('analysis/report.md', markdown_payload)
            archive.writestr('analysis/statistics.json', stats_payload)
            for name, payload in prepared.items():
                archive.writestr('analysis/' + name, payload)
            for path, arcname in sources:
                archive.write(path, arcname)
            archive.writestr('analysis/omitted.txt', omitted_text)
        measured_zip = temporary.stat().st_size
        if len(html_payload) == html_size and len(stats_payload) == stats_size and measured_zip == zip_size:
            break
        html_size, stats_size, zip_size = len(html_payload), len(stats_payload), measured_zip
    atomic_bytes(analysis / 'report.html', html_payload)
    atomic_bytes(analysis / 'statistics.json', stats_payload)
    os.replace(temporary, archive_path)
    return list(document['files'])

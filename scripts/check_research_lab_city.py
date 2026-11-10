#!/usr/bin/env python3
"""Один обычный прогон экономики города и, по флагу, пауза/остановка.
Это проверка лаборатории, не вывод о балансе. Godot не нужен.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import json
import time
from pathlib import Path

from research_city_report import CANONICAL, report
from research_lab import Lab, read
from research_lab_presets import city_plan


def wait_until(predicate, seconds, message):
    start = time.monotonic()
    while time.monotonic() - start < seconds:
        if predicate():
            return
        time.sleep(0.05)
    raise AssertionError(message)


def progress_of(lab, key, job):
    attempt = job['attempt'] or 1
    path = lab.workspace/key/'runs'/job['id']/str(attempt)/'run-progress.json'
    if not path.is_file():
        return None
    try:
        return json.loads(path.read_text(encoding='utf-8'))
    except json.JSONDecodeError:
        return None


def run_city(workspace, ticks):
    plan = city_plan(ticks=ticks)
    lab = Lab(workspace)
    key = lab.enqueue(plan)
    lab.start()
    lab.thread.join(plan['timeoutSeconds'] + 60)
    if lab.thread is not None and lab.thread.is_alive():
        lab.pause()
        raise AssertionError('Прогон не завершился за отведённый предохранитель')
    series = next(item for item in lab.series if item['id'] == key)
    job = series['jobs'][0]
    if job['status'] != 'succeeded':
        raise AssertionError(job.get('error') or job['status'])
    folder = lab.workspace/job['result']
    summary = read(folder/'summary.json')
    final = read(folder/'final-state.json')
    treasury = next(item['balanceMinor'] for item in final['accounts'] if item['kind'] == 'Treasury')
    lines = (folder/'metrics.csv').read_text(encoding='utf-8').splitlines()
    if lines[0] != CANONICAL:
        raise AssertionError('Заголовок metrics.csv изменился')
    cells = lines[-1].split(',')
    if int(cells[3]) != treasury:
        raise AssertionError('Колонка treasury_minor не равна балансу счёта казны')
    document = read(folder/'metrics-format.json')
    if document.get('version') != 1 or document.get('treasuryColumn') != 'treasury_minor':
        raise AssertionError('metrics-format.json не подтверждает колонку казны')
    data = report(folder)
    if not data['complete'] or data['treasuryBalanceMinor'] != treasury:
        raise AssertionError('Отчёт не совпал с итоговым счётом казны')
    if data['population'] is None or data['municipalMinor'] is None:
        raise AssertionError('public-economy.json не попал в отчёт')
    if read(folder/'manifest.json').get('economyProfileId') != 'vnext-i6c72-wages-1':
        raise AssertionError('Обычная партия взяла не текущий профиль зарплат')
    if not (folder/'economy-samples.jsonl').is_file():
        raise AssertionError('Обычная запись не сохранила срезы экономики')
    logs = list((lab.workspace/key/'checks').glob(job['id'] + '-a' + str(job['attempt']) + '.stdout.log'))
    if not logs:
        raise AssertionError('Журнал stdout попытки не сохранён')
    return dict(ticks=ticks, treasuryMinor=treasury, population=data['population'], municipalMinor=data['municipalMinor'],
                activeProductions=data['activeProductions'], constructed=data['buildings']['constructed'],
                existing=data['buildings']['existing'], profile='vnext-i6c72-wages-1')


def pause_city(workspace):
    plan = city_plan(name='city-pause', title='Пауза экономики', ticks=5000)
    lab = Lab(workspace)
    key = lab.enqueue(plan)
    lab.start()
    series = next(item for item in lab.series if item['id'] == key)
    job = series['jobs'][0]
    try:
        wait_until(lambda: job['status'] == 'running' and (progress_of(lab, key, job) or {}).get('tick', 0) >= 1, 180,
                   'Прогон не дошёл до первого тика')
        lab.views.control(key, 'paused')
        wait_until(lambda: (progress_of(lab, key, job) or {}).get('status') == 'paused', 30, 'Пауза прогона не видна в heartbeat')
        frozen = progress_of(lab, key, job)['tick']
        time.sleep(1)
        held = progress_of(lab, key, job)
        if held['tick'] != frozen or held['status'] != 'paused':
            raise AssertionError('Во время паузы тик продолжил идти')
        lab.views.control(key, 'running')
        wait_until(lambda: (progress_of(lab, key, job) or {}).get('status') in ('running', 'voting', 'exporting'), 30,
                   'Прогон не продолжился после паузы')
        lab.views.control(key, 'cancelled')
        lab.thread.join(120)
        if job['status'] != 'cancelled':
            raise AssertionError(job.get('error') or job['status'])
        summary = read(lab.workspace/job['result']/'summary.json')
        if summary.get('status') != 'cancelled':
            raise AssertionError('Остановленный прогон не записан как cancelled')
        if lab.compare([key])['groups'][0]['complete'] != 0:
            raise AssertionError('Неполный прогон попал в статистику')
    finally:
        if job['status'] == 'running':
            try:
                lab.views.control(key, 'cancelled')
            except ValueError:
                pass
        lab.pause()
        if lab.thread is not None:
            lab.thread.join(60)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--ticks', type=int, default=120)
    parser.add_argument('--pause', action='store_true')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    result = run_city(output/'study', args.ticks)
    if args.pause:
        pause_city(output/'pause')
        result['pauseCancel'] = True
    (output/'gate.json').write_text(json.dumps(dict(status='succeeded', **result), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('City laboratory check passed', flush=True)


if __name__ == '__main__':
    main()

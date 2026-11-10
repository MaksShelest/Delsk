#!/usr/bin/env python3
"""Короткий реальный опыт R2: две целевые ставки NPC, два seed, 120 тиков.
Это не серия 10 seeds × 1000 тиков. Её можно выбрать в мастере, здесь она не запускалась.
Author: Шелестов Максим Евгеньевич.
"""
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from research_experiment import build_plan
from research_lab import Lab


def main():
    workspace = ROOT / 'artifacts' / 'lab-r2-demo'
    if workspace.exists():
        shutil.rmtree(workspace)
    built = build_plan(dict(
        template='wages', title='Демонстрация: целевая ставка NPC 0,15 и 0,25',
        seedCount=2, firstSeed=42, ticks=120, repetitions=1, recording='normal', npcs=3,
        groups=[dict(label='A', value=15), dict(label='B', value=25)]))
    lab = Lab(workspace)
    key = lab.enqueue(built['plan'])
    lab.start()
    while True:
        with lab.lock:
            thread = lab.thread
        if thread is None:
            break
        thread.join(1)
    series = lab.series[0]
    failed = [job for job in series['jobs'] if job['status'] != 'succeeded']
    destination = ROOT / 'docs' / 'verification' / 'r2' / 'demo'
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    analysis = workspace / key / 'analysis'
    for name in ('report.html', 'report.md', 'summary.csv', 'runs.csv', 'statistics.json', 'experiment.json', 'results.zip'):
        source = analysis / name
        if not source.is_file():
            print('Нет файла отчёта:', source, file=sys.stderr)
            return 1
        shutil.copy2(source, destination / name)
    print('series', key)
    print('jobs', [(job['id'], job['status']) for job in series['jobs']])
    print('copied', destination)
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())

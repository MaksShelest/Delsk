#!/usr/bin/env python3
"""Run paired I6-C8C/I6-C8D comparisons through Research.Runner."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from itertools import chain, product
import json
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
RUNNER = ['dotnet', str(ROOT / 'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
MAPS = ('compact-valley', 'woodland-region')
INDUSTRIES = ('woodworking', 'food', 'dairy', 'materials')
DIFFICULTIES = ('Easy', 'Medium', 'Hard')


def scenarios(cohort):
    if cohort == 'short':
        return [(*case, 'Medium', 'production', 1200) for case in product(MAPS, INDUSTRIES, [17])]
    if cohort == 'human':
        cases = chain(product(MAPS, INDUSTRIES, [17]), product(MAPS, ('woodworking', 'dairy'), [42]))
        return [(*case, 'Medium', strategy, 2400) for case in cases for strategy in ('raw', 'production')]
    if cohort == 'npc':
        return [(map_id, INDUSTRIES[(index + seed) % 4], seed, difficulty, 'npc', 3600)
                for map_id, (index, difficulty), seed in product(MAPS, enumerate(DIFFICULTIES), (17, 42))]
    return [(map_id, 'woodworking', 17, difficulty, 'npc', 7200)
            for map_id, difficulty in zip(MAPS, ('Medium', 'Hard'), strict=True)]


def checked(command, deadline, folder, name):
    return subprocess.run([sys.executable, 'scripts/run_check.py', '--name', name,
                           '--timeout', str(deadline), '--log-directory', str(folder),
                           '--', *command], cwd=ROOT, check=False).returncode


def run(case, profile, output, cohort):
    map_id, industry, seed, difficulty, strategy, ticks = case
    run_directory = output / profile.stem / f'{map_id}-{difficulty}-{industry}-{seed}-{strategy}'
    run_directory.mkdir(parents=True)
    scenario = f'scenarios/vnext/{map_id}-i6c8c.json'
    if strategy != 'npc':
        return checked([*RUNNER, '--study-i6c8d', scenario, str(profile), industry,
                        str(seed), strategy, str(ticks)], 180, run_directory, 'study')

    options = run_directory / 'options.json'
    options.write_text(json.dumps(dict(npcCount=3, difficulty=difficulty, seed=seed,
                                       startingIndustry=industry)), encoding='utf-8')
    archive = run_directory / 'continuous'
    command = [*RUNNER, '--scenario', scenario, '--economy', str(profile), '--new-game', str(options),
               '--script', 'baseline', '--output', str(archive), '--ticks', str(ticks),
               '--economy-sample', 'true', '--human-ballot', 'self', '--compress-controller-turns', 'true']
    checkpoint = run_directory / 'checkpoint.json'
    long_run = cohort == 'long'
    if long_run:
        command += ['--checkpoint', str(checkpoint), '--checkpoint-at', str(ticks // 2), '--progress', 'true']
    result = checked(command, 270 if long_run else 180, run_directory, 'study')
    if result or not long_run:
        return result

    resumed = run_directory / 'resumed'
    result = checked([*RUNNER, '--load', str(checkpoint), '--output', str(resumed),
                      '--ticks', str(ticks), '--compress-controller-turns', 'true'],
                     270, run_directory, 'resume')
    if result:
        return result

    def hashes(folder):
        metadata = json.loads((folder / 'summary.json').read_text(encoding='utf-8'))
        return (metadata['tick'], metadata['stateHash'], metadata['controllerStateHash'])

    result = (hashes(resumed) == hashes(archive))
    (run_directory / 'resume-matches.json').write_text(json.dumps(result), encoding='utf-8')
    return 0 if result else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cohort', choices=['short', 'human', 'npc', 'long'], required=True)
    parser.add_argument('--workers', type=int, choices=range(1, 5), default=2)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    profiles = output / 'profiles'
    command = [*RUNNER, '--export-i6c8-profiles', 'config/vnext/economy.i4-mobile-equipment.json', str(profiles)]
    if checked(command, 30, output, 'profiles'):
        return 1
    shutil.copyfile(ROOT / 'config/vnext/balance.b1.json', profiles / 'balance.b1.json')
    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        work = [pool.submit(run, case, profiles / f'{version}.json', output, args.cohort)
                for case, version in product(scenarios(args.cohort), ('i6c8c', 'i6c8d'))]
        failures = sum(future.result() != 0 for future in work)
    print(f'{len(work) - failures}/{len(work)} complete')
    return int(failures > 0)


if __name__ == '__main__':
    sys.exit(main())

#!/usr/bin/env python3
"""P58 local research queue. All simulation/validation is in the existing C# runner.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import copy
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import threading
import uuid

from analyze_paired_series import spread
from run_research_series import runner_command, run_checked, commit

ROOT = Path(__file__).resolve().parents[1]
# Deliberately narrow scalar surface; catalogues/geometry stay in source profiles.
FACTORS = {
    'npc.difficulty': ['Easy', 'Medium', 'Hard'],
    'npc.negotiationsEnabled': [True, False],
    'npc.tradeEnabled': [True, False],
    'npc.politicsEnabled': [True, False],
    'npc.logisticsEnabled': [True, False],
    'npc.cashReserveMinor': [0, 3000, 6000],
    'npc.maximumWageMinor': [35, 50, 75],
    'world.startingCashMinor': [0, 20000],
    'world.population': [100, 200],
    'world.labor.mobilityNumerator': [1, 2],
    'world.rules.wageAttractionExponent': [1, 3],
    'world.capitalDemand.bootstrapBaseBudgetMinor': [30000, 60000],
    'world.capitalDemand.demandNumerator': [10, 11],
}
DEFAULT = dict(version=1, id='negotiations-paired', scenario='scenarios/presentation-map-one-v3.json',
               economy='config/economy.json', controllers='scenarios/baseline-controllers.json',
               ticks=600, seeds=[42, 43, 44], repetitions=1, mode='algorithmic',
               factors=[dict(path='npc.negotiationsEnabled', values=[True, False])])


def unique(pairs):
    out = {}
    for k, v in pairs:
        if k in out:
            raise ValueError('Duplicate JSON field: ' + k)
        out[k] = v
    return out


def read(path):
    if path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError('Input exceeds 2MiB')
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def read_metadata(path):
    """Owned persistent queue/results, not an input plan. Preserve duplicate-field validation."""
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    os.replace(temporary, path)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def integer(n, low, high):
    return type(n) is int and low <= n <= high


def fields(value, allowed, required=()):
    if not isinstance(value, dict) or set(value) - set(allowed) or set(required) - set(value):
        raise ValueError('Missing/unknown fields: ' + ', '.join(allowed))


def values(factor):
    fields(factor, ['path', 'values', 'range'], ['path'])
    key = factor['path']
    if key not in FACTORS or ('values' in factor) == ('range' in factor):
        raise ValueError('Use one supported factor and exactly one of values/range')
    if 'range' in factor:
        r = factor['range']
        fields(r, ['start', 'stop', 'step'], ['start', 'stop', 'step'])
        if not all(integer(r[k], 0, 10**9) for k in r) or r['step'] <= 0 or r['start'] > r['stop']:
            raise ValueError('Invalid inclusive integer range')
        if (r['stop'] - r['start']) // r['step'] > 31:
            raise ValueError('At most 32 values per factor')
        result = list(range(r['start'], r['stop'] + 1, r['step']))
    else:
        result = factor['values']
    if not isinstance(result, list) or not 1 <= len(result) <= 32 or len({json.dumps(v) for v in result}) != len(result):
        raise ValueError('Use 1..32 distinct scalar values')
    exemplar = FACTORS[key][0]
    if any(type(v) is not type(exemplar) or isinstance(v, str) and v not in FACTORS[key] or
           type(v) is int and not integer(v, 0, 10**9) for v in result):
        raise ValueError('Invalid factor value type/range: ' + key)
    return result


def source(path, workspace):
    if not isinstance(path, str):
        raise ValueError('Input path must be text')
    p = (ROOT / path).resolve()
    if not (p.is_relative_to(ROOT) or p.is_relative_to(workspace)) or p.suffix != '.json':
        raise ValueError('Inputs must be JSON inside the repository or lab workspace')
    return p


def scalar(document, path, value):
    node = document
    parts = path.split('.')
    for part in parts[:-1]:
        if part not in node or not isinstance(node[part], dict):
            raise ValueError('Missing parameter: ' + path)
        node = node[part]
    if parts[-1] not in node:
        raise ValueError('Missing parameter: ' + path)
    node[parts[-1]] = value


def expand(plan, workspace):
    if not isinstance(plan,dict):raise ValueError("Plan must be an object")
    fields(plan, ['version', 'id', 'scenario', 'economy', 'controllers', 'ticks', 'seeds',
                  'repetitions', 'mode', 'modelConfig', 'factors', 'composition', 'timeoutSeconds', 'profile', 'economyData', 'modelData', 'participants', 'recordWorld', 'ordinaryNpcCount'],
           ['version', 'id', 'scenario', 'economy', 'ticks', 'seeds', 'repetitions', 'mode', 'factors'] + ([] if 'ordinaryNpcCount' in plan else ['controllers']))
    if type(plan['version']) is not int or plan['version'] != 1 or not isinstance(plan['id'],str) or not re.fullmatch(r'[a-zA-Z0-9_-]{1,64}', str(plan['id'])):
        raise ValueError('Invalid plan version/id')
    if not integer(plan['ticks'], 1, 10000) or not integer(plan['repetitions'], 1, 500):
        raise ValueError('Use ticks1..10000, repetitions1..500')
    seeds = plan['seeds']
    if not isinstance(seeds, list) or not seeds or len(seeds) != len(set(seeds)) or any(not integer(s, 0, 2**64-1) for s in seeds):
        raise ValueError('Use distinct uint64 seeds')
    if plan['mode'] not in ('algorithmic', 'hybrid') or (plan['mode'] == 'hybrid') != ('modelConfig' in plan or 'modelData' in plan) or ('modelConfig' in plan and 'modelData' in plan):
        raise ValueError('Hybrid requires explicit real modelConfig; algorithmic forbids it')
    if type(plan.get('profile',False)) is not bool or plan.get('profile',False) and plan['mode']!='algorithmic':
        raise ValueError('Step profiling requires an algorithmic run')
    if not integer(plan.get('timeoutSeconds', 180), 1, 900):
        raise ValueError('Use timeoutSeconds1..900')
    if not isinstance(plan['factors'], list) or len(plan['factors']) > 6:
        raise ValueError('At most 6 factors')
    ordinary='ordinaryNpcCount' in plan
    if ordinary and (not integer(plan['ordinaryNpcCount'],3,10) or plan['mode']!='algorithmic' or any(k in plan for k in ('controllers','participants','composition'))):
        raise ValueError('Ordinary setup requires 3..10 NPC, algorithmic mode and no controllers/participants/composition override')
    if ordinary and any(f.get('path')=='npc.negotiationsEnabled' for f in plan['factors']):
        raise ValueError('Negotiation override is not an OrdinaryGameOptions setting')
    axes = [values(f) for f in plan['factors']]
    paths = [f['path'] for f in plan['factors']]
    if len(paths) != len(set(paths)):
        raise ValueError('Conflicting duplicate factors')
    count = len(seeds) * plan['repetitions']
    for axis in axes:
        count *= len(axis)
    if count > 10000:
        raise ValueError('At most 10000 cells per series')
    scenario = read(source(plan['scenario'], workspace))
    economy = copy.deepcopy(plan['economyData']) if 'economyData' in plan else read(source(plan['economy'], workspace))
    controllers = dict(version=1,humanActorId=1,finalTick=plan['ticks'],npcs=[]) if ordinary else read(source(plan['controllers'], workspace))
    model = copy.deepcopy(plan['modelData']) if 'modelData' in plan else read(source(plan['modelConfig'], workspace)) if 'modelConfig' in plan else None
    if not isinstance(economy,dict) or type(plan.get('recordWorld',True)) is not bool:raise ValueError('Invalid economy/recording options')
    from research_workspace import prepare_participants
    setup=prepare_participants(plan,scenario,controllers,economy)
    if plan['ticks'] > economy['world']['maxTicks']:
        raise ValueError('Ticks exceed profile maximum')
    if 'composition' in plan:
        composition = plan['composition']
        if not isinstance(composition, list) or not composition:
            raise ValueError('Composition must enumerate every NPC')
        for n in composition:
            fields(n, ['actorId', 'preferredInput'], ['actorId', 'preferredInput'])
        if sorted(n['actorId'] for n in composition) != sorted(n['actorId'] for n in controllers['npcs']):
            raise ValueError('Composition must enumerate every NPC exactly once')
        for n in controllers['npcs']:
            n['preferredInput'] = next(c['preferredInput'] for c in composition if c['actorId'] == n['actorId'])
    arms = []
    for index, combination in enumerate(itertools.product(*axes)):
        e, c = copy.deepcopy(economy), copy.deepcopy(controllers)
        c['finalTick'] = plan['ticks']
        ordinary_difficulty='Medium'
        for path, value in zip(paths, combination):
            if ordinary and path=='npc.difficulty':
                ordinary_difficulty=value
            elif path in ('npc.difficulty', 'npc.negotiationsEnabled'):
                for n in c['npcs']:
                    n[path.split('.')[1]] = value
                    if path == 'npc.difficulty' and value != 'Hard':
                        n.pop('planning', None)
            else:
                scalar(e, path, value)
        arms.append(dict(id=f'arm-{index}', factors=dict(zip(paths, combination)), economy=e, controllers=c, setup=setup,ordinary=dict(npcCount=plan['ordinaryNpcCount'],difficulty=ordinary_difficulty) if ordinary else None))
    return scenario, model, arms


def command(folder, job, output):
    path = folder / 'inputs' / job['arm']
    mode = job['mode']
    cmd = runner_command(str(output), 'baseline' if mode == 'algorithmic' else 'hybrid', 'headless',
                         job['ticks'], job['seed'], str(folder/'inputs/scenario.json'),
                         str(path/'controllers.json'), 'ollama', str(folder/'inputs/model.json'))
    # Reuse the common runner CLI, without rebuilding per cell.
    cmd = ['dotnet', str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'), *cmd[cmd.index('--')+1:]]
    if (path/'ordinary.json').is_file():
        # The C# resolver owns actor slots, capacities and modern policy settings.
        options=read(path/'ordinary.json')|dict(seed=job['seed'])
        startup=output.with_name(output.name+'-new-game.json');write(startup,options)
        cmd=cmd[:2]+[v for flag,value in zip(cmd[2::2],cmd[3::2]) if flag not in ('--controllers','--seed') for v in (flag,value)]
        cmd+=['--new-game',str(startup)]
        if not job.get('profile'):cmd+=['--progress','true']
    if job.get('profile'):cmd += ['--profile','true']
    if job.get('recordWorld',False):cmd += ['--research','true']
    if job.get('modelWait',False):cmd += ['--model-wait','true']
    if (path/'setup.json').exists():cmd += ['--research-setup',str(path/'setup.json')]
    return [*cmd, '--economy', str(path/'economy.json'), '--human-ballot', 'self']


def measure(folder):
    """Research-only outputs. Missing metrics stay null rather than invented zero."""
    from research_metrics import measure as measure_export
    return measure_export(folder)


class Lab:
    def __init__(self, workspace, executor=run_checked):
        self.workspace = Path(workspace).resolve()
        self.workspace.mkdir(parents=True, exist_ok=True)
        self.executor = executor
        os.environ.setdefault('DELSK_COMMIT', commit() or 'unknown')
        self.commit = subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True,timeout=10).strip()
        self.lock = threading.RLock()
        self.thread = None
        self.paused = True
        from research_workspace import Workspace
        self.views=Workspace(self)
        self.series = read_metadata(self.workspace/'queue.json') if (self.workspace/'queue.json').exists() else []
        # An interrupted process is never silently considered successful or reissued.
        for s in self.series:
            for j in s['jobs']:
                if j['status'] == 'running':
                    j.update(status='interrupted', error='Host stopped; explicitly retry interrupted/failed cells')
        self.save()

    def save(self):
        write(self.workspace/'queue.json', self.series)

    def snapshot(self):
        with self.lock:
            series=copy.deepcopy(self.series)
            for s in series:
                for j in s['jobs']:
                    if j['status']=='running':
                        file=self.workspace/s['id']/'runs'/j['id']/str(j['attempt'])/'progress.json'
                        if not file.exists():file=file.with_name('run-progress.json')
                        try:j['progress']=read(file)
                        except (OSError,ValueError):pass
            return dict(paused=self.paused, running=self.thread is not None and self.thread.is_alive(),
                        workspace=str(self.workspace), series=series)

    def enqueue(self, plan):
        scenario, model, arms = expand(plan, self.workspace)
        # All arms pass actual C# validators before publishing any queued jobs.
        with tempfile.TemporaryDirectory(prefix='.validate-', dir=self.workspace) as temporary:
            stage = Path(temporary)
            write(stage/'inputs/scenario.json', scenario)
            if model is not None:
                write(stage/'inputs/model.json', model)
            for arm in arms:
                for name in ('economy', 'controllers', 'setup', 'ordinary'):
                    if arm.get(name) is None:continue
                    write(stage/f"inputs/{arm['id']}/{name}.json", arm[name])
                if arm.get('ordinary') is not None and arm['economy'].get('vNext'):
                    full=source(plan['economy'],self.workspace).parent/'balance.b1.json'
                    if not full.is_file() or digest(full)!=arm['economy']['vNext']['fullBalanceSha256']:
                        raise ValueError('Ordinary full balance/profile mismatch')
                    (stage/f"inputs/{arm['id']}/balance.b1.json").write_bytes(full.read_bytes())
                sample = dict(arm=arm['id'], mode=plan['mode'], seed=plan['seeds'][0], ticks=plan['ticks'])
                code, _ = self.executor('validate-'+arm['id'], 45, stage/'checks',
                                        [*command(stage, sample, stage/'unused'), '--validate-only', 'true'])
                if code:
                    detail = (stage/'checks'/('validate-'+arm['id']+'.stderr.log'))
                    raise ValueError('Runner rejected '+arm['id']+': '+(detail.read_text(encoding='utf-8')[-3000:] if detail.exists() else str(code)))
            key = plan['id'] + '-' + uuid.uuid4().hex[:10]
            folder = self.workspace/key
            folder.mkdir()
            # Snapshot validated inputs, not the possibly edited source files.
            import shutil
            shutil.copytree(stage/'inputs', folder/'inputs')
            shutil.copytree(stage/'checks', folder/'validation') if (stage/'checks').exists() else None
        write(folder/'plan.json', plan)
        jobs = [dict(id=f"{arm['id']}-s{seed}-r{repeat}", arm=arm['id'], factors=arm['factors'],
                     seed=seed, repeat=repeat, ticks=plan['ticks'], mode=plan['mode'], profile=plan.get('profile',False),recordWorld=plan.get('recordWorld',True) and not plan.get('profile',False),modelWait=not plan.get('profile',False) and 'ordinaryNpcCount' not in plan, status='queued', attempt=0)
                for arm in arms for seed in plan['seeds'] for repeat in range(plan['repetitions'])]
        from research_workspace import binary_hashes
        s = dict(id=key,name=plan['id'],archived=False, plan=copy.deepcopy(plan), jobs=jobs, commit=self.commit,binaries=binary_hashes(),
                 inputs={str(p.relative_to(folder)): digest(p) for p in sorted((folder/'inputs').rglob('*.json'))})
        with self.lock:
            self.series.append(s)
            self.save()
        return key

    def pause(self):
        with self.lock:
            self.paused = True

    def start(self, retry=False):
        with self.lock:
            if retry:
                for s in self.series:
                    for j in s['jobs']:
                        if not s.get('archived') and j['status'] in ('failed', 'interrupted','cancelled'):
                            j['status'] = 'queued'
            self.paused = False
            self.save()
            if self.thread is None or not self.thread.is_alive():
                self.thread = threading.Thread(target=self.drain, daemon=False)
                self.thread.start()

    def drain(self):
        while True:
            with self.lock:
                pending = next(((s, j) for s in self.series if not s.get('archived') for j in s['jobs'] if j['status'] == 'queued'), None)
                if self.paused or pending is None:
                    # Close the start/drain race while holding the same lock.
                    self.thread = None
                    return
                s, j = pending
                j.update(status='running', attempt=j['attempt']+1)
                self.save()
                job = copy.deepcopy(j)
            folder = self.workspace/s['id']
            out = folder/'runs'/job['id']/str(job['attempt'])
            cmd=[]
            try:
                from research_workspace import binary_hashes,validate_recording
                if s.get('binaries',{})!=binary_hashes():raise ValueError('Runner binaries changed; create a new series')
                if any(digest(folder/p) != h for p, h in s['inputs'].items()):
                    raise ValueError('Validated input snapshot was changed')
                cmd = command(folder, job, out)
                code, meta = self.executor(job['id']+'-a'+str(job['attempt']), s['plan'].get('timeoutSeconds', 180), folder/'checks', cmd)
                if code==2 and (out/'summary.json').exists() and read(out/'summary.json').get('status')=='cancelled':
                    with self.lock:j.update(status='cancelled',result=str(out.relative_to(self.workspace)),command=cmd)
                    raise InterruptedError('Researcher cancelled')
                if code:
                    raise ValueError(f'Runner exit {code}; see checks/{job["id"]}-a{job["attempt"]} logs')
                if job.get('recordWorld'):validate_recording(out)
                metrics = measure(out)
                with self.lock:
                    j.update(status='succeeded', metrics=metrics, result=str(out.relative_to(self.workspace)), check=meta, command=cmd, artifacts=[p.name for p in sorted(out.iterdir()) if p.suffix in ('.json','.jsonl','.csv')])
                    j.pop('error', None)
            except Exception as error:
                with self.lock:
                    j.update(status='cancelled' if isinstance(error,InterruptedError) else 'failed', error=str(error),result=str(out.relative_to(self.workspace)),command=cmd)
            with self.lock:
                j['logs'] = [str(p.relative_to(self.workspace)) for p in
                             sorted((folder/'checks').glob(job['id']+'-a'+str(job['attempt'])+'.*')) if p.is_file()]
                s['bytes']=sum(p.stat().st_size for p in folder.rglob('*') if p.is_file() and not p.is_symlink())
                write(folder/'series.json', s)
                self.export_summary(folder, self.compare([s['id']]))
                self.save()

    def export_summary(self, folder, summary):
        import csv
        write(folder/'summary.json', summary)
        with (folder/'summary.csv').open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.writer(stream)
            writer.writerow(['series','arm','metric','n','mean','median','minimum','maximum','iqr'])
            for group in summary['groups']:
                for metric, value in group['metrics'].items():
                    writer.writerow([group['series'],group['arm'],metric,*[value[k] for k in ('n','mean','median','minimum','maximum','iqr')]])

    def compare(self, selected):
        with self.lock:
            groups = []
            for s in self.series:
                if s['id'] not in selected:
                    continue
                for arm in dict.fromkeys(j['arm'] for j in s['jobs']):
                    jobs = [j for j in s['jobs'] if j['arm'] == arm and j['status'] == 'succeeded']
                    metrics = {k: spread([j['metrics'][k] for j in jobs if type(j['metrics'].get(k)) in (int, float)])
                               for k in sorted({k for j in jobs for k in j['metrics'] if type(j['metrics'][k]) in (int, float)})}
                    groups.append(dict(series=s['id'], arm=arm, complete=len(jobs), metrics=metrics))
            pairs = []
            for left,right in itertools.combinations(groups,2):
                a = next(s for s in self.series if s['id']==left['series'])
                b = next(s for s in self.series if s['id']==right['series'])
                from research_workspace import compatible
                if not compatible(self,a,b,left['arm'],right['arm']):
                    continue
                aj = {(j['seed'],j['repeat']):j for j in a['jobs'] if j['arm']==left['arm'] and j['status']=='succeeded'}
                bj = {(j['seed'],j['repeat']):j for j in b['jobs'] if j['arm']==right['arm'] and j['status']=='succeeded'}
                keys = sorted(aj.keys() & bj.keys())
                common = left['metrics'].keys() & right['metrics'].keys()
                pairs.append(dict(left=[left['series'],left['arm']],right=[right['series'],right['arm']],
                    pairs=len(keys),independentSeeds=len({k[0] for k in keys}),
                    declaredContrasts=dict(mode=[a['plan']['mode'],b['plan']['mode']],
                        modelConfigurationHashes=[a['inputs'].get('inputs/model.json'),b['inputs'].get('inputs/model.json')],
                        factors=sorted({f['path'] for s in (a,b) for f in s['plan']['factors']})),
                    differences={m:spread([bj[k]['metrics'][m]-aj[k]['metrics'][m] for k in keys
                        if type(aj[k]['metrics'].get(m)) in (int,float) and type(bj[k]['metrics'].get(m)) in (int,float)]) for m in sorted(common)}))
            return dict(groups=groups, pairedRightMinusLeft=pairs, scope='Research-only aggregates; paired analysis requires identical inputs except the declared factors. Repeated algorithmic seeds are replicas, not independent samples.')


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, default=Path('artifacts/lab'))
    p.add_argument('--plan', type=Path)
    p.add_argument('--run', action='store_true', help='Drain the persisted queue without the UI')
    p.add_argument('--port', type=int, default=8765)
    p.add_argument('--prepare', action='store_true', help='Bounded restore/build of the shared runner before starting')
    p.add_argument('--open-browser', action='store_true', help='Open this laboratory, or reopen the already running host')
    a = p.parse_args(argv)
    if sys.version_info < (3, 10):
        p.error('Python 3.10+ is required')
    if not 0 <= a.port <= 65535:
        p.error('Port must be 0..65535 (0 selects an available port)')
    os.chdir(ROOT)
    os.environ['DELSK_COMMIT'] = commit() or 'unknown'
    # One writer per queue, across browser/CLI instances; released automatically on crash.
    owner = (a.output.resolve()/'host.lock')
    owner.parent.mkdir(parents=True,exist_ok=True)
    lock = owner.open('a+b')
    try:
        if os.name == 'nt':
            import msvcrt
            if owner.stat().st_size == 0: lock.write(b'0');lock.flush()
            lock.seek(0);msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        else:
            import fcntl
            fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    except OSError:
        lock.close()
        from research_lab_launch import reopen
        if a.open_browser and not a.run and not a.plan and reopen(a.output.resolve()):
            return 0
        p.error('Лаборатория уже использует эту папку. Дождитесь подготовки или закройте прежнее окно запуска.')
    lab = None
    try:
        from research_lab_launch import prepare, require_runner
        if a.prepare:
            prepare(a.output.resolve())
        require_runner()
        lab = Lab(a.output)
        if a.plan:
            print('Queued:', lab.enqueue(read(a.plan)))
        if a.run:
            lab.start()
            while True:
                with lab.lock:
                    t = lab.thread
                if t is None:
                    break
                t.join(.2)
            return int(any(j['status'] != 'succeeded' for s in lab.series for j in s['jobs']))
        from research_lab_server import serve
        serve(lab, a.port, a.open_browser)
        return 0
    except (ValueError, OSError) as error:
        print('Не удалось запустить лабораторию: '+str(error), file=sys.stderr, flush=True)
        return 1
    finally:
        if lab is not None:
            lab.pause()
            if lab.thread is not None:
                lab.thread.join()  # Existing bounded job finishes before releasing the host lock.
        lock.close()


if __name__ == '__main__':
    sys.exit(main())

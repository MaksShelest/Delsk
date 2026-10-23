"""Research workspace files and views. No economic calculations.
Author: Шелестов Максим Евгеньевич.
"""
import copy
import csv
import io
import json
from pathlib import Path
import re
import shutil
import urllib.request
from urllib.parse import urlsplit
import uuid


def model_tags(endpoint):
    url = urlsplit(endpoint)
    if (url.scheme not in ('http', 'https') or url.hostname not in ('localhost', '127.0.0.1', '::1')
            or url.username or url.password or url.path not in ('', '/') or url.query or url.fragment):
        raise ValueError('Используйте локальный адрес Ollama без пути и учётных данных')
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args):
            return None
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    with client.open(endpoint.rstrip('/')+'/api/tags', timeout=3) as response:
        raw = response.read(65537)
        if len(raw)>65536:
            raise ValueError('Слишком большой список моделей')
        return json.loads(raw)


def prepare_participants(plan, scenario, controllers, economy):
    """Resolve startup data only; the actual runner validates it after applying economy."""
    from research_lab import fields, integer
    if 'participants' not in plan:
        return None
    members=plan['participants']
    if not isinstance(members,list) or not 2<=len(members)<=16:
        raise ValueError('Нужны человек и 1–15 NPC')
    result=[]
    npcs=[]
    template=controllers['npcs'][0]
    for index, member in enumerate(members):
        fields(member,['id','cashMinor','inventoryCapacity','position','preferredInput','difficulty'],
               ['id','cashMinor','inventoryCapacity','position'])
        if not integer(member['id'],1,16) or member['id']!=index+1 or not integer(member['cashMinor'],0,2**53-1) or not integer(member['inventoryCapacity'],1,10**9):
            raise ValueError('Участники: последовательные ID с 1, точные деньги и положительная вместимость')
        fields(member['position'],['x','z'],['x','z'])
        if not all(integer(member['position'][k],0,scenario['mapWidth' if k=='x' else 'mapHeight']-1) for k in ('x','z')):
            raise ValueError('Старт вне карты')
        preference=member.get('preferredInput','wood' if index%2 else 'grain')
        if preference not in {n['itemId'] for n in scenario['resourceNodes']}:
            raise ValueError('Предпочтение должно соответствовать доступному сырью')
        difficulty=member.get('difficulty','Medium')
        if difficulty not in ('Easy','Medium','Hard'):
            raise ValueError('Неизвестная сложность')
        result.append(dict(id=index+1,cashMinor=member['cashMinor'],position=member['position'],
                           inventoryCapacity=member['inventoryCapacity'],publicPreferenceItem=preference if index else ''))
        if index:
            npc=copy.deepcopy(template)
            npc.update(actorId=index+1,preferredInput=preference,difficulty=difficulty)
            npc.pop('planning',None)
            npcs.append(npc)
    if len({(m['position']['x'],m['position']['z']) for m in result})!=len(result):
        raise ValueError('Стартовые клетки должны различаться')
    controllers.update(humanActorId=1,npcs=npcs)
    return dict(participants=result)


def binary_hashes():
    from research_lab import ROOT,digest
    folder=ROOT/'src/Research.Runner/bin/Release/net10.0'
    return {p.name:digest(p) for p in sorted(folder.glob('*.dll'))}


def compatible(lab,left,right,left_arm,right_arm):
    """Pair only equivalent inputs after removing explicitly declared factors."""
    from research_lab import read
    if left.get('binaries')!=right.get('binaries') or left['plan']['ticks']!=right['plan']['ticks']:
        return False
    if {j.get('modelWait',False) for j in left['jobs']}!={j.get('modelWait',False) for j in right['jobs']}:
        return False
    factors={f['path'] for s in (left,right) for f in s['plan']['factors']}
    def normalized(series,arm):
        folder=lab.workspace/series['id']/'inputs'
        scenario=read(folder/'scenario.json')
        economy=read(folder/arm/'economy.json')
        controllers=read(folder/arm/'controllers.json')
        setup=read(folder/arm/'setup.json') if (folder/arm/'setup.json').exists() else None
        for path in factors:
            if path in ('npc.difficulty','npc.negotiationsEnabled'):
                for npc in controllers['npcs']:
                    npc.pop(path.split('.')[1],None)
                    if path=='npc.difficulty':npc.pop('planning',None)
            else:
                parts=path.split('.');node=economy
                for part in parts[:-1]:node=node.get(part,{})
                node.pop(parts[-1],None)
        return scenario,economy,controllers,setup
    return normalized(left,left_arm)==normalized(right,right_arm)


def validate_recording(folder):
    summary=json.loads((folder/'summary.json').read_text(encoding='utf-8'))
    if not (folder/'world-index.jsonl').exists():
        raise ValueError('Не найдена запись мира')
    expected=0
    offset=0
    with (folder/'world-index.jsonl').open(encoding='utf-8') as index, (folder/'world-frames.jsonl').open('rb') as frames:
        for line in index:
            row=json.loads(line)
            if row['tick']!=expected or row['offset']!=offset or not 0<row['length']<4*1024*1024:
                raise ValueError('Неполная последовательность кадров')
            data=frames.read(row['length'])
            frame=json.loads(data)
            if len(data)!=row['length'] or frame['tick']!=expected or not data.endswith(b'\n'):
                raise ValueError('Неполный кадр мира')
            if expected==summary['tick'] and frame['stateHash']!=summary['stateHash']:
                raise ValueError('Последний кадр не совпал с итогом')
            expected+=1
            offset+=row['length']
        if frames.read(1) or expected!=summary['tick']+1:
            raise ValueError('Запись мира не дошла до конечного шага')


class Workspace:
    def __init__(self,lab):
        self.lab=lab

    def series(self,key):
        found=next((s for s in self.lab.series if s['id']==key),None)
        if found is None:
            raise ValueError('Неизвестный опыт')
        return found

    def folder(self,key):
        self.series(key)
        path=self.lab.workspace/key
        if path.is_symlink() or path.resolve().parent!=self.lab.workspace:
            raise ValueError('Недопустимая папка опыта')
        return path

    def file(self,key,relative):
        base=self.folder(key)
        path=base/relative
        if not path.resolve().is_relative_to(base) or path.is_symlink() or not path.is_file() or path.suffix not in ('.json','.jsonl','.csv','.log'):
            raise ValueError('Неизвестный файл опыта')
        return path

    def describe(self,key):
        base=self.folder(key)
        files=[]
        for path in sorted(base.rglob('*')):
            if path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(base):
                files.append(dict(path=path.relative_to(base).as_posix(),bytes=path.stat().st_size))
        return dict(files=files,bytes=sum(f['bytes'] for f in files),series=copy.deepcopy(self.series(key)))

    def manage(self,data):
        from research_lab import write
        action=data['action']
        with self.lab.lock:
            selected=data.get('selected',[data.get('id')])
            rows=[self.series(key) for key in selected]
            if action not in ('rename','archive','restore','delete','duplicate'):
                raise ValueError('Неизвестное действие')
            if action!='rename' and any(j['status']=='running' for s in rows for j in s['jobs']):
                raise ValueError('Сначала остановите активный опыт')
            if action=='rename':
                name=data['name'].strip()
                if not 1<=len(name)<=120:raise ValueError('Название: 1–120 символов')
                rows[0]['name']=name
            elif action in ('archive','restore'):
                for s in rows:s['archived']=action=='archive'
            elif action=='delete':
                # Resolve all targets before mutating. Only known workspace-owned series.
                paths=[self.folder(s['id']) for s in rows]
                for path in paths:
                    shutil.rmtree(path)
                self.lab.series=[s for s in self.lab.series if s not in rows]
            elif action=='duplicate':
                return dict(plan=copy.deepcopy(rows[0]['plan']))
            self.lab.save()
            return self.lab.snapshot()

    def control(self,key,action):
        from research_lab import write
        if action not in ('paused','running','cancelled'):
            raise ValueError('Неизвестная команда')
        with self.lab.lock:
            series=self.series(key)
            job=next((j for j in series['jobs'] if j['status']=='running'),None)
            if job is None:raise ValueError('Активного прогона нет')
            if action=='cancelled':
                self.lab.paused=True
            out=self.folder(key)/'runs'/job['id']/str(job['attempt'])
            write(out/'control.json',dict(action=action))
        return dict(action=action)

    def preview(self,key,relative,cursor=0):
        path=self.file(key,relative)
        cursor=max(0,int(cursor))
        with path.open('rb') as stream:
            stream.seek(cursor)
            raw=stream.read(64*1024)
            # Text pages end at a complete UTF-8 character.
            while raw:
                try:text=raw.decode('utf-8-sig' if cursor==0 else 'utf-8');break
                except UnicodeDecodeError as e:
                    if e.start<len(raw)-4:raise ValueError('Файл не UTF-8')
                    raw=raw[:-1]
            else:text=''
        return dict(text=text,cursor=cursor,nextCursor=cursor+len(raw),hasMore=cursor+len(raw)<path.stat().st_size,size=path.stat().st_size)

    def attempt(self,key,job_id,attempt):
        s=self.series(key)
        job=next((j for j in s['jobs'] if j['id']==job_id),None)
        if job is None or not 1<=int(attempt)<=job['attempt']:
            raise ValueError('Неизвестная попытка')
        return f"runs/{job_id}/{int(attempt)}"

    def world(self,key,job,attempt,tick):
        prefix=self.attempt(key,job,attempt)
        tick=int(tick)
        index=self.file(key,prefix+'/world-index.jsonl')
        with index.open(encoding='utf-8') as stream:
            row=next((json.loads(l) for l in stream if json.loads(l)['tick']==tick),None)
        if row is None:raise ValueError('Этот шаг ещё не записан')
        with self.file(key,prefix+'/world-frames.jsonl').open('rb') as stream:
            stream.seek(row['offset'])
            return json.loads(stream.read(row['length']))

    def chart(self,key,job,attempt):
        prefix=self.attempt(key,job,attempt)
        file=self.file(key,prefix+'/agents.csv')
        with file.open(encoding='utf-8') as stream:
            rows=list(csv.DictReader(stream))
        actors=sorted({r['actor_id'] for r in rows})
        budget=max(1,500//max(1,len(actors)))
        selected=[];stride=1
        for actor in actors:
            samples=[r for r in rows if r['actor_id']==actor]
            step=max(1,(len(samples)+budget-1)//budget);stride=max(stride,step)
            points=samples[::step]
            if samples and samples[-1] not in points:points.append(samples[-1])
            selected.extend(points)
        selected.sort(key=lambda r:(int(r['tick']),int(r['actor_id'])))
        return dict(rows=selected,sourceRows=len(rows),stride=stride)

    def journal(self,key,job,attempt,kind,cursor=0,actor='',search='',important='false'):
        names={'events':'live-events.jsonl','decisions':'live-decisions.jsonl','models':'model-transcripts.jsonl'}
        if kind not in names:raise ValueError('Неизвестный журнал')
        prefix=self.attempt(key,job,attempt)
        try:path=self.file(key,prefix+'/'+names[kind])
        except ValueError:return dict(rows=[],nextCursor=0,hasMore=False)
        rows=[]
        with path.open('rb') as stream:
            stream.seek(max(0,int(cursor)))
            for _ in range(1000):
                raw=stream.readline(2*1024*1024)
                if not raw:break
                if not raw.endswith(b'\n'):break
                row=json.loads(raw)
                actor_id=row.get('actorId',row.get('actor',{}))
                actor_id=actor_id.get('value','') if isinstance(actor_id,dict) else actor_id
                if actor and str(actor_id)!=actor:continue
                text=raw.decode('utf-8').lower()
                if search.lower() not in text:continue
                if important=='true' and not any(term in text for term in ('reject','fail','invalid','timeout','cancel','fallback')):continue
                rows.append(row)
                if len(rows)>=100:break
            position=stream.tell()
        return dict(rows=rows,nextCursor=position,hasMore=position<path.stat().st_size)

    def profiles(self):
        from research_lab import read
        file=self.lab.workspace/'profiles.json'
        return read(file) if file.exists() else []

    def save_profile(self,data):
        from research_lab import write
        if data['kind'] not in ('economy','model'):
            raise ValueError('Неизвестный тип профиля')
        name=str(data['name']).strip()
        if not 1<=len(name)<=120:raise ValueError('Название: 1–120 символов')
        values=self.profiles()
        key=uuid.uuid4().hex
        relative=f"profiles/{key}.json"
        write(self.lab.workspace/relative,data['value'])
        values.append(dict(id=key,name=name,kind=data['kind'],path=str(self.lab.workspace/relative)))
        write(self.lab.workspace/'profiles.json',values)
        return values

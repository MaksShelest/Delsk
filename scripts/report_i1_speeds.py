#!/usr/bin/env python3
"""Publish measured Core I1 journey times; refuses missing/conflicting rows.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
models={'running':'Персонаж','sputnik20':'Спутник-20','muravey70':'Муравей-70','eger70':'Егерь-70','vector200':'Вектор-200','trakt_d1':'Тракт-Д1'}
surfaces=['Вне дороги','Грунтовка','Дорога I','Магистраль II'];data={}
for line in a.input.read_text(encoding='utf-8').splitlines():
 d=json.loads(line)
 if d['kind'] not in models:continue
 key=(d['kind'],d['surface'],d['profile'])
 if d['distance']!=120 or d['ticks']<=0:raise ValueError('Invalid distance/time')
 if key in data and data[key]!=d:raise ValueError('Conflicting measurement: '+str(key))
 data[key]=d
expected={(kind,surface,profile) for kind in models for surface in range(4) for profile in range(3)}
if set(data)!=expected:raise ValueError('Missing/extra actual measurements: '+str(expected-set(data)))
if a.output.exists() and any(a.output.iterdir()):raise ValueError('Use a new empty output directory')
a.output.mkdir(parents=True,exist_ok=True)
rows=[];lines=['| Объект | Покрытие | Номинал, км/ч | Исходное время | H2 | Новое время | Реальное ускорение |','|---|---|---:|---:|---:|---:|---|']
for kind,name in models.items():
 for surface,label in enumerate(surfaces):
  old,h2,new=(data[(kind,surface,profile)] for profile in range(3))
  if kind!='running' and (old['roadUnits']!=h2['roadUnits'] or h2['roadUnits']!=new['roadUnits']):raise ValueError('Distance billing changed')
  ratio=old['ticks']/new['ticks'];relative=h2['ticks']/new['ticks']
  rows.append(dict(kind=kind,surface=surface,nominalKmh=new['nominal'],originalTicks=old['ticks'],h2Ticks=h2['ticks'],i1Ticks=new['ticks'],originalToI1=ratio,h2ToI1=relative,roadUnits=new.get('roadUnits',0)))
  lines.append(f"| {name} | {label} | {new['nominal']} | {old['ticks']} | {h2['ticks']} | {new['ticks']} | ×{ratio:.3f}; к H2 ×{relative:.3f} |")
payload=dict(author='Шелестов Максим Евгеньевич',baseHead='b96c59ca6e3392bacdd94cbd1b8e6a332c0be5a4',distanceCells=120,economicTickSeconds=1,measurementCount=len(data),inputSha256=hashlib.sha256(a.input.read_bytes()).hexdigest(),rows=rows,scope='Measured Core movement between120 adjacent free cells. Finishing tick is discrete. Prepared paid road construction, not natural economic progression.')
(a.output/'speeds.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(a.output/'table.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('Published72 measured journeys /24 comparison rows; distance bills equal')

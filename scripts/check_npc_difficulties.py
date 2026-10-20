#!/usr/bin/env python3
"""Paired actual worlds, not a prediction of which difficulty should win. Шелестов Максим Евгеньевич."""
import argparse,hashlib,json,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--ticks',type=int,default=600);p.add_argument('--seeds',default='42,43,44');a=p.parse_args()
if not 120<=a.ticks<=1200:p.error('Use 120..1200 bounded ticks')
root=Path(__file__).resolve().parents[1];out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
runner=['dotnet','src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];rows=[]
def check(name,timeout,command):
 subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,command)],cwd=root,check=True)
def read(path):return json.loads(path.read_text())
for seed in map(int,a.seeds.split(',')):
 world=None
 for difficulty in ['Easy','Medium','Hard']:
  config=read(root/'scenarios/baseline-controllers.json');config['finalTick']=a.ticks
  for npc in config['npcs']:
   npc['difficulty']=difficulty;npc['politicalBudgetMinor']=5000
   if difficulty=='Hard':npc['planning']={'maxCandidates':12,'depth':2,'horizonTicks':120,'maxEvaluations':32,'capitalOpportunityBasisPoints':1000}
  label=f'{seed}-{difficulty.lower()}';config_path=out/f'{label}.controllers.json';config_path.write_text(json.dumps(config))
  common=[*runner,'--script','baseline','--scenario','scenarios/presentation-map-one-v3.json','--economy','config/economy.json','--controllers',config_path,'--seed',seed,'--ticks',a.ticks,'--human-ballot','self']
  check(label,120,[*common,'--profile','true','--output',out/label])
  folder=out/label;scenario=(folder/'scenario.json').read_bytes()
  if world is None:world=scenario
  assert scenario==world,'Difficulty rewrote the initial world'
  for saved in read(folder/'controllers.json'):
   if saved['actor']['value']==1:continue
   state=json.loads(saved['stateJson']);assert saved['kind']=='utility-modular-v3' and state['settings']['difficulty']==difficulty
  state=read(folder/'final-state.json');city=read(folder/'public-economy.json');profile=read(folder/'profile.json');elections=read(folder/'elections.json')
  assert profile['checkpointHashMatches']
  rows.append({'seed':seed,'difficulty':difficulty,'worldSha256':hashlib.sha256(scenario).hexdigest(),
   'npcCapitalMinor':sum(x['capitalMinor'] for x in city['participants'] if x['actor']['value']!=1),
   'zeroCashNpcCount':sum(x['balanceMinor']==0 for x in state['accounts'] if x['kind']=='Participant' and x['id']['value'] in {p['accountId']['value'] for p in state['participants'] if p['id']['value']!=1}),
   'lateDeliveries':sum(c['status']=='Expired' for c in state['deliveryContracts']),
   'latePickups':sum(c['releasedQuantity']>0 for c in state['marketAllocations']),
   'npcEmployment':sum(g['headcount'] for g in state['employmentGroups'] if g['ownerId']['value']!=1),
   'firstVictory':elections['firstVictory'],'averageTickMs':profile['averageTickMs'],'p95TickMs':profile['p95TickMs']})
  check(label+'-balances',30,[sys.executable,'scripts/verify_results.py',folder]);check(label+'-public',30,[sys.executable,'scripts/verify_public_economy.py',folder])
  if seed==42 and difficulty=='Hard':
   save=out/'hard.save.json';cut=min(250,a.ticks//2)
   check('hard-save',120,[*common,'--checkpoint',save,'--checkpoint-at',cut,'--output',out/'hard-saved'])
   check('hard-load',120,[*runner,'--load',save,'--ticks',a.ticks,'--output',out/'hard-loaded'])
   check('hard-compare',30,[sys.executable,'scripts/verify_checkpoint_exports.py',folder,out/'hard-saved',folder,out/'hard-loaded'])
   check('hard-replay',120,[*runner,'--script','baseline','--scenario',folder/'scenario.json','--replay',folder/'commands.jsonl','--ticks',a.ticks,'--output',out/'hard-replayed'])
   check('hard-replay-compare',30,[sys.executable,'scripts/verify_checkpoint_exports.py','--world-only',folder,out/'hard-replayed'])
report={'status':'succeeded','ticks':a.ticks,'rows':rows,'scope':'Paired public worlds, one algorithmic implementation, no LLM; zero cash is a recovery metric, not formal bankruptcy. No universal superiority claim.'}
(out/'paired-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')

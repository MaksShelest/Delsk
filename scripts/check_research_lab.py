#!/usr/bin/env python3
"""Actual browser → common runner → CLI/checkpoint/replay equivalence.
Author: Шелестов Максим Евгеньевич.
Requires Playwright and Chromium only for this test, not the laboratory itself.
"""
import argparse,copy,json,subprocess,threading
from pathlib import Path
from playwright.sync_api import sync_playwright
from research_lab import DEFAULT,Lab,ROOT,write
from research_lab_server import make_server
from run_research_series import run_checked
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
lab=Lab(out/'lab');server=make_server(lab,0);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
plan=copy.deepcopy(DEFAULT);plan.update(ticks=120,seeds=[42])
def check(name,cmd):
 code,_=run_checked(name,90,out/'checks',list(map(str,cmd)))
 if code:raise RuntimeError(name+' failed')
try:
 with sync_playwright() as pw:
  browser=pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
  page=browser.new_page(viewport=dict(width=1380,height=1000))
  page.goto(f'http://127.0.0.1:{server.server_port}/')
  page.wait_for_function("() => document.getElementById('plan').value.length>0")
  page.locator('#ticks').fill('120');page.locator('#seeds').fill('42')
  bad=copy.deepcopy(plan);bad['factors']=[dict(path='npc.maximumWageMinor',values=[1])]
  page.locator('#plan').fill(json.dumps(bad));page.locator('#queue').click()
  page.wait_for_function("() => document.getElementById('error').textContent.includes('Runner rejected')")
  assert not lab.series,'Invalid combinations entered queue'
  page.locator('#plan').fill(json.dumps(plan));page.locator('#queue').click()
  page.wait_for_function("() => document.querySelectorAll('input[name=series]').length===1",timeout=60000)
  assert all(j['status']=='queued' for j in lab.series[0]['jobs'])
  page.locator('[data-action=start]').click()
  page.wait_for_function("() => document.getElementById('series').textContent.includes('running')",timeout=15000)
  page.locator('[data-action=pause]').click()
  page.wait_for_function("() => document.getElementById('series').textContent.includes('succeeded')",timeout=60000)
  assert [j['status'] for j in lab.series[0]['jobs']]==['succeeded','queued']
  page.reload();page.wait_for_selector('[data-action=resume]');page.locator('[data-action=resume]').click()
  page.wait_for_function("() => document.getElementById('series').textContent.includes('2 / 2')",timeout=60000)
  page.locator('input[name=series]').check();page.locator('#compare').click()
  page.wait_for_function("() => document.getElementById('comparison').textContent.includes('capitalMinor')")
  page.locator('td details summary').first.click()
  with page.expect_download() as download:page.get_by_text('metrics.csv',exact=True).first.click()
  download.value.save_as(out/'downloaded-metrics.csv')
  denied=page.evaluate("async()=>({noToken:(await fetch('/api/state')).status,foreignOrigin:(await fetch('/api/state',{headers:{'X-Lab-Token':'wrong'}})).status})")
  assert denied==dict(noToken=403,foreignOrigin=403)
  page.screenshot(path=str(out/'laboratory.png'),full_page=True)
  browser.close()
 s=lab.series[0];job=s['jobs'][0];base=out/'lab'/job['result'];cmd=job['command'].copy()
 cmd[cmd.index('--output')+1]=str(out/'cli');check('cli',cmd)
 check('ui-cli',[ 'python3','scripts/verify_checkpoint_exports.py',base,out/'cli'])
 cmd[cmd.index('--output')+1]=str(out/'saved');cmd+=['--checkpoint',str(out/'mid.save.json'),'--checkpoint-at','59'];check('save',cmd)
 runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
 check('load',[*runner,'--load',out/'mid.save.json','--ticks','120','--output',out/'loaded'])
 check('save-load',[ 'python3','scripts/verify_checkpoint_exports.py',base,out/'saved',base,out/'loaded'])
 check('replay',[*runner,'--script','baseline','--scenario',base/'scenario.json','--replay',base/'commands.jsonl','--ticks','120','--output',out/'replay'])
 check('replay-hash',['python3','scripts/verify_checkpoint_exports.py','--world-only',base,out/'replay'])
 restored=Lab(out/'lab');assert all(j['status']=='succeeded' for j in restored.series[0]['jobs']);assert restored.paused
 write(out/'gate.json',dict(status='succeeded',uiInvalidRejected=True,pauseBetweenCells=True,downloadCsv=True,
  restartKeepsCompleted=True,access=denied,uiCliHash=job['metrics']['stateHash'],saveLoadReplay=True,jobs=s['jobs']))
 print('Actual laboratory browser/CLI/save/load/replay gate passed')
finally:
 lab.pause();server.shutdown();server.server_close();thread.join(3)

#!/usr/bin/env python3
"""Actual browser → common runner → CLI/checkpoint/replay equivalence.
Author: Шелестов Максим Евгеньевич.
Requires Playwright and Chromium only for this test, not the laboratory itself.
"""
import argparse,copy,json,subprocess,threading,zipfile
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
  assert page.locator('#preset').input_value()=='quick'
  assert page.locator('#estimate').inner_text().startswith('Запланировано прогонов: 1')
  page.locator('#advanced summary').first.click()
  page.get_by_text('Изменяемые параметры — добавить свой фактор',exact=True).click()
  page.locator('#factor').select_option('world.population');page.locator('#factor-values').fill('100, 200');page.locator('#add').click()
  assert json.loads(page.locator('#plan').input_value())['factors']==[dict(path='world.population',values=[100,200])]
  assert page.locator('#estimate').inner_text().startswith('Запланировано прогонов: 2')
  page.get_by_role('button',name='Убрать Население',exact=True).click()
  page.locator('#map').select_option('scenarios/presentation-map-three-v2.json')
  assert json.loads(page.locator('#plan').input_value())['scenario']=='scenarios/presentation-map-three-v2.json'
  page.locator('#map').select_option('scenarios/presentation-map-one-v3.json')
  page.locator('#mode').select_option('hybrid');page.locator('#run').click()
  page.wait_for_function("() => document.getElementById('error').textContent.includes('конфигурации реальной модели')")
  assert not lab.series
  page.locator('#mode').select_option('algorithmic')
  page.locator('#ticks').fill('120');page.locator('#seeds').fill('42,')
  page.locator('#queue').click();page.wait_for_function("() => document.getElementById('error').textContent.includes('каждый seed')")
  assert not lab.series
  page.locator('#seeds').fill('42')
  bad=copy.deepcopy(plan);bad['factors']=[dict(path='npc.maximumWageMinor',values=[1])]
  page.locator('#plan').fill(json.dumps(bad));page.locator('#queue').click()
  page.wait_for_function("() => document.getElementById('error').textContent.includes('Runner rejected')")
  assert not lab.series,'Invalid combinations entered queue'
  page.locator('#plan').fill(json.dumps(plan));page.locator('#queue').click()
  page.wait_for_function("() => document.querySelectorAll('input[name=series]').length===1",timeout=60000)
  assert all(j['status']=='queued' for j in lab.series[0]['jobs'])
  page.locator('[data-action=start]').click()
  page.wait_for_selector('[data-status=running]',timeout=15000)
  page.locator('[data-action=pause]').click()
  page.wait_for_selector('[data-status=succeeded]',timeout=60000)
  assert [j['status'] for j in lab.series[0]['jobs']]==['succeeded','queued']
  page.reload();page.wait_for_selector('[data-action=resume]');page.locator('[data-action=resume]').click()
  page.wait_for_function("() => document.getElementById('series').textContent.includes('2 / 2')",timeout=60000)
  page.locator('input[name=series]').check();page.locator('#compare').click()
  page.wait_for_function("() => document.getElementById('comparison').textContent.includes('Оценка капитала')")
  page.locator('td details summary').first.click()
  page.wait_for_timeout(2300)
  assert page.locator('td details').first.get_attribute('open') is not None
  with page.expect_download() as download:page.get_by_text('metrics.csv',exact=True).first.click()
  download.value.save_as(out/'downloaded-metrics.csv')
  # Beginner path: choose a preset and run without touching JSON or Start Queue.
  page.locator('#preset').select_option('baseline');page.locator('#preset').select_option('quick')
  page.locator('#run').click()
  page.wait_for_function("() => document.querySelectorAll('input[name=series]').length===2",timeout=60000)
  page.wait_for_function("() => document.querySelectorAll('[data-status=succeeded]').length===3",timeout=60000)
  page.wait_for_function("() => document.getElementById('comparison').textContent.includes('quick-check')")
  with page.expect_download() as download:page.get_by_text('Скачать серию ZIP',exact=True).last.click()
  archive_path=out/'downloaded-series.zip';download.value.save_as(archive_path)
  with zipfile.ZipFile(archive_path) as z:
   assert z.testzip() is None
   key=lab.series[1]['id']
   assert json.loads(z.read(key+'/plan.json'))['ticks']==120
   assert any(n.endswith('/manifest.json') for n in z.namelist())
  page.set_viewport_size(dict(width=640,height=900))
  assert page.evaluate('document.documentElement.scrollWidth<=window.innerWidth')
  denied=page.evaluate("async()=>({noToken:(await fetch('/api/state')).status,invalidToken:(await fetch('/api/state',{headers:{'X-Lab-Token':'wrong'}})).status})")
  auth=page.evaluate('token')
  denied['foreignOrigin']=page.request.get(f'http://127.0.0.1:{server.server_port}/api/state',headers={'X-Lab-Token':auth,'Origin':'https://outside.invalid'}).status
  denied['foreignHost']=page.request.get(f'http://127.0.0.1:{server.server_port}/api/state',headers={'X-Lab-Token':auth,'Host':'outside.invalid'}).status
  assert denied==dict(noToken=403,invalidToken=403,foreignOrigin=403,foreignHost=403)
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
 restored=Lab(out/'lab');assert all(j['status']=='succeeded' for s in restored.series for j in s['jobs']);assert restored.paused
 write(out/'gate.json',dict(status='succeeded',uiInvalidRejected=True,pauseBetweenCells=True,downloadCsv=True,
  restartKeepsCompleted=True,access=denied,uiCliHash=job['metrics']['stateHash'],saveLoadReplay=True,
  presetOneClick=True,factorForm=True,mapSelection=True,missingModelConfigRejected=True,
  filePanelPreservedAcrossRefresh=True,seriesZip=True,narrowViewport=True,jobs=s['jobs']))
 print('Actual laboratory browser/CLI/save/load/replay gate passed')
finally:
 lab.pause();server.shutdown();server.server_close();thread.join(3)

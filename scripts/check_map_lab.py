#!/usr/bin/env python3
"""Live browser chooses each paired map/profile and executes actual research jobs.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,threading,time
from pathlib import Path
from playwright.sync_api import sync_playwright
from research_lab import Lab,read
from research_lab_server import make_server
from research_workspace import validate_recording
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--chromium',default='/usr/bin/chromium');a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
lab=Lab(a.output/'lab');server=make_server(lab,0);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start();errors=[];results=[]
try:
 with sync_playwright() as pw:
  browser=pw.chromium.launch(executable_path=a.chromium,headless=True,args=['--no-sandbox','--disable-dev-shm-usage']);page=browser.new_page(viewport=dict(width=1440,height=1000));page.on('pageerror',lambda e:errors.append(str(e)))
  page.goto(f'http://127.0.0.1:{server.server_port}/');page.wait_for_function("() => document.getElementById('plan').value.length>0")
  for key,source in [('small-river','small_river'),('large-districts','large_districts')]:
   page.locator('[data-page=new]').first.click();page.locator('#map').select_option('scenarios/vnext/'+key+'.json')
   page.wait_for_function('(key) => {let p=JSON.parse(document.getElementById("plan").value);return p.scenario.endsWith(key+".json")&&p.economyData.id==="vnext-c1-storage-1"}',arg=key)
   plan=json.loads(page.locator('#plan').input_value());assert plan['economy']=='config/vnext/economy.c1-storage.json'
   page.locator('#experiment-name').fill('map-'+key);page.locator('#ticks').fill('30');page.locator('#seeds').fill('42');page.locator('#run').click();page.wait_for_selector('#page-detail:not([hidden])',timeout=60000)
   series=lab.series[-1];job=series['jobs'][0];end=time.monotonic()+120
   while job['status'] in ('queued','running'):
    if time.monotonic()>end:raise AssertionError('Research map job timeout')
    time.sleep(.1)
   assert job['status']=='succeeded',job
   folder=lab.workspace/series['id']/'runs'/job['id']/'1';manifest=read(folder/'manifest.json');assert manifest['mapId']==source and manifest['economyProfileId']=='vnext-c1-storage-1';assert manifest['mapDesign']['sourceId']==source
   validate_recording(folder);page.screenshot(path=str(a.output/(key+'.png')),full_page=True);results.append(dict(map=source,tick=read(folder/'summary.json')['tick'],status='succeeded',recording='validated',sourceSha256=manifest['mapDesign']['sourceSha256']))
  assert not errors,errors;browser.close()
 (a.output/'gate.json').write_text(json.dumps(dict(status='succeeded',runs=results,scope='Actual Chromium controls, lab queue, C# validators and runner; explicit research composition, no LLM'),indent=2)+'\n')
finally:
 server.shutdown();server.server_close();thread.join(5)
print('Both paired maps launched and recorded through the live laboratory')

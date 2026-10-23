#!/usr/bin/env python3
"""Real browser/runner/recording/CLI/load/replay acceptance. No second economy.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import threading
import time
import zipfile
from playwright.sync_api import sync_playwright
from research_lab import Lab,ROOT,read,write
from research_lab_server import make_server
from research_workspace import validate_recording
from run_research_series import run_checked

p=argparse.ArgumentParser()
p.add_argument('--output',type=Path,required=True)
p.add_argument('--chromium',default=os.environ.get('DELSK_CHROMIUM_PATH','/usr/bin/chromium'))
a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
lab=Lab(out/'lab');server=make_server(lab,0);host=threading.Thread(target=server.serve_forever,daemon=True);host.start()
errors=[]
def check(name,command):
    code,_=run_checked(name,120,out/'checks',list(map(str,command)))
    if code:raise RuntimeError(name+' failed')
def wait(predicate,seconds=60):
    start=time.monotonic()
    while not predicate():
        if time.monotonic()-start>seconds:raise AssertionError('Timed out')
        time.sleep(.05)
try:
    with sync_playwright() as pw:
        browser=pw.chromium.launch(executable_path=a.chromium,headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
        page=browser.new_page(viewport=dict(width=1440,height=1000))
        page.on('pageerror',lambda e:errors.append(str(e)))
        page.goto(f'http://127.0.0.1:{server.server_port}/')
        page.wait_for_function("() => document.getElementById('plan').value.length>0")
        page.locator('[data-page=new]').first.click()
        page.locator('#map').select_option('scenarios/city-corridor-v1.json')
        page.wait_for_function("() => JSON.parse(document.getElementById('plan').value).scenario==='scenarios/city-corridor-v1.json'")
        assert page.locator('#map option').count()==5
        page.locator('#setup-tabs [data-tab=members]').click()
        page.locator('#npc-count').fill('2');page.locator('#npc-count').press('Tab')
        page.locator('[data-member="0"][data-key=cashMinor]').fill('37.25')
        page.locator('[data-member="0"][data-key=cashMinor]').press('Tab')
        page.locator('#setup-tabs [data-tab=economy]').click()
        page.locator('[data-economy="world.population"]').fill('230')
        page.locator('[data-economy="world.population"]').press('Tab')
        page.locator('#setup-tabs [data-tab=basic]').click()
        page.locator('#ticks').fill('600');page.locator('#seeds').fill('42')
        page.locator('#run').click()
        page.wait_for_selector('#page-detail:not([hidden])',timeout=60000)
        page.locator('[data-control=paused]').click()
        page.wait_for_function("() => document.getElementById('live-progress').textContent.includes('Пауза')",timeout=20000)
        first=lab.series[0];key=first['id'];job=first['jobs'][0]
        attempt=lab.workspace/key/'runs'/job['id']/'1'
        before=read(attempt/'progress.json')['tick'];page.wait_for_timeout(400)
        assert read(attempt/'progress.json')['tick']==before
        page.locator('[data-control=running]').click()
        wait(lambda:job['status']!='running',90)
        assert job['status']=='succeeded',job
        validate_recording(attempt)
        scenario=read(attempt/'scenario.json')
        assert len(scenario['participants'])==3 and scenario['participants'][0]['cashMinor']==3725 and scenario['population']==230
        page.wait_for_function("() => document.getElementById('runs').textContent.includes('Готово')",timeout=10000)
        page.locator('#detail-tabs [data-tab=world]').click()
        page.locator('#world-tick').fill('30');page.locator('#open-tick').click()
        page.wait_for_function("() => document.getElementById('frame-hash').textContent.length>5")
        assert '37,25' in page.locator('#world-actors').inner_text()
        page.screenshot(path=str(out/'world.png'),full_page=True)
        page.locator('#detail-tabs [data-tab=journal]').click()
        page.wait_for_function("() => document.getElementById('journal').textContent.length>0")
        page.locator('#detail-tabs [data-tab=analysis]').click()
        page.wait_for_function("() => document.getElementById('chart-note').textContent.includes('agents.csv')")
        page.locator('#detail-tabs [data-tab=files]').click()
        page.get_by_role('button',name='runs/'+job['id']+'/1/summary.json',exact=False).click()
        page.wait_for_function("() => document.getElementById('file-preview').textContent.includes('succeeded')")
        # Refreshing status must preserve the open file.
        text=page.locator('#file-preview').inner_text();page.wait_for_timeout(1200)
        assert page.locator('#file-preview').inner_text()==text
        with page.expect_download() as download:page.locator('#zip').click()
        download.value.save_as(out/'series.zip')
        with zipfile.ZipFile(out/'series.zip') as z:
            assert z.testzip() is None and key+'/runs/'+job['id']+'/1/world-index.jsonl' in z.namelist()
        page.on('dialog',lambda d:d.accept('Проверенный опыт') if d.type=='prompt' else d.accept())
        page.locator('#rename').click()
        page.wait_for_function("() => document.getElementById('detail-name').textContent==='Проверенный опыт'")
        page.locator('#archive').click();wait(lambda:first.get('archived') is True)
        page.locator('#show-archive').check()
        page.get_by_role('button',name='Проверенный опыт',exact=True).click()
        page.locator('#archive').click();wait(lambda:first.get('archived') is False)
        page.locator('#show-archive').uncheck()
        page.get_by_role('button',name='Проверенный опыт',exact=True).click()
        page.locator('#duplicate').click()
        page.wait_for_selector('#page-new:not([hidden])')
        assert json.loads(page.locator('#plan').input_value())['participants'][0]['cashMinor']==3725
        page.locator('#ticks').fill('1200');page.locator('#experiment-name').fill('cancel-check')
        page.locator('#run').click();page.wait_for_selector('#page-detail:not([hidden])',timeout=60000)
        page.locator('[data-control=cancelled]').click()
        second=lab.series[1];wait(lambda:second['jobs'][0]['status']=='cancelled',30)
        cancelled=lab.workspace/second['jobs'][0]['result']
        assert read(cancelled/'summary.json')['status']=='cancelled'
        assert not lab.compare([second['id']])['groups'][0]['complete']
        page.locator('#delete').click();wait(lambda:len(lab.series)==1)
        assert not (lab.workspace/second['id']).exists()
        page.locator('nav [data-page=models]').click()
        page.locator('#model-tag').fill('fixture-model')
        page.locator('#save-model').click()
        page.wait_for_function("() => document.getElementById('model-profiles').textContent.includes('fixture-model')")
        page.locator('nav [data-page=new]').click()
        page.locator('#setup-tabs [data-tab=members]').click()
        assert page.locator('[data-member-model="2"] option').count()==2
        page.locator('nav [data-page=catalog]').click()
        page.set_viewport_size(dict(width=640,height=900))
        assert page.evaluate('document.documentElement.scrollWidth<=window.innerWidth')
        page.screenshot(path=str(out/'narrow.png'),full_page=True)
        page.set_viewport_size(dict(width=1440,height=1000))
        page.screenshot(path=str(out/'catalog.png'),full_page=True)
        auth=page.evaluate('token')
        base=f'http://127.0.0.1:{server.server_port}'
        assert page.request.get(base+'/api/state').status==403
        assert page.request.get(base+'/api/state',headers={'X-Lab-Token':auth,'Origin':'https://foreign.invalid'}).status==403
        assert page.request.get(base+'/api/state',headers={'X-Lab-Token':auth,'Host':'foreign.invalid'}).status==403
        assert page.request.get(base+'/api/preview?id='+key+'&path=../../queue.json',headers={'X-Lab-Token':auth}).status==400
        assert not errors,errors
        browser.close()
    # Same inputs through direct CLI, with and without observation; restore and recorded replay.
    cmd=job['command'].copy()
    cmd[cmd.index('--output')+1]=str(out/'cli');check('workspace-cli',cmd)
    plain=cmd.copy();plain[plain.index('--research')+1]='false';plain[plain.index('--output')+1]=str(out/'plain')
    check('workspace-plain',plain)
    check('workspace-observer-equality',['python3','scripts/verify_checkpoint_exports.py',attempt,out/'cli',attempt,out/'plain'])
    saved=plain.copy();saved[saved.index('--output')+1]=str(out/'saved')
    saved+=['--checkpoint',str(out/'mid.save.json'),'--checkpoint-at','59']
    check('workspace-save',saved)
    runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
    check('workspace-load',[*runner,'--load',out/'mid.save.json','--ticks','600','--output',out/'loaded'])
    check('workspace-save-load',['python3','scripts/verify_checkpoint_exports.py',attempt,out/'saved',attempt,out/'loaded'])
    check('workspace-replay',[*runner,'--script','baseline','--scenario',attempt/'scenario.json','--replay',attempt/'commands.jsonl','--ticks','600','--output',out/'replay'])
    check('workspace-replay-equality',['python3','scripts/verify_checkpoint_exports.py','--world-only',attempt,out/'replay'])
    restored=Lab(lab.workspace);assert restored.series[0]['jobs'][0]['status']=='succeeded' and restored.paused
    write(out/'gate.json',dict(status='succeeded',browserErrors=errors,recordedFrames=601,
        pauseResumeCancel=True,archiveRestoreDelete=True,independentSettings=True,
        inlineFiles=True,world=True,chart=True,modelProfiles=True,zip=True,narrow640=True,
        uiCliObserverLoadReplay=True,accessGuards=True,stateHash=job['metrics']['stateHash']))
    print('Research workspace browser/runner/load/replay acceptance passed')
finally:
    lab.pause()
    if lab.thread:lab.thread.join(180)
    server.shutdown();server.server_close();host.join(5)

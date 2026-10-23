#!/usr/bin/env python3
"""Final presentation/validation smoke over an already completed real workspace.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
from pathlib import Path
import threading
from playwright.sync_api import sync_playwright
from research_lab import Lab,write
from research_lab_server import make_server
p=argparse.ArgumentParser();p.add_argument('--workspace',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--chromium',required=True);a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
lab=Lab(a.workspace);before=len(lab.series);assert before
server=make_server(lab,0);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
errors=[]
try:
    with sync_playwright() as pw:
        browser=pw.chromium.launch(executable_path=a.chromium,headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
        page=browser.new_page(viewport=dict(width=1440,height=1000))
        page.on('pageerror',lambda e:errors.append(str(e)))
        page.goto(f'http://127.0.0.1:{server.server_port}/')
        page.wait_for_function("()=>document.getElementById('plan').value.length>0")
        rejected=page.evaluate("""()=>{
            const result=[];
            for(const text of ['{"id":"a","id":"b"}','{"nested":{"key":1,"key":2}}','{"seed":18446744073709551615}']){
                try{parsePlan(text);result.push(false);}catch(e){result.push(true);}
            }
            return result;
        }""")
        assert rejected==[True,True,True]
        page.locator('input[name=series]').first.check()
        page.locator('#compare').click()
        page.wait_for_function("()=>document.getElementById('comparison').textContent.includes('Оценка капитала')")
        assert 'МБ' in page.locator('#catalog').inner_text()
        page.screenshot(path=str(out/'catalog.png'),full_page=True)
        page.locator('nav [data-page=new]').click()
        page.locator('#mode').select_option('hybrid');page.locator('#run').click()
        page.wait_for_function("()=>document.getElementById('error').textContent.includes('Выберите профиль')")
        page.locator('#mode').select_option('algorithmic')
        page.locator('#seeds').fill('42,');page.locator('#run').click()
        page.wait_for_function("()=>document.getElementById('error').textContent.includes('каждый seed')")
        page.locator('#seeds').fill('42')
        page.locator('#setup-tabs [data-tab=economy]').click()
        page.locator('[data-economy="npc.maximumWageMinor"]').fill('1')
        page.locator('[data-economy="npc.maximumWageMinor"]').press('Tab')
        page.locator('#run').click()
        page.wait_for_function("()=>document.getElementById('error').textContent.includes('Runner rejected')",timeout=60000)
        assert len(lab.series)==before
        page.locator('[data-economy="npc.maximumWageMinor"]').fill('50')
        page.locator('[data-economy="npc.maximumWageMinor"]').press('Tab')
        page.locator('#error').evaluate('(e)=>e.hidden=true')
        page.locator('#setup-tabs [data-tab=basic]').click()
        page.screenshot(path=str(out/'new-experiment.png'),full_page=True)
        page.locator('nav [data-page=catalog]').click()
        page.locator('[data-open]').first.click()
        page.locator('#detail-tabs [data-tab=world]').click()
        page.locator('#world-tick').fill('600');page.locator('#open-tick').click()
        page.wait_for_function("()=>document.getElementById('frame-hash').textContent.length>0")
        page.screenshot(path=str(out/'world.png'),full_page=True)
        page.set_viewport_size(dict(width=640,height=900))
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
        page.screenshot(path=str(out/'narrow.png'),full_page=True)
        assert not errors,errors
        browser.close()
    write(out/'gate.json',dict(status='succeeded',comparisonUi=True,diskSize=True,missingModelRejected=True,
        invalidSeedRejected=True,realCoreValidationRejected=True,noInvalidSeriesPublished=True,
        finalFrameVisible=True,narrow640=True,duplicateAndUnsafeJsonRejected=True,browserErrors=errors))
    print('Final workspace views/validation gate passed')
finally:
    server.shutdown();server.server_close();thread.join(5)

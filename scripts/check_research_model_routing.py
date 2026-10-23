#!/usr/bin/env python3
"""HTTP protocol fixture through real runner, not a real neural model.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
import json
from pathlib import Path
import threading
import time
from research_lab import ROOT,read,write
from research_workspace import validate_recording
from run_research_series import run_checked

p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
requests=[];active=0;maximum=0;guard=threading.Lock()
class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    def do_GET(self):
        body=json.dumps(dict(models=[dict(name='fixture-a'),dict(name='fixture-b')])).encode()
        self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
    def do_POST(self):
        global active,maximum
        body=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        fields=body['format']['properties']
        actor=fields['actorId']['enum'][0]
        assert body['model']=={2:'fixture-a',4:'fixture-b'}[actor]
        assert body['messages'][0]['content']
        with guard:
            active+=1;maximum=max(maximum,active);requests.append(body)
        try:
            time.sleep(.3)
            reply={key:fields[key]['enum'][0] for key in ('decisionId','actorId','observationVersion','sessionEpoch')}
            reply['optionId']=next((s for s in fields['optionId']['enum'] if s!='hold'),'hold')
            reply['reasonSummary']='HTTP protocol fixture, not a real model'
            payload=json.dumps(dict(done=True,message=dict(role='assistant',content=json.dumps(reply)),prompt_eval_count=100,eval_count=30)).encode()
            self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(payload)));self.end_headers();self.wfile.write(payload)
        finally:
            with guard:active-=1
server=ThreadingHTTPServer(('127.0.0.1',0),Handler);host=threading.Thread(target=server.serve_forever,daemon=True);host.start()
endpoint=f'http://127.0.0.1:{server.server_port}/'
config=dict(policy=dict(minimumIntervalTicks=30,applyDelayTicks=2,decisionTtlTicks=2,budgetPeriodTicks=120,maximumRequestsPerPeriod=2),
            runtime=dict(queueCapacity=3,timeoutMilliseconds=3000),
            bindings={'2':dict(endpoint=endpoint,model='fixture-a'),'3':None,'4':dict(endpoint=endpoint,model='fixture-b')})
write(out/'models.json',config)
runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
def check(name,command):
    code,_=run_checked(name,90,out/'checks',list(map(str,command)))
    if code:raise RuntimeError(name+' failed')
try:
    command=[*runner,'--scenario','scenarios/city-corridor-v1.json','--economy','config/economy.json',
        '--controllers','scenarios/baseline-controllers.json','--script','hybrid','--model-mode','ollama',
        '--model-config',out/'models.json','--ticks','60','--human-ballot','self','--model-wait','true',
        '--research','true','--output',out/'continuous','--checkpoint',out/'mid.save.json','--checkpoint-at','30']
    failures=[]
    def execute():
        try:check('routing-continuous',command)
        except Exception as e:failures.append(str(e))
    worker=threading.Thread(target=execute);worker.start()
    waiting=[];deadline=time.monotonic()+90
    while worker.is_alive() and time.monotonic()<deadline:
        file=out/'continuous/progress.json'
        if file.exists():
            try:
                progress=read(file)
                if progress['status']=='waiting-model':waiting.append(progress)
            except (OSError,ValueError):pass
        time.sleep(.04)
    worker.join(1);assert not worker.is_alive() and not failures,failures
    assert requests and maximum==1
    assert {b['model'] for b in requests}=={'fixture-a','fixture-b'}
    assert waiting and any(a['tick']==b['tick'] and a['heartbeatAt']!=b['heartbeatAt'] for a,b in zip(waiting,waiting[1:]))
    transcripts=[json.loads(l) for l in (out/'continuous/model-transcripts.jsonl').read_text().splitlines()]
    opened=[t for t in transcripts if t['status']=='Requested']
    assert len(opened)==len(requests)
    for t in opened:
        prompt=t['request']['prompt']
        assert hashlib.sha256(prompt.encode()).hexdigest()==t['request']['promptHash']
        assert t['actorId'] in (2,4)
    validate_recording(out/'continuous')
    check('routing-mixed-load',[*runner,'--load',out/'mid.save.json','--ticks','60','--model-mode','ollama',
        '--model-config',out/'models.json','--model-wait','true','--output',out/'loaded'])
    check('routing-world-continuation',['python3','scripts/verify_checkpoint_exports.py','--world-only',out/'continuous',out/'loaded'])
    count=len(requests)
    check('routing-replay',[*runner,'--scenario',out/'continuous/scenario.json','--script','baseline',
        '--replay',out/'continuous/commands.jsonl','--ticks','60','--output',out/'replay'])
    assert len(requests)==count
    check('routing-replay-equality',['python3','scripts/verify_checkpoint_exports.py','--world-only',out/'continuous',out/'replay'])
    manifest=read(out/'continuous/manifest.json')
    assert manifest['timingPolicy']=='ResearchWaitAtDecision-v1'
    controllers=read(out/'continuous/controllers.json')
    assert next(c for c in controllers if c['actor']['value']==3)['kind']!='hybrid-plan-v1'
    write(out/'gate.json',dict(status='succeeded',fixture='HTTP protocol fixture; no real neural inference',
        singlePhysicalSlot=True,models=['fixture-a','fixture-b'],algorithmActor=3,recordedPromptReply=True,
        waitKeepsTick=True,mixedBindingsLoad=True,worldContinuation=True,replayWithoutInference=True,requests=len(opened)))
    print('Research model routing HTTP protocol fixture passed')
finally:
    server.shutdown();server.server_close();host.join(5)

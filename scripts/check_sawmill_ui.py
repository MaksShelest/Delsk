#!/usr/bin/env python3
"""Actual Godot desktop/compact interaction probe; build/import client first. Шелестов Максим Евгеньевич."""
import argparse,base64,gzip,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--godot',required=True);p.add_argument('--xvfb',default='Xvfb');p.add_argument('--output',type=Path,required=True);p.add_argument('--display',type=int,default=95);p.add_argument('--mode',choices=['sawmill','model-errors'],default='sawmill');a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
def check(name,timeout,command):
 subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,command)],cwd=root,check=True)
scenario=json.loads((root/'scenarios/land-and-content.json').read_text());scenario['id']='ui-sawmill-v1';scenario['participants'][0]['position']={'x':4,'z':0}
(out/'scenario.json').write_text(json.dumps(scenario));config=json.loads((root/'scenarios/baseline-controllers.json').read_text());config['npcs']=[dict(config['npcs'][0],actorId=2)];config['finalTick']=1
(out/'controllers.json').write_text(json.dumps(config))
save=out/'ui.save.json'
check('fixture',30,['dotnet','src/Research.Runner/bin/Release/net10.0/Research.Runner.dll','--scenario',out/'scenario.json','--script','baseline','--controllers',out/'controllers.json','--ticks',0,'--checkpoint',save,'--output',out/'fixture'])
env=os.environ.copy();env.update(DISPLAY=f'127.0.0.1:{a.display}.0',LIBGL_ALWAYS_SOFTWARE='1',XDG_DATA_HOME=str(out/'data'),XDG_CONFIG_HOME=str(out/'config'),XDG_CACHE_HOME=str(out/'cache'))
userdata=Path(env['XDG_DATA_HOME'])/'godot/app_userdata/Delsk';userdata.mkdir(parents=True,exist_ok=True)
destination=userdata/'play-economy-production-recipes-1.json'
http=None;requests=0
if a.mode=='sawmill':shutil.copyfile(save,destination)
else:
 destination.unlink(missing_ok=True)
 from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
 import threading
 class ErrorModel(BaseHTTPRequestHandler):
  def log_message(self,*args):pass
  def do_GET(self):
   body=b'{"models":[{"name":"qwen3.5:4b"}]}'
   self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
  def do_POST(self):
   global requests
   self.rfile.read(int(self.headers['Content-Length']));requests+=1;self.send_response(503);self.send_header('Content-Length','0');self.end_headers()
 http=ThreadingHTTPServer(('127.0.0.1',11434),ErrorModel);threading.Thread(target=http.serve_forever,daemon=True).start()
(userdata/'display.cfg').write_text('[display]\nfullscreen=false\n')
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen([a.xvfb,f':{a.display}','-screen','0','1440x1024x24','-nolisten','unix','-listen','tcp','-ac'],env=env,stdout=log,stderr=log)
 try:
  for _ in range(50):
   try:
    with socket.create_connection(('127.0.0.1',6000+a.display),timeout=.1):break
   except OSError:time.sleep(.1)
  subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name','window','--timeout','70','--log-directory',str(out/'checks'),'--',a.godot,'--path',str(root/'src/Client.Godot'),'--script',str(root/('tests/Client.Probes/'+('sawmill' if a.mode=='sawmill' else 'model-errors')+'.gd')),'--rendering-method','gl_compatibility','--audio-driver','Dummy','--','--windowed','--output',str(out)],cwd=root,env=env,check=True)
 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
  if http:http.shutdown();http.server_close()
assert not json.loads((out/'window.json').read_text())['failures']
if a.mode=='model-errors':
 state=json.loads(gzip.decompress(base64.b64decode(json.loads(destination.read_text())['payloadJson'])))
 turns=[t['decision']['modelTrace'] for t in state['controllers']['turns'] if t['decision'].get('modelTrace')]
 fallbacks=[t for t in turns if t['resolution']=='FallbackResolved' and t['reasonCode']=='UserFallback']
 assert len(fallbacks)==3 and any(t.get('retryCount')==1 for t in fallbacks) and 2<=requests<=6
 for controller in state['controllers']['states']:
  policy=json.loads(controller['stateJson']);assert not policy['settings']['enabled'] and policy['gameModel']['timingPolicy']=='pause-at-decision-v1'
 (out/'model-state-check.json').write_text(json.dumps({'status':'succeeded','fixture':'HTTP503; not a real model','requests':requests,'recordedUserFallbacks':len(fallbacks),'recordedRetries':sum(t.get('retryCount',0) for t in fallbacks),'worldTick':json.loads(state['worldJson'])['tick']},indent=2)+'\n')
print(a.mode+' rendered interaction probe passed')

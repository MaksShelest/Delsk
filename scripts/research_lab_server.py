"""Loopback-only, token protected UI for the research runner queue.
Author: Шелестов Максим Евгеньевич.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import secrets
import shutil
import tempfile
import threading
import zipfile
from urllib.parse import urlsplit, unquote, parse_qs
from research_lab import DEFAULT, FACTORS, ROOT, unique, write


def make_server(lab, port=8765):
    token = secrets.token_urlsafe(32)
    instance = secrets.token_hex(16)
    stopping = threading.Event()
    class Handler(BaseHTTPRequestHandler):
        timeout = 30
        def log_message(self, *_):
            pass

        def send(self, code, body, content='application/json; charset=utf-8'):
            raw = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode('utf-8')
            self.send_response(code)
            self.send_header('Content-Type', content)
            self.send_header('Content-Length', str(len(raw)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; connect-src 'self'")
            self.end_headers()
            self.wfile.write(raw)

        def authorized(self):
            # Reject foreign origins and DNS rebinding as well as missing tokens.
            expected = f'127.0.0.1:{self.server.server_port}'
            return self.headers.get('Host') == expected and self.headers.get('Origin', 'http://'+expected) == 'http://'+expected and secrets.compare_digest(self.headers.get('X-Lab-Token', ''), token)

        def do_GET(self):
            path = urlsplit(self.path).path
            if path == '/api/health':
                expected = f'127.0.0.1:{self.server.server_port}'
                if self.headers.get('Host') != expected or self.headers.get('Origin', 'http://'+expected) != 'http://'+expected:
                    return self.send(403, {'error':'Use the loopback URL'})
                return self.send(200, dict(application='Delsk Research Lab', instance=instance))
            if path == '/':
                if self.headers.get('Host') != f'127.0.0.1:{self.server.server_port}':
                    return self.send(403, {'error':'Use the printed loopback URL'})
                html = (ROOT/'scripts/research_lab.html').read_text(encoding='utf-8').replace('__TOKEN__', token)
                return self.send(200, html.encode('utf-8'), 'text/html; charset=utf-8')
            if path in ('/research_lab.css','/research_lab.js'):
                if self.headers.get('Host')!=f'127.0.0.1:{self.server.server_port}':return self.send(403,{'error':'Use loopback'})
                return self.send(200,(ROOT/'scripts'/path[1:]).read_bytes(),'text/css; charset=utf-8' if path.endswith('.css') else 'text/javascript; charset=utf-8')
            if not self.authorized():
                return self.send(403, {'error':'Local researcher token required'})
            try:
                q={k:v[0] for k,v in parse_qs(urlsplit(self.path).query).items()}
                if path=='/api/profiles':return self.send(200,lab.views.profiles())
                if path=='/api/profile':
                    from research_lab import read
                    profile=next((p for p in lab.views.profiles() if p['id']==q['id']),None)
                    if profile is None:raise ValueError('Неизвестный профиль')
                    target=Path(profile['path']).resolve()
                    if not target.is_relative_to(lab.workspace/'profiles'):raise ValueError('Недопустимый профиль')
                    return self.send(200,read(target))
                if path=='/api/config':
                    from research_lab import read,source
                    from research_lab_presets import MAPS
                    map_path=q.get('map',DEFAULT['scenario']);choice=next((m for m in MAPS if m['path']==map_path),{})
                    profile=choice.get('economy','config/economy.json')
                    return self.send(200,dict(economy=read(ROOT/profile),economyPath=profile,scenario=read(source(map_path,lab.workspace))))
                if path=='/api/describe':return self.send(200,lab.views.describe(q['id']))
                if path=='/api/preview':return self.send(200,lab.views.preview(q['id'],q['path'],q.get('cursor',0)))
                if path=='/api/world':return self.send(200,lab.views.world(q['id'],q['job'],q['attempt'],q['tick']))
                if path=='/api/chart':return self.send(200,lab.views.chart(q['id'],q['job'],q['attempt']))
                if path=='/api/journal':return self.send(200,lab.views.journal(q['id'],q['job'],q['attempt'],q['kind'],q.get('cursor',0),q.get('actor',''),q.get('search',''),q.get('important','false')))
                if path == '/api/state':
                    return self.send(200, lab.snapshot())
                if path == '/api/catalog':
                    from research_lab_presets import MAPS, FACTOR_LABELS, presets
                    return self.send(200, dict(default=DEFAULT, factors=FACTORS, presets=presets(), maps=MAPS, factorLabels=FACTOR_LABELS,
                        scenarios=[str(p.relative_to(ROOT)) for p in sorted((ROOT/'scenarios').glob('*.json')) if 'controllers' not in p.name]))
                if path.startswith('/archives/') and path.endswith('.zip'):
                    key = unquote(path[len('/archives/'):-4])
                    # Only a known series can be archived; no filesystem path is accepted.
                    with tempfile.TemporaryFile() as stream:
                        with lab.lock:
                            series = next((s for s in lab.series if s['id'] == key), None)
                            if series is None:
                                raise ValueError('Неизвестная серия')
                            if any(j['status'] == 'running' for j in series['jobs']):
                                raise ValueError('Дождитесь завершения активного прогона или поставьте очередь на паузу')
                            folder = (lab.workspace/key).resolve()
                            if not folder.is_relative_to(lab.workspace):
                                raise ValueError('Unknown series folder')
                            with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
                                for file in sorted(folder.rglob('*')):
                                    if file.is_file():
                                        if file.is_symlink() or not file.resolve().is_relative_to(folder):
                                            raise ValueError('Unexpected link in series')
                                        archive.write(file, key+'/'+str(file.relative_to(folder)).replace('\\', '/'))
                        length = stream.tell()
                        stream.seek(0)
                        self.send_response(200)
                        self.send_header('Content-Type', 'application/zip')
                        self.send_header('Content-Length', str(length))
                        self.send_header('Content-Disposition', 'attachment; filename="'+key+'.zip"')
                        self.send_header('Cache-Control', 'no-store')
                        self.send_header('X-Content-Type-Options', 'nosniff')
                        self.end_headers()
                        shutil.copyfileobj(stream, self.wfile)
                        return
                if path.startswith('/files/'):
                    parts=unquote(path[7:]).split('/',1)
                    if len(parts)!=2:raise ValueError('Unknown result file')
                    target=lab.views.file(parts[0],parts[1])
                    self.send_response(200)
                    self.send_header('Content-Type','application/octet-stream')
                    self.send_header('Content-Length',str(target.stat().st_size))
                    self.send_header('X-Content-Type-Options','nosniff')
                    self.send_header('Cache-Control','no-store')
                    self.end_headers()
                    with target.open('rb') as stream:shutil.copyfileobj(stream,self.wfile)
                    return
                return self.send(404, {'error':'Unknown route'})
            except (ValueError, OSError,KeyError,TypeError) as e:
                return self.send(400, {'error':str(e)})

        def do_POST(self):
            if not self.authorized():
                return self.send(403, {'error':'Local researcher token required'})
            if stopping.is_set():
                return self.send(409, {'error':'Лаборатория завершает работу; новые задания не запускаются'})
            try:
                size = int(self.headers.get('Content-Length', '0'))
                if not 0 < size <= 2*1024*1024:
                    raise ValueError('Use a JSON body up to2MiB')
                data = json.loads(self.rfile.read(size), object_pairs_hook=unique)
                if self.path=='/api/manage':return self.send(200,lab.views.manage(data))
                if self.path=='/api/control':return self.send(200,lab.views.control(data['id'],data['action']))
                if self.path=='/api/profiles':return self.send(200,lab.views.save_profile(data))
                if self.path=='/api/models':
                    from research_workspace import model_tags
                    return self.send(200,model_tags(data['endpoint']))
                if self.path == '/api/queue':
                    return self.send(200, dict(id=lab.enqueue(data)))
                if self.path == '/api/pause':
                    lab.pause()
                elif self.path == '/api/shutdown':
                    with lab.lock:
                        stopping.set()
                        lab.pause()
                    self.send(200, dict(stopping=True))
                    threading.Thread(target=self.server.shutdown, daemon=True).start()
                    return
                elif self.path in ('/api/start', '/api/resume', '/api/retry'):
                    with lab.lock:
                        if stopping.is_set():
                            raise ValueError('Лаборатория завершает работу')
                        lab.start(retry=self.path == '/api/retry')
                elif self.path == '/api/compare':
                    return self.send(200, lab.compare(data['selected']))
                else:
                    return self.send(404, {'error':'Unknown route'})
                return self.send(200, lab.snapshot())
            except (ValueError, KeyError, TypeError, OSError) as e:
                return self.send(400, {'error':str(e)})
    server = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    # Finish admitted validation/download requests before the owner releases its queue lock.
    server.daemon_threads = False
    server.instance_id = instance
    server.stopping = stopping
    return server


def serve(lab, port, launch_browser=False):
    server = make_server(lab, port)
    url = f'http://127.0.0.1:{server.server_port}/'
    host_file = lab.workspace/'host.json'
    write(host_file, dict(url=url, instance=server.instance_id))
    print('Лаборатория готова: '+url+'\nИгра/Godot не требуются. Оставьте это окно открытым. Результаты: '+str(lab.workspace), flush=True)
    if launch_browser:
        from research_lab_launch import open_browser
        open_browser(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        lab.pause()
    finally:
        lab.pause()
        if lab.thread is not None:
            print('Ожидание завершения активного ограниченного прогона…', flush=True)
            lab.thread.join()
        host_file.unlink(missing_ok=True)
        server.server_close()

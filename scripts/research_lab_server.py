"""Loopback-only, token protected UI for the research runner queue.
Author: Шелестов Максим Евгеньевич.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import secrets
from urllib.parse import urlsplit, unquote
from research_lab import DEFAULT, FACTORS, ROOT, unique


def make_server(lab, port=8765):
    token = secrets.token_urlsafe(32)
    class Handler(BaseHTTPRequestHandler):
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
            if path == '/':
                if self.headers.get('Host') != f'127.0.0.1:{self.server.server_port}':
                    return self.send(403, {'error':'Use the printed loopback URL'})
                html = (ROOT/'scripts/research_lab.html').read_text(encoding='utf-8').replace('__TOKEN__', token)
                return self.send(200, html.encode('utf-8'), 'text/html; charset=utf-8')
            if not self.authorized():
                return self.send(403, {'error':'Local researcher token required'})
            try:
                if path == '/api/state':
                    return self.send(200, lab.snapshot())
                if path == '/api/catalog':
                    return self.send(200, dict(default=DEFAULT, factors=FACTORS,
                        scenarios=[str(p.relative_to(ROOT)) for p in sorted((ROOT/'scenarios').glob('*.json')) if 'controllers' not in p.name]))
                if path.startswith('/files/'):
                    target = (lab.workspace/unquote(path[7:])).resolve()
                    if not target.is_relative_to(lab.workspace) or not target.is_file() or target.suffix not in ('.json', '.jsonl', '.csv', '.log'):
                        raise ValueError('Unknown result file')
                    return self.send(200, target.read_bytes(), 'application/octet-stream')
                return self.send(404, {'error':'Unknown route'})
            except (ValueError, OSError) as e:
                return self.send(400, {'error':str(e)})

        def do_POST(self):
            if not self.authorized():
                return self.send(403, {'error':'Local researcher token required'})
            try:
                size = int(self.headers.get('Content-Length', '0'))
                if not 0 < size <= 2*1024*1024:
                    raise ValueError('Use a JSON body up to2MiB')
                data = json.loads(self.rfile.read(size), object_pairs_hook=unique)
                if self.path == '/api/queue':
                    return self.send(200, dict(id=lab.enqueue(data)))
                if self.path == '/api/pause':
                    lab.pause()
                elif self.path in ('/api/start', '/api/resume', '/api/retry'):
                    lab.start(retry=self.path == '/api/retry')
                elif self.path == '/api/compare':
                    return self.send(200, lab.compare(data['selected']))
                else:
                    return self.send(404, {'error':'Unknown route'})
                return self.send(200, lab.snapshot())
            except (ValueError, KeyError, TypeError, OSError) as e:
                return self.send(400, {'error':str(e)})
    return ThreadingHTTPServer(('127.0.0.1', port), Handler)


def serve(lab, port):
    server = make_server(lab, port)
    print(f'Research laboratory: http://127.0.0.1:{server.server_port}/ (private researcher exports; pause between runs)', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        lab.pause()
    finally:
        server.server_close()

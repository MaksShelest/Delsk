"""Startup support only; simulation remains in Research.Runner.
Author: Шелестов Максим Евгеньевич.
"""
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request
from urllib.parse import urlsplit
import webbrowser

from run_research_series import prepare_dotnet, run_checked

ROOT = Path(__file__).resolve().parents[1]


def open_browser(url):
    try:
        if webbrowser.open(url, new=2):
            return
    except (webbrowser.Error, OSError):
        pass
    print('Откройте адрес в браузере вручную: ' + url, flush=True)


def reopen(workspace):
    """Called only after the OS lock reports another owner. Never open arbitrary URLs."""
    try:
        record = json.loads((workspace/'host.json').read_text(encoding='utf-8'))
        parsed = urlsplit(record['url'])
        if (parsed.scheme != 'http' or parsed.hostname != '127.0.0.1' or not parsed.port
                or parsed.username or parsed.password or parsed.path != '/' or parsed.query or parsed.fragment):
            return False
        # No proxy or redirects for this narrowly loopback-only health check.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args):
                return None
        client = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        with client.open(record['url']+'api/health', timeout=2) as response:
            health = json.loads(response.read(1024))
        if health != dict(application='Delsk Research Lab', instance=record['instance']):
            return False
        print('Лаборатория уже работает. Открываю существующую очередь: '+record['url'], flush=True)
        open_browser(record['url'])
        return True
    except (OSError, ValueError, KeyError, TypeError):
        return False


def prepare(workspace):
    if not shutil.which('dotnet'):
        prepare_dotnet()
    if not shutil.which('dotnet'):
        raise ValueError('Не найден .NET SDK. Установите .NET SDK '+json.loads((ROOT/'global.json').read_text())['sdk']['version']+
                         ' с dotnet.microsoft.com/download и снова откройте ResearchLab.cmd. Godot не требуется.')
    expected = json.loads((ROOT/'global.json').read_text())['sdk']['version']
    try:
        version = subprocess.run(['dotnet', '--version'], capture_output=True, text=True, timeout=30, cwd=ROOT)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise ValueError('Не удалось проверить .NET SDK: '+str(error)) from error
    if version.returncode or version.stdout.strip() != expected:
        raise ValueError('Для этого checkout требуется .NET SDK '+expected+
                         '. Установите его рядом с другими SDK и повторите запуск.\n'+version.stderr.strip())
    logs = workspace/'startup'
    for name, timeout, command in [
        ('restore', 180, ['dotnet', 'restore', 'src/Research.Runner/Research.Runner.csproj', '--locked-mode', '--disable-build-servers']),
        ('build', 180, ['dotnet', 'build', 'src/Research.Runner/Research.Runner.csproj', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1']),
    ]:
        print(('1/2 · Подготовка зависимостей' if name == 'restore' else '2/2 · Сборка исследовательского runner')+
              '… Первый запуск может занять несколько минут.', flush=True)
        code, _ = run_checked('lab-'+name, timeout, logs, command)
        if code:
            raise ValueError('Подготовка не завершена ('+name+', код '+str(code)+'). Логи: '+str(logs)+
                             '. Проверьте SDK/доступ к NuGet и повторите запуск; результаты опытов сохранены.')


def require_runner():
    if not (ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll').is_file():
        raise ValueError('Runner ещё не собран. Запустите ResearchLab.cmd или добавьте --prepare к команде запуска.')

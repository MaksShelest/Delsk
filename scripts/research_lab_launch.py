"""Startup support only; simulation remains in Research.Runner.
Author: Шелестов Максим Евгеньевич.
"""
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import urllib.request
from urllib.parse import urlsplit
import webbrowser

from run_research_series import prepare_dotnet, run_checked

ROOT = Path(__file__).resolve().parents[1]
RUNNER = ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'
SDK_LINK = 'https://dotnet.microsoft.com/download/dotnet/10.0'
PYTHON_LINK = 'https://www.python.org/downloads/'
GIT_LINK = 'https://git-scm.com/download/win'


def expected_sdk():
    return json.loads((ROOT/'global.json').read_text(encoding='utf-8'))['sdk']['version']


def open_browser(url):
    try:
        if webbrowser.open(url, new=2):
            return
    except (webbrowser.Error, OSError):
        pass
    print('Браузер не открылся. Откройте адрес вручную: ' + url, flush=True)


def git_head():
    """Реальный SHA или None. Отсутствие Git не считается версией опыта."""
    if not shutil.which('git') or not (ROOT/'.git').exists():
        return None
    try:
        sha = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True, timeout=10, stderr=subprocess.DEVNULL)
    except (subprocess.SubprocessError, OSError):
        return None
    if not isinstance(sha, str):
        return None
    sha = sha.strip()
    return sha if re.fullmatch(r'[0-9a-f]{40}', sha) else None


def git_dirty():
    """True, когда SHA нельзя считать описанием собранного кода."""
    if not shutil.which('git') or not (ROOT/'.git').exists():
        return True
    try:
        out = subprocess.check_output(['git', '-C', str(ROOT), 'status', '--porcelain', '--untracked-files=normal'], text=True, timeout=10, stderr=subprocess.DEVNULL)
    except (subprocess.SubprocessError, OSError):
        return True
    return not isinstance(out, str) or bool(out.strip())


def source_fingerprint():
    digest = hashlib.sha256()
    files = []
    for base in (ROOT/'src/Simulation.Core', ROOT/'src/Simulation.Hosting', ROOT/'src/Research.Runner', ROOT/'src/Agents.Algorithmic', ROOT/'src/Agents.Llm'):
        if not base.is_dir():
            continue
        for path in base.rglob('*'):
            if not path.is_file() or 'bin' in path.parts or 'obj' in path.parts:
                continue
            if path.suffix.lower() in {'.cs', '.csproj', '.props', '.targets'}:
                files.append(path)
    for extra in (ROOT/'global.json', ROOT/'Directory.Build.props', ROOT/'Directory.Build.targets'):
        if extra.is_file():
            files.append(extra)
    for path in sorted(set(files)):
        digest.update(str(path.relative_to(ROOT)).encode())
        digest.update(b'\0')
        digest.update(path.read_bytes())
        digest.update(b'\0')
    return digest.hexdigest()


def runner_digest():
    return hashlib.sha256(RUNNER.read_bytes()).hexdigest() if RUNNER.is_file() else None


def fresh(workspace):
    path = Path(workspace)/'startup'/'runner-stamp.json'
    if not path.is_file() or not RUNNER.is_file():
        return False
    try:
        stamp = json.loads(path.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return False
    return stamp.get('sdk') == expected_sdk() and stamp.get('sources') == source_fingerprint() and stamp.get('runnerSha256') == runner_digest()


def diagnose(workspace, action):
    folder = Path(workspace)/'startup'
    folder.mkdir(parents=True, exist_ok=True)
    info = dict(python='.'.join(map(str, sys.version_info[:3])), platform=platform.platform(),
                sdkExpected=expected_sdk(), sdkFound=None, git=git_head() is not None, runner=RUNNER.is_file(),
                writable=os.access(folder, os.W_OK), action=action)
    report = folder/'environment.json'
    report.write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return info


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
    workspace = Path(workspace)
    logs = workspace/'startup'
    logs.mkdir(parents=True, exist_ok=True)
    if not os.access(logs, os.W_OK):
        raise ValueError('Нет права записи в ' + str(logs) + '. Лаборатория хранит там журналы запуска и не трогает старые опыты.')
    if fresh(workspace):
        diagnose(workspace, 'skip')
        print('Runner уже собран и соответствует исходникам. Повторная сборка не нужна.', flush=True)
        return
    if not shutil.which('dotnet'):
        prepare_dotnet()
    if not shutil.which('dotnet'):
        diagnose(workspace, 'sdk-missing')
        raise ValueError('Не найден .NET SDK ' + expected_sdk() + '. Он нужен, чтобы собрать исследовательский runner; саму экономику считает этот runner, не Python. '
                         'Godot и Ollama для обычного опыта не нужны. Установите SDK ' + expected_sdk() + ' (не только runtime) с ' + SDK_LINK + ' и снова откройте ResearchLab.cmd.')
    try:
        version = subprocess.run(['dotnet', '--version'], capture_output=True, text=True, timeout=30, cwd=ROOT)
    except subprocess.TimeoutExpired as error:
        diagnose(workspace, 'sdk-timeout')
        raise ValueError('Проверка .NET SDK не завершилась за 30 секунд. Повторите запуск. Если зависание останется, проверьте установку SDK ' + expected_sdk() + '.') from error
    except OSError as error:
        diagnose(workspace, 'sdk-error')
        raise ValueError('Не удалось проверить .NET SDK: ' + str(error)) from error
    stdout = version.stdout if isinstance(version.stdout, str) else ''
    stderr = version.stderr if isinstance(version.stderr, str) else ''
    if version.returncode or stdout.strip() != expected_sdk():
        diagnose(workspace, 'sdk-mismatch')
        found = stdout.strip() or stderr.strip() or 'не определена'
        raise ValueError('Для этого checkout нужен .NET SDK ' + expected_sdk() + ' (roll-forward отключён). Сейчас: ' + found +
                         '. Установите именно эту версию с ' + SDK_LINK + ' рядом с другими SDK и повторите запуск.\n' + stderr.strip())
    for name, timeout, command in [
        ('restore', 180, ['dotnet', 'restore', 'src/Research.Runner/Research.Runner.csproj', '--locked-mode', '--disable-build-servers', '--disable-parallel', '-m:1']),
        ('build', 180, ['dotnet', 'build', 'src/Research.Runner/Research.Runner.csproj', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1']),
    ]:
        print(('1/2 · Подготовка зависимостей' if name == 'restore' else '2/2 · Сборка исследовательского runner') +
              '… Первый запуск может занять несколько минут.', flush=True)
        try:
            code, _ = run_checked('lab-' + name, timeout, logs, command)
        except subprocess.TimeoutExpired as error:
            diagnose(workspace, name + '-timeout')
            raise ValueError('Шаг ' + name + ' превысил ' + str(timeout) + ' с. Журнал: ' + str(logs) + '. Результаты опытов не удалялись.') from error
        if code:
            diagnose(workspace, name + '-failed')
            raise ValueError('Подготовка не завершена (' + name + ', код ' + str(code) + '). Журнал: ' + str(logs) +
                             '. Проверьте SDK и доступ к NuGet. Старые исследования сохранены.')
    if not RUNNER.is_file():
        diagnose(workspace, 'runner-missing')
        raise ValueError('Сборка завершилась, но файл runner не найден: ' + str(RUNNER))
    stamp = dict(sdk=expected_sdk(), sources=source_fingerprint(), runnerSha256=runner_digest())
    (logs/'runner-stamp.json').write_text(json.dumps(stamp, indent=2) + '\n', encoding='utf-8')
    diagnose(workspace, 'built')


def require_runner():
    if not RUNNER.is_file():
        raise ValueError('Runner ещё не собран. Запустите ResearchLab.cmd или добавьте --prepare к команде запуска.')

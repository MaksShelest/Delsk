# Доказательства I1

Автор: Шелестов Максим Евгеньевич. Локальная проверка Linux, SDK10.0.401,
Godot4.7.2 Mono, Xvfb/OpenGL llvmpipe, `DOTNET_PROCESSOR_COUNT=2`.
Результаты и ограничения — [итоговый отчёт](../../IMPLEMENTATION_REPORT_I1.md),
команды — [протокол воспроизведения](../../research/i1/README.md).

- `checks.json`, `ordinary.json`, `developer.json`: итоговые счётчики уникальных
  тестов, отдельные suites, реальные exit codes, ожидаемые отказы Developer Release
  и сохранённые неудачные попытки разработки.
- `measurements.jsonl`: исходные измерения движка; `speeds.json` и `speed-table.md`
  сравнивают72 поездки в24 строках. Дистанция каждой поездки120 клеток.
- `runner.json`: пять загруженных моделей, новые ordinary-миры трёх размеров,
  load/replay/аудиты и проверка native-сохранения.
- `headless.json`: прежняя полная headless-регрессия и точные сравнения экспортов.
- `fixtures.json.gz`: lossless словарь с шестью активными сохранениями, пятью
  исходными сценариями/лентами команд и ожидаемыми хешами. Это подготовленные
  тестовые миры, не пользовательские партии. После извлечения в новую owned-папку
  её можно передать как `--unit` в `check_i1_runner.py`; `i1-fixtures` этой папки —
  как `--fixtures` в `check_i1_native.py`.
- `unchanged-inputs.json`:84 прежних финансовых/географических входа и SHA256.
- `native/`: реальные PNG, результаты platform/I1 probes, импорт и SHA исходников.
  Это программная Linux-проверка; физические Windows/Android не заявляются.
- `logs.json.gz`: lossless UTF-8 JSON-словарь `путь → полное содержимое` журналов,
  TRX и метаданных ограниченных команд, включая неудачные попытки. Пути внутри
  архива отсчитываются от локального `work/i1`; сценарии/сохранения и кэши туда
  не входят. Отдельные прогоны не складываются как новые уникальные тесты.
- `manifest.json`: размер и SHA256 каждого опубликованного файла; в `checks.json`
  дополнительно указан SHA распакованного архива и число его файлов.

Проверка архива без извлечения произвольных путей:

```python
import gzip, hashlib, json
from pathlib import Path
p = Path('docs/verification/i1')
for entry in json.loads((p/'manifest.json').read_text())['files']:
    data = (p/entry['path']).read_bytes()
    assert len(data) == entry['bytes']
    assert hashlib.sha256(data).hexdigest() == entry['sha256']
raw = gzip.decompress((p/'logs.json.gz').read_bytes())
meta = json.loads((p/'checks.json').read_text())['logArchive']
assert hashlib.sha256(raw).hexdigest() == meta['uncompressedSha256']
logs = json.loads(raw)
assert len(logs) == meta['fileCount']
print(logs['checks/i1-final-build.stdout.log'])
```

Все агрегаты требуют фактического завершения частей. `exit1` у
`forbidden-developer-release` — проверяемый защитный отказ. Нулевой выбор тестов,
таймаут или отсутствие Xvfb не считаются успешной приёмкой; исходные журналы
сохранены рядом с последующими успешными проверками.

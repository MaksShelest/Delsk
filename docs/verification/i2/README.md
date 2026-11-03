# Доказательства I2

Автор: Шелестов Максим Евгеньевич. Дата:04.11.2026 (Europe/Samara).
Полный результат — [IMPLEMENTATION_REPORT_I2](../../IMPLEMENTATION_REPORT_I2.md).
Код: `f157106a82df8050e8f998c9ec7ee8404e51b9b8`, PR[#80](https://github.com/MaksShelest/Delsk/pull/80).
Linux, SDK10.0.401, Godot4.7.2 Mono, Xvfb/software OpenGL,
`DOTNET_PROCESSOR_COUNT=2`; физические устройства не заявляются.

- `ci/original/`: три полных ZIP исходных отказов, проверенные размер/SHA/CRC.
  Первые два — отказы I2 на c893da1b; третий — последующий300s timeout
  сохранённой Developer-регрессии I1 до перераспределения двух частей.
- `ci/raw/`: полные исходные job-логи и снимки GitHub API с полной пагинацией.
  Промежуточные cancelled/failure не заменяются успешными статусами.
  Подписанные временные download URL не публикуются. Дублирующая base64
  передача архивов из временного диагностического workflow не включена;
  оригинальные ZIP сохранены полностью.
- `code-ci-logs.json.gz`: полный JSON-словарь39 успешных job-логов итогового
  кода (ключ — job ID, поля `name`/`text`);37 Core +2 Godot. Метаданные
  обоих завершённых runs, полные jobs и artifacts — `ci/raw/code-417301*.json`.
- `ordinary.json`, `developer.json`:44/44 в каждом варианте,7 частей.
  `runner.json`:35 процессов,21 экспорт,12 точных сравнений, accounting/privacy.
  `unchanged-inputs.json`:84 прежних финансовых/географических SHA.
  `retained-i1-measurements.jsonl` и `retained-i1-phases.jsonl` сохраняют
  поездки120 клеток и фазы обеих перераспределённых Developer-частей.
- `native/`:9 карта×NPC,27 фаз,63 PNG, ordinary import/cache/audit,
 692 SHA входов, platform probe и readback-хеши. Все runtime-ссылки
  зафиксированы до добавления пробы; визуализация сравнивается с Core.
- `tracked-native-fixed/`: отдельный реальный Тракт-Д1,3 фазы и7 PNG.
  `frozen.json` содержит исходные/диагностические DLL и SHA helper.
  Изменена только изолированная проба: выбор модели, длительность ввода,
  безопасное завершение загруженного ребра и ожидание layout справки.
  `diagnosis/` сохраняет отказ первой попытки и readback исходного ребра.
- `logs.json.gz`: lossless UTF-8 JSON-словарь `путь → полное содержимое`547
  журналов/TRX/метаданных bounded команд. Есть исходный safety failure,
  неудачные диагностические попытки и ожидаемый защитный отказ Developer Release.
  Отдельные повторы не суммируются как новые уникальные тесты.
- `fixtures.json.gz`:143 файла; исходные сценарии/команды/активные сохранения,
  native-подготовка с явным капиталом, сохранённые границы и финальные состояния.
  Обычные NPC и их контроллеры сохранены. Это не доказательство естественного
  заработка, баланса длительной партии или физического GPU/FPS.
- `checks.json`: фактические локальные счётчики, размер/SHA распакованных
  архивов. `manifest.json`: размер и SHA каждого опубликованного файла.

Проверка без извлечения произвольных путей:

```python
import gzip, hashlib, json
from pathlib import Path
p = Path('docs/verification/i2')
for e in json.loads((p/'manifest.json').read_text())['files']:
    data = (p/e['path']).read_bytes()
    assert len(data) == e['bytes']
    assert hashlib.sha256(data).hexdigest() == e['sha256']
for name,key in [('logs.json.gz','logArchive'),('fixtures.json.gz','fixtureArchive'),('code-ci-logs.json.gz','codeCiLogArchive')]:
    raw = gzip.decompress((p/name).read_bytes())
    meta = json.loads((p/'checks.json').read_text())[key]
    assert len(raw) == meta['uncompressedBytes']
    assert hashlib.sha256(raw).hexdigest() == meta['uncompressedSha256']
    assert len(json.loads(raw)) == meta['fileCount']
```

Воспроизведение из корня репозитория с SDK и Godot Mono указанной версии.
`$DELSK_GODOT` — путь к настоящему executable, Xvfb должен быть установлен
либо передан через `--xvfb`. Для каждого запуска нужна новая owned-папка;
прошлые результаты не удалять. Команды выполняются последовательно:

```bash
export DOTNET_PROCESSOR_COUNT=2
python3 scripts/run_check.py --name i2-ordinary --timeout 780 --log-directory work/i2-repro/checks -- python3 scripts/check_headless.py --phase tests --suite i2 --output work/i2-repro/ordinary
python3 scripts/run_check.py --name i2-developer --timeout 780 --log-directory work/i2-repro/checks -- python3 scripts/check_developer.py --suite i2 --output work/i2-repro/developer
python3 scripts/run_check.py --name i2-runner --timeout 600 --log-directory work/i2-repro/checks -- python3 scripts/check_i2_runner.py --fixtures work/i2-repro/ordinary/i2-fixtures --output work/i2-repro/runner
python3 scripts/run_check.py --name i2-native --timeout 4200 --log-directory work/i2-repro/checks -- python3 scripts/check_i2_native.py --fixtures work/i2-repro/ordinary/i2-fixtures --output work/i2-repro/native --godot "$DELSK_GODOT"
python3 scripts/run_check.py --name i2-tracked --timeout 900 --log-directory work/i2-repro/checks -- python3 scripts/check_i2_native_retained.py --prepared work/i2-repro/native --fixtures work/i2-repro/ordinary/i2-fixtures --output work/i2-repro/tracked --case native-compact_valley-6 --model trakt_d1 --godot "$DELSK_GODOT"
```

Runner-артефакты и обычные сборки должны соответствовать текущему исходнику;
нельзя выдавать старые бинарники за новый прогон. Дополнительная native-проба
отклоняет изменение любого из692 входов, helper либо frozen DLL.
Дедлайны фаз120s, smoke30s, I1 частей300s и Developer I1 aggregate1000s
сохранены. Таймаут, отсутствие дисплея, нулевой выбор тестов или незавершённый
процесс не считаются успехом. Итоговый статус CI проверяется на странице PR.

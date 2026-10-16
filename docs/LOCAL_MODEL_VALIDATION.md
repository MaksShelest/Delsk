# Локальное завершение P23

Автор: Шелестов Максим Евгеньевич. Дата: 16.10.2026.

P22 предоставляет Ollama HTTP transport и защищённый гибрид, проверенный fake-ответами. Эти проверки не доказывают P23. Для P23 нужны реальная модель, измеренный ответ с применённой обычной командой, fallback и метаданные конкретных файлов/runtime. Ниже процедура для Windows PowerShell из корня Delsk. Linux использует те же аргументы runner.

## 1. Проверить runtime и модель

Установить Ollama из официального источника, если его ещё нет. Не переустанавливать работающий runtime. `ollama serve` должен работать в отдельном терминале либо как уже существующая локальная служба. Для отдельного временного процесса можно использовать `python scripts/run_check.py --name ollama-serve --timeout 600 --log-directory artifacts/p23-local/checks -- ollama serve`. Не запускать второй сервер и не останавливать чужие процессы.

```powershell
New-Item -ItemType Directory -Force artifacts/p23-local | Out-Null
python scripts/run_check.py --name ollama-version --timeout 15 --log-directory artifacts/p23-local/checks -- ollama --version
python scripts/run_check.py --name ollama-list --timeout 15 --log-directory artifacts/p23-local/checks -- ollama list
curl.exe --fail --silent --show-error --max-time 15 http://127.0.0.1:11434/api/version --output artifacts/p23-local/runtime.json
```

Кандидат проекта — `qwen3.5:4b`; сначала проверить доступность конкретного тега и его лицензию. Если модель уже есть, скачивание не требуется. Иначе одна bounded попытка: `python scripts/run_check.py --name ollama-pull --timeout 300 --log-directory artifacts/p23-local/checks -- ollama pull qwen3.5:4b`. Отсутствующий/недоступный тег — явный blocker, не повод молча заменить модель без записи конфигурации. Модельные веса не класть в git.

```powershell
$showBody = @{model='qwen3.5:4b'; verbose=$true} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:11434/api/show -ContentType application/json -Body $showBody -TimeoutSec 15 |
    ConvertTo-Json -Depth 50 | Set-Content -Encoding utf8NoBOM artifacts/p23-local/model-show.json
curl.exe --fail --silent --show-error --max-time 15 http://127.0.0.1:11434/api/tags --output artifacts/p23-local/model-tags.json
python scripts/run_check.py --name ollama-license --timeout 15 --log-directory artifacts/p23-local/checks -- ollama show --license qwen3.5:4b
```

Записать выбранный tag и digest из tags, parameter_size/quantization_level/model_info/license из show, версию runtime, OS/CPU/GPU/RAM/VRAM. Неизмеренная память — null с причиной. Не считать размер весов фактической RAM/VRAM. Проверка лицензии конкретного артефакта предшествует его распространению.

Для прогрева использовать один `/api/chat` с `stream=false`, выбранным model, коротким текстом, `options.num_ctx=4096`, `num_predict=16` и внешним timeout60s. Прогрев не считается игровым решением P23 и не смешивается с его latency.

## 2. Проверить готовый P22

```powershell
python scripts/check_headless.py --output artifacts/p23-local/p22-regression
```

Должны пройти bounded restore/build/tests, старые runner/load/replay, S7 и model-off. Настройки модели — `scenarios/local-model.example.json`: localhost, один общий runtime, очередь3, timeout4000ms, apply через15ticks, TTL15, минимум30ticks между запросами/участника, до4запросов/120ticks, контекст4096 и ответ512токенов. Промпт дополнительно ограничен16KiB, ответ4KiB и HTTP body64KiB. Байты не являются точным числом токенов: фактические counts сохраняются из Ollama.

Если модель не успевает после прогрева, допускается отдельная явно записанная конфигурация timeout10000ms/applyDelayTicks30/decisionTtlTicks30. Не изменять сроки торговли или экономические правила ради модели. Хранить точный JSON и его hash рядом с результатами.

## 3. Реальные дилеммы через общий runner

Использовать `--mode paced`: один tick/секунда. Ускоренный `LiveHeadlessStress` пригоден для проверки fallback, но не заменяет realtime пилот. Нельзя ждать HTTP внутри Step или продлевать окно торговли. Один экземпляр ModelRuntime обслуживает трёх NPC, а `keep_alive=5m` и один model сохраняют общие веса. Каждый запрос содержит только собственный разрешённый контекст/память NPC.

```powershell
python scripts/run_check.py --name p23-development --timeout 180 --log-directory artifacts/p23-local/checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --script hybrid --model-mode ollama --model-config scenarios/local-model.example.json --mode paced --ticks 90 --output artifacts/p23-local/development
python scripts/run_check.py --name p23-logistics --timeout 180 --log-directory artifacts/p23-local/checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --script hybrid --model-mode ollama --model-config scenarios/local-model.example.json --scenario scenarios/baseline-logistics.json --controllers scenarios/baseline-logistics-controllers.json --mode paced --ticks 90 --output artifacts/p23-local/logistics
python scripts/run_check.py --name p23-support --timeout 180 --log-directory artifacts/p23-local/checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --script hybrid --model-mode ollama --model-config scenarios/local-model.example.json --scenario scenarios/baseline-politics.json --controllers scenarios/baseline-politics-controllers.json --mode paced --ticks 90 --output artifacts/p23-local/support
```

Дилеммы: развитие производства или добыча/ликвидность; снабжение производства или другая экономическая цель; покупка/продажа поддержки или сохранение собственного шанса. Модель выбирает только перечисленный plan option; числа/цены/actor не редактирует. В кратком аукционе алгоритм остаётся основным: закрывшееся окно не открывается ради ответа.

`model-decisions.jsonl` содержит private researcher trace: identity, requested/apply ticks/epoch, фактический prompt/hash/options, raw response, status/reason, latency и token counts. `decisions.jsonl` связывает разрешение с commandIds, `events.jsonl` — с Accepted/Rejected и реальной мутацией. Для P23 требуется хотя бы одно `ModelResolved` с непустым commandIds и `CommandAccepted`; один HTTP200 или выбранный Hold не доказывает фактическое применение команды. Проверить раздельную память NPC и отсутствие чужих support terms/будущих цен в prompt. Эти файлы нельзя показывать другим участникам как публичные события.

Сохранить manifest, точный model config/hash, model-show/tags/runtime, commit/rules, prompt version/hash, latency/tokens, количество ModelResolved/FallbackResolved/invalid/stale. Текущий P22 manifest честно оставляет modelDigest=null: перед закрытием P23 измеренные метаданные нужно добавить в его evidence/manifest, а не объявлять это поле подтверждённым по имени тега.

## 4. Проверить настоящий fallback и replay

Создать копию config с endpoint `http://127.0.0.1:1/` (недоступный локальный порт), не останавливая рабочую службу. Выполнить тот же `--script hybrid --model-mode ollama --mode paced --ticks 20` с этой копией и timeout60s. Должны быть записаны fallback и последующие обычные команды алгоритма, а игра должна продолжаться. Если этот порт занят, выбрать проверенно недоступный loopback port и записать его.

```powershell
python scripts/run_check.py --name p23-replay --timeout 30 --log-directory artifacts/p23-local/checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --script hybrid --scenario artifacts/p23-local/development/scenario.json --replay artifacts/p23-local/development/commands.jsonl --ticks 90 --output artifacts/p23-local/development-replayed
python scripts/verify_checkpoint_exports.py --world-only artifacts/p23-local/development artifacts/p23-local/development-replayed
```

Replay не принимает live model options и не создаёт адаптер. Снимок посреди незавершённого slot после load получает новую epoch и один fallback на сохранённом applyTick; старый callback не используется. Это намеренное правило, поэтому live continuation после прерывания не обязана совпасть с непрерванным ответом модели. После уже разрешённого slot обычный plan/command state продолжается без повторной инференции; это доказано P22 fake save/load тестами, реальную ветку также проверить локально.

После реальных результатов повторить полный bounded gate и применимый Godot smoke, записать измеренный отчёт P23 и сделать отдельный commit. Пока реальный запрос/применение/fallback/метаданные не подтверждены, P23 остаётся открытым. Серии P24/P25 и UI в эту процедуру не входят.

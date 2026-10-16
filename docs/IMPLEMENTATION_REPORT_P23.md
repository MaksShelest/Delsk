# P23: реальная локальная модель

Автор: Шелестов Максим Евгеньевич. Дата: 16.10.2026.

База — `68606aa0fea0302c14e0d086a12193bd26b79deb`, ветка `feat/p23-real-model-validation`. P22 на этом коммите уже содержал Ollama transport, слоты и fake S7. `ModelDigest` в runner по-прежнему `null`: имя тега не считается digest. Измеренные metadata записаны здесь и в [verification/p23/gate.json](verification/p23/gate.json). Игровые World12/Rules baseline-1/Session2/Envelope1 и сроки торговли не менялись. P24 не начинался. Commit по запросу пользователя не создавался.

## Среда

Ollama `0.40.5` уже слушал `http://127.0.0.1:11434`; второй сервер не запускался, `qwen3.5:4b` повторно не скачивалась. Тег `qwen3.5:4b`, digest `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04`, family `qwen35`, parameter_count `4205751296`, quantization `Q4_K_M`, license `apache-2.0`. Файл весов `3324173934` байт; это не RAM и не VRAM. Видимая RAM `49924832` КБ. GPU — NVIDIA GeForce RTX 5060 Laptop, `8151` МиБ по `nvidia-smi`, driver `617.42`; после прогрева `/api/ps` показал `size_vram=3140495604`. OS — Windows 11 Pro `10.0.26200`, CPU Intel Core i7-14650HX, 16 ядер / 24 потока. Контекст запроса остаётся `num_ctx=4096`, хотя у файла модели context_length `262144`.

SDK `10.0.401` взят из пользовательского `%USERPROFILE%\.dotnet` через `DOTNET_ROOT` этой сессии; глобальный git/dotnet config не менялся. `python` на машине — заглушка Store, поэтому оркестрация шла через `py -3` (Python 3.14.0); вложенные проверки используют тот же интерпретатор. Godot .NET — `4.7.2.stable.mono.official.ed1daf0bf`.

Прогрев одним `/api/chat`, `num_predict=16`, в игровые latency не входит: загрузка модели около `4.93` с, `content` пустой, `16` токенов ушли в thinking. Отдельный запрос той же JSON-схемы без `think:false` за `7.846` с исчерпал `512` токенов (`done_reason=length`) и тоже оставил `content` пустым. С `think:false` тот же запрос за `1.811` с вернул ровно требуемый JSON, `70` completion tokens. Это диагностический запрос, не игровое решение.

## Исправление адаптера

`qwen3.5:4b` по умолчанию думает. Прежний `OllamaAdapter` не отключал thinking, поэтому весь `num_predict` расходовался до `message.content`, а ответ не становился intent. В тело `/api/chat` добавлено `think:false`. Игровые правила, цены, сроки и валидатор не менялись. Регрессия — утверждение в `OllamaAdapterTests.BoundedNonStreamingSchemaRequestAndMeasuredTokensUseOneLocalEndpoint`. Отдельный конфиг `timeout=10000` / `applyDelayTicks=30` не понадобился: после исправления игровые ответы укладывались в `4000` мс.

Конфиг прогонов — неизменённый `scenarios/local-model.example.json`, SHA-256 `db343e0e579127d48f9d8e2fd41b13efc6ddb7f8ce8261b6e6b7729b716282f3`. Prompt version `plan-choice-v1`. Пример prompt hash первого development-запроса `04e368be1393a28ea634e60f4a4b12740e8f4382bde8e505dbe11c237ec89438`, `3911` байт.

## Реальные прогоны

Все три дилеммы: `--mode paced`, 90 тиков, один общий runtime, exit 0.

| Прогон | ModelResolved | FallbackResolved | invalid | stale | Примечание |
| --- | --- | --- | --- | --- | --- |
| development | 6 Accepted | 0 | 0 | 0 | два плана применены |
| logistics | 0 | 6 `StaleRelevantState` | 0 | 6 | ещё один запрос на тике 87 не дошёл до apply 102 |
| support | 6 Accepted, все Hold | 0 | 0 | 0 | команды модели нет |

Development, latency `1501–1916` мс, prompt tokens `1209–1336`, completion tokens `55–90`. На тике 14 модель выбрала `HarvestForSale` для участников 2 и 4 и `Hold` для участника 3. Core принял первые команды плана: `s1:2:1` и `s1:4:1`, обе `MoveTo`, обе `CommandAccepted`, с `MovementStarted` и `MovementArrived`. Дальше тот же выбранный план исполнил алгоритм, и Core принял `Harvest` wood 14 (`s1:2:2`, `s1:4:2`), обратный `MoveTo` (`s1:2:3`, `s1:4:3`) и `SellAvailableToCapital` wood 14 (`s1:2:4`, `s1:4:4`). На тике 71 все трое выбрали `Hold`. Поздние `s1:2:5` и `s1:4:5` — уже алгоритмический `SellCarried`, не выбор модели. За 90 тиков другие цели, кроме добычи и Hold, в список вариантов не входили.

Logistics, latency `1310–2206` мс, prompt tokens `1103–1360`, completion tokens `52–95`. Модель вернула перечисленные optionId, включая `ResumeProduction`, но к apply tick relevant fingerprint уже изменился, поэтому ни одна команда модели не ушла в Core. Алгоритм продолжил партию: 32 `CommandAccepted`, производство, поставки и перемещения. Это штатный stale fallback, не ослабление проверки свежести.

Support, latency `1274–1616` мс, prompt tokens `889–1293`, completion tokens `49–66`. Участнику 2 предлагались `SignSupport`, `AdjustWage` и `Hold`; модель шесть раз выбрала `Hold`. `SupportProposed` / `Accepted` / `Signed` в events принадлежат алгоритму.

Fallback: копия конфига с endpoint `http://127.0.0.1:1/`, порт не слушал. Три `FallbackResolved` / `ModelUnavailable`, latency `2029–2082` мс, token counts отсутствуют. Затем алгоритм отправил `MoveTo`, Core принял `s1:2:1`, `s1:3:1` и `s1:4:1`, партия дошла до тика 20.

Replay development без `--model-mode` дал `recorded-headless`, ноль строк `model-decisions.jsonl` и побайтовое world-only совпадение с живым прогоном. Checkpoint живого прогона на тике 20, после уже разрешённых слотов, загружен до тика 40 без модели: все 18 сравниваемых файлов, включая `model-decisions.jsonl`, совпали; нового запроса не было.

В prompt нет чужих support terms, будущих цен, seed и world PRNG. Память участников раздельна. `public-events.jsonl` не содержит `requestJson`, `responseJson`, `reasonSummary` и `ModelResolved`.

## Проверки

Предварительный `scripts/check_headless.py` прошёл на сборке до исправления thinking: 618/618. Финальный gate после исправления снова 618/618, failures 0, 26 runner-сценариев, 22 обычные save/load пары плюс controlled, 9 replays, 81 export / 54 comparison pairs, exit 0.

Godot .NET 4.7.2 на этой Windows-машине: locked restore, Release build, import, `--smoke` и `verify_platform_probe.py` exit 0. 62 экономических хеша, restore 12/34/55/60; 40 выборных, restore 9/19/20/21; 120 support, restore 17/18/19/27/99/100. State hash `befa38addd66ef7c7c7d9e6c7d98561edd21c4a4673ad32ba689a7bb45f89fff` совпадает с прежней platform fixture. Экспорт EXE/APK и телефон не запускались.

## Границы

P23 закрыт в границах этой процедуры: реальный запрос, применённая команда, fallback, replay, metadata и локальный Godot smoke. Не проверены серии P24/P25, UI, Android device gate P06 и текущие EXE/APK на устройстве. Полный месяц paced hybrid не гонялся. Непрерванный ответ модели после load незавершённого слота по-прежнему не обещается. Полный verbose `model-show` с tensor metadata лежит только в `artifacts/p23-local` и в git не входит.

# Отчёт P12: checkpoint/load и проверка без графики

Автор: Шелестов Максим Евгеньевич. Дата:14.10.2026.
Ветка `feat/checkpoint-p12`, база — пользовательский merge PR №5 `91b1d9d`. Источник требований v0.3 неизменён, SHA-256 соответствует DECISIONS.

## Сделано

Реализована одна итерация P12. Core экспортирует explicit current-state DTO и восстанавливает его без выполнения старых команд. Сохранены все существующие системы P00–P11: конфигурация/версии/hash/tick/ID/PRNG, счета/проводки/holds, инвентари/stock/capacity, участники/jobs/paths/cargo, production/extraction WIP и взаимные machine links, land/buildings/reservations, N/unemployed/credit/growth/pending wages, capital windows/requests/settlements, queues/dedup/results и committed event prefix.

Hosting сохраняет session ID,epoch,paused,sequence counters,tape,metrics и recorded plan/cursor/label. Loading проверяет checksum,версии,ссылки,неотрицательность/балансы/математику/holds/commit/hash в отдельном instance, не меняя живой мир при отказе. Epoch увеличивается, прежний callback отклоняется. Elapsed не восстанавливается, offline time не применяется.

File store: temporary в том же каталоге, flush, полное контрольное чтение, replace с valid backup; corruption не вращается поверх хорошего backup. Явное восстановление через recover-backup, строгий default. До64MiB; полная trace история bounded прогона, без базы/универсального graph serializer.

Runner: --checkpoint/--checkpoint-at/--load, absolute --ticks, recorded continuation. Fixture с checkpoint-at сначала записывает свой полный план, затем сохраняет actual playback с cursor; synchronous C# стек не сериализуется. Godot получает общий save/load/backup, autosave30/pause/focus/выход; desktop smoke проверяет file restore на12/34/55/60.

Добавлены132 текущих xUnit-теста (26 новых случаев к106), однокомандная headless регрессия и [HEADLESS_TESTING.md](HEADLESS_TESTING.md) с точным ответом о тестировании ядра/всей экономики без графики. Gameplay RulesVersion equipment-1 сохранён; игровой hash/баланс не менялись. Версии checkpoint отдельные.

## Что действительно проверено

| Проверка | Фактический результат |
| --- | --- |
| Locked restore + Release build | Успешно,0 warnings/errors, SDK10.0.401 |
| xUnit через встроенный test assembly runner | **132/132 passed**,0 failed/skipped |
| Все5 ранее реализованных сценариев |62/32/465/291/200 ticks, числовые ожидания/инварианты сохранены|
| Current-state load / дальнейшие тики | Перевозка с грузом, обычная/личная machine добыча, production/extraction WIP, trade window, land reservation, рост/credit/pending wage; continuous и loaded одинаковы |
| Actual file backup/corruption | Replace на desktop FS выполнен; main/backup проверены, corrupt default отказ, явный backup доступен, good backup не перезаписан corruption |
| CLI save@55/load→62 и save@101/load→200 | Совпали final-state, **все tick hashes**, commands/events/ledger/metrics/labor; manifests сохранили правильные labels |
| Однокомандная headless suite | Успех, включая restore/build/tests/5runs/2save-load pairs/independent verifier;12.613s в данной среде |
| Godot .NET4.7.2 Linux import/smoke | Все62 hashes; **actual save/load на12/34/55/60**, искусственная lifecycle pause/resume |
| Спецификация / diff | Исходный SHA-256 сохранён, diff --check успешен |

[Метаданные проверок](verification/p12/checks.json) фиксируют commands/timeouts/exit codes/counters/hashes/размеры. В данной среде xUnit запускался как `dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll`; успешный локальный dotnet test не заявляется. Remote Core/Windows CI подтверждаются фактическими ссылками в PR, отдельно от таблицы локальных запусков.

Примеры размеров self-contained checkpoint:125435 bytes для cycle@55,185708 для equipment@101. Это measurements малых fixtures, не обещание размера/скорости длинной партии. Крупные histories требуют последующего профилирования/compaction.

## Границы и незавершённое

Сохранения покрывают **существующие** системы. NPC/controller state P13, полный рынок/доставка/выборы P14–P21 и LLM ещё не реализованы; каждый добавляемый модуль расширяет DTO/валидацию/load tests. `final-state.json` всё ещё inspection, загрузка идёт из отдельного --checkpoint файла. Внешний неподтверждённый JSONL tail не исполняется автоматически; snapshot содержит свой committed prefix/accepted inbox.

Godot пока technical playback, не свободная ручная игра. На телефоне pause/kill/resume и file replace не проверялись, новый APK/готовый Windows EXE не экспортировались. Android device gate остаётся открыт. Windows CI после успеха подтверждает desktop replay/file loading, а не Android или реальное аварийное выключение носителя.

## Как проверить локально и что дальше

`python scripts/check_headless.py --output artifacts/headless` собирает/тестирует все готовые экономические системы и восстановление без Godot. Без Python достаточно .NET build/xUnit/runner команд в HEADLESS_TESTING.md. Полная будущая экономика будет запускаться тем же Core/runner; headless сохраняет игровые сроки, а не телепортирует товар.

Следующая итерация — **P13**,3 алгоритмических NPC: общие команды/allowlist observation, utility/маленькие plans, восстановление при cash0/блокировках, goals/plans/trust/coefficient state в checkpoint и new autonomous headless fixture. Это первая автономная тестовая экономика, ещё без полного рынка/политики. Не добавлять одновременно P14–P21/LLM/крупный UI. Весь месячный объём сохранён.

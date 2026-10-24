# Пакет B: восстановление последовательных B1/B2

Автор: Шелестов Максим Евгеньевич.24.10.2026, Europe/Samara.

Пользователь явно запросил B1, затем B2 в одном продолжении. Это заменяет
промежуточную stop-точку промпта B1; C не начинать. B1 отдельный gate/commit/PR,
B2 отдельная ветка поверх проверенного B1 (если не слит), отдельный PR без merge.

База fda7203 — merge A2/PR57, исходное дерево чистое. A2 dependency semantic CLI
с source+A1 прошёл exit0. Ветка feat/vnext-package-b1. Scratch /workspace/work/delsk-b1;
env /workspace/work/delsk-tablets-v1/env.sh, SDK10.0.401/Godot4.7.2 уже доступны.

Этап1 B1: аудит и первая реализация. Hosting OrdinaryNewGame.Resolve без мира/
Algorithmic/LLM; immutable ControllerAssignment в Core, concrete factory
AutonomousConfiguration.ForOrdinary в Algorithmic. Options strict3..10, default3,
source seed, human binding. Старые пять карт требуют расширить не только spawn
pool, но и число shared shop plots (Core requires shops>=actors). Старые места/
ресурсы/терминалы не двигаются. Legacy count4/default seed сохраняет позиции,
иначе startup SHA rotation; public runtime navigation использует прежний GridMap.

Core optional initial config: StartingTreasuryMinor(default0), Labor.MaximumPopulation
(defaultnull), NewGame provenance(defaultnull); все JsonIgnore default/null для
совместимости old hash/save. Treasury входит в InitialMoney и restore-ledger;
cap входит в точную growth schedule/restore/invariants. Новые data fields имеют
validators; модели мира/индивидуальных работников нет. Версии не повышались:
обратная совместимость optional fields ещё должна быть доказана regression.

UI count SpinBox/summary RU/EN, сохранение across rebuild, тот же resolver для
preview/start. Controller attach/hybrid runtime прежние; load берёт saved human/
roster. Цвета/имена на11, scroll для всех voting buttons. Core/client build прошли.
Проверки НЕ завершены. Далее meaningful B1 tests, CLI3/6/10/save/replay/measurements,
native Godot probe, A1/A2/full headless, отчёт/commit/PR. Потом B2 по полному промпту.

B2 ещё НЕ реализуется: source maps имеют corridor7488 (>legacy PlotID limit1000).
Вариант: corridor без purchasable Plot (BuyPlot/Build требуют existing plot),
геометрию/industrial lots/spawns/civic metadata сохранить optional typed config;
реальные building-blocked cells в Core. Это только предварительный ориентир,
перед B2 сверить topology/source и не считать PNG реализацией.

Этап2 B1 gate: завершены1028ordinary (30новых), A1process43/A2process49,
full headless102exports/14replays/69pairs, runtime35process3/6/10/voting599/load602,
Linux1132hashes/35restores/stoppedfinal10, ordinary audit. Native3/6/10 RU/EN800x720
все exit0 + clean logs. Probe использует root input coords для embedded popup;
90s multi-window lifecycle против отдельного smoke30s, fresh per-attempt user data.
Старый A2 save tick50 exact hash8e1a85419d83a0581dcc71e20db6a7dc9aca3ad6dfc5432bad91a6ffb6efd7a9.
Developer gate1034tests и forbidden Release прошли, Python lab37/37. Native3/6/10 exit0.
B1 готов к commit/push/PR, затем новая B2 branch поверх B1, не merge. B2 ещё не кодировался.

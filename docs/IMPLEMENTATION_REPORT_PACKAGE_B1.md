# Пакет B1: обычная партия на3–10 NPC

Автор: Шелестов Максим Евгеньевич.24.10.2026.
Основа fda7203, merge A2/PR57. Дерево до работы чистое, A2 semantic dependency
проверен реальным CLI. Полный supported subset A2 не стал default экономикой.

## Реализация и граница

Один Hosting resolver готовит immutable scenario/controller assignments без
мира, HTTP и конкретных политик. Клиент и новый CLI `--new-game` используют его;
Algorithmic создаёт отдельные настоящие policies/settings/checkpoints для всех
NPC. Existing research до15 и explicit сценарии не ограничены ordinary диапазоном.
Человеческий actor связан явно, сложность применяется ко всем NPC; общий Hybrid
runtime и его pending/save barrier сохранены.

Меню: native SpinBox3..10/default3, total count, RU/EN, сохранение при смене
карты/языка, preview без мира, прежние replacement/Continue. Пять старых карт
получают11проходимых стартов и достаточное число общих торговых мест без
переноса прежних объектов/ресурсов. Версионированная независимая seeded rotation
не расходует World PRNG. Старый default4 сохраняет позиции. Прокрутка кандидатов,
имена с actor number, цвета дополнительных actors, полная debug legend.

Агрегаты/команда/ограничения описаны в [ORDINARY_NEW_GAME.md](ORDINARY_NEW_GAME.md).
Добавлены optional initial `StartingTreasuryMinor`, `Labor.MaximumPopulation`,
`NewGame` provenance: DTO/validators/hash/checkpoint ledger/capped growth вместе.
Отсутствующие/default поля JsonIgnore, старый World18/Rules production-recipes-1/
Session2/Envelope2 сохранён. Прежние save читаются без пересоздания roster или
чтения нового внешнего профиля. Казна включается в initial money один раз.
Новый capped runtime доказывается тестом роста/restore/forged checkpoint rejection.

## Фактическая приёмка

| Проверка | Результат |
|---|---|
| Locked restore/build Release | exit0,0warnings/errors |
| Unit/integration |1028/1028,0skip;30новых B1 cases |
| Все counts3..10 ×5старых карт | scenario/controller validation, уникальные старты/IDs, без подаренной собственности |
| Невалидные options |2/11/fraction/null/type/unknown/duplicates/overflow явно reject |
|11actors | strict majority/ровно50%, текущая workforce, трудовая fairness при дефиците и перестановке,10capital suppliers, независимые accounts |
| Privacy | own/bilateral support, третьи actors не получают условия |
| Hybrid10 | protocol fixture, один shared physical slot; pending load не requery; не реальная LLM |
| Real CLI3/6/10 |35process checks:180ticks каждый, все NPC действуют; save23/direct load/replay exact, независимые balances; save599 перед выборами/load602 |
| A1/A2 process gates |43/49, exit0; supported projection/WIP/replay не сломаны |
| Полный headless |102exports/14replays/69comparison pairs, все24runner сценария |
| Старый A2 save | load tick50: хеш8e1a85419d83a0581dcc71e20db6a7dc9aca3ad6dfc5432bad91a6ffb6efd7a9 сохранён |
| Developer/lab |1034Developer tests, forbidden Release ожидаемо отвергнут;37Python tests |
| Ordinary client audit/import/Linux smoke |exit0;1132tick hashes+stopped final10;35restore boundaries |

Native оконная проверка выполняется `scripts/check_ordinary_ui.py`: клавиатура/
клики, count3/6/10, RU/EN800×720, каталог/preview без session, Start, последний
реальный Actor_N и caption, Cancel replacement, Continue и свежий disk load.
Все3native lifecycle пройдены (exit0, Godot logs без ошибок), сохранены screenshots и native-check.json.

Bounded build180s/suite120s/import120s/smoke30s; каждый runtime chunk300s.
Комплексный native lifecycle90s (несколько окон+два старта и software GL),
отдельный smoke30s сохранён. Первые попытки native выявили ошибку координат
embedded popup в probe; исправлен input dispatch, продуктовые сигналы не эмулируются.
Неверная пробная команда dotnet test была заменена штатным запуском test executable.

Измерение10NPC/180ticks: average13.6844ms, p9520.6864ms, checkpoint1,154,005bytes,
encode229.95ms/decode448.28ms, working set230,486,016bytes, exact restore hash.
Включены controllers/invariants/sample/hash и JIT; software headless/облачная
машина — это не hardware FPS и не доказательство превосходства Hard.
Логи/TRX/JSON: /workspace/work/delsk-b1/{checks,headless,runtime-verified,native},
старое сохранение не перезаписано. Повторяемые скрипты находятся в репозитории.

P62 ordinary portion закрыт; пространственная проверка
новых карт — B2/P68. Здесь B2/C/XP/цеха/новая стратегия/автопарк не реализованы.
Windows/Android hardware, реальные модели и длительный баланс не проверялись.

GitHub Windows CI: ordinary import/hash и ExportRelease audit прошли; isolated HUD
editor import дважды завершил загрузку ресурсов, но процесс не вышел до120s.
В test-only importer явно добавлен --quit; deadline/ошибки/17+17 HUD checks сохранены.
Итог повторного CI отмечается по реальному результату, не по наличию исправления.

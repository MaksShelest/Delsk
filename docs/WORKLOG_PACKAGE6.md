# Пакет 6 + планшет лесопилки: точка продолжения

Автор: Шелестов Максим Евгеньевич. Обновлено20.10.2026 (Europe/Samara).

## Задание и границы

Пользователь разрешил пакет6 из §12.1 DELSK_NEXT_STAGE_PLAN.md и интеграцию приложенного планшета лесопилки. В нормативном пакете6 **P51–P53 и P56** (старый краткий итог пакета5 упустил P56). Без P57+. После каждого крупного этапа обновлять этот файл и фиксировать проверяемый commit. Никаких субагентов не требуется. Один итоговый PR, без merge.

## Текущее состояние

- База: слитый PR#41, `66de3a4`, рабочее дерево до начала чистое. Ветка `feat/package6-npc-sawmill`.
- До изменений: World18/Rules production-recipes-1, Session2/Envelope2;835 ordinary/841 Developer. Полноценный utility-v2, P22 adapter/runtime/fallback уже существуют; не писать второй executor или HTTP runtime.
- Архив сохранён в `docs/design/sawmill-v1/source.zip`, оригинальный README рядом. Это дизайн с демонстрационными данными, не спецификация экономики. Подключать именные узлы к реальным проекциям/командам; demo.gd/project.godot не импортировать как игровую логику. Шрифт Nunito с OFL обязателен при переносе.
- Этап1 P51 реализован: PolicyProfile.cs, общий legacy-v2 / modular-v3 executor, Easy/Medium/Hard, canonical module versions и experience schema1 без фиктивного обучения. Исторические fixtures остаются Legacy и checkpoint2; новые сложности используют checkpoint3. Full Release:841/841,0 failed/skipped; лог checks/p51-suite.*, TRX p51-suite.trx. Проверены exact continuation всех3, reject unknown module version и различие recipe decision Easy/Medium. Hard имеет ETA cost, расширенный forecast — следующий P53.
- Этап2 P52 реализован: HiringEconomics/EconomicWages, публичные Labor/Handling/WageExponent в ControllerObservation.Economics, оценка собственного сырья не0, конечного спроса/места/кадровой задержки/оплаты всей команды/транспорта, reserve/cooldown, отдельный политический лимит с учётом proposal+commitment один раз. Economics module2; прежний modular economics1 явно отклоняется, Legacy2 не меняется.15 целевых tests прошли, полный suite —850/850 (checks/p52-suite.*, p52-suite.trx).
- Этап3 P53: bounded forecast (12 кандидатов/глубина2/горизонт120/32 оценки по умолчанию), конечные опубликованные возможности, альтернативная стоимость/резерв/штат/маршрут/сроки, disjoint продолжения; trace сохраняется, planning module2. Full856 tests до регрессии profiler, затем7 целевых P53 прошли. Paired9×600, seeds42/43/44, balances/public audits и Hard save/load/replay: все прошли. В profiler исправлен конечный voting barrier (регрессионный тест). Метрики: docs/verification/package6/paired-results.json. **Превосходство Hard не доказано**: капитал относительно Medium −6939/+1671/0, занятость0 у всех, просрочек0. Это короткая стартовая серия, не доказательство лучшей долгосрочной стратегии/формального банкротства.
- Этап4 P56: программная интеграция готова (LocalGameModel/HybridController/ModelRuntime и Godot, новые-game сложности/гибрид/проверка tags, host barrier, bounded retry/UserFallback, save/load metadata).39 целевых tests и полный867/867 прошли; Debug client build0 warnings/errors. Restore сначала отказал из-за недоступного NuGet audit в sandbox, повтор с разрешённой сетью прошёл. **Реальная приёмка P56 открыта**, точная процедура docs/LOCAL_MODEL_GAME.md.
- Этап5 завершён: supplied assets/scene/OFL и SawmillTabletPresenter над прежними actions/access; desktop≥980, ниже читаемая компактная карточка. Help/Close/Time общие, gear/source/destination открывают прежние routes. Оконный portable gate scripts/check_sawmill_ui.py: RU/EN/no-truck→purchase/production Stop-Resume/queued-on-pause/800×600. Реальный HTTP503-стенд --mode model-errors:6 запросов/3 retry/3 UserFallback; конфигурация/RetryCount сохранены; это не настоящая модель.
- Финальный gate:868/868 ordinary и874/874 Developer,0 failures/skips; Debug/ExportRelease build,5 DLL audit без Developer, отрицательная Developer Release rejection. Full headless102exports/14replay/69pairs, отдельная third-map900/load/replay, paired9×600. Последние изменения retry budget дополнительно покрыты868/874 после полного baseline gate867. Linux ordinary+isolated Developer1132 hashes + stopped10/35 restores. PCK содержит sawmill scene/fonts/OFL, Developer/obj отсутствуют. Спецификация SHA e416b331a8353d5f39d8ff042288ad2493d39aae2c506a40ecc3f30de56fd56c не изменилась.
- Артефакты/исходные digest/краткие логи: docs/verification/package6; полный отчёт docs/IMPLEMENTATION_REPORT_PACKAGE6.md. Scratch /workspace/work/delsk-package6: final868.trx, developer-final/tests.trx, headless/, maps/, paired-final/, sawmill-done/, model-probe/, platform-done.json, developer-platform-done.json, checks/. HTTP-стенд и screenshots не подменяют реальный model gate.
- Коммиты стадий:5610a3d recovery/source archive,62e4ef6 P51,8df16c5 P52,af8a8af P53,52ebc0a P56 software. Итоговый UI/gate commit8a8279a. Ветка отправлена, PR#42 открыт: https://github.com/MaksShelest/Delsk/pull/42 (base feat/economic-cycle-p00-p05), не merged. Remote base повторно проверен:66de3a4 не изменился. Эта публикационная заметка — отдельный docs commit; исходники и результаты gates не менялись.
- После публикации работа не начинается заново: открыть ветку/PR и этот файл. Следующий технический шаг — реальная приёмка P56 по LOCAL_MODEL_GAME.md и более длинная paired P53 на активных производственных мирах. Пакет7/P57+ не начинать до явного следующего задания; P56 целиком и преимущество Hard не объявлены принятыми.
- Реальная модель в этой среде пока недоступна: ollama/gguf не найдены в workspace, loopback11434/api/tags connection refused (bounded3с). P56 реальную приёмку нельзя объявлять выполненной по историческому Windows P23; программную интеграцию и тестовый runtime реализовать, если модель останется недоступна — оставить точную локальную процедуру.

## Этапы

1. P51: модули/3 сложности/версии и restore, затем gate и commit.
2. P52: маржа/найм/зарплата/резервы с объяснимыми решениями, gate и commit.
3. P53: bounded planning + paired measurements, gate и commit.
4. P56: подключение прежнего runtime в обычную игру, ошибки/ожидание/fallback/save-load; реальный запрос только при фактической доступности модели. Недоступную проверку явно оставить открытой.
5. Планшет лесопилки по архиву + RU/EN/права/пауза, общий bounded gate, отчёт/push/PR.

## Воспроизведение и восстановление

- Начать с `git status --short --branch`, `git log -5 --oneline`, затем этот файл и AGENTS.md. Не начинать заново законченный этап. Незакоммиченные изменения сначала изучить.
- Среда: `source /workspace/work/delsk-package6/env.sh` (копия env пакета5), SDK10.0.401; Godot4.7.2 Mono в `/workspace/.tools/godot/`. Scratch/logs: `/workspace/work/delsk-package6/`.
- Каждый build/test/runner через `python3 scripts/run_check.py --name NAME --timeout SEC --log-directory /workspace/work/delsk-package6/checks -- COMMAND ...`.
- xUnit: исполняемый `tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll`, не пустой `dotnet test`. Не запускать несколько полных suites одновременно (180с при конкуренции CPU).
- Публикация разрешена; git author/committer: Шелестов Максим Евгеньевич /314023140a@gmail.com. Не класть модели/кеши/секреты в git.

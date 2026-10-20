# Пакет 8 — восстановление работы

Автор: Шелестов Максим Евгеньевич. Начало20.10.2026 (Europe/Samara).

База: `370673e`, слитый PR#43, чистое дерево. Ветка `feat/package8-research-acceptance`.

1. P58: локальный исследовательский интерфейс над существующим Research.Runner, строгий план факторов/копии профилей, устойчивая очередь, отдельные результаты, сравнение. Проверить UI→CLI equivalence, границы паузы/ошибки/повторный запуск и независимый negotiation factor. Сохранить промежуточный commit.
2. P59: парные ограниченные серии и реальные Linux измерения, независимая проверка потоков/балансов, явные пределы выводов. Не менять баланс без измеренной причины; не обещать Hard efficacy. Сохранить отчёт/commit.
3. P60: полный bounded regression, Godot/обычная и Developer сборки, итоговая сводка ВСЕХ пакетов, ограничения, push/один PR без merge.

Использовать `/workspace/work/delsk-package8/env.sh`, scratch/checks там же. Старые результаты — package7; новые gate не подменять старыми. World18/production-recipes-1/Session2/Envelope2 сохраняются. Исследовательская оболочка не включается в обычный клиент и не содержит второго симулятора. Пауза очереди — между ограниченными заданиями, не приостановка ОС/экономических тиков внутри runner. История исследований приватна, loopback interface — только локальный исследователь.

Открыто до начала: новая реальная модель P56, эффективность Hard P53, Windows EXE/Android устройство, hardware FPS/аудио, рост полной истории P27. Эти gates не закрываются документом или fake-моделью.

## Этап1 — P58, реализация

Добавлены scripts/research_lab.py, research_lab_server.py и research_lab.html: stdlib-only loopback UI/CLI над общим runner, независимые snapshots всех входов, allowlist факторов/диапазоны/seeds/повторы/состав/сложность/real hybrid, C# preflight всех комбинаций до очереди, atomic queue/recovery/attempts, пауза между bounded jobs, исходные экспорты/CSV-summary/агрегаты/paired differences. Один файловый lock на host. Никакого нового Core/3D/публичного журнального endpoint. Процедура — RESEARCH_LAB.md.

Независимый nullable controller flag NegotiationsEnabled: default/null не меняет прежнюю сериализацию. Off не выключает trade/politics; BuySupport использует прежний прямой ProposeSupport. Первоначальный тест обнаружил обходной ProposeTrade в PoliticalGoals — исправлен. Runner validate-only теперь действительно создаёт обычные/гибридные контроллеры и проверяет их settings, без Step/HTTP inference.

Focused C#12/12; Python queue/factor tests9/9. Настоящий Chromium UI→CLI/save59/load/replay120 прошёл (browser-v2; отдельный повтор после расширения списка downloads идёт). Из UI проверены неверная комбинация до queue, pause/resume, скачивание CSV, access403; полные экспорты совпадают через штатный verifier. Полный bounded headless gate запущен отдельно; результат записать перед commit. Не считать fake executor unit-тестов моделью или симуляцией.

Этап1 gate завершён:880 ordinary tests,102exports/14replay/69 comparisons; полный scripts/check_headless.py exit0, включая independent balances/privacy/model-safety. browser-compact — повторная реальная UI/CLI/save/load/replay проверка после улучшения таблиц/списка файлов; exit0. Python9/9, focused C#12/12. P58 программная приёмка закрыта; model availability не обещается preflight. Следующий этап2 — только измерения/анализ P59 и profiling; не перенастраивать экономику ради ожидаемого победителя.

## Этап2 — P59, текущая точка

Stage-only profiling в Hosting: optional StepMeasured, controller/core/sampling CPU wall phases; SessionProfiler измеряет process CPU/allocated bytes/peak managed+working set и encode/decode; callback не сериализуется, обычный Step не включает Stopwatch при отсутствии subscriber. Старый deterministic profiling test расширен; focused EconomyProfile gate пройден. World/rules/checkpoint версии прежние.

Лаборатория поддерживает algorithmic profile:true. research_metrics считает capital/cash/employment/payroll/idle/transport/late/model latency и **операционный денежный результат** с явным определением (не accrued net profit, не escrow/refunds).12 Python tests. Режимы/планы — docs/research/package8-{difficulties,negotiations,recovery,long-history}.json. summarize_lab.py проверяет каждый экспорт независимыми штатными money/goods/public-capital verifiers и сохраняет компактный воспроизводимый отчёт.

Paired difficulties1200, seeds42/43/44,9 фактических прогонов: Hard−Medium capital = −91938/−48677/0; Hard output0 во всех трёх. Это **отрицательный результат**, не закрытая эффективность Hard. На Easy/Medium активность зависит от seed; текущие цели/веса предпочитают добычу/продажи, полный баланс фабрик не доказан. Экономика не перенастраивалась ради нужного победителя.

Запущены последовательно pilot17 ячеек (9difficulty+6negotiation+2zero-cash), отдельный long-history5000. Scratch `/workspace/work/delsk-package8/study-*`; все bounded, точные log metadata в checks. Дождаться результата, independent summarize gate, затем isolated Godot VisualModelsProbe с новыми измерениями geometry/cache/stock/frame/memory (тестовый класс не в ordinary!). GPU timing остаётся null, Xvfb не hardware60FPS. После этапа2 commit; этап3 — final headless/developer/client/Godot gates, отчёт и push/PR.

Этап2 завершён в доступном исследовательском объёме: все18run и36 independent money/public checks exit0, профиль5000 с совпадением world/controller hash. Long save58.6MB/decode15.7s/working set3.94GB/peak4.95GB — открытая проблема истории, не mobile pass. Actual Godot geometry/cache/stock и60 rendered-frame timing прошли без ошибок. Таблица/команды/границы — RESEARCH_RESULTS_PACKAGE8.md, компактные исходные цифры/SHA — verification/package8/research-study.json. P59 **не объявляется полностью закрытым**: Hard efficacy/ROI/универсальный баланс/target hardware остаются. Для P60 уже запущен final-engine (headless затем Developer); не прерывать, логи в package8/checks. Следующий шаг: client Debug/ExportRelease/audit/ordinary+Developer Linux hash, UI RU/EN/compact, evidence/cumulative docs/push/PR.

## Этап3 — P60 и итог

Финальный gate завершён:880ordinary/886Developer/12Python;102exports/14replay/69pairs, S7/model-off/independent invariants; ordinary+Developer Godot1132hashes/stopped10/35restores; Debug/ExportRelease0warnings/errors,5DLL audit, PCKinspection, RU/EN/compact bakery+sawmill, HTTP503 retry/UserFallback и Dummy audio. Все exit0, Developer Release ожидаемо1. Лаборатория повторно проверена настоящим браузером после финальных правок: invalid/missingseed, profile validation, pause/restart/download, CLI/save/load/replay, token/Host/Origin403. source/log/evidenceSHA — verification/package8/gate-summary.json.

Обновлены AGENTS/статус/план/архитектура/решения/README. Полный накопительный итог ВСЕХ пакетов, невыполненное/отличия/сверхплана — NEXT_STAGE_SUMMARY.md; текущая передача IMPLEMENTATION_REPORT_PACKAGE8.md. P58 закрыт software. P59/P60 и весь план **полностью не закрыты**: Hard/ROI/история и реальные model/device/hardware gates. Следующий небольшой шаг — Hard seed42/43 goal-ranking, затем paired контроль на новых seeds; также обязательны история/устройства/модель перед релизом.

До передачи осталось: финальный git diff/source evidence audit; commit, push feat/package8-research-acceptance и один PR к feat/economic-cycle-p00-p05 без merge; записать PR/commit в recovery. Старые failed предварительные проверки не удалялись из scratch, в итоговый pass не засчитывались. Полная спецификация SHA e416b331… неизменна, economy исходный профиль не менялся.

Передача опубликована: code/evidence `f61ad2abac6c479b4fff71bb22bde1c162fd38bb`, предыдущие этапы `a5c2cfe`/`13a24c6`, PR https://github.com/MaksShelest/Delsk/pull/44, base370673e, веткаfeat/package8-research-acceptance. Push успешен; PR открыт, не merge. Эта заключительная запись — documentation-only. После неё проверить HEAD==origin и чистое дерево. Рабочие большие данные остаются в /workspace/work/delsk-package8, долговременная краткая доказательная база — docs/verification/package8. Начинать продолжение с NEXT_STAGE_SUMMARY и открытых Hard/history/model/device gates; не переисполнять готовые пакеты1–8 целиком.

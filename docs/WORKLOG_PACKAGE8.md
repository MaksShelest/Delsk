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

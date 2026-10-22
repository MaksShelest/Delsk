# Упрощение исследовательской лаборатории — контекст

Автор: Шелестов Максим Евгеньевич. 22.10.2026 (Europe/Samara).

## Этап 1 — анализ и запуск

Чистая исходная ветка 758ea62; fetch обнаружил слитые PR49 (8a83f18) и PR50
(25d48a2, заменяемые скины/образы). Рабочая ветка
`feat/research-lab-easy-start` создана от актуального 25d48a2.
Временная ветка `temporary/legacy-design-758ea62` с архивом дизайна не относится
к разработке и не включается в неё. Её удаление ждёт подтверждения скачивания.

Фактически P58 уже имеет stdlib loopback UI, persisted queue/lock, общий runner,
проверку всех вариантов до запуска, attempts/retry, метрики и save/load/replay gate.
Проблемы использования: ручная сборка, обязательный output, ручной URL, JSON для
обычных опытов, раздельные queue/start, непонятные имена метрик, нет ZIP всей серии.

Объём: Windows launcher (py -3 с fallback python), bounded подготовка .NET runner,
открытие браузера/повторное открытие работающего host, guided presets без JSON,
одна кнопка запуска, понятные результаты/ошибки и экспорт серии. Существующая
очередь/симуляция/приватность/CLI остаются авторитетными. Без Godot/LLM requirements
для алгоритмических исследований; без изменения экономики, save или policy.

## Этап 2 — проверка и передача

После реализации: Python unit/HTTP tests, реальный Chromium→runner, простой запуск,
повторное открытие/порт/пауза/retry/экспорт, прежний CLI/save/load/replay и access gate.
Обновить RESEARCH_LAB/README/DECISIONS/AGENTS, записать фактические результаты,
commit/push/PR без merge. Windows double-click проверяется локально владельцем;
не выдавать Linux исполнение за аппаратную Windows-проверку.

## Выполнение и проверка

Оба этапа завершены в Linux: launch helpers/presets/формы/автосводки/ZIP/логи/shutdown,
без изменения Core/Hosting/клиента. Восстановление: README и RESEARCH_LAB описывают
ResearchLab.cmd, API/UI используют прежний Lab. Файлы источников и точные SHA в
verification/research-lab-easy-start/gate-summary.json.

Финальный gate:23Python,891.NET, launcher/reopen/lock/SDK/Unicode/shutdown,
12preset-map/21C#validation, Chromium640/form/missing-model/invalid/queue/pause/resume,
CSV+ZIP/privacy,7exports/4pairs CLI/save/load/replay. Логи scratch
/workspace/work/delsk-lab-easy/checks; браузер browser-pass; launcher launcher-pass.
Windows .cmd/hardware и реальная LLM не проверены; Godot не менялся/не запускался.
Отчёт IMPLEMENTATION_REPORT_RESEARCH_LAB_EASY_START.md. Осталось перед передачей:
проверить diff/author, commit+push и открыть PR к feat/economic-cycle-p00-p05 без merge.

Передача опубликована: основной commit `89a600768179ef080ba44d3d224c033ec9a84c3a`,
ветка `feat/research-lab-easy-start`, PR https://github.com/MaksShelest/Delsk/pull/51
к `feat/economic-cycle-p00-p05`. Push подтверждён, PR открыт без merge. Эта запись —
documentation-only; результаты локальных gates выше не являются результатами remote CI.

## Исправление Windows CI после передачи

GitHub PR51: Core/research прошёл, Windows run39777598124 упал на
`hud-god-import`, exit3221225477 (0xC0000005), stderr пустой; job119820402998.
Один явный повтор (attempt2/job119827059326) дал тот же сбой. Предыдущий
код89a6007 прошёл Windows; между ним и332296b изменены только8строк документации.
Отказ не связан с Python лаборатории или ошибкой компиляции приложения.

В одноразовом HUD/God stage включён штатный ProjectSettings
`editor/import/use_multiple_threads=false`. Закреплённый Godot4.7.2 читает этот
флаг через GLOBAL_GET в editor/file_system/editor_file_system.cpp. Рабочий
project.godot/экспорт/правила игры не меняются. Nonzero exit остаётся fatal;
повторные попытки/пропуск ошибок в скрипт не добавлены.

Полный bounded Linux check_hud_god_ui.py с serial import прошёл:17Debug+17
ExportDebug native checks, restore/build/import и строгая проверка логов. Evidence
verification/research-lab-easy-start/hud-import-fix.json; полные логи
/workspace/work/delsk-lab-easy/ci-fix. Перед закрытием исправления нужен зелёный
Windows workflow на новом commit в том же PR51, без merge.

# Итерация P13: автономные алгоритмические участники

Автор: Шелестов Максим Евгеньевич. Дата: 14.10.2026.

## Объём и исходное состояние

Работа от пользовательского merge PR№6: c1b07e8ae015ddbfb45c8d85ebe3f4e8d32b2a1b. Отдельная ветка feat/autonomous-p13; существующие файлы/работа сохранены. P13 завершён в границах начальных контроллеров: полный рыночный/политический baseline остаётся P21. Спецификация v0.3 не менялась, SHA-256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376.

## Что реализовано

- Core IController и отдельный вложенно immutable ControllerObservation: публичная карта/контент/условия/занятость+own хозяйство; нет foreign accounts/stock, мирового seed/hash/журнала. Agents.Algorithmic зависит только от Core; Hosting остаётся Core-only.
- Utility и finite plan executor: восстановление cash, ручная добыча/подход, подвоз сырья, выпуск/разгрузка/сбыт, покупка земли/стройка, техника/automatic режим, зарплатная эвристика. Вся бухгалтерия/найм/перевозка/работа выполняется Core после обычных команд. NPC ждёт реального окончания busy/торгового hold, accepted не считается мгновенной доставкой.
- Хост фиксирует все observations до выбора/подачи действий, feedback только свой. Общий Core budget одинаков; baseline выбирает1 действие/тик. При отказе остаток плана очищается и цель временно исключается; Idle при отсутствии feasible goal. Private SplitMix64 отдельно от мировых streams.
- Живые контроллеры сохраняют settings,goals,reason,steps/cursor,pending command,timers,PRNG,trust/counters. Session schema2, concrete restore factory в runner; старые Session1 читаются. Decode без RestoreControllers запрещает дальнейший Step, политики не исчезают. Хеш контроллеров отдельно от прежнего хеша мира. Command-only replay повторяет мир без вызова policy; load продолжает сохранённую policy.
- Автономный CLI и два JSON:1 idle human+3 NPC,2 цепочки, старт без предприятий; --ticks/seed/scenario/controllers/output. Экспорт agents.csv/decisions.jsonl/controllers.json, manifest/summary дополнены. CSV energy_minor переименован treasury_minor, поскольку казна включает землю/технику/энергию.
- Регрессия/CI расширены автономным прогоном и save/load; пользовательская инструкция объясняет C# versus Python, параметры и результаты.

## Что фактически проверено локально

| Проверка | Результат |
| --- | --- |
| Locked restore и Release build Delsk.sln | Exit0,0 warnings/0 errors; новый project/lockfiles |
| xUnit standalone | **148 passed**,0 failed/0 skipped; прежние132 и16 новых cases |
| Бounded общий headless набор | Exit0,43.556s:5 fixtures+autonomous,3 checkpoint/load пары, независимые баланс/capacity/pop проверки |
| Exact continuation | cycle@55/equipment@101/autonomous@350: побайтно совпали world/ledger/events/commands/metrics/labor/controllers/decisions/agents |
| Controller tests |3 NPC построили/произвели/продали/купили технику; cash0; отсутствие invalid loop при unreachable; competitive land rejection/replan; одинаковый исход при перестановке controllers; own allowlist/foreign cash privacy; budget; pause/replay;5 direct checkpoint cuts@1/55/120/350/701 |
| Legacy P12 | Загружен настоящий предыдущий schema1 файл cycle.save.json; тот же мир/ledger/events/commands/labor и tick hashes; отдельно учтено новое имя CSV-колонки |
| Дополнительные seeds7/99 | По1000 тиков,3 производства/3 машины в каждом; независимые деньги/товар/вместимости/pop passed |
| Godot .NET Linux | Реальная Debug сборка, import+headless scene+log/report verifier exit0;62 hashes прежней fixture, actual save/load@12/34/55/60; lifecycle adapter checked |
| Источник/diff | SHA спецификации прежний, git diff --check без ошибок |

Машиночитаемая компактная запись — [verification/p13/checks.json](verification/p13/checks.json). Полные большие журналы не коммитились. CI jobs добавлены; удалённые статусы смотрятся в checks PR, этот отчёт фиксирует локальные фактические запуски.

## Автономный пилот

Стандартный scenario/policy JSON,seed42,1000 тиков;3.3749885s до экспорта на этой среде. Это total runner time с подготовкой, не CPU профиль и не обещание скорости другого мира.

| Показатель | Результат |
| --- | --- |
| NPC2/3/4 наличные |93475 /70200 /97058 minor units (934.75 /702 /970.58 денег); первоначально200 каждому |
| Хозяйства |3 production sites и3 оплаченные малые машины; все имели выпуск/автодобычу и занятость |
| Выпуск/экспорт |plank81/80, bread50/37; остатки в физических запасах |
| Население |28=21 безработных+7 занятых; начальное20, прирост8 |
| Decision trace |426 записанных turns,3 отказа PlotOwned; новый план после конкурентной покупки |
| Checkpoint@350 |2688136 bytes; полный trace+политики+мир |
| World hash |77c972a5d35ed75220354f00d4185fc0ad52f2fbf27f9a68f5db714c5464012f |
| Controller hash |b9ba6205dec96eb021cb68484d2edde509353b356698a60ae8559d341d1b6488 |

Это проверенный пилот, не статистическое доказательство прибыли/оптимальности/интересной игры. xUnit использует также другой private tie seed; exact численные исходы не обещаны для произвольного сценария.

## Что ещё не сделано или не проверено

- Участнические offers/auctions/получение/поставочные контракты, физическая автологистика, demand1.1, выборы/секретная сделка и поддерживающий их baseline — P14–P21. Конечная наличная закупка P05 не заменяет эти системы.
- Вариативные экономические коэффициенты: сейчас принимается только×1, остальные отклоняются. OwnVictory/HumanDefeat и trust сохраняются, но до политики/договоров не имеют эффекта. P21/P24 сохраняют этот объём.
- Базовый NPC строит1 primary production и покупает1 малую машину; property upgrades/склады/лавочки/сложные цепочки относятся к расширению baseline P21. Эвристика допускает простой/повторный найм, не гарантирует оптимальное использование техники или конкурентную зарплату. Replanning временно блокирует всю цель, что просто, но консервативно.
- Свободный human input/автономная сцена Godot/игровые карточки — P26. Проверенная сцена всё ещё прежняя техническая fixture; из её успеха не следует UI/device pass автономного режима.
- Новый APK/Windows EXE не экспортировались. Реальный Android телефон, suspension/kill/relaunch и длинная партия не проверены; device gate P06 открыт.
- Реальной модели, сравнения режимов, графиков/статистики/профиля RAM нет. Запись полного trace ограничена envelope64MiB; длинные сессии требуют измерения/сегментации. Average time из summary не использовать как чистый CPU профиль, особенно для load/recorded fixtures.

## Передача и следующий шаг

Запуск/настройки/выгрузки/сохранение — [HEADLESS_TESTING.md](HEADLESS_TESTING.md). Без Python:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Следующая ограниченная итерация — **P14**: собственные sell offers/каталог, общий buy window и shortage auction для всех заявок, bounded raises/withdraw/pay-as-bid/partial allocation, stock/cash holds, seeded ties. Сразу включить новые поля в checkpoint/allowlist и проверить save/load внутри окна/раунда. Выдача/возврат/штраф отдельно P15; товар в P14 не телепортировать. Политику/модель/UI одновременно не добавлять; автоматического merge нет.

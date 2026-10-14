# Delsk

Исследовательская экономическая игра с автономными NPC.
Автор: Шелестов Максим Евгеньевич.

Реализован первый консольный экономический стенд P00–P05: ручная добыча, движение по сетке, конечные запасы, производственная партия, числовой штат, зарплата, энергия и продажа столице из физически доставленного наличия. Управление сейчас задаётся проверяемым сценарием; автономные контроллеры добавляются в P13/P21.

## Запуск

Нужен .NET SDK **10.0.401** (x64). SDK закреплён в global.json. Из корня репозитория:

```bash
dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/economic-cycle.json --output artifacts/cycle
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/zero-cash-recovery.json --script recovery --output artifacts/recovery
```

Первый сценарий добывает10 брёвен, переносит их к лесопилке, производит5 досок, переносит доски в терминал и получает оплату после окна5 тиков. Рецепт/зарплата/цены взяты из числовой фикстуры S1 с заменой контрагента на внешнего покупателя P05. Это подготовленное хозяйство для проверки, а не бесплатный старт обычной партии.

Результаты: `manifest.json`, `commands.jsonl`, `events.jsonl`, `ledger.jsonl`, `metrics.csv`, `summary.json`, `final-state.json`. Последний файл — выгрузка для осмотра; загрузка игрового сохранения относится к P12.

```bash
python scripts/verify_results.py artifacts/cycle artifacts/recovery
```

Флаг `--mode paced` выполняет тот же сценарий примерно с одним тиком в секунду; headless не ждёт реальные секунды, но сохраняет пути/длительности. `--seed N` задаёт seed. Повтор записанных команд:

```bash
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/economic-cycle.json --replay artifacts/cycle/commands.jsonl --ticks 62 --output artifacts/replay
```

62 — проверенная длительность этой конкретной фикстуры. Для изменённого сценария используйте tick из его summary, а не это число.

## Проверки

Встроенный xUnit-исполнитель запускает тот же набор без IPC оболочки dotnet test:

```bash
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -parallelMode none -result-trx artifacts/tests.trx
```

На обычной машине также доступен MTP-режим .NET10:

```bash
dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore -- --report-trx --results-directory artifacts/test-results --minimum-expected-tests 60
```

В облачном окружении второй вариант остановился на запрете named pipes; непосредственный запуск xUnit прошёл. Это не отключение/замена тестов. Фактические результаты и ограничения — в [отчёте первого этапа](docs/IMPLEMENTATION_REPORT_P00_P05.md).

Для любой внешней проверки есть ограничение времени и отдельные логи:

```bash
python scripts/run_check.py --name build --timeout 180 -- dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
```

GitHub Actions для ядра выполнен успешно. Godot и Android не нужны для запуска консольного стенда.

## Платформенный стенд P06

Добавлена минимальная сцена Godot 4.7.2 .NET с тем же Core/Hosting. Linux и Windows CI воспроизводят записанные команды и проверяют все 62 хеша. Созданы тестовые Windows x86_64 и подписанный Android ARM64 экспорты. APK ещё не запускался на настоящем телефоне; готовый Windows EXE тоже не проверен запуском.

[Запуск сцены и экспорты](src/Client.Godot/README.md). [Отчёт P06, версии и открытые проверки](docs/IMPLEMENTATION_REPORT_P06.md). P07–P10 добавлены в ядро; следующая итерация — P11: добывающая техника. Полный месячный объём остаётся в плане.

## Земля и развитие P07–P08

Реализованы покупка/бронь земли, атомарная стройка целого footprint, отдельный склад, физическая лавочка, две цепочки wood→plank / grain→bread, два уровня и upgrade из встроенного склада. Новое предприятие имеет нулевой штат и нанимает через кадровую систему P09–P10. Исторический regression-сценарий фиксирует две заранее заданные числовые группы длинным трудовым шагом:

```bash
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/land-and-content.json --script development --output artifacts/development
python scripts/verify_results.py artifacts/development
```

Проверенный результат:465 тиков, обе продажи, три новых здания и upgrade. Материалы стройки отражены в ConstructionInput, все физические запасы и деньги сверены. На этапе P07–P08 прошли64 xUnit-теста; текущий набор содержит83. [Отчёт итерации](docs/IMPLEMENTATION_REPORT_P07_P08.md).

Расширение DTO изменило хеши; platform fixture обновлена по проверенному прежнему циклу. Ранее выданные бинарные сборки P06 содержат прежнее ядро; в этой итерации они не пересоздавались. Игровые экраны строительства/найма остаются P26.

## Документация

- [Источник требований — спецификация0.3](docs/requirements/game_specification.md), сохранён без изменений.
- [Архитектура](docs/ARCHITECTURE.md).
- [Решения и риски](docs/DECISIONS.md).
- [План и фактический статус задач](docs/IMPLEMENTATION_PLAN.md).
- [Инструкции для разработки и Cursor](AGENTS.md).
- [Отчёт платформенного этапа P06](docs/IMPLEMENTATION_REPORT_P06.md).

- [Отчёт P07–P08](docs/IMPLEMENTATION_REPORT_P07_P08.md).

## Числовая занятость P09–P10

Реализованы агрегированные группы, зарплата³ с caps, прирост, задержка ставки, постепенные переходы и неоплачиваемый простой. 83 xUnit-теста проверены; автономные NPC остаются P13/P21. Новый прогон:

```bash
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/labor-dynamics.json --script labor --output artifacts/labor
```

[Windows: загрузка в Rider и Godot](docs/LOCAL_SETUP.md). [Отчёт P09–P10](docs/IMPLEMENTATION_REPORT_P09_P10.md). Godot пока проигрывает техническую fixture; новый найм проверяется в runner/labor.csv.

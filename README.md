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
dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore -- --report-trx --results-directory artifacts/test-results --minimum-expected-tests 30
```

В облачном окружении второй вариант остановился на запрете named pipes; непосредственный запуск xUnit прошёл. Это не отключение/замена тестов. Фактические результаты и ограничения — в [отчёте первого этапа](docs/IMPLEMENTATION_REPORT_P00_P05.md).

Для любой внешней проверки есть ограничение времени и отдельные логи:

```bash
python scripts/run_check.py --name build --timeout 180 -- dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
```

GitHub Actions workflow подготовлен; его удалённый запуск пока не подтверждён. Godot и Android не нужны для запуска этого стенда. Совместимость клиента и экспорт проверяются следующим этапом P06.

## Документация

- [Источник требований — спецификация0.3](docs/requirements/game_specification.md), сохранён без изменений.
- [Архитектура](docs/ARCHITECTURE.md).
- [Решения и риски](docs/DECISIONS.md).
- [План и фактический статус задач](docs/IMPLEMENTATION_PLAN.md).
- [Инструкции для разработки и Cursor](AGENTS.md).

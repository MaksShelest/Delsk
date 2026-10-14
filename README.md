# Delsk

Исследовательская экономическая игра с автономными NPC.
Автор: Шелестов Максим Евгеньевич.

В C# реализованы P00–P13: тики/команды, деньги и конечные запасы, личное движение/добыча, две производственные цепочки, земля/стройка/уровни, числовой найм по зарплата³ с постепенными переходами, зарплата/энергия/неоплачиваемый простой, техника и direct checkpoint/load. Три алгоритмических NPC сами развивают хозяйства без модели. Слот человека в автономном консольном стенде бездействует; свободный игровой ввод/интерфейс ещё не готов.

## Быстрый запуск без Python и Godot

Нужен .NET SDK **10.0.401 x64**, закреплённый в global.json. Из корня репозитория (PowerShell):

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

148 xUnit-тестов проверены локально. Результат runner — статус/тик/хеш/путь в консоли и JSON/JSONL/CSV в `artifacts/run-42`: summary, деньги/товар/численности, agents/labor, decisions/commands/events/ledger, manifest и inspection final-state. Все игровые расчёты и политики на C#; Python только запускает проверки и читает результаты. [Параметры, значение прогона и файлы](docs/HEADLESS_TESTING.md).

`--mode headless` по умолчанию выполняет те же тики без ожидания реальных секунд; перевозка остаётся физической. `--mode paced` ждёт1 секунду/тик. Мир задаётся `--scenario`, политики — `--controllers`; начальные настройки лежат в scenarios/autonomous-*.json.

## Сохранение и регрессии

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --checkpoint artifacts/npc.save.json --checkpoint-at 350 --output artifacts/npc-continuous
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/npc.save.json --ticks 1000 --output artifacts/npc-loaded
```

Checkpoint сохраняет текущий мир и план каждого NPC; load продолжает без повторения первых350 тиков и без offline catch-up. final-state.json — только inspection, не save. Командную ленту можно повторить `--replay PATH --scenario PATH --ticks FINAL_TICK` без вызова контроллеров; это проверка мира, не восстановление их планов.

Если установлен Python, одна команда с hard timeouts выполняет restore/build/tests,5 scripted fixtures+автономный мир,3 пары checkpoint/load и независимые проверки выгрузок:

```powershell
python scripts/check_headless.py --output artifacts/headless
```

Старые scripts: cycle/economic-cycle.json, recovery/zero-cash-recovery.json, development/land-and-content.json, labor/labor-dynamics.json, equipment/equipment-cycle.json. Они сохранились как регрессии. На обычной машине доступен также MTP `dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore`; в этой облачной среде использован встроенный xUnit runner из-за ограничения IPC.

## Границы готовности

Рынок участников/аукционы, частичные сделки/поставки, автоматическая доставка, спрос/волны, выборы и скрытые договоры — P14–P21. Локальный адаптер, серии и анализ — P22–P25. Первая автономная тестовая экономика уже запускается; полный месячный прототип ещё разрабатывается.

Godot .NET4.7.2 подключает общий Core/Hosting и пока проверяет62-тактовую fixture/save/load. Платформенный этап P06 создал тестовые Windows/Android экспорты; APK не проверен на реальном телефоне, device gate открыт. P13 не создаёт новый игровой UI/APK/EXE. [Локальная работа в Rider/Godot](docs/LOCAL_SETUP.md), [клиентский стенд](src/Client.Godot/README.md).

## Документация

- [Неизменённая спецификация0.3](docs/requirements/game_specification.md).
- [Архитектура](docs/ARCHITECTURE.md), [решения/риски](docs/DECISIONS.md), [план/статус](docs/IMPLEMENTATION_PLAN.md).
- [Готовность минимальной версии](docs/PROTOTYPE_STATUS.md), [запуск без графики](docs/HEADLESS_TESTING.md).
- [Отчёт текущего P13](docs/IMPLEMENTATION_REPORT_P13.md), [инструкции разработки/Cursor](AGENTS.md).
- Исторические отчёты: [P00–P05](docs/IMPLEMENTATION_REPORT_P00_P05.md), [P06](docs/IMPLEMENTATION_REPORT_P06.md), [P07–P08](docs/IMPLEMENTATION_REPORT_P07_P08.md), [P09–P10](docs/IMPLEMENTATION_REPORT_P09_P10.md), [P11](docs/IMPLEMENTATION_REPORT_P11.md), [P12](docs/IMPLEMENTATION_REPORT_P12.md).

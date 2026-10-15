# Delsk

Исследовательская экономическая игра с автономными NPC.
Автор: Шелестов Максим Евгеньевич.

В C# реализованы P00–P16, P17a и P17b1–P17b2: тики/команды, деньги и конечные запасы, личное движение/добыча, две производственные цепочки, земля/стройка/уровни, числовой найм по зарплата³ с постепенными переходами, зарплата/энергия/неоплачиваемый простой, техника и direct checkpoint/load. Три алгоритмических NPC сами развивают хозяйства без модели. Слот человека в автономном консольном стенде бездействует; свободный игровой ввод/интерфейс ещё не готов.

## Быстрый запуск без Python и Godot

Нужен .NET SDK **10.0.401 x64**, закреплённый в global.json. Из корня репозитория (PowerShell):

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

281 xUnit-тестов проверены локально. Результат runner — статус/тик/хеш/путь в консоли и JSON/JSONL/CSV в `artifacts/run-42`: summary, деньги/товар/численности, agents/labor, decisions/commands/events/ledger, manifest и inspection final-state. Все игровые расчёты и политики на C#; Python только запускает проверки и читает результаты. [Параметры, значение прогона и файлы](docs/HEADLESS_TESTING.md).

`--mode headless` по умолчанию выполняет те же тики без ожидания реальных секунд; перевозка остаётся физической. `--mode paced` ждёт1 секунду/тик. Мир задаётся `--scenario`, политики — `--controllers`; начальные настройки лежат в scenarios/autonomous-*.json.

## Сохранение и регрессии

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --checkpoint artifacts/npc.save.json --checkpoint-at 350 --output artifacts/npc-continuous
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/npc.save.json --ticks 1000 --output artifacts/npc-loaded
```

Checkpoint сохраняет текущий мир и план каждого NPC; load продолжает без повторения первых350 тиков и без offline catch-up. final-state.json — только inspection, не save. Командную ленту можно повторить `--replay PATH --scenario PATH --ticks FINAL_TICK` без вызова контроллеров; это проверка мира, не восстановление их планов.

Если установлен Python, одна команда с hard timeouts выполняет restore/build/tests,6 scripted fixtures+автономный мир,4 пары checkpoint/load и независимые проверки выгрузок:

```powershell
python scripts/check_headless.py --output artifacts/headless-p17a
```

Старые scripts: cycle/economic-cycle.json, recovery/zero-cash-recovery.json, development/land-and-content.json, labor/labor-dynamics.json, equipment/equipment-cycle.json. Они сохранились как регрессии. На обычной машине доступен также MTP `dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore`; в этой облачной среде использован встроенный xUnit runner из-за ограничения IPC.

## Границы готовности

P14 добавил предложения продажи, окна/дефицитные аукционы и резервы покупки. P15 добавил физическое получение/возвраты; P16 добавил поставочные договоры; P17a добавил первую физическую автодоставку; её остальные части, спрос/волны, выборы и скрытые договоры — P17b–P21. Локальный адаптер, серии и анализ — P22–P25. Первая автономная тестовая экономика уже запускается; полный месячный прототип ещё разрабатывается.

Godot .NET4.7.2 подключает общий Core/Hosting и пока проверяет62-тактовую fixture/save/load. Платформенный этап P06 создал тестовые Windows/Android экспорты; APK не проверен на реальном телефоне, device gate открыт. P14 не создаёт новый игровой UI/APK/EXE. [Локальная работа в Rider/Godot](docs/LOCAL_SETUP.md), [клиентский стенд](src/Client.Godot/README.md).

## Документация

- [Неизменённая спецификация0.3](docs/requirements/game_specification.md).
- [Архитектура](docs/ARCHITECTURE.md), [решения/риски](docs/DECISIONS.md), [план/статус](docs/IMPLEMENTATION_PLAN.md).
- [Готовность минимальной версии](docs/PROTOTYPE_STATUS.md), [запуск без графики](docs/HEADLESS_TESTING.md).
- [Отчёт текущего P14](docs/IMPLEMENTATION_REPORT_P14.md), [инструкции разработки/Cursor](AGENTS.md).
- Исторические отчёты: [P00–P05](docs/IMPLEMENTATION_REPORT_P00_P05.md), [P06](docs/IMPLEMENTATION_REPORT_P06.md), [P07–P08](docs/IMPLEMENTATION_REPORT_P07_P08.md), [P09–P10](docs/IMPLEMENTATION_REPORT_P09_P10.md), [P11](docs/IMPLEMENTATION_REPORT_P11.md), [P12](docs/IMPLEMENTATION_REPORT_P12.md).

## Рынок P14

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --output artifacts/market
```

Scripted S2:51 ticks, лот15 wood, заявки6/8/3, ставки5/6/7 денег → allocations4/8/3 и escrow89. Товар физически остаётся в лавочке, продавец не получил выручку; выдача/TTL/возврат/штраф проверены отдельным pickup сценарием P15. Смотреть market.json/summary/events/ledger. Это проверка торгового ядра; автономные P13 NPC пока используют столицу, расширение их рыночных решений — P21.

Историческая версия P14 — World schema2/Rules market-1: прежние World1 saves отклоняются, автоматической миграции нет. Старые файлы сохраняются и открываются прежней версией из feat/autonomous-p13. Проверки итерации P14 использовали P14-файлы и отдельный Godot save path. [Отчёт и ограничения](docs/IMPLEMENTATION_REPORT_P14.md).


P15 проверяет физическую выдачу несколькими рейсами:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup
```

Результат:52 ticks, из10 wood по500 получено6, остаток4 освобождён; Paid3000/Fine100/Refund1900 minor. Seller13100/buyer6900, общие деньги20000 сохранены. market.json/summary.json показывают counters/deadline/status, ledger.jsonl — выплаты, events.jsonl — получение/просрочку, scenario.json — фактические настройки. [Отчёт P15](docs/IMPLEMENTATION_REPORT_P15.md).

Исторические сохранения P15: World3/Rules pickup-1, Session2/Envelope1. World1/2 не мигрируются и отклоняются; оставить прежние файлы для прежних веток в отдельных копиях. Новый Godot save path содержит pickup-1. P16 поставочные обязательства уже реализованы; полный Gate C по-прежнему P21.


P16 проверяет будущую поставку с физической частичной приёмкой:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/delivery-lifecycle.json --script delivery --output artifacts/delivery
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/capital-delivery.json --script capital-delivery --output artifacts/capital-delivery
```

Participant demo48 ticks: Q10/Delivered6/Unfulfilled4; Paid3000/Refund2000/Fine100/BondReturn150 minor. Supplier12900/buyer7100, total20000. Capital demo47 ticks использует тот же lifecycle и общий бюджет заказа,6 единиц реально экспортированы. delivery.json/summary/ledger/events показывают состояния и расчёты; scenario.json — настройки. [Отчёт P16](docs/IMPLEMENTATION_REPORT_P16.md).

Исторический P16 World4/Rules delivery-1, Session2/Envelope1. World1–3 не мигрируются, старые saves оставить для прежних веток в отдельных копиях; Godot пишет user://platform-checkpoint-delivery-1.json. P17a транспорт и числовая logistics группа уже реализованы; полный месячный Gate C/P21 остаётся впереди.


P17a — первый реальный рейс с числовым экипажем:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-cycle.json --script logistics --output artifacts/logistics
```

75 ticks: wood20→plank10, автопоставка10, возврат машины; logistics wages300minor, all wages800, supplier108200/buyer70000, total200000. Purchase creates N0, группа нанимается из population4. Source/cargo/return place/full wage holds настоящие; груз не телепортируется. [Отчёт P17a](docs/IMPLEMENTATION_REPORT_P17A.md), [местная проверка](docs/LOCAL_SETUP.md). Full P17 ещё не завершён, NPC пока не выбирают новые маршруты (P21).

Исторический P17a: World5/Rules logistics-1, Session2/Envelope1; World1–4 не мигрируются. Новые paths сохраняют прежние files/branches. [Готовность MVP~60% и предложение исследований](docs/RESEARCH_READINESS.md): complete algorithmic headless P21, real model pilots P22/P23, series/analysis P24/P25. Это оценки/gates, не проведённые научные эксперименты.


P17b1 — улучшение транспорта: [PR](https://github.com/MaksShelest/Delsk/pull/12), ветка `feat/logistics-upgrade-p17b1`.

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-upgrade.json --script logistics-upgrade --output artifacts/logistics-upgrade
```

Проверено176 ticks:60wood→30plank,10 на upgrade за30000minor,20 доставлено одним реальным рейсом. Вместимость10→20, экипаж min/capacity2→3, численность через обычный найм.308/308 tests,12scenarios/9load pairs, Windows62hashes. [Отчёт](docs/IMPLEMENTATION_REPORT_P17B1.md). Исторический P17b1: World6/Rules logistics-2, Session2/Envelope1; World1–5 не мигрируются. Новые APK/EXE/device tests не проводились. P17b2 own output destination до заполнения реализован отдельно; остальная логистика/политика/model/UI по плану, full P17 ещё не готов.


P17b2 — собственный склад/лавочка UntilFull: [PR](https://github.com/MaksShelest/Delsk/pull/13), ветка `feat/logistics-own-output-p17b2`.

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-storage-cycle.json --script own-storage --output artifacts/own-storage
```

Проверено339/339tests/13scenarios/10load pairs и Windows62hashes. Fixture180ticks:60wood→30plank, trips10+5 в собственный склад15, output15. Own receipt не создаёт выручку/контракт; transport payroll600minor. Настройка берёт продукт из рецепта, объём из свободного места. Full destination ждёт без зарплаты и сам возобновляется после освобождения места, начатый trip заканчивается после stop. [Фактический отчёт](docs/IMPLEMENTATION_REPORT_P17B2.md). Current World7/Rules logistics-3, Session2/Envelope1; World1–6 не мигрируются. Следующий P17b3 — входящие поставки из своего склада, весь P17/Android device gate остаются открытыми.

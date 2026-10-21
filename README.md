> Экспорт в Godot: **Export With Debug включена → Debug и режим бога; выключена → Release без них**. Для основного project.godot Python-команда не нужна. [Пошаговая инструкция](docs/DEVELOPER_TOOLS.md).

> Переработка планшетов после PR#46: [реализация, проверки и ограничения](docs/IMPLEMENTATION_REPORT_TABLETS_V1.md). Пять производств, биржа, четыре раздела игрока и справка RU/EN на живых данных. PR#45 остаётся отдельным draft; экономические версии и открытые аппаратные gates прежние.

> Текущий итог пакета8: [сводка всех8пакетов, пробелы и дополнения](docs/NEXT_STAGE_SUMMARY.md), [результат и gates](docs/IMPLEMENTATION_REPORT_PACKAGE8.md), [исследовательская лаборатория](docs/RESEARCH_LAB.md). P58 software готов; P59/P60 полностью не закрыты: Hard/длинная история/ROI, текущая реальная модель и аппаратные Windows/Android остаются открытыми. Датированные срезы ниже — история.

> Актуальная дорожная карта20.10.2026: [P28–P60 и пакеты](docs/DELSK_NEXT_STAGE_PLAN.md). Пакет1 — [результат/проверки/ограничения](docs/IMPLEMENTATION_REPORT_PACKAGE1.md), [матрица команд](docs/ACTION_ACCESS_MATRIX.md). Исторические этапы ниже не являются текущим статусом нового пакета.

# Delsk

Пакет5 P47–P50, выбранный вариант C: [результат и проверки](docs/IMPLEMENTATION_REPORT_PACKAGE5.md), [рецепты/цепочка/баланс](docs/PRODUCTION_CHAINS.md). Атомарные многокомпонентные рецепты, молочное сырьё/свёкла, сыр/сахар/выпечка.835 ordinary/841 Developer tests;102exports/14replay/69comparisons, Linux Godot/RU/EN. Следующий пакет6 P51–P53, с P51. Ниже — исторические срезы.

Пакет4 P43–P46: [результат и проверки](docs/IMPLEMENTATION_REPORT_PACKAGE4.md), [лавочки, переговоры и получение](docs/PARTICIPANT_TRADE.md).805 ordinary/811 Developer tests, полный runner/load/replay/privacy gate, Linux Godot/UI. Дальше пакет5 с P47; P48+ требует выбора контента владельцем.

Пакет3 P37–P42: [результат/проверки/границы](docs/IMPLEMENTATION_REPORT_PACKAGE3.md), [ресурсы, погрузка и маршруты](docs/PHYSICAL_AUTOMATION.md). Три карты, конечные ресурсы/права, движущаяся техника;773 ordinary/779 Developer tests и Linux gates. Исторический отчёт; граница пакета4 уточнена ниже.

Пакет2: [результат и проверки](docs/IMPLEMENTATION_REPORT_PACKAGE2.md), [Developer-сборка и голосование](docs/DEVELOPER_TOOLS.md). P36/P54/P55; исторический срез пакета2.

Исследовательская экономическая игра с автономными NPC.
Автор: Шелестов Максим Евгеньевич.

В C# реализованы и проверены P00–P22: физическая экономика/логистика, рынки и сроки, периодический спрос столицы, выборы, приватная материально оплаченная поддержка и utility baseline трёх NPC без LLM. Контроллеры используют те же команды/валидаторы, что и человек, видят только разрешённую проекцию. В P26.6 доступны игровой ввод, Debug, две карты, планшеты с тактической паузой и непрерывная добыча. Завершены также торговля участников/общие площадки C. Основной баланс — [config/economy.json](config/economy.json), [руководство по параметрам и опытам](docs/ECONOMY_CONFIGURATION.md). Текущий [отчёт P27](docs/IMPLEMENTATION_REPORT_P27.md) отделяет выполненную облачную часть от локальной аппаратной приёмки и ограничений длинной истории. [Проверки и ограничения P20/P21](docs/IMPLEMENTATION_REPORT_P20_P21.md).
P22 добавляет необязательный Ollama HTTP transport и безопасный выбор готового плана. Модель выключена по умолчанию;Fake/остальные unit-тесты не заменяют реальный P23. [Отчёт P22](docs/IMPLEMENTATION_REPORT_P22.md), [локальное завершение P23](docs/LOCAL_MODEL_VALIDATION.md).

## Быстрый запуск без Python и Godot

Нужен .NET SDK **10.0.401 x64**, закреплённый в global.json. Из корня репозитория (PowerShell):

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script baseline --economy config/economy.json --seed 42 --output artifacts/run-42
```

694 xUnit-теста прошли локально. Результат runner — статус/тик/хеш/путь в консоли и JSON/JSONL/CSV в `artifacts/run-42`: summary, деньги/товар/численности, agents/labor, decisions/commands/events/ledger, manifest и inspection final-state. Все игровые расчёты и политики на C#; Python только запускает проверки и читает результаты. [Параметры, значение прогона и файлы](docs/HEADLESS_TESTING.md).

`--mode headless` по умолчанию выполняет те же тики без ожидания реальных секунд; перевозка остаётся физической. `--mode paced` ждёт1 секунду/тик. Мир задаётся `--scenario`, политики — `--controllers`; начальные настройки лежат в scenarios/baseline-*.json (historical autonomous-*.json сохранены).

## Сохранение и регрессии

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --checkpoint artifacts/npc.save.json --checkpoint-at 350 --output artifacts/npc-continuous
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/npc.save.json --ticks 1000 --output artifacts/npc-loaded
```

Checkpoint сохраняет текущий мир и план каждого NPC; load продолжает без повторения первых350 тиков и без offline catch-up. final-state.json — только inspection, не save. Командную ленту можно повторить `--replay PATH --scenario PATH --ticks FINAL_TICK` без вызова контроллеров; это проверка мира, не восстановление их планов.

Если установлен Python, одна команда с hard timeouts выполняет restore/build/618tests,26 runner-сценариев,22обычные пары checkpoint/load+controlled fixture,9replay и независимые проверки81выгрузки:

```powershell
python scripts/check_headless.py --output artifacts/headless-p22
```

Старые scripts: cycle/economic-cycle.json, recovery/zero-cash-recovery.json, development/land-and-content.json, labor/labor-dynamics.json, equipment/equipment-cycle.json. Они сохранились как регрессии. На обычной машине доступен также MTP `dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore`; в этой облачной среде использован встроенный xUnit runner из-за ограничения IPC.

## Границы готовности

P00–P26.5 есть в ветке клиента. P26.6 открыт: меню, 2D Debug, свободное движение, HUD и контекстные действия одной партии собраны; оконный показ и устройство не закрыты, затем P27. Во втором месяце прототип развивается в более полноценную визуальную игру по спецификации, при необходимости с простыми Blender-моделями/готовыми ассетами. Проверки конкретных fixtures/seed не означают доказанный баланс всех конфигураций; [фактическая готовность и ограничения](docs/PROTOTYPE_STATUS.md).

Godot .NET4.7.2 использует общий Core/Hosting: Linux smoke проверяет222tick hashes и14реальных file restores (экономика, выборы, приватная поддержка). Windows/Android экспорты ранее созданы; актуальный EXE/APK на устройствах в этой итерации не запускался, Android device gate P06 открыт. [Локальная работа](docs/LOCAL_SETUP.md), [клиентский стенд](src/Client.Godot/README.md).

## Документация

- [Спецификация0.3 с уточнением визуального клиента от 17.10.2026](docs/requirements/game_specification.md).
- [Архитектура](docs/ARCHITECTURE.md), [решения/риски](docs/DECISIONS.md), [план/статус](docs/IMPLEMENTATION_PLAN.md).
- [Готовность минимальной версии](docs/PROTOTYPE_STATUS.md), [запуск без графики](docs/HEADLESS_TESTING.md).
- [Отчёт P20/P21](docs/IMPLEMENTATION_REPORT_P20_P21.md), [инструкции разработки/Cursor](AGENTS.md).
- Исторические отчёты: [P00–P05](docs/IMPLEMENTATION_REPORT_P00_P05.md), [P06](docs/IMPLEMENTATION_REPORT_P06.md), [P07–P08](docs/IMPLEMENTATION_REPORT_P07_P08.md), [P09–P10](docs/IMPLEMENTATION_REPORT_P09_P10.md), [P11](docs/IMPLEMENTATION_REPORT_P11.md), [P12](docs/IMPLEMENTATION_REPORT_P12.md).

Исторические результаты отдельных итераций ниже сохраняют их прежние версии и ограничения. Текущий статус указан выше.

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

Проверено339/339tests/13scenarios/10load pairs и Windows62hashes. Fixture180ticks:60wood→30plank, trips10+5 в собственный склад15, output15. Own receipt не создаёт выручку/контракт; transport payroll600minor. Настройка берёт продукт из рецепта, объём из свободного места. Full destination ждёт без зарплаты и сам возобновляется после освобождения места, начатый trip заканчивается после stop. [Фактический отчёт](docs/IMPLEMENTATION_REPORT_P17B2.md). Исторический P17b2:World7/Rules logistics-3, Session2/Envelope1; World1–6 не мигрируются. Входящие поставки из своего склада реализованы следующей частью, весь P17/Android device gate остаются открытыми.


P17 — входящие поставки на предприятие (технический ID P17b3): [PR](https://github.com/MaksShelest/Delsk/pull/14). Индексы a/b1/b2/b3 — небольшие итерации внутри исходного пункта P17, не дополнительные этапы MVP; расшифровка в начале [плана](docs/IMPLEMENTATION_PLAN.md).

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/inbound-cycle.json --script inbound --output artifacts/inbound
```

Проверено110ticks,369/369tests,14scenarios/11load pairs/Windows62hashes.30wood со своего склада→inbound10+5→7production batches→7plank исходящим рейсом в склад. Вход/выход/производство управляются отдельно, одна машина/числовая группа. Реальные empty approach/loading/loaded return, защищённые goods/input place/payroll. [Фактические проверки](docs/IMPLEMENTATION_REPORT_INBOUND.md). Current World8/Rules logistics-4, World1–7 без migration, device gate открыт. Следующая ограниченная работа P17 — controlled late cargo/physical return, общая очередь/fairness ещё не завершена.


P17 — просрочка и физический возврат груза: [PR №15](https://github.com/MaksShelest/Delsk/pull/15), [отчёт](docs/IMPLEMENTATION_REPORT_LOGISTICS_RETURN.md). Исправлены сохранение позднего груза и ставка, назначенная в тик подготовки рейса. Проверки включают реальную трудовую конкуренцию, возвратное место и расчёты после expiry. Аварийный сценарий — явно помеченная test-only checkpoint fork, обычный scheduler не отправляет заведомо поздние рейсы. World8/Rules logistics-5, старые Rules без миграции. Полная проверка: `python scripts/check_headless.py --output artifacts/headless-late-return`. Следующее — собственное следующее производство как исходящее назначение; общая очередь/fairness ещё остаются в P17.

Фактическая проверка текущего возврата:393/393теста,14обычных сценариев и отдельный аварийный сценарий,12пар сохранения суммарно; Godot/Windows прошёл. Подробные результаты и ограничения приведены в отчёте.


P17 — продукт в следующее своё производство: `SetLogisticsProduction` разрешает input другого собственного Enterprise с совместимым рецептом; прежние physical UntilFull/stock/place/return/payroll и независимые stops. [Отчёт и проверки](docs/IMPLEMENTATION_REPORT_OWN_PRODUCTION.md). Ветка `feat/logistics-own-production`, World8/Rules logistics-6 без миграции старых Rules. Следующее — общая очередь/nearest deadline/fairness и pause/load; P17/Gate C и Android device gate ещё открыты.

```sh
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-production-cycle.json --script own-production --output artifacts/own-production
python scripts/check_headless.py --output artifacts/headless-own-production
```


P17 и P18: сохраняемая очередь логистики с deadline/urgency/ротацией и общей квотой завершена; добавлены городской выпуск по базовым ценам, потребление/запасы, сглаживание, периодический спрос1.1/bootstrap, товарные волны и отдельный приватный PRNG. [Отчёт](docs/IMPLEMENTATION_REPORT_P17_P18.md). Ветка `feat/logistics-queue-capital-demand`, World10/Rules capital-demand-1 без migration; следующий пункт P19. Android device gate P06 открыт.

```sh
python scripts/check_headless.py --output artifacts/headless-p17-p18
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/autonomous-demand.json --script autonomous --output artifacts/npc-demand
```

P19 — периодические выборы: ElectionId/current numeric worker blocks, строгий порог>50%, SetVoteChoice/default self, первый победитель и ContinueAfterVictory→Sandbox. [Отчёт и реальные проверки](docs/IMPLEMENTATION_REPORT_P19.md):512tests,19scenarios,16обычныхsave/load пар+controlled,3replays; Linux Godot62экономических+40выборных hashes. Ветка `feat/elections-p19`, World11/Rules elections-1. Следующее P20; политические договоры/LLM/UI не добавлены.

### UI01: компактный интерфейс (18.10.2026)

Расширенная игровая область, действия поверх карты, заполнение кнопки добычи, RU/EN справка с паузой и отдельная история биржи. Полный экран/окно переключаются по **Alt+Enter** (или Alt+Enter цифрового блока), а также в меню. Изображение адаптивно заполняет экран без полей. [Что сделано и что дальше](docs/INTERFACE_ITERATIONS.md), [фактические проверки](docs/IMPLEMENTATION_REPORT_UI01.md).

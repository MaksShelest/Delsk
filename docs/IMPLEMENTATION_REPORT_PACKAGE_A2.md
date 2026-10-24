# A2 — полный B1, семантика и явно выбираемый supported subset

Автор: Шелестов Максим Евгеньевич.24.10.2026, Europe/Samara.

**A2 выполнен в границе промпта**, с явно ограниченным supported subset.
Полный bounded headless gate и отдельные A2/A1 process gates прошли.
B1/B2 не начаты этой итерацией.

## Основа и результат

Исходная копия A1 e5aa8de чистая. Fetch и GitHub подтвердили merge PR55/A1
4c61179 и PR56/инструкций A2 c85ed26→19d8cf1. Изменения после A1 — только9
документов; новой параллельной реализации не было. Рабочая ветка
`feat/vnext-package-a2` создана от19d8cf16d626c42859ab9f88ba7cb894898f63bf.
Прочитаны инструкции A2/guide, актуальные архитектурные решения/план/спецификация,
B1 и результат A1; проверены фактические CatalogReader, EconomyProfile/Apply,
Production executor, ResourceSiteRules, runner/checkpoint/тесты.

Сохранены A1 формат/catalog/reader/CLI и43process gate. Добавлен отдельный
`delsk-vnext-balance-1`, а не скрытое расширение delsk-vnext-catalog-1.
Полный `config/vnext/balance.b1.json` содержит ВСЕ15 исходных разделов и3metadata
источника, типизированных immutable C# records/maps/arrays Hosting. Source JSON
не изменён. Извлечение механическое; coins→minor checked×100, bp и рациональные
множители целые. Исходные optional base identities стали явными required полями.

Строгий bounded reader (1MiB/depth32/collection limits) отвергает decoded duplicate
keys, unknown/missing/null/type/integer/ID/enum/ref/version/range/checked overflow.
Переиспользует A1 для items/recipes/industries; новые JSON options изолированы.
Provenance: явные source+A1 сравнивают raw SHA и все исходные sections/metadata,
включая будущие поля. Дополнительно независимый test проходит каждый source leaf;
[полная таблица139source patterns и coverage](vnext/PACKAGE_A2_MAPPING.md).

## Семантический результат

- Пять ступеней/XP/ёмкости/цеха/контур≤6клеток/персонал, предыдущая input capacity
  для расширения, склады200/300/400 и согласованность дублирующих настроек.
- Стройматериалы всех6отраслей: **63/66/64/63/54/46** (≤70).
- **69** V1/V2/V3 партий рассчитаны точно с ceil рациональной работы, входом,
  выходом, XP-арифметикой, вместимостью доступной ступени и полной прямой маржой.
- Найден конечный witness30строек/расширений,35explored states. Расходуется общий
  один seed-пакет steel12/glass6/asphalt20; steel/glass не становятся бесконечным
  импортом по наличию seed. Повторяемый выпуск требует grounded ресурсных/импортных
  входов и достигнутой отрасли/уровня; XP-переход — доступного положительного XP.
  Closed SCC отвергается, grounded cycle допускается. Limit30000 отдельно
  диагностируется; анализ консервативен для циклов преобразования конечных seeds.
- Прямая маржа: output reference−входы−полный труд5×0,20×T−услуга0,02×T.
  Минимум поставленного B1 **868minor**, но ни положительная маржа, ни pass
  структуры не означают dynamic balance/ROI/темп/ограниченный спрос.
- Startup report: steel3125/glass4250minor по125%, конечные пакеты12/6,
  refresh1200 и cap2. Лесопилка10000земля+17000работы+15000wood+9375steel=51375.
  Собственная добыча wood оставляет36375cash need против20000старта; дефицит16375,
  то есть33wood по справочной spot500. Это статический cash-example, не игровое
  прохождение/измеренное время. Источник денег не требует уже построенного завода.
- Coverage/error/warning раздельны. Совместимость техники, исполнение цехов/XP/
  установки/импортного расписания/дорог/счетов/услуг/долгого ROI не заявлены.
  Низкая маржа диагностируется warning, B1 не переписывается автоматически.

## Активная runtime-проекция

`config/vnext/economy.b1-supported.json`, id `vnext-b1-supported-1`, создаётся
C# adapter из проверенного B1 и явного baseline, выбирается прежним `--economy`.
Активны34товара/23V1-рецепта с общими1..8входами, T work ticks, service=T×rate;
8настоящих ресурсных профилей с finite stock/regen/purchase/lease, единая ручная
длительность2, cash/capacity/land/reservation/wage step/demand/election scalars.
Demand11000bp точно сокращён11/10 под фактические bounds Core.

Runtime fixture `supported-production-fixture-1`: заранее готовое предприятие и
5работников, пустые запасы, четыре участника; реальная добыча3stone, Transfer,
crush_stone→5gravel,20полных рабочих тиков, wages2000/service40, cash17960.
Это **подготовленное здание**, не бесплатная стартовая фабрика и не автономный NPC.
Checkpoint18 внутри pinned WIP, direct load и command replay без модели.

Ограничения adapter явные: Core имеет линейную производительность/одну линию,
MinimumStaff1; кривая B1, unlock/XP/V2/V3/пять ступеней остаются metadata.
Новых production building templates нет: текущий builder мгновенный и берёт
материалы из личного inventory, а bare ground70/стройка60/расширение — C/D.
Техника в subset выключена, а не подменяет отсутствующие resource nodes wood.
Warehouse/shop, labor/market/delivery/logistics/handling/movement/population/
growth/NPC/волны/maxTicks явно остаются baseline-значениями в JSON.
Профиль фиксирован на4участниках/их demand budget; другой состав отвергается.
Масштабирование партии — следующее отдельное B1, не спрятанная реализация A2.

Неподдерживаемые значения не подменены похожими runtime числами. Подробная таблица
source→unit→DTO→validation→consumer/следующий пакет и legacy mapping — по ссылке
выше. Девять старых ItemId сохранены; отраслевые sawmill→woodworking,
bakery/sugar-factory/confectionery→food и dairy→dairy не переименовывают мир,
RecipeId/прошлый WIP/сохранённые конфигурации не переписываются.

## CLI и provenance

После актуального Release build, из корня:

```sh
python3 scripts/extract_vnext_catalog.py --check
python3 scripts/extract_vnext_balance.py --check

dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --validate-vnext-balance config/vnext/balance.b1.json --source docs/vnext/data/BALANCE_VNEXT.json --catalog config/vnext/catalog.b1.json --output artifacts/vnext-a2-semantic
```

Для воспроизводимого экспорта профиля добавить `--baseline-economy config/economy.json`
и выбрать **новый/пустой** output: появится economy.b1-supported.json.
C# output побайтово совпадает с committed supported profile. Без source+catalog
CLI проверяет full самостоятельно, честно пишет provenanceVerified=false;
такой запуск не экспортирует профиль. source/catalog допускаются только вместе.

CLI возвращается **до ModelServices/SimulationEngine/контроллеров/HTTP**. Запрещены
любые simulation/model/replay/research флаги, missing/duplicate/unknown arguments.
Exit0 — static semantic pass (warnings допустимы), exit2 — отказ. Отчёт
balance-validation.json, scope=semantic, balanced=null, exact SHA/counts/
coverage/errors/warnings/batches/construction/bootstrap/startup. IO/unsafe output
даёт stderr+2 без ложного «сохранённого» отчёта. Temporary+atomic nonoverwrite move;
чужая серия, файл или готовый отчёт не удаляются/не заменяются.

Profile provenance — optional узкие nonauthoritative метаданные EconomyProfile,
отсутствуют в прежней сериализации с null. Проверяется hash канонического payload;
stale metadata после редактирования отвергается. Новый-run manifest содержит
source/full/catalog/baseline/profile-file SHA, capabilities/limitations и реальный
resolved ConfigHash. Файл хешируется при чтении. Load/replay используют saved
resolved config/id/hash без внешнего файла; source-file provenance не добавлялась
в authoritative save и не выдумывается при load. Каталог hash ≠ ConfigHash.
LF закреплён для B1/catalog/full/supported и baseline config, чтобы точный SHA не
менялся при Windows autocrlf. Сам config/economy.json не редактировался.

## Команды проверки

SDK10.0.401/net10.0, текущие lock-файлы. Все внешние процессы через run_check,
stdout/stderr/exit сохранены в /workspace/work/delsk-a2. Полный headless gate
выполнен из-за реального изменения runner dispatch/script/manifest:

```sh
python3 scripts/run_check.py --name a2-headless --timeout 780 -- python3 scripts/check_headless.py --output artifacts/a2-headless
python3 scripts/run_check.py --name a2-cli --timeout 180 -- python3 scripts/check_vnext_balance.py --output artifacts/a2-cli
python3 scripts/run_check.py --name a1-regression --timeout 180 -- python3 scripts/check_vnext_catalog.py --output artifacts/a1-regression
python3 scripts/run_check.py --name lab-unit --timeout 60 -- python3 -m unittest discover -s tests/Research.Lab -v
```

Внутри headless: locked restore180s, Release/no-restore build180s, полный xUnit
**120s**, отдельные CLI30s, bounded pilots≤300s. Старый скрипт имел test timeout180;
сейчас приведён к A2 требованию120, suite укладывается без расширения лимита.

- Locked restore/build exit0,0warnings/errors на финальной сборке.
- **998/998 xUnit**,0failed/skipped/not-run,70,951s<120s:51новый A2case,
  полный B1 и fixture5/1/1, все strict sections, ссылки/ступени/объёмы/целые/
  overflow/rationals, closed/grounded cycle/finite seed, source/A1/future metadata
  conflicts, stale profile, exact WIP load/replay/zero-cash recovery.
- Полный headless gate: **24базовых сценария**,5baseline cases,
  **102exports/14replay/69comparison pairs**, все exit0. Отдельные money/goods/
  public-capital/model-privacy audits прошли, включая S7 controlled fake,
  модель off равна baseline. Это не реальная нейросеть.
- **49A2 real process checks**: extraction дважды одинаковая/stale source,
  valid/малый/unknown format/strict fields/ref/нет файла/100MiB input,
  source/full/A1 consistency, finite bootstrap/batch capacity,18запрещённых
  mixed flags, unsafe args/output preservation. Новый prepared runtime smoke:
 4exports/3exact comparison pairs, checkpoint18внутри WIP. Hash денег/товаров/
  commands/events/metrics/решений совпал при direct load и replay.
- **43A1 process checks** прошли вновь, включая прежний economy-v6,
  checkpoint59/direct load/replay (4exports/3pairs) и21mixed flag.
- **37Python laboratory tests** green с разрешённым loopback. Новые API/
  браузерные экраны не добавлялись, лабораторные исходники не редактировались.
- Source/full/catalog/profile/scenarios после gate не изменились.
  Краткое actual evidence: verification/package-a2. Длинные логи/бинарники/
  выгрузки не коммитятся. CI дополнен extraction и49process checks A2;
  прежние A1/полный headless/Developer/Windows Godot jobs сохранены.

Предварительные проверки не скрыты: compiler/analyzer xUnit2013/2031 исправлены;
первый focused запуск нашёл реальные ограничения equipment-node/demand ratio,
adapter исправлен,51focused green. В одном промежуточном full suite3duplicate-ID
fixtures получили CollectionLimit раньше DuplicateId; fixtures заменяют ID внутри
допустимого размера, лимит reader не ослаблен. Окончательный полный suite зелёный.
Первый Python lab запуск в sandbox получил7socket PermissionError; loopback gate
повторён с разрешением сети, без изменения приложения/проверок.

## Совместимость, непроверенное и stop

World18/Rules production-recipes-1/Session2/Envelope2 и обычный economy-v6 прежние.
Core/Client.Godot/старые сценарии/стандартный ExportDebug не изменялись. Каталог и
прежний default не переключают уже созданный мир на B1; миграция не объявлена.

A2 закрывает собственную границу A, ранний каталог/адаптацию P63 и основание P61.
**Полный игровой P63, новые authoritative состояния P61 и прежние P53/P56/P59/P60
не закрыты.** Нет новых ботов/карт/UI, параллельных цехов/XP/заданий/сноса/overflow/
автопарка/услуг/дорог. Динамический баланс/полный старт всех отраслей с нуля,
20–35чпрогресс, реальные Windows/Android устройства, GPU/реальная LLM в этой
итерации не проверялись. Godot/реальная LLM для smoke A2 не требовались.

Далее только отдельное задание **B1 — первая половина пакета B**, затем B2;
B1 здесь не начат. PR без автоматического merge. Ветка feat/vnext-package-a2;
реализация [a8aa0c2](https://github.com/MaksShelest/Delsk/commit/a8aa0c267802beb2a17338531fbda4dd8a895155),
[PR57](https://github.com/MaksShelest/Delsk/pull/57) открыт без merge.
Финальный handoff также уточняет generic coverage без фиксированного числа партий
для маленьких fixtures и убирает trailing whitespace; экономические правила прежние.

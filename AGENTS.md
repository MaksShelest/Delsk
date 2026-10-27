## E3,27.10.2026 — явное текущее задание

- Пользователь разрешил только E3/P71 поверх финального E2. Основа d9e17e3
  (mergePR67) содержит2ca4b12/проверенный25f6689. Ветка feat/vnext-e3-accounts.
  Услуги доставки не начинать автоматически. Прежние запреты автоматического
  старта E3 ниже не блокируют это явное задание.
- Новое правило владельца заменяет старые500/500: grace1000 active ticks inclusive,
  первая пеня t+1001, затем+1000. Source BALANCE и §15/P71 синхронизированы.
- Worklog: docs/WORKLOG_PACKAGE_E3.md. Реальные gates/evidence фиксировать после
  выполнения, WIP/compile failures/timeout не считать итогом. Никакого auto merge.
- Переиспользовать RoadBody/TrafficTravel и EconomyTransaction/holds/escrow.
  Счёт не создаёт cash. Class/share/ставки из typedBALANCE, fixedinteger остатки,
  principal first, пени без капитализации. Вклады фиксируются при фактическом
  движении; собственные и NPC машины не дублировать.
- E3 optional profile vnext-e3-accounts-1 для новых designed games, World26,
  limited25–18 без Billing. Старые resolved profiles не подменять. Новые
  billing.json/hash/save/replay/privacy и independent billing_audit обязательны.
- Signed rounding residues per payer/recipient сохраняют10/90 при мелких счетах;
  все целые копейки общего накопления выставляются каждые120. Clearing всегда0,
  это не второй кошелёк. Автоплата из frozen free budgets перед пенями после
  текущих receipts/refunds. Доход этой же autofase доступен следующему tick.
- Previous/E2/E3 suites исчерпывающие и непересекающиеся, отдельные requiredjobs.
  Не повышать старые deadlines и не удалять тесты. BillingProbe только isolated
  stage, запрещён обычным AssemblyAudit; ordinary client/God flags сохранять.

## E2,27.10.2026 — реализованный software-объём

- Явное задание E2/P70, основа26aeaab/PR66 содержит финальный E1/7163598.
  Ветка feat/vnext-e2-roads, PR67 без auto merge. E3/счета и услуги не начинать.
  Итог: docs/IMPLEMENTATION_REPORT_PACKAGE_E2.md и verification/roads-e2/.
- Gameplay25f6689: опциональный vnext-e2-roads-1 для новых designed maps/lab,
  World25 limited24–18 без Roads, старые resolved profiles не подменять.
  Ступени1/2/3 append-only с городом0, прежний Тракт/единственный cargo70,
  реальные материалы/деньги/числовые группы capacity2, личный/1рабочий половина.
  Пауза сохраняет batch/обход/занятость; после последней клетки сразу completed.
- Traffic общий для Vehicle/Freight/Equipment, RoadBody только вычисляемая
  spatial projection старых inventories. Fixed progress/один quota10quanta,
  exclusive cells/edges, actual entry leases закрывающегося обхода. Без частной
  земли/импульсов/reverse/teleport. Старые грузы, holds и numeric labour сохранять.
- Новые состояния в hash/save/restore/replay/roads.json, own DTO и independent
  road_audit обязательны вместе с прежними капиталом/flows/privacy. Задания
  funded из казны, once percell/stage, не E3. Helper не обещает любой затор.
- 27 E2 tests;51runner processes/35exports/24pairs; actual World24 exact;
  nativeRU/EN/656 matched inputs, legacy1132hashes/35restores. Core25f6689
  все9jobs success; Windows25f6689 success. CI-only follow-up импортирует96
  идентичных assets отдельным Mono editor project, проверяет source_md5 и
  переносит только verified resources, затем ordinary runtime/release/HUD.
- Previous+roads — исчерпывающие непересекающиеся suites, отдельные required
  jobs; прежние deadlines не увеличивать. Timeout/failure не считать pass;
  исходные попытки сохранены в WORKLOG_PACKAGE_E2 и process records.
  Prepared sources/cash не выдавать за natural progression/ROI/device acceptance.
  Следующий шаг — review E2, не автоматический запуск E3.

## E1,27.10.2026 — реализованный software-объём

- Пользователь явно разрешил E1 поверх финального PR65/ce8f0e3. Историческое
  «пакет E не начинать» ниже не блокирует это задание. E2/дороги/новый помощник,
  счета и сторонние услуги не начинать автоматически.
- Ветка feat/vnext-e1-fleet, PR66 без автоматического merge. Итог:
  docs/IMPLEMENTATION_REPORT_PACKAGE_E1.md, docs/verification/fleet-e1/final-gate.json;
  восстановление/failed attempts: docs/WORKLOG_PACKAGE_E1.md. Проверенный code
  snapshot c62352b; последующие изменения отчёта не меняют его runtime inputs.
  Перед следующей итерацией проверить наличие финального E1 в выбранной основе.
- E1 отдельным vnext-e1-fleet-1 только для новых двух designed maps/lab presets;
  D3 и прежние профили/сохранённый resolved config не подменять. World24,
  limited23–18 только без E1 fields. Rules/Session/Envelope прежние.
- Один Vehicle cargo inventory. Actor/Logistics.Trip используют ту же позицию
  и груз; BuyLogistics на E1 требует купленную машину. Freight/queue/holds/crew
  переиспользуются, free large-body handling запрещён. Staff числовой.
- Конечные6, поставка500, непроданный минимум2500, purchased не заменять;
  покупка через общий inclusive5tick window с реальным money hold/seeded tie.
  Будущие модели/PRNG доступны только исследователю, own DTO immutable.
- Дорога определяется InitialCityDirtCells, коридор ещё не покрытие. Без топлива.
  Площадки/комплекты оплачиваются материалами/деньгами, монтаж сохраняется;
  мощность независимых людей складывается без двойного счёта водителя.
- Independent scripts/fleet_audit.py и fleet.json обязательны вместе с прежними
  балансами/капиталом; native FleetProbe только isolated stage, ordinary audit
  запрещает его. Не ослаблять deadlines или вычёркивать тесты; partitions
  исчерпывающие, проверяют skips/overlap по TRX. Heavy gates последовательно.
- 1219 ordinary,21 E1; E1 runner34process/22exports/16pairs, native RU/EN;
  CI unit/core/developer/production/fleet/verify и Windows ordinary/Release/God
  tools success на c62352b: Core40443103322, Windows40443103227.
  Unit/runs отдельные обязательные jobs, исходные timeout124 сохранены как failure.
  Prepared fixtures не доказывают natural progression/ROI/device acceptance.
  Следующий ограниченный шаг — review PR66; E2 только отдельным заданием.

## D3 и восстановление,26.10.2026

- Основа28a41a7/PR64. Ветка feat/vnext-package-d3, recovery ac9e706 сохранён
  в origin, один PR65. Не выдавать WIP-снимок за итог. ПакетE не начинать.
- vnext-d3-production-1 default только новых двух designedmaps/lab presets;
  legacy5/default/A2/C1/D1/D2 profiles и resolved saves не подменять. 34items,
  23V1 +46derived V2/V3,6industries. XP открывает tech; local equipment version
  отдельно оплачивается300/900coins +exact materials,120active ticks. Старые
  рецепты доступны, модернизация только stopped/noWIP, C2 ждёт и считает вложения.
- Imports конечные,125% fixedreference, ceil(N/4), cap2packages; 5additives30,
  steel12/glass6 each1200, asphalt20/2400. После первого actual materials-tierIV
  bootstrap asphalt не пополнять, уже оплаченные/остатки не удалять, demolition
  не перезапускает bootstrap. XP без корпусаIV поставку не останавливает.
- BuyImport обычная AtLocation terminal команда; external debit +own pickup,
  затем прежний physical timed Transfer. No reverse deposits/trading source.
  Imported отдельный flow/stockdelta, никогда G/Produced. Independent audit
  scripts/import_audit.py обязателен; ExternalOut=retired demand+actual import.
- World23 limited22/21/20/19/18, new fields optional/default omitted. Own immutable
  content observation, полный production-content.json researcher only. Import
  receipts/modernization/material terms не публиковать в public stream.
- NPC те же команды/планы: imports, local versions, staging, feasible recipes.
  Начальная специализация смотрит все V1 (зерно→мука), не только default хлеб;
  прибыльный недоступный рецепт не блокирует доступный. Полная оптимизация внеD3.
- God XP backend переиспользован изD2. Immediate money/stock/XP на текущем travel
  quantum без Step и без обхода holds/capacity. Quantum в audit/playback, ordinary
  API отсутствует. Стандартный ExportDebug включает, обычный Debug/Release нет.
- Replay использует один persisted playback cursor: ordinary inputs +heading в
  исходном порядке, god edit на точном AfterCommandCount/quantum. Session2 и
  optional RecordedCommand.TravelQuantum прежние; legacy quantum0 допускается.
  Step/Runner и final boundary обязаны учитывать quanta; save/load сохраняет cursor.
- Native ProductionProbe только isolatedtests, AssemblyAudit запрещает его.
  CI serial root importer только disposable checkout, finally восстанавливает
  точные project.godot bytes. Не игнорировать timeout/errors. Required verify
  включает core/developer/production. Heavy local gates выполнять последовательно.
- Recovery/results: WORKLOG_PACKAGE_D3.md, IMPLEMENTATION_REPORT_PACKAGE_D3.md,
  PRODUCTION_D3.md, verification/production-d3/final-gate.json. Prepared resources,
  high XP/initial buildings не выдавать за natural progression; compact ordinary
  first output2094, полные карты только short3/10NPC smoke+load/replay. Длительный
  баланс/endgame/hardware/реальная LLM не закрыты. Старые записи ниже исторические.

## CI follow-up D2,26.10.2026

- PR64 исходныеfailures: CorexUnit120s, HUDMonoeditor shutdown120s; push15min
  wholejob наDeveloper. Не выдаватьtimeout124 заsuccess; игровыеassertнепадали.
- Полныеordinary/Developer suites разделены test_partitions.py наbase/integration/
  D2, exhaustiveness/skip/overlap проверяютсяTRX. Deadline120/180 прежние.
- Core/Developer jobsраздельны, aggregateverify requiresbothsuccess. Не ослаблять
  существующуюобязательнуюпроверку. WindowsHUD используетverifiedrootimports при
  точныхbytes/source_md5/cachefiles; managedstageassembliesнекопируются.
- Исправлена настоящая DeveloperPanellegacyXP orphanwidget/RID leak: lazycreation
  толькодляDevelopment.20nativechecksDebug+ExportDebug, legacyabsence/D2SetAddXP,
  cleanup истрогиеGodotlogs. Подробно CI_D2_FIX.md/worklog/evidence.

## D2,26.10.2026

- Основа29cb58b/PR63merged. Пользователь явно ограничил D2: XP/задания/контуры/
  платные проекты/оснащение. Полный контент, импорт и V2/V3 — D3, не начинать.
- vnext-d2-development-1 — checked A2→C1→D1 + typedBALANCE, default только новых
  designedmaps/lab. Старые5/defaultv6/A2/C1/D1 bytes и resolved saves не подменять.
- XP actor+industry, production только actual completion, quests по существующему
  funded DeliveryContract с baselineDelivered и sourceonce. Two slots/review1200,
 5/15/30batch50/100/200;60/500 только explicit public project и>=3600remaining.
  Не создавать новый спрос/бюджет; purchased goods допустимы. No productionCount.
- Footprint уровней1/3/4/5/6, capacities80/120/160/200/240. Full contour claim не
  дарит землю. Initial staged ground70/60ticks, expansion materials input, отдельная
  земля/цена/120/180/300/480. Executor прежний StorageConstruction.Development.
  Newcell fullcapacityhold development:job защищает late deposits; reverse holds
  validated. До completion geometry/caps прежние, новые places ещё не outfit/staff.
- PrepareEnterpriseExpansion drains WIP без следующих стартов, не C2prepare и не
  отмена trade/logistics. C2cancel/salvage peractualcells/allpaidmaterials, no double.
  Вторая фабрика знает tech, но платит свойI/expansion/outfit и нанимает из населения.
- World22 limited21/20/19/18 без новых полей; default omitted. XP proofs indexed
  bybatchID при capture/restore. Quest restore проверяет receipt interval/baseline/
  acceptance/knowntech. Own immutable DTO; development.json полный researcher only.
- Baseline plans/ordinary actions, staging target исключён из Fetch sources,
  иначе loop. Developer XP только прежний conditional ResearchModification,
  audit/replay/nonclean; ordinary ActionKind выдачиXP не добавлять.
- Presentation uses actual building footprint. Native DevelopmentProbe только
  isolatedtests, запрещён AssemblyAudit. Usage DEVELOPMENT_D2.md, decision60,
  report IMPLEMENTATION_REPORT_PACKAGE_D2.md, recovery WORKLOG_PACKAGE_D2.md.
- Gates:1153ordinary/1160Developer/37Python,36D2; headless102exports/14replay/69pairs;
  D2process31checks/18exports/16pairs, C1/C2/D1 regressions, actualWorld21exact,
  nativeRU/EN/GodotD2hashes+legacy1132/35restores, cleanordinaryaudit и2maplabruns.
  Evidence docs/verification/development-d2/. Не выдавать prepared
  material/accelerated geometry fixtures за natural progression/длительный баланс.

## D1,25.10.2026

- Основа85955d7/PR62merged. Явные EconomicRules.Workshops; старые профили/пять
  карт не подменять. Новый vnext-d1-workshops-1 для designed new games/lab presets:
  checked A2+C1+typedBALANCE,6базовых фабрик с1начальным оснащённым местом.
- Дополнительные линии используют прежний EnterpriseState/Batch/EmploymentGroup,
  отдельные global IDs/parent, общий конечный input/output/кошелёк и EconomyTransaction.
  Нельзя добавлять вторую экономику, транспорт или индивидуальных жителей.
- Кривая0/25/45/65/85/100, масштаб100work/full tick. Batch.Terms фиксирует
  рецепт/ставку/услугу2minor/full-work tick. Текущий N меняет скорость/payroll,
  Stop сохраняет WIP/hold, простой бесплатен. Нельзя сменить рецепт с WIP.
- AssignWorkshopWorkers переносит только текущих работников между своими линиями
  одного предприятия. Наём прежний постепенный из общего населения. Montage60active
  ticks оплачивает реальные материалы/input+деньги один раз, создаёт N0/off.
- Fairness: persisted last successful start serial внутри прежней ротации ролей;
  сырьё/полный выход/payroll атомарны. Сохранять порядок и все holds в restore/hash.
- Логистика обслуживает все выбранные рецепты/продукты, принятый рейс прежний.
  C2 подготовка останавливает новые старты всех линий, ждёт всеWIP/montage;
  release всех групп, outfitmaterials в actual investments, no duplicate equipment.
- World21 limited20/19/18 без D1fields; null/default omitted, старый World20/C2
  actualsave сохранил hash/metrics. Rules production-recipes-1/Session2/Envelope2.
  Own immutable DTO, полный workshops.json только researcher; ordinary audit
  должен запрещать WorkshopProbe наряду с прочими isolated probes.
- Финальные gates:1117ordinary/1123Developer/37Python,22D1; headless102exports/
 14replay/69pairs; D1process18checks/10exports/9pairs; C1/C2 regression, actual
  native montage/start/Stop/staff/Continue RU800/EN1100, Linux1132hashes/35restores,
  newmap-native и обе карты через browserlab, clean ordinary audit.
- XP/новые места/контуры/строительные проекты/V2,V3 и импорт — D2, не начинать
  автоматически. Не объявлять natural progression доказанным prepared3/6cell fixtures.
  Инструкция WORKSHOPS_D1.md, отчёт IMPLEMENTATION_REPORT_PACKAGE_D1.md,
  восстановление WORKLOG_PACKAGE_D1.md. Реальные финальные gates см. отчёт/evidence.

## C2,25.10.2026

- От9220b4c/PR61merged: read-only own quote из фактического ConstructionMaterialConsumed;
 3000minor/cell+1500/extra cell level, recovery floor6000bp каждого ItemId после суммы.
  Initial free scenario assets не создают paid material refund. Cash не возвращается.
- Prepare/CancelPreparation отдельны от Demolish/CancelConstruction. New work/promises
  blocked, existing WIP/accepted freight/foreign pickup rights/normal holds сохраняются;
  blockers+fresh token+location+money перед commit. UI preview+hold2sec, cancel/no shortclick.
- Все normal goods и salvage на своих ground70/cell, excess C1/1200active ticks;
  old overflow IDs/deadlines не обновлять. ConstructionRecovered отдельный flow,
  неProduced/Harvested;40% discard explicit. Population/land сохраняются.
- Retired Building/Enterprise и empty historical inventory IDs сохраняются для old
  contracts и собственной машины, исключены из active views/capital/walls/accessanchors.
  Archive входит в C2 canonical hash. Не удалять empty inventory identity при rebuild,
  если на неё ссылаются historical records. Preserved truck uses прежний explicit-route
  executor после сноса/повторной стройки; PlayerTablet retained routes, no new fleet.
- C2 также на legacy five maps: HasGroundStorage после первого сноса; config.Storage
  не подменять, старые warehouse/build/delivery semantics прежние. Frozen defaults
  demolition6000/3000/1500 и ground70/1200 проверены against BALANCE; optional overrides
  в EconomicRules.Demolition. World20 limited readers18/19, legacy/C1 exacthash unchanged.
-1095ordinary/1101Developer/37Python,18newC2; headless102exports/14replay/69pairs;
  C2process16checks/10exports/9pairs/1996ticks; native RU/EN800/1100 preview/short+2s
  hold/pause/diskContinue/materials, Linux legacy1132hashes/35restores+stopped10.
  Full evidence/report IMPLEMENTATION_REPORT_PACKAGE_C2.md; usage DEMOLITION_C2.md,
  recovery WORKLOG_PACKAGE_C2.md, verification/demolition-c2. CI C2 gate added.
- Existing shared Level applies per footprint cell. Heterogeneous expansions/industry
  XP/production construction stages/full material import относятся к D, не выдумывать.
  Current timed construction cancellation = C1 warehouse build/upgrade. D1 не начинать
  автоматически. Device Windows/Android/long balance остаются открытыми.

## C1,25.10.2026

- От45fae36 (PR58+59merged): optional EconomyRules.Storage, own manual ground70;
  one-cell warehouses200/300/400 с real material/payment и60/120/180ticks.
  Atomic start, same inventory, persisted StorageConstruction; investment WIP.
- C1 buy-delivery forecast вместо новых невозможных normal holds. Старые holds/
  товарные права/escrow сохранены; manual+Freight receipt normal-first/OverflowLot.
 1200active ticks/600,120warnings/independent deadlines; promote free room,
  partial manual TakeOverflow через VolumeWork, explicit remaining Lost sink.
  При освобождении места pending rescue отменяется со снятием backpackhold;
  already transferred не дублировать. Stock reservations/production не берут overflow.
- World19 читает18 только без C1 fields; old18 exacthash проверен. Rules production-
  recipes-1/Session2/Envelope2 прежние; legacy null/default omitted, профили не migrate.
  New maps default economy.c1-storage.json (typed full+A2 derivation/provenance),
  старые5/defaultv6/A2bytes неизменны. Own UI/HUD, no public Overflow/rescue events.
-1077ordinary/1083Developer/37Python, headless102exports/14replay/69pairs;
  C1process24checks/14exports/13pairs; native RU/EN and2293C1hashes;
  legacyGodot1132hashes/35restores+stopped10. Report IMPLEMENTATION_REPORT_PACKAGE_C1.md,
  usage STORAGE_C1.md, recovery WORKLOG_PACKAGE_C1.md, verification/storage-c1.
- Не объявлять ordinary new-map полную стройку доступной: D templates/import ещё
  отсутствуют, реальные plank/steel/beam/concrete не подменять/free stock не выдавать.
  C1fixtures явно prepared sources/backpack2000. C2/снос/capacity shrink/NPCновые
  стратегии не начаты; P67/P74 и пакетC целиком не закрыты. C2 автоматически не начинать.

## Карты B2,24.10.2026

- От verified B1/PR58 (74b0ca5,56badfa): MapDesignReader -> existing ScenarioDefinition,
  strict source delsk-map-design-1, typed optional Layout/map-adapter-1. Source maps
  неизменны; C# --export-map-design воспроизводит scenarios/vnext/*.json. Source SHA,
  snapshot bytes/profile SHA/runtime ConfigHash различать. Initial scenario limit2MiB
  для large snapshot; строгие A1/A2 reader limits не менялись.
- small_river64x48/48lots/8districts и large_districts128x96/96lots/24districts,
  8resource IDs,15stalls/11spawns. Runtime profile только explicit supported A2,
  default economy-v6 на старых5картах прежний. CLI/UI incompatible profile reject;
  preview не мир/HTTP, ошибки профиля не ломают каталог. Общий B1 setup/controllers.
- MapLayout хранит exact roads/lots/district/civic/source IDs, исходник civic не имеет
  entry и terminal/dealer body: южный approach, terminal north2x2/dealer north5x3;
  townhall exact source2x2. Bodies blocked Core; decorations не collision authority.
  Protected corridor/civic/resource не Plot. Buy/Build старые; no free contours.
- 3D без промышленных stakes/grid; city dirt объединена, big ground без12kmeshes.
  Ore/sand quarry fallback, milk farm. New-map mini reuses read-only DebugBoard,
  no time/commands. Ратуша City+existing FillPolitics (scroll), terminal exchange,
  dealer paused HelpOverlay о будущем E. No real vehicle sales/road ownership tiers.
- World18/Rules production-recipes-1/Session2/Envelope2: Layout null omitted,
  resolved geometry в save, load без source regeneration. Матрица/limitations:
  IMPLEMENTATION_REPORT_PACKAGE_B2.md, vnext/PACKAGE_B_ACCEPTANCE.md; usage MAPS_PACKAGE_B2.md.
 1058ordinary/1064Developer,102exports/14replays/69pairs, Linux1132hashes/35restores;
  real CLI64checks, native2mapsx3/6/10 RU/EN, lab28pairs/49arms+real Chromium2runs.
  Не закрывать C/D/E, hardware, real-model/long balance; C автоматически не начинать.

## Ordinary B1,24.10.2026

- От fda7203/A2: OrdinaryNewGame.Resolve в Hosting, separate policies factory
  Algorithmic;3..10 NPC + one human. --new-game only new baseline/hybrid + economy,
  no load/replay/seed/research-setup. Research1..15 не ограничен ordinary диапазоном.
- Optional initial treasury/capped labor/NewGame provenance default-omitted:
  World18/Rules production-recipes-1/Session2/Envelope2; old save exact hash.
  Treasury initial money ровно один раз; capped population schedule/restore strict.
- Legacy4/default positions прежние; иначе SHA startup rotation, без World RNG.
  Aggregate ceil(base*N/4), A2 population20/max60 per actor через verified full balance,
  profile default economy-v6 прежний. Save resolved roster, load не resolver.
- Отчёт IMPLEMENTATION_REPORT_PACKAGE_B1.md, usage ORDINARY_NEW_GAME.md, recovery
  WORKLOG_PACKAGE_B.md.1028ordinary tests; real CLI35process checks/3,6,10;
  native3lifecycle RU/EN800x720;102exports/14replays/69pairs, Linux1132hashes/35restores.
  Пользователь явно запросил затем отдельный B2; C не начинать.

## A2: полный B1 и supported subset,24.10.2026

- A2 реализован по docs/IMPLEMENTATION_REPORT_PACKAGE_A2.md; mapping/coverage:
  docs/vnext/PACKAGE_A2_MAPPING.md. A1 формат/catalog/CLI неизменны.
- Полный delsk-vnext-balance-1/balance.b1.json содержит15typed sections+source
  metadata. extract_vnext_balance.py --check, source/full/A1 consistency; read-only
  semantic CLI --validate-vnext-balance до мира/ModelServices, exit0/2, safe output.
  Scope semantic/balanced=null; static witness/маржа не доказывают динамический баланс.
- economy.b1-supported.json/--economy явный fixed4 subset:34items/23V1/8resource
  profiles, прежний executor/линейный штат; будущие цеха/XP/tiers/V2,V3/import/fleet/
  roads/services metadata. Production templates отсутствуют до C/D; оборудование
  subset выключено. Нельзя подменять resource IDs wood или менять default игру.
- Profile.VNext — optional provenance с payload SHA; stale edits отвергаются.
  Save хранит прежний resolved config, не зависит от внешнего B1. World18/Rules
  production-recipes-1/Session2/Envelope2 прежние, default economy-v6 сохранён.
- Реальные gates998ordinary tests/102exports/14replay/69pairs, A2process49,
  A1process43, lab37. Следующий только отдельный B1 (итерация пакета B, не баланс B1).
  P61/P63 целиком и P53/P56/P59/P60 не закрыты; автоматически B1 не начинать.

## Каталог vNext, A1,24.10.2026

- A1 — catalog_only, не EconomyProfile/игровой профиль. Отдельный формат
  delsk-vnext-catalog-1 в Hosting; B1subset config/vnext/catalog.b1.json. Генерация
  scripts/extract_vnext_catalog.py (--check),34/23/6 — данные B1, не лимит reader.
- Source SHA и catalog SHA — точные байты разных файлов. Для B1/catalog закреплён LF;
  reader не нормализует digest. immutable/Ordinal,1MiB/depth32, все поля required,
  duplicate/unknown/null/type/version/reference/checked overflow reject.
- --validate-vnext-catalog PATH --output DIR должен выйти ДО ModelServices/мира;
  не смешивать с любыми simulation/model/research flags. Exit0/2, scope=structure,
  output только новый/пустой, без перезаписи существующих результатов.
- A2 не начат: прочие данные/adapter EconomyProfile/graph/capacity/bootstrap/runtime
  smoke отдельно. Не менять существующий economy.json/сценарии/World18/rules/save
  ради метаданных. Отчёт IMPLEMENTATION_REPORT_PACKAGE_A1.md, recovery WORKLOG_PACKAGE_A1.md.

## Исследовательское пространство и две карты,23.10.2026

- Последняя реализация P58: docs/RESEARCH_WORKSPACE.md, MAPS_RESEARCH.md и IMPLEMENTATION_REPORT_RESEARCH_WORKSPACE.md. Историческая пауза очереди между bounded jobs сохранена; новые pause/resume/cancel активной попытки идут только на безопасной границе Research.Runner observer. Core/World18/Rules production-recipes-1/Session2/Envelope2 не менялись.
- Кадры0..finalTick записываются на диск с UTF-8 byte-offset index; frozen voting записывается после tally, replay подаёт конечные бюллетени до завершения записи. Не включать неполные/failed/cancelled записи в статистику.
- Новая timing-группа ResearchWaitAtDecision-v1 не продвигает мир в ожидании модели. Per-NPC bindings используют один ModelRuntime; mixed load учитывает алгоритмические actors, pending не requery. Prompt/reply только researcher. API облачных моделей и редактор карт не реализованы.
- PlayMaps.All включает прежние три карты, FourQuarters и CityCorridor. Из PR45 перенесён только сценарий; не сливать устаревшую draft-ветку целиком. Геометрическое равенство/600тиков не доказательство P59/Hard/ROI.
- Входы и DLL закреплены SHA256; после обновления runner создаётся новая серия. Собственные profiles не переписывают начатые опыты. Тесты:37 лабораторных unit, check_research_workspace.py (реальный браузер/runner), check_research_model_routing.py (явная HTTP fixture). Фактические окончательные числа — только из отчёта.

# Инструкции работы в Delsk

Автор проекта и материалов: Шелестов Максим Евгеньевич.

Актуальное представление — в разделе «Планшеты v1»; версии экономики/статусы плана — в разделе «Пакет8»; датированные сведения предыдущих итераций ниже сохранены как история, не как параллельные текущие правила.

## Простой запуск лаборатории, 22.10.2026 (Europe/Samara)

- ResearchLab.cmd / ResearchLab.sh — оболочка существующего scripts/research_lab.py:
  --prepare --open-browser --port 0, output artifacts/lab по умолчанию. SDK из global.json,
  bounded locked restore/build Release перед UI; не запускать старый DLL после ошибки.
  Игра/Godot/LLM не требуются. Пользователь Windows использует py -3; не требовать pip/npm.
- OS host.lock держится до окончания активного bounded job. Повторный запуск открывает
  прежнюю очередь только после проверки loopback instance health; host.json без токена.
  UI shutdown — пауза и ожидание активного опыта, не убийство/обрезка тика.
- Presets/формы создают прежние планы P58 и проходят прежние валидаторы. JSON/CLI
  сохранены; Minor → монеты только presentation. ZIP полной серии недоступен во время
  running; старые token/Host/Origin и own workspace guards обязательны и для ZIP/shutdown.
- Gates: tests/Research.Lab, check_research_lab_startup.py и check_research_lab.py.
  Браузерный тест использует реальные runner/CLI/save/load/replay, не fake-экономику.
  Windows double-click не выдавать за проверенный облаком. Инструкция RESEARCH_LAB.md,
  контекст WORKLOG_RESEARCH_LAB_EASY_START.md. Core/save/политики не менялись.

## Планшеты v1, 20.10.2026 (Europe/Samara)

- База77d40e9 включает PR46; PR45 open/draft e195be1 просмотрен read-only и не включён. Отчёт IMPLEMENTATION_REPORT_TABLETS_V1.md, восстановление WORKLOG_TABLETS_V1.md. Это оформление, не новый пакет экономики: World18/production-recipes-1/Session2/Envelope2 прежние.
- TabletDesign.Theme scoped: HUD/Developer не перекрашивать. ProductionTabletPresenter обслуживает пять производств, точный Supports guard рецепта; нестандартный рецепт/узкое окно сохраняет generic controls. Данные PlayerView, реальные HouseholdObjectCard actions; no demo.gd/sample values. Общие Help/Close и footer делегируют прежнему TabletState. Help всегда пауза с восстановлением режима. Резерв вместимости не physical stock.
- ExchangeTablet — пять страниц поверх прежних presenters. Собственная лавка для MyOffers отделена от actual location; прежние AccessFor/Submit/валидация обязательны. Player четыре вкладки, own/bilateral privacy прежняя. Не добавлять Core команды ради архивной кнопки. Вторичные формы native/adaptive, исходные48сцен не все импортированы.
- Native TabletDesignProbe*.cs только isolated stage; forbidden ordinary audit/PCK. scripts/check_tablet_design.py и check_sawmill_ui.py --mode tablets-host проверяют реальные игровые состояния RU/EN/1440/800/640 и host pause. В export presets сохранять tablets_v1/LICENSE.txt/OFL/audio. Архив только docs/design, не shipped demo. Linux gates не означают Windows/Android/hardware acceptance.

- Дополнение21.10.2026: глобальный Alt+Enter/Alt+KpEnter в Playfield._Input до GUI/context, release/echo не переключают; прежние ToggleFullscreen/display.cfg, headless/embedded guard. canvas_items aspect expand обязателен для заполнения без letterbox. scripts/check_sawmill_ui.py --mode fullscreen требует Xvfb+xfwm4/dbus-run-session/xprop; проверяет actual window rect/persistence/paused world. Результаты отдельно fullscreen-summary.json, прежние unit/runner counts не называть повторным запуском.

## Стандартный экспорт Godot,21.10.2026 (Europe/Samara)

- CI-дополнение22.10.2026: check_hud_god_ui.py включает editor/import/use_multiple_threads=false только в одноразовом stage после Windows import crash0xC0000005. Не переносить флаг в рабочий проект без отдельного основания, не игнорировать exit code и не убирать17+17native checks.

- Владелец явно заменил stage-only правило: обычная галочка Export With Debug оригинального project.godot должна включать God mode без Python. Root Directory.Build.props по умолчанию включает DelskDeveloper при Configuration=ExportDebug, до fallbackfalse. Это одинаково для Core/Hosting/всех references. Debug editor/runner и Release остаются обычными; guard на stage действует для явного Developer Debug, но не для ExportDebug. Release с явно принудительным Developer всё ещё запрещён.
- Основной проект src/Client.Godot/project.godot: галочка→Debug/God; без галочки→нет Debug/God/API. Optional packager сохранён для CI/автоматизации, не требовать его от пользователя. Не возвращать прежнее требование DelskDeveloperPackage для стандартного ExportDebug. Новое правило supersedes историческое решение50 и отчёт PR48.
- Стандартный .NET exporter оставляет DLL при debug→release в одной папке: addon addons/delsk_export удаляет только известные Simulation.Developer.dll/.pdb из own Windows managed bundle после подтверждения Client+deps и отсутствия Developer в libraries. Debug/failed release/соседние файлы сохраняются; addon исключён изPCK. Не заменять узкий guard рекурсивной очисткой выходной папки.
- check_hud_god_ui.py сбрасывает forced stage props перед ExportDebug:17nativechecks именно source defaults без CLIflags. Отчёт/восстановление IMPLEMENTATION_REPORT_STANDARD_DEBUG_EXPORT.md/WORKLOG_STANDARD_DEBUG_EXPORT.md. Явные сохранения/аудит/пауза/приватность прежние; аппаратный Windows запуск не приравнивать к успешному экспорту EXE.

## HUD и режим бога, 20.10.2026

- Developer-кнопка «Режим бога» находится в Debug; прежний вход из меню сохранён. Все реализации/поля/транзакции только в Developer/**/*.cs, optional partial hooks исчезают из ordinary сборки. Не включать God-mode в обычный Debug/Release. AssemblyAudit проверяет новые entrypoints и исключение тестового HudGodProbe.
- Деньги вводятся в монетах с шагом0.01, Core получает exact minor; stock — абсолютное общее количество выбранного item/inventory. Резервы/вместимость/границы тика защищены прежним Core API. Escape/Close закрывают окно без выхода из Debug и отменяют advance. Изолированный build_developer_client.py обязателен.
- ResourceInventoryHud: только положительный physical backpack stock, включая reserved physical goods; цепочки по2строки, fallback новых товаров. Не суммировать чужие/складские запасы в HUD. Процедура native controls probe — scripts/check_hud_god_ui.py; headless не означает оконную/hardware приёмку. Инструкции в docs/DEVELOPER_TOOLS.md.

## Пакет8, актуально20.10.2026 (Europe/Samara)

- База — слитый PR#43 (`370673e`). P58 программно закрыт: stdlib loopback research UI/CLI над прежним runner, validated snapshots/отдельные profiles, atomic queue/file lock/recovery/attempts, pause между bounded jobs, исходные JSON/CSV/JSONL и paired summaries. Не включать в ordinary client и не добавлять вторую симуляцию. Процедура RESEARCH_LAB.md, контекст WORKLOG_PACKAGE8.md.
- NegotiationsEnabled null сохраняет прежние settings/hash; false выключает NPC negotiation protocol, не trade/politics, BuySupport идёт прежним прямым ProposeSupport. Это policy factor, не запрет команд Core. Сохранять exact checkpoint; не делать общего politics-off вместо negotiations-off.
- Hosting-only optional StepMeasured/SessionProfiler измеряют controller/Core/sampling, CPU/allocations/memory/encode/decode; не входят в save/PRNG/hash. World18/production-recipes-1/Session2/Envelope2 прежние. Никакого удаления истории/нового режима бюджета/сдвига fixed-price G ради benchmark. Manifest stagePackage8, настройки/версии сложности по каждому controller.
- P59 не закрыт полностью:18 actual runs/paired1200+long5000, independent balances; Hard−Medium capital−91938/−48677/0, negotiation pilot inactive0/0, recovery всех3NPC с cash0 без shop. Длинная история save58.6MB/decode15.7s/peak4.95GB. RESEARCH_RESULTS_PACKAGE8.md хранит отрицательные результаты/определения метрик; cash operating result не accrued profit. Не выдавать3seed/подготовленную food-fixture за универсальный баланс/ROI.
- P60 software/Linux gates:880 ordinary/886Developer/12Python,102exports/14replay/69pairs,1132Linux hashes и35restores; evidence в IMPLEMENTATION_REPORT_PACKAGE8.md. Windows EXE/Android-device/hardwareFPS/audio/реальный P56/Hard efficacy остаются открытыми. Весь P28–P60 **не завершён**. Сводка всех8пакетов, отклонений и дополнительных задач — NEXT_STAGE_SUMMARY.md. Следующий шаг — закрывать перечисленные gates, не новый произвольный P61.

## Пакет7, актуально20.10.2026 (Europe/Samara)

- От слитого PR#42 (`853b993`) реализован пакет7 **P30/P57** и архивный планшет пекарни. Владелец явно разрешил следующий пакет; нормативный §12.1 допускает визуальный пакет раньше. Не подменять им открытые real-model P56/Hard efficacy/P27 gates. Факты — docs/IMPLEMENTATION_REPORT_PACKAGE7.md; восстановление этапов — docs/WORKLOG_PACKAGE7.md. Следующий пакет8 P58–P60, здесь не начинался.
- World18/Rules production-recipes-1/Session2/Envelope2 и controller policies прежние. PresentationMarker.Level/Footprint/Walls/Warehouse — UI-only. Чужие goods/capacity не покидают projection; собственные piles используют physical quantity, не future capacity holds. Форма PresentationFrame не меняет World hash/save. Площадка не добавляет collision authority; anchor/walls прежние.24 piles/4 layers max; stock cache отдельно от static geometry. Модели только существующих L1/L2, shop purple, без process bars.
- P57: только PublicSimulationEvent/PublicEmploymentView; никаких чужих reasons/terms/контроллерных планов в речи.2 simultaneous/3.2s lifetime/8s actor cooldown, приоритет важной реплики, cosmetic clock/hash без World PRNG. Подписи на разных высотах; tablet/menu/help/pause/focus скрывают/сбрасывают эффекты. SFX2-player pool/схлопывание batch, ambience + optional music0% default. user://feedback.cfg вне simulation; три громкости/speech toggle. WAV собственные процедурные/CC0, LICENSE и генератор сохранять. Dummy audio pass не равен динамикам устройства.
- ProductionTabletPresenter заменяет имя SawmillTabletPresenter и обслуживает sawmill/bakery. Те же HouseholdObjectCard buttons/access; Bakery Grain/Bread paths, Upgrade CostPlanks — доски. Общие Help/Close/Time и прежние routes. Нестандартный/многокомпонентный рецепт — полная generic карточка; ширина<980 compact. Не возвращать demo.gd/примерные значения. Шрифты архивов идентичны, reuse existing Nunito/OFL; source bakery zip сохранён в docs/design/bakery-v1.
- 879 ordinary/885 Developer tests;102exports/14replay/69pairs, Linux hashes1132+stopped10/35 restores, actual bakery/sawmill RU/EN/compact/preferences и audio-state/window probes. Gallery — специально подготовленные visual DTOs, не работа NPC. tests/Client.Probes/VisualModelsProbe.cs компилировать только в изолированном stage, запрещён в ordinary audit/PCK. scripts/check_visual_models.py и scripts/check_sawmill_ui.py --mode bakery воспроизводят проверки. Windows/Android/hardware60FPS/real model не объявлять пройденными.

## Пакет6, актуально20.10.2026 (Europe/Samara)

- От слитого PR#41 (`66de3a4`) реализованы P51/P52, программный P53/P56 и планшет лесопилки из архива. Нормативный пакет6 по §12.1 включает P56. Полная приёмка открыта: текущей реальной модели нет; paired600 не доказал преимущество Hard. Факты — docs/IMPLEMENTATION_REPORT_PACKAGE6.md, восстановление по этапам — docs/WORKLOG_PACKAGE6.md. До новых пакетов завершить реальные/сравнительные gates; не выдавать HTTP503/tags/исторический P23 за новый inference.
- World18/Rules production-recipes-1, Session2/Envelope2 прежние. Legacy utility-v2/checkpoint2 совместим; Easy/Medium/Hard — modular-v3/checkpoint3 с canonical executor1/simple1/economics2/planning2, experience schema1 без обучения. Неизвестные версии reject. Сложность выбирается только для новой партии; load/continue восстанавливают exact saved policy.
- Один прежний executor и обычные валидаторы. HiringEconomics/forecast читают только ControllerObservation, не WorldState/future prices/PRNG/чужие секреты. Политический лимит отдельно от бизнеса, signed exposure один раз. Hard defaults12 candidates/depth2/horizon120/evaluations32, disjoint continuation, только первый plan исполняется и пересматривается; не обещать оптимум.
- P56: один runtime; LocalGameModel сохраняет composition manifest в HybridState. Host barrier в TickScheduler через предикат; ProgressWaiting только публикует replies без Core.Step/экономического времени. Retry1/slot сохраняется в trace.RetryCount; queue budget не обходить. UserFallback разрешает слот один раз, отключение модели сохраняется. Load pending/ready=Interrupted без HTTP requery, replay без модели. Никаких prompts/secret terms в public UI/debug feedback. Точная процедура — docs/LOCAL_MODEL_GAME.md.
- Планшет: только supplied assets/scene+OFL, не demo.gd/фиктивные цифры. SawmillTabletPresenter делегирует HouseholdObjectCard buttons/access; общие Help/Close/Time, independent production/inbound/outbound. Ширина<980 — компактная старая карточка с реальными controls. scripts/check_sawmill_ui.py --mode sawmill/model-errors воспроизводит оконные gates через Godot/Xvfb; model-errors занимает localhost11434 и даёт HTTP503, не реальную модель.

## Пакет5, актуально20.10.2026 (Europe/Samara)

- От слитого PR#40 (`7042c91`) реализован пакет5 P47–P50. Владелец явно выбрал C: milk→cheese, beet→sugar, grain+sugar+milk→pastry. Факты/границы — docs/IMPLEMENTATION_REPORT_PACKAGE5.md; процедура/баланс — docs/PRODUCTION_CHAINS.md. Следующий пакет6 P51–P53, с P51. P32 ещё зависит от сложностей; не выдавать fixture специализации за новую сложность.
- World18/Rules production-recipes-1, Session2/Envelope2. World17 явно отклоняется без миграции/перезаписи; отдельный Godot save path. Профиль presentation-economy-v6, новые карты one-v3/two-v3/three-v2; старые файлы не перестраивать. Спецификация0.5 неизменна.
- Recipe.Ingredients — нормализованные1–8 входов, legacy InputItem/InputQuantity только альтернативный формат JSON. Один конечный input inventory; комплект+зарплата+энергия+output hold атомарны. WIP фиксирует recipeId/inputs, входят в hash/checkpoint/invariants/capital/exports. SetRecipe/upgrade только stopped без WIP; AdditionalRecipeIds кумулятивно открывает альтернативы, не меняет вложения.
- Найм/потребности/прибыль/учёт используют все входы. Входящий executor выбирает наименее обеспеченный компонент с admitted cargo и защищает место остальных компонентов одного комплекта; explicit route применяет ту же защиту. Несколько источников — прежняя persisted queue. NPC получает только публичный каталог уровня через ControllerObservation, выбирает recipe по полным входам/энергии/payroll/округлённым рабочим тикам. Без второго planner/LLM/привилегированных команд.
- Молочное сырьё — фермерские поставки в конечном возобновляемом пункте P38, без животных/порчи. Лесная/зерновая машина сохраняет тип. Новые сыроварня/сахарный завод/кондитерская имеют L1/L2; кондитерская строится из древесины10+досок10, без собственного продукта. Цена с землёй90000 против34000 малой фабрики. Bootstrap60000 за период60 тиков через прежний механизм, не новый бюджет каждый tick; market price отдельно от fixed G/capital.
- Проверено835 ordinary/841 Developer tests;102exports/14replay/69comparisons и отдельно900-tick third-map/load/replay. Linux Godot1132 tick hashes + stopped final10,35 file restores; RU/EN new content cards/models/icons, ordinary metadata/PCK без Developer. baseline-food — явно подготовленные3 специализации с прежними весами200/40 и начальным капиталом/предприятиями:20cheese/21sugar/5pastry за900. Стандартные NPC/сложности не меняются. Hardware/long-game/live-model gates остаются отдельными.

## Пакет4, актуально19.10.2026

- После слитого PR#39 (`5fd27c4`) реализованы только P43–P46, пакет4 по §12.1. Старое «P43–P47» в отчёте пакета3 было ошибкой границы: P47 относится к пакету5 и здесь не начат. Факты — docs/IMPLEMENTATION_REPORT_PACKAGE4.md, процедура — docs/PARTICIPANT_TRADE.md. P33 теперь включает встречные переговоры; P32 сохраняет зависимости от сложностей.
- World17/Rules participant-trade-1, Session2/Envelope2. World16 явно отклоняется, не мигрируется/не перезаписывается; новый Godot save path. Ordinary profile presentation-economy-v5: shop L1=warehouse L1=200 через config; прежняя география/materials/prices сохраняются. Спецификация0.5 неизменна.
- Реальная своя лавочка обязательна обеим сторонам новых participant trades/support, бронь/земля не заменяют её. Столица — прежнее исключение; голосование без лавочки. Отмена/выдача/исполнение уже заключённых обязательств продолжаются. Источник лота — собственный building input/output, не machine/cargo/carrier/WIP; public hold защищает от direct double sale.
- Переговоры — частная версионированная подготовка, не второй контрактный движок. Sale создаёт прежний pickup claim; Purchase/Supply — прежний DeliveryContract; Support — AcceptedAwaitingResources прежнего P20, полный физический SignSupport обязателен. Численные terms только через ordinary validators. Нет ранних holds; TTL60,4 версии,16 активных/сторону (config). Counter не продлевает TTL; accept только reviewed version другой стороны.
- Pending negotiations имеют отдельный namespace по инициатору и не двигают публичный NextId. Terms/history доступны сторонам и research; не включать их в public events/чужой Observation/debug/error. Source position публикуется до заявки; чужие остатки — нет. Research negotiations.json входит в22 direct/18 world-only comparison files.
- Buyer guide использует shared BFS/BlockedCells и конкретный claim; несколько рейсов/Stop/expiry не отнимают полученное. Existing supplier-contract truck поддерживает negotiated future delivery, проверяется активный рейс/restore. Buyer-pickup роли у нынешнего транспорта нет; не создавать ради неё новый fleet executor.
- Подтверждены805 ordinary/811 Developer tests;94 exports/12 replay/63 comparisons, отдельно third-map900/load/replay. Linux Godot проверяет также220 trade hashes и restore32/33/34/35/38/51/69. Длинная партия/Windows EXE/Android/60FPS/новая живая модель остаются отдельными gates.

## Пакет3, актуально19.10.2026

- После PR#38 реализованы P37–P42; факты/границы — docs/IMPLEMENTATION_REPORT_PACKAGE3.md, процедура — docs/PHYSICAL_AUTOMATION.md. Историческое указание следующего пакета уточнено разделом «Пакет4». P32/P33 сохраняют зависимости; спецификация0.5 не изменяется.
- World16/Rules physical-automation-1, Session2/Envelope2. World15 не мигрируется/не перезаписывается. Профиль presentation-economy-v4, ordinary maps one-v2/two-v2/three-v1. Profile меняет баланс ресурсов, не координаты/публичность/initial owner; география явно версионирована в сценарии. Старые карты/сохранённые config не перестраивать.
- VolumeWork — единая целочисленная работа личного переноса и обработки грузовика. Переносить только завершённые единицы, сохранять remainder/holds; Stop/уход освобождает остаток. Pickup/Delivery платят по фактическому приёму, deadline закрывает незавершённый остаток. Godot рисует подтверждённый прогресс, не выдаёт товар.
- Freight использует прежние LogisticsState/crew/cargo/queue, а не вторую систему. Handling = worker rate × level × current N, при N0 ждёт; начатое движение сохраняет committed speed. Предварительный бюджет включает обработку/возможный возврат; ставка меняется после фактического конца рейса. Груз/return capacity сохраняются при Stop/expiry/edit. Вход custom route на базу подчинён InboundEnabled, отправка — Enabled, production независимо.
- Natural stock/claim/regen и права сохраняются отдельно от личных товаров. Lease expiry phase0 до работы,1200 — параметр пилота; unfinished job отменяется, готовое остаётся. Public медленнее восстанавливается, ручная скорость та же. Техника только на купленной клетке; оплачивать фактическое движение и добычу, простой бесплатен. Возобновлять добычу при освобождении места, не ждать пустого возвращения грузовика; переезд источника при активном вывозе запрещён.
- Walls — клетки относительно building footprint, anchor всегда gate. Shared BFS/manual/minimap; unsafe build отклоняется до оплаты, нельзя перекрывать выданный pickup/committed path. При сглаживании клеточного движения не срезать углы.
- Routes:32 extraction stops,64 jobs; ordinary commands/validators, только свои building/equipment источники, свои совместимые building input получатели. Активный рейс/груз переживает edit. Никаких индивидуальных работников/нового fleet planner/LLM.
- Research physical-state.json обязателен во всех exports;21 direct/17 world-only comparison files. Независимо проверять природный stock по ResourceConsumed/ResourceRegenerated и bought-resource land в public capital. Ordinary773/Developer779 tests; Linux hash probe включает physical81 ticks и restore14/18/25/33/42/76. Аппаратные Windows/Android/60FPS/длинная история P27 остаются отдельными gates.

## Пакет2, актуально19.10.2026

- P36/P54/P55 реализованы после PR#37. Факты — docs/IMPLEMENTATION_REPORT_PACKAGE2.md, процедура — docs/DEVELOPER_TOOLS.md. Далее пакет3 P37–P42; P32/P33 ещё зависят от сложностей/переговоров. Спецификация0.5 не перезаписывается.
- Обычный профиль presentation-economy-v3:600 ticks, pauseForBallots=true. Freeze после workforce phase5/expiry7; один commit экономического тика, затем типизированные phase9 control events без нового Step. Публично только awaiting IDs/свой вес/свой required candidate до результата. Один принятый бюллетень, signed binding неизменен, no-winner тоже исполняет договор. Не расходовать бизнес-фазы/PRNG/дедлайны на ожидание. Legacy automatic fixtures сохраняют pauseForBallots=false.
- Human/NPC используют один SetVoteChoice validator. Headless с новым профилем требует явный --human-ballot self, сохранённый SelfBallotController. Hybrid ballot использует общий bounded runtime, новый poll barrier без нового economic tick; после load pending/ready ballot slot получает fallback нового epoch без HTTP requery. Replay читает принятые входы.
- World15/Rules stopped-elections-1, Session2/Envelope2; World14 не мигрируется/не перезаписывается. Voting snapshot/submitted, research audit/overrides входят в checkpoint/hash/invariants; research-modifications.json входит в20 direct/16 world-only comparisons.
- Developer только DelskDeveloper=true, Debug, отдельные obj/bin/locks. Compile Remove находится в Directory.Build.targets после SDK items. Release/ExportRelease с флагом запрещён. Клиент только через изолирующий packager; .gdignore/export filters и metadata audit защищают обычный пакет. Нельзя возвращать DeveloperModify в обычный Core/Hosting/ActionKind.
- Вмешательства только на безопасной границе, вне voting и travel quantum: target money/total stock, wage exponent1–5; существующие holds/capacity защищены. Audit old/new + input ordinal, replay проверяет старое значение. Developer сохранения маркированы и имеют отдельный путь; чистые comparisons исключают Developer/modified. Не выдавать injection за G/harvest.
- Developer journal ограничен256 записями/16 feasible candidates и ограниченными текстами, без connection secrets. Полная существующая session history сохраняет известные ограничения P27. Forward budget проверяется между атомарными Step, максимум8 за кадр, stop/cancel на выборе/модели/victory; фокус/help/lifecycle независимы. Не возобновлять forward автоматически после load.

## Начало задачи

1. Прочитать `docs/requirements/game_specification.md` (v0.5) полностью, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/IMPLEMENTATION_PLAN.md` и более узкие AGENTS.md, если они появятся.
2. Проверить git status/diff, текущую ветку, последние коммиты и удалённые refs. Сохранять существующую работу. Не начинать повторную реализацию без проверки файлов.
3. Выполнять ограниченный объём текущего задания. В архитектурной ветке только документы; код начинается отдельным заданием/PR.
4. Новые явные указания пользователя имеют приоритет. В источнике отличать принятое правило, план, предложение и открытый параметр. Поздние §23–25 уточняют ранние варианты. Не переоткрывать согласованные вопросы.
5. Программный месячный объём P00–P26.6 A/B/C и облачная часть P27 реализованы. Актуальные факты/профиль — IMPLEMENTATION_REPORT_P27.md, настройка — ECONOMY_CONFIGURATION.md. Полная локальная приёмка Windows EXE/Android/текущая живая Gate D остаётся открыта; шаги P27_LOCAL_ACCEPTANCE.md. Длинная история имеет измеренные ограничения save/load/памяти;60FPS на устройствах не заявлять. Текущие версии World13/Rules research-economy-1, Session2/Envelope2, utility-economy-v2/checkpoint2; нижние исторические версии отдельных итераций не переписывать. Полноценный второй месяц не начат.

## Пакет1, актуально19.10.2026

- Действующий следующий план — [docs/DELSK_NEXT_STAGE_PLAN.md](docs/DELSK_NEXT_STAGE_PLAN.md), включён по явному заданию пользователя (§12.2). Его принятые уточнения выше старых вариантов спецификации0.5; исходный файл спецификации не перезаписывать. P00–P27/UI01 — история, не повторять. Пакет1: P28/P29/P31–P35; сложности P32 и встречные переговоры P33 остаются зависимостями P51–P53/P45. Факты — IMPLEMENTATION_REPORT_PACKAGE1.md; новые товары §3 остаются предложением.
- Core ActionPolicy/AccessFor задаёт Remote/AtLocation для всех ActionKind. Сохранять порядок privacy/busy/resource отказов старых обработчиков. SetWage требует собственной базы production/extraction/logistics; NPC строит MoveTo→SetWage и сохраняет обычный план. Не возвращать удалённую привилегию ради прежнего тайминга.
- PublicEconomy — одинаковый публичный итог для человека/алгоритма/модели. Чужим не выдаётся breakdown/escrow ID/stock/private terms. CapitalReference-v1 считает account balance вместе с holds один раз, buyer pickup/delivery escrow, supplier bond, stock/WIP, нормативные land/building/equipment levels. Столица/WagesSink исключены из имущества города; казна включена. Стадия без бонуса подтверждается CityProgression; её candidate/since входят в checkpoint/hash/journal.
- Два показателя труда: текущая оплата×N/N и attraction×обеспеченные готовые вакансии/вакансии; pending отдельно, нулевой знаменатель — отсутствие данных. Не подменять оплату announced/pending ставкой. Взвешенные суммы Int128, бухгалтерия long minor. Профиль presentation-economy-v2 задаёт начальные20 и ручной шаг5; исторические fixtures и сохранённый config не переписывать.
- Текущие версии World14/Rules household-management-1, Session2/Envelope2; policy utility-economy-v2/checkpoint2. World13 старых правил не мигрируется и не перезаписывается; Godot использует новый versioned path. Каждый новый export включает public-economy.json и сравнивается при load/replay; stage journal валидируется.
- Глобальный планшет переживает движение, локальная карточка закрывается при утрате места. Menu/help/focus независимы от tactical pause; подготовленная команда ждёт Step. Не создавать фиктивные сложности/переговоры/таймеры погрузки. Графика P30, выборы P36, ресурсная аренда P38 и P45+ не входят в пакет1.

## Текущее уточнение P26.6 (18.10.2026)

- Спецификация0.5 §27 по заданию18.10.2026 добавляет единый экономический профиль и итоговую проверку; §26 A/B/C реализован. Считать готовность по IMPLEMENTATION_REPORT_P27.md и фактическим gates, не объявлять полную аппаратную приёмку по облачному pass.
- Пользователь 17.10.2026 явно разрешил загружать изменения в MaksShelest/Delsk, когда просит разработать, добавить или исправить функциональность. Создавать отдельную ветку и PR; автоматическое слияние этим не разрешено.

## Архитектурные ограничения

- Авторитетные правила только в независимом C# `Simulation.Core`; runner и Godot используют те же правила. Никакой зависимости Core от Godot, UI, HTTP, Python, файловых путей и wall clock.
- Человек и NPC — один Participant, разные контроллеры, одинаковые команды/валидатор/права.
- Жители исключительно числа: population, unemployed и EmploymentGroup.Headcount. Запрещены Citizen/Worker, индивидуальные кошельки, маршруты и персональные контроллеры жителей.
- Алгоритмический режим полноценный. LLM только намерения через сменяемый адаптер; не меняет мир, не блокирует Step, не видит чужие секреты. Один runtime, ограниченная очередь, fallback и дедупликация решений.
- Один поток меняет мир. Инвентари/деньги/обязательства обновляются атомарно. Резерв — право на ту же единицу, не копия товара или денег.
- Деньги/количества/авторитетное время целые или фиксированной точности; проверять переполнения. Float допустим в визуализации, не в бухгалтерии.
- Headless ускоряет вызов тиков, сохраняя длительность добычи/работы/перевозки. Груз никогда не телепортируется. В первом месяце не пропускать пустые тики оптимизатором.
- Сохранять нормативный порядок фаз и включительные дедлайны из ARCHITECTURE §8. Общие дефицитные окна не исполнять по одному NPC. Равенства — воспроизводимый fairness, не постоянный первый ID.
- Производство, входные поставки и отправка продукции управляются раздельно. Начатый рейс завершается; не удалять его груз при Stop/expiry.
- Простой без зарплаты; численность не обнуляется, уход постепенный. Отменённую оплату20% не возвращать. Готовая пустая группа может нанимать; новая зарплата не создаёт человека мгновенно.
- Наём по весу зарплаты с ограничением мест и общей численности; default exponent3, исследовательский профиль задаёт Economy.WageAttractionExponent. В выборах текущая численная занятость даёт один голос на работника, безработные не голосуют; победа только строго>50%.
- Политический договор доступен сторонам и исследователю. Обязательство собственного блока активируется атомарно с фактической компенсацией; один блок/выборы, без перепродажи приобретённых голосов.
- Snapshot JSON, journal JSONL, export CSV/JSON; Python только анализ. Не вводить сервер, SQL, event bus, ECS, DI framework или интерфейс для каждого класса без практической нужды.

## Реализация и изменения требований

- Держать модули небольшими, обычные классы/records и явные вызовы предпочтительнее универсальных абстракций. Создавать проекты по мере реальной необходимости, без пустых заглушек всех будущих систем.
- Для визуального клиента соблюдать границы [P26.6](docs/requirements/DELSK_P26_6_PLAYABLE_PRESENTATION.md): меню, Debug, свободное движение и HUD не создают вторую экономику. Курс и позиция внутри клетки живут в Core. Контекстная команда доступна только на клетке участника; дальнее наведение только объясняет. P27 идёт после P26.6. План или headless smoke не равны визуальной приёмке.
- Баланс и контент — версионированные настройки. Стартовые допущения перечислены в DECISIONS; изменения помечать как допущения, не как согласованные исторические правила.
- Сохранять весь месячный объём §15. Не объявлять числовой найм, автоматику и выборы «потом вне MVP». При риске срока сообщить о переносе фактического завершения, а не незаметно сокращать требования.
- Не изменять `docs/requirements/game_specification.md` без явного указания. Его исходный SHA-256 указан в DECISIONS как исторический; уточнение визуального клиента от 17.10.2026 внесено по явному заданию пользователя. Новые технические решения записывать отдельно.
- Секретные договоры не передавать в generic world dump, prompt, чужой debug/error или публичный event stream. Проверять allowlist проекций.
- Состояние PRNG, очереди/окна/дедлайны, грузы/holds, WIP, кадровые остатки и разрешённые решения должны сохраняться. Replay читает решения, а не повторно спрашивает модель.
- При выходе/сворачивании останавливать ticks, при resume не делать офлайн-catch-up. Проверить это на устройстве, не только в unit tests.

## Проверки и облачное выполнение

- До фиксации toolchain проверять официальные требования Godot/.NET/Android. SDK version и target framework — разные вещи. Не переустанавливать рабочее окружение пользователя без необходимости.
- xUnit проверяет смысл: деньги, товар, численность, границы сроков, приватность, голоса, partial/expiry, dedup и replay. Не имитировать успешную проверку заглушкой.
- Каждая внешняя команда — с hard timeout, сохранёнными stdout/stderr и exit code. Лимиты в IMPLEMENTATION_PLAN §4. Долгие проверки запускать в управляемой сессии, периодически сообщать результат.
- При timeout завершать только своё process tree; не оставлять зависшие дочерние процессы. Не повторять зависшую команду бесконечно.
- Не заявлять build/test/device/model pass без фактического запуска и результата. Экспорт APK не равен проверке на телефоне. Fake model не равна реальному эксперименту.
- Для земли использовать Plots/BuildingTypes/Buildings; legacy Enterprises оставлены для старых фикстур и не смешиваются с землёй. Стройка создаёт нулевой штат. Начальные численности — только инициализация EmploymentGroup; текущий штат и ставка принадлежат группе. Улучшение сохраняет численность, ID и footprint, требует остановки без WIP и материалов во встроенных инвентарях предприятия.
- RulesVersion сейчас research-economy-1. LaborSettings null включает настройки по умолчанию; исторические regression fixtures явно растягивают трудовой шаг, не использовать их баланс для обычной партии. ConstructionInput отдельно учитывает необратимый расход на стройку/уровни. Изменение формы DTO меняет хеши: после проверки старого сценария явно обновить platform fixture, сохраняя старые отчёты как историю. Не выдавать replay за load checkpoint.
- Текущие версии: World schema13, Session schema2, Envelope format2; controller utility-economy-v2/checkpoint2. World11/P19 и support-1/P20, World10/P18 без migration. World8/PR16 и World9/очередь также без migration. World1/P13, World2/P14 и World3/P15 и World4/P16 и World5/P17a и World6/P17b1 и World7/P17b2 сохранения отклоняются (миграции нет); прежние файлы/ветки сохранять. CheckpointStore выполняет direct state restore и checksum/reference/hash validation. Snapshot сохраняет существующие системы, не автоматически будущие: каждый новый модуль обязан расширять DTO/validator, hold ownership, controller state и continuous-vs-load tests. final-state остаётся inspection; не сериализовать delegates и не подменять load replay.
- P11: production/extraction входят в общий EmploymentGroups/LaborWorkplaces и единый ротируемый work order. Новая роль должна входить в population, budget, wages, private/public projections, hash/replay и snapshot P12 ровно один раз. Личные machine jobs и automatic WIP/holds при load восстанавливаются с взаимными ссылками; сами временные Func из LaborWorkplace не сериализовать.
- Найм: phase0 применяет pending wages, phase5 планирует прирост/readiness/одновременные кадровые delta. Учесть будущую оплату при readiness и один общий бюджет владельца; payroll phase4 использует старый N до перехода. Credit — единицы1/denominator, labor PRNG и LastGrowthPeriod входят в hash/save P12. Чужим контроллерам передавать только PublicEmploymentView.
- После достаточных проверок конкретного риска прекращать необязательное расширение тестов и отдавать результат.
- Клиентский проект: `src/Client.Godot/Delsk.Client.sln`, отдельно от Core solution. Godot .NET/SDK/templates ровно4.7.2, общий TFM net10.0, SDK10.0.401. Перед пересмотром читать отчёт P06; не переключать TFM только из-за устаревшего минимального требования документации.
- Godot export может вернуть exit0 при ошибке публикации C#. Проверять журнал через `scripts/verify_platform_probe.py` и состав сборки; на этой среде DOTNET_PROCESSOR_COUNT=1 нужен экспорту. В PowerShell `'-m:1'` передавать в кавычках. Runtime-locks в obj не коммитить.
- В git не класть credentials/keystore/passwords, модельные веса, кеши, builds и большие журналы. Небольшие тестовые данные и manifest допустимы.

## Git, передача в Cursor и авторство

- Отдельная ветка на задачу, небольшие коммиты и PR с объяснением проблемы, решения, проверок и оставшихся рисков. Без автоматического merge, force push, reset --hard или clean чужой работы.
- Перед push проверить diff и author/committer. Автор — Шелестов Максим Евгеньевич; по разрешению пользователя от 17.10.2026 допустимы английское написание имени и подтверждённый GitHub-логин `MaksShelest` в git author/committer. Это тот же автор. Не добавлять иных авторов, Co-authored-by и служебных подписей инструментов. Технические описания LLM как компонента игры сохраняются.
- Не менять глобальный git config; при необходимости настройка только этого репозитория. Не придумывать личный email: использовать подтверждённый адрес либо GitHub noreply владельца.
- Cursor следует тем же инструкциям; локальное окружение не даёт права дублировать правила в UI. Передача: branch/commit, фактические статусы задач, воспроизводимая команда, непройденные gates.
- В конце задачи кратко сообщить что изменено, что реально проверено, ссылку на PR и точный следующий ограниченный шаг. Не выдавать архитектуру/план за реализацию.

## Контроллеры P13

- Agents.Algorithmic -> Core; Hosting не знает concrete policy. Наблюдение ControllerObservation immutable через вложенные ImmutableArray; никакого engine/ScenarioDefinition/raw seed/research hash/foreign stock в Decide. Future secret fields только own/bilateral allowlist.
- Hosting фиксирует все observations перед Decide/Send и даёт только own results. Единственный путь действий — обычные Send/Enqueue/Core validation; общий command budget8, текущий baseline выбирает1. Busy и sale hold не равны завершённой операции.
- Capture сохраняет settings/goals/finite plan/cursor/pending ID/timers/private PRNG/trust/counts. Session2 сохраняет controller blobs/awaiting refs/turns/samples; composition root обязан RestoreControllers до Step. Attach только tick0; не выдать command replay за восстановление памяти NPC.
- Вариативные economic coefficients пока не реализованы: settings принимают только1. В P21 политические weights/trust влияют на цену и риск поддержки. Исследовательские серии P24 не вычёркивать. Три NPC самостоятельные. Godot Playfield показывает 3D-сцену, HUD, подсказки, контекстные действия, меню, 2D Debug и свободный ход человека одной партии; PlatformProbe остаётся smoke-проверкой хешей. Это не полированный UI. NPC по-прежнему ходят дискретным MoveTo.
- Параметры запуска/выгрузок — HEADLESS_TESTING; новая итерация расширяет checkpoints и independent continuous/load comparison. Snapshot может достигнуть64MiB из-за полных traces; следить за реальными размерами. В P17 не добавлять demand generator P18, политику/LLM/UI; остальные P17 части не вычёркивать.

## Рынок P14

- Market/MarketContracts/MarketValidation — конкретные Core classes. Публикация только из собственного Shop.Input; реальные запасы заранее доставлены. Publish/requests/raises/withdraw — remote management в phase1, окна закрываются phase6; cutoff включительный, новый лот доступен заявкам со следующего барьера. Round проверять явно, не исправлять запоздалую ставку.
- Stock hold лота и cash hold quantity×currentPrice реальны; MaximumPrice private policy ceiling. Initial request можно заменить/отозвать до аукциона; одна запись на buyer. В auction новых участников/повторного вступления после withdraw нет. Цена/объём лота неизменны, досрочную отмену продавцом не добавляли.
- Дефицит переводит всех в auction без предварительной выдачи. Default2 rounds×5, minimum raise1 minor; отдельный MarketRandom сейчас только для auction ties, будущий P18 price generator обязан иметь отдельный stream. При final allocation unsold/unused holds освобождаются; выделенные деньги идут в ContractEscrow, lot hold заменяется индивидуальными pickup claims в той же атомарной транзакции.
- Allocation начинает AwaitingPickup: товар в seller shop, escrow=Q×price. P15 продолжает тот же ID: Q=C+R+X; hold/escrow принадлежат только R, за C платится при локальной загрузке, X освобождается на inclusive deadline phase7 с refund/fine. Уже полученный груз не возвращать при expiry. CollectPurchase phase3 проверяет buyer/location/idle/carrier capacity; чужой Transfer прав на claim не даёт.
- Public offers/bids без MaximumPrice/MoneyHold; own bids и buyer/seller allocations по allowlist, nested ImmutableArray. World/research exports полные; не передавать их чужим controllers. P13 baseline пока торгует столице, auction decisions — P21. Новые snapshots/market.json/export validators и save/load tests обновлять вместе.
- Обновлённый Godot SavePath включает RulesVersion; old save не удалять и не вращать в backup нового формата. Platform hashes обновлены после проверки прежних денежных/товарных результатов; не откатывать expectations для маскировки ошибки.


## Выдача P15 и следующий этап

- Pickup.cs: оплата/товар/hold/counters атомарны. Partial не резервирует полное Q приёмного места, принимается только явное положительное количество. Деньги buyer не списывать второй раз после funding escrow. Нулевые fine/refund не создавать как Pay(0).
- Сохранять historical Q/escrow account/terminal contract; expected holds только на R>0. Проверять Q=C+R+X и Paid+Fine+Refund+escrow=Q×price, ledger по escrow/сторонам. Mutable Remaining не добавлять.
- P15 набор205 tests,8 scenarios/5 save-load pairs; pickup.save tick47 перед expiry48 в обратном рейсе. Статусы и номера сверять с отчётом; scenario.json входит в побайтное сравнение. Python читает fine config из экспорта, не рассчитывает игровой мир.
- P16 реализовал согласованный план: buy offers/принятие поставочного обязательства, funded escrow покупателя, залог поставщика, полный резерв конечного места, ручная физическая частичная приёмка, inclusive deadline и штраф из залога без долга. P17 автоматические рейсы остаётся отдельным шагом. Не смешивать Pickup conditional claim и future delivery obligation и не изменять исходную спецификацию.


## Поставочные обязательства P16

- DeliveryOffer/Delivery/DeliveryValidation принадлежат Core. CreateBuyOffer reserve full budget, supplier requests hold requested bond, phase6 whole-window proportional allocation. При недостаточной full destination capacity весь batch не заключает договоры; никакого first-supplier priority или скрытой quota downsize.
- Capital spot/future share existing order/remaining/budget в одном batch; future contract не создаёт вторую эмиссию. Один actor может иметь только одну семью request на capital window. Bootstrap spot recovery cash0 сохраняется.
- DeliverContract — phase3 actual supplier carrier у fixed destination, available stock, one TX: stock/capacity/payment, full return bond. Phase7 inclusive expiry refund buyer/unpaid price, fine из bond buyer (capital: compensation), unused bond supplier. Pickup fine имеет противоположное направление и не объединяется с DeliveryFine.
- P16 R/I=0/U=remaining, personal carrier unassigned until receipt. Это не claim про готовую авто-логистику; P17 обязан добавить R/I/return place/ETA без второй копии quota. Closed financial contract не удаляет goods или started trip.
- Participant offer TTL180 от publication — стартовое допущение. Window cutoff firstrequest+5 limited by offer expiry, window counter fixed for published barrier. Withdraw/cancel только незаключённый остаток, уже активный contract остаётся. Signed-contract cancellation не добавлена новым правилом.
- Checkpoint4 сохраняет buy offers/requests/window, contracts/escrow/bond/capacity, shared capital future bids/compensation ID, pending receipt. Новые holds включать в reverse validation/NextId/hash, projections только parties/research. delivery.json и scenario.json входят в exact byte continuation.
- P16 проверено249 tests/10 scenarios/7 load pairs. Details — IMPLEMENTATION_REPORT_P16.md. Старые World1–3 saves сохранять для прежнего кода; current Godot path содержит delivery-1.


## Автологистика P17a

- One system per enterprise/output, one slot, BuyLogistics N0. Numeric Logistics group входит в EmploymentGroups/Workplaces/Cubic caps/population/payroll ровно один раз. Не создавать работников/их wallets/AI.
- SetLogisticsContract только own system/own supplier contract, quantity из U/source/cargo volume; ручной quantity отклонять. Prepare phase4 reserves R/stock/full wage budget, next phase2 load R→I/source→cargo, reserve return place после освобождения source. Phase3 arrival использует общий ReceiveDelivery; I→D, source return hold released; return empty всё ещё работает/платится.
- Cargo has exclusive stock hold; generic Transfer из/в него запрещён. Manual DeliverContract максимум U, чтобы не потратить R/I. Source return place и salary hold не дублировать на destination и не освобождать при финансовом Fulfilled, пока они нужны маршруту.
- Payroll из held upper capacity×committed wage×roundtrip ticks, actual N per working tick; no loading/idle wage. Active labor Cost0: funded budget второй раз не резервируется. New wage effective end+1. Return final working tick платится до release unused hold/completion. Overflow check ставки/настроек до принятия.
- Snapshot5 saves system/group/immutable trip path/progress/holds/committed parameters, validators reverse refs/time/R/I/source/cargo/return/money/NextId. logistics.json сравнивается побайтно.281 tests/11 scenarios/8 load pairs прошли; current rules logistics-1, old World1–4 saves сохранять для прежнего кода.
- P17a не закрывает P17 целиком: controlled late cargo/crew perturbations, upgrades, incoming/outgoing queues and general own destinations — P17b. Started trip сохраняется после stop/expiry, никаких teleport/drop cargo. Не объявлять primitive return branch проверенной полной late-route системой.
- Research readiness/topics в RESEARCH_READINESS.md — предложения, не новые gameplay requirements и не completed research. MVP~60% субъективная инженерная оценка; полноценный headless baseline P21, реальные LLM pilots P22/P23, серии/анализ P24/P25. Тесты correctness не заменяют результаты гипотез.


## Улучшение транспорта P17b1

- UpgradeLogistics — локальная команда владельца у предприятия, idle, Enabled=false, Trip=null. Цена в казну, материалы только свободные Input→Output предприятия, одна transaction; ConstructionInput учитывает расход. Производство управляется независимо.
- LogisticsSettings.Upgrade nullable/immutable Materials; null — одноступенчатый scenario. Явный sample:30000minor+10plank/cargo20/min+capacity crew3, скорость1 прежняя. ID/численность/ставки/контракт сохраняются; новая capacity не создаёт людей. Проверять planning/minimumCrew/payroll/restore по Level.
- World6/Rules logistics-2, старые World1–5 не мигрируются. Сохранять Level, сверять group capacity/cargo capacity по catalog. ImmutableArray сохраняет allowlist. Подтверждены308 tests/12 scenarios/9load pairs, Windows62 hashes/load12/34/55/60. Локального SDK в новой среде нет, результаты — actual remote CI, не local pass.
- P17b2 own output destination/UntilFull реализован отдельной частью; следующий P17b3 — входящая поставка из своего склада. Полнота P17b (incoming queues, fairness, independent stops, controlled late-return и crew perturbations) остаётся обязательной. [Отчёт](docs/IMPLEMENTATION_REPORT_P17B1.md).


## Собственное исходящее назначение P17b2

- SetLogisticsStorage(System,Destination,Enabled) — remote phase1, только own Warehouse/Shop.Input. Товар из текущего recipe output, quantity из free output/cargo/destination с UnitVolume, manual item/quantity/contract отклоняются. Активный Trip нельзя переназначить; отключение того же назначения не отменяет его.
- Source stock+destination capacity+full payroll резервируются атомарно в prepare; при load товар уходит в exclusive cargo и резервируется return place. Own receipt — внутренний перенос без денег/контрактов/эмиссии, затем empty return. NoDestinationSpace → unpaid wait/gradual outflow, освобождение места → готовность/найм/новые рейсы.
- OwnDestination on system и immutable Trip взаимоисключается с ContractId, references/paths/holds проверяются структурно и reverse ownership. World7/Rules logistics-3, Session2/Envelope1; World1–6 не мигрируются. Проверено339 tests/13scenarios/10load pairs, Windows62hashes/load12/34/55/60. [Отчёт](docs/IMPLEMENTATION_REPORT_P17B2.md) и compact evidence — фактические удалённые проверки; локального SDK/Godot нет.
- Следующий P17b3 — вход из своего склада на предприятие с independent stop, прежний один slot/group. Общие queues/fairness, следующее production destination, controlled late-return и workforce perturbations остаются обязательными частями P17b. Full P17 ещё не готов.


## P17: входящие поставки на предприятие

- Технический ID P17b3 — только подпункт P17. Расшифровка в начале IMPLEMENTATION_PLAN; в отчётах первым писать название функции. Не путать b с отдельным этапом B.
- SetLogisticsInbound(System,Source,Enabled): own Warehouse/Shop.Input, item из recipe input, quantity из free source/cargo/input place. Свой input/личный/cargo/чужой источник запрещены. Source/enabled отдельно от output/production; нельзя переназначить начатый incoming trip. Upgrade требует выключить оба направления.
- Один slot/group: контрактный output → input shortage на одну партию → собственный output → input refill. Это selector двух настроек, не полнота общей queue/fairness всех P17.
- Prepare stock/input-place/payroll, затем FetchingEmpty до источника; реальная загрузка в ArrivalTick, ReturningInput до предприятия, EndTick receipt/release/payroll. Legacy LoadTick = departure barrier для incoming. Stock/return source отличается от дома машины: валидировать все refs/holds по Trip.InboundSource.
- World8/Rules logistics-4, Session2/Envelope1. World1–7 без migration, старые saves сохранять. Доказательства/непроверенное — [отчёт входящих поставок](docs/IMPLEMENTATION_REPORT_INBOUND.md). Следующий P17: controlled late cargo/return и workforce perturbations; queue/fairness/другие направления остаются обязательными.

Проверка входящих поставок:369/369tests,14scenarios/11load pairs, Windows62hashes/load12/34/55/60; independently36exports/22comparisons. Actual remote CI, не local SDK/Godot pass. Compact evidence docs/verification/inbound/checks.json.


## P17: просрочка и физический возврат

- [Отчёт](docs/IMPLEMENTATION_REPORT_LOGISTICS_RETURN.md): controlled checkpoint fork только в тестовой сборке; не игровая смена срока/задержка и не replay от начала. Ordinary admission ETA≤deadline прежний. Локального SDK/Godot нет; результаты считать подтверждёнными только по actual CI.
- Outbound может пережить финансовый Expired; cargo/return place/payroll остаются до физического возврата. ReturningCargo требует expiry/rejection event; ReturningEmpty — своевременного accepted event. Snapshot path/time/stock/capacity/ownership checks обязательны.
- При phase1 SetWage→phase4 prepare одного тика pending pay/attraction после успешной подготовки переносить за EndTick. Группа/CommittedWage должны совпадать; активный pending pay только EndTick+1. Численность в дороге влияет на фактическую оплату, а путь/вместимость начатого рейса фиксированы.
- World8/Rules logistics-5, Session2/Envelope1: DTO прежние, правила зарплатной границы изменены. Старые World1–7 и logistics-4 без миграции; сохранять файлы/ветки, Godot path отдельный.
- Следующее ограниченное задание P17 — исходящий продукт в input собственного следующего производства с реальным местом/путём/сохранением. Общая очередь/priorities/fairness и оставшиеся сценарии contract quota обязательны; полный P17 не закрыт.

Фактически проверено на2d2b0371:393/393 tests,14 обычных сценариев/11 load pairs +1 controlled fork/1 load pair, Windows62hashes/load12/34/55/60; независимая перепроверка39exports/23comparisons. Первый CI имел5 ошибок новых фикстур/ожиданий (не скрывать); итоговые exit codes в verification/logistics-return/checks.json.


## P17: продукт в следующее собственное производство

- SetLogisticsProduction — input другого own Enterprise, текущие output/input items совместимы; ручные quantity/item/contract запрещены. OwnDestination использует прежние UntilFull/stock/place/return/payroll/slot/group; Warehouse/Shop command прежний. Структурная проверка own destination общая, inbound source по-прежнему только Warehouse/Shop.
- Рецепты влияют на будущую подготовку. Committed trip/item/place сохраняются при смене рецепта; новые несовместимые задания ждут IncompatibleRecipe, выключение разрешено. Не подменять физическую приёмку новым договором/эмиссией; stop запрещает новые trips.
- World8/Session2/Envelope1 прежние, Rules logistics-6 без migration старых Rules. [Отчёт](docs/IMPLEMENTATION_REPORT_OWN_PRODUCTION.md). Следующий bounded P17 — общая очередь/nearest deadline/fairness и pause/load; оставшиеся quota cases обязательны, весь P17 не закрыт.


## Завершённые P17/P18

- QueueLogisticsJob upsert по contract/source/destination, без ручной квоты; отдельные direction flags через SetLogisticsEnabled. Прежние Set-команды управляют primary маршрутом. Очередь до64 intents, holds только у выбранного рейса; LastServed/DispatchCount и Trip.JobKey сохраняются. Приоритеты: ближайший deadline, input shortage при enabled production, output, optional refill. Pure reads не сдвигают fairness; равенства oldest-first + seed offset.
- CapitalDemandSettings null сохраняет исторический fixed-order fixture. Для периодической экономики использовать scenarios/autonomous-demand.json или capital-demand-cycle.json. При включённом генераторе начальные CapitalOffers пусты: отдельного второго bootstrap бюджета нет.
- G считает harvested+produced по фиксированным базовым ценам, отдельно consumed/stock change; WIP не finished output. Сглаживание1/2 с остатком, target=max(bootstrap,floor(smoothed×11/10)), bounded base-price rounding. Деньги q×actual fixed order price отличаются от базового ориентира. Новый бюджет только при публикации периода, имеющиеся refunds используются прежде эмиссии; истёкший незаключённый остаток выводится отдельной проводкой.
- Отдельный demand PRNG/фазы только research/checkpoint. Observation получает текущие CapitalPrices и до16 опубликованных периодов CapitalPriceHistory, включая товар с q0, никогда private cycles/random/future state. Цены подписанного контракта не меняются.
- World10/Rules capital-demand-1, старые saves не мигрируются/не перезаписываются. Каждый новый module расширяет DTO/validators/hold ownership/hash/exports/tests. Следующее P19, device gate отдельно открыт.

## Выборы P19

- Elections.cs: ElectionSettings null включает period1200/baseVote1; отдельный последовательный ElectionId с1. SetVoteChoice(Election,Candidate) — authenticated phase1, только текущий ID, последний выбор до/на voting tick. Выбор только собственный; без выбора self. После голосования choices очищаются.
- Phase8 после phase5: один EmploymentGroups список production/extraction/logistics, текущий N включая простаивающих; numeric workers не дублируются на направлениях/рейсах. Один direct ballot на actor/election, tally через Int128 strict2×votes>total. Ровно50% не победа; следующий ID/период.
- Первый winner/election/tick неизменен, будущих выборов нет. ContinueAfterVictory только first winner, через общий command path; Sandbox сохраняет результат. Victory — опубликованный итог; пауза/завершение сессии остаётся ответственностью хоста через существующий SetPaused, Core не читает UI/wall clock.
- Исследовательская ElectionStateView хранит все choices и immutable results/workforce snapshots; ElectionObservation — только own choice, public schedule/participants/final ballots/result. P20 support contracts не добавлять в общий DTO.
- World11/Rules elections-1, Session2/Envelope1. Strict restore сверяет историю N с EmploymentChanged/creation/growth, I10 ballots/I11 population weights/strict majority/journal/first winner/mode. Новый elections.json входит в15 файлов exact continuation. Проверено512tests/19scenarios/16ordinary load pairs +controlled/3replays,57exports/36comparisons. Godot Linux62экономических+40выборных hashes, restore12/34/55/60 и9/19/20/21. Следующее P20; Android device gate открыт.

## Приватная поддержка P20

- SupportSettings nullable: общая trading point/конечные собственные секции, материальная компенсация. Propose/Accept/Reject — phase1, Sign — phase3 после физической доставки. Accepted не связывает голос, partial stock holds exclusive; Sign atomic full transfer+commitment, один собственный block/election. Phase7 expiry unsigned, phase8 executed независимо от победы/current workforce/без рекурсивных блоков.
- Приватное состояние только InspectSupport/research journal/checkpoint/hash/support.json; не добавлять SupportResearchState в WorldView. Controller/participant SupportObservation только own/bilateral. PublicEventsSince фильтрует private commands/payload/choice, без raw sequence gaps/command IDs. Proposal ID namespace per candidate, не глобальный видимый counter. Чужие/неизвестные ID дают одинаковый NotAvailable.
- World12/Rules support-1, Session2/Envelope1. Restore проверяет canonical sections/holds/receipt/block/lifecycle/journal. Старые saves без migration. Compensation ImmutableArray: CommandEnvelope equality структурная, чтобы decode/dedup/playback после load не конфликтовали.
- P20 gate538tests/20scenarios/17ordinary load pairs+controlled/4replays,61exports/39comparisons17files, Godot Linux222hashes/14file restores. Доказательства IMPLEMENTATION_REPORT_P20_P21 и verification/p20-p21/p20-gate.json. P21 в промежуточном commit ещё впереди; latest user разрешил продолжить только после этого gate.

## Алгоритмический baseline P21

- Политика получает только ControllerObservation и собственные CommandResult. Никакого WorldState/Inspect, чужих private support, будущих цен или world PRNG. Research.Runner assertions могут читать полный мир после прогона, контроллер — нет.
- Общий валидатор и barrier Hosting для человека/NPC; план действий не резервирует товар. Доступный баланс/свободный stock исключают holds; Needs вычитают собственные bids/claims/requests/contracts. Один будущий supply contract за раз, bid не выше собственного maximum и не более одного повышения за round.
- Сроки/подбор оплаченных покупок/готовые обязательства до расширения. ETA учитывает путь, вместимость и действующий trip; нельзя продать необходимые контракту товары ради другой цели. Remote auction/wage/support/logistics решения сохраняют SuspendedPlan и продолжают его после feedback; это состояние сохраняется.
- Политика оценивает материальную цену своего текущего блока и риск чужого strict majority; OwnVictoryWeight приоритетен. Условия доступны только сторонам. Передача компенсации — реальные Move/Harvest/Transfer/Sign; один неудачный Sign NotAvailable помнится до конца election. Собственные предложения ограничены двумя открытыми.
- utility-economy-v2 имеет private PRNG, persisted plan/cursor/pending ID/wage cooldown/bid reviews/suspended plan/failed signs. Старые controller v1 и старые Rules отклоняются явно, не перезаписывать старые saves. Runtime seed derived SHA256 controller-v1:seed:actor; порядок AttachControllers не меняет barrier.
- baseline-economy — обычный старт трёх NPC/две цепочки/периодический спрос. baseline-auction/logistics/politics — подготовленные стресс-фикстуры, не утверждённый игровой баланс. LLM отключена полностью. CI и локальный scripts/check_headless.py используют один полный bounded gate.

## Локальный адаптер и decision slots P22

- Agents.Llm -> Core/Algorithmic, Hosting не знает concrete LLM. IController BindSession/BeginDecisionBarrier/EndDecisionBarrier используют существующие epoch и общий frozen observation barrier. HTTP/Task/wall clock не входят в Core. Replay не создаёт адаптер.
- ModelOptions — чистый preview разных utility goals, до4+Hold; ответ выбирает только optionId с exact decisionId/actor/observationVersion/sessionEpoch. Запрещены unknown/duplicate fields, редактирование price/quantity/actor, oversized/control JSON. RelevantVersion проверяет own/bilateral и referenced public terms; чужая нерелевантная добыча не делает план stale.
- Модель выбирает только новый неисполненный план. Уже начатый план/принятый supply/оплаченный pickup/короткий auction bid продолжает алгоритм. Неотправленный план снимается до ожидания и пересчитывается на applyTick; не сохранять дубликат remote action в SuspendedPlan. Новое обязательство при ожидании также исполняет baseline.
- Shared ModelRuntime: один физический inference, queue3, one pending/actor, batch admission с tick rotation и frozen responses. Default min interval30, apply15/TTL15, max4/120ticks, HTTP4s, retries0. Cancellation-ignoring request удерживает физический slot до фактического окончания, но не блокирует Step/fallback. Minimum apply2; bounds не поднимать без записи конфигурации.
- HybridState v1 сохраняет utility checkpoint2, logical Pending/Ready slot/request/options/reply/relevant versions, serial/cooldown/budget/memory/resolutions. Task/HTTP не сериализовать. Load меняет epoch: незавершённый slot, включая ещё не применённый Ready, получает один fallback на сохранённом applyTick, повторного запроса нет. Старые callbacks не применяются. Resolved slot не выполняется повторно.
- model-decisions.jsonl/ModelTrace — private researcher evidence, не public event/чужой prompt. Память только собственная; raw reason модели не возвращать как инструкции следующего prompt. Policy settings/trust/current published prices разрешены; future state/PRNG/договоры третьих сторон запрещены.
- hybrid по умолчанию off, --model-mode ollama требует explicit config и loopback endpoint. Realtime и LiveHeadlessStress ставятся только при включённом runtime; выключенная модель сохраняет headless/paced и совпадает с baseline по миру. ControlledFakeS7 не выдавать за реальную LLM. Source World12/Rules baseline-1, Session2/Envelope1 прежние, hybrid-plan-v1/checkpoint1. P26.6 открыт перед P27. P27 не начат.


## Планшеты и общий ход времени — P26.6 B1

- TabletState — состояние взаимодействия UI, не второй симулятор. При открытии пауза всего мира по умолчанию; команды остаются обычными Send и ждут следующего тика. Включение времени, разрешённое движение или закрытие планшета снимает только тактическую причину паузы. Menu/Debug/lifecycle продолжают останавливать мир, без catch-up. Повторное ApplyPause без изменения состояния не сбрасывает накопитель времени.
- CanTravel и CapitalTradingOf — узкие read-only Core-запросы. Не менять через них состояние, не раскрывать чужие заявки/holds. Версии World12/Rules baseline-1, Session2/Envelope1 и policy прежние. Личный ввод движения при Busy не снимает тактическую паузу. Движение в той же клетке планшет сохраняет; выход за footprint/терминал закрывает.
- Биржа B1 — только столичные spot-заказы; торговля участников и будущие поставочные обязательства не объявлены реализованными в UI. В Debug прежние прямые команды остаются. Фильтр/порядок сохраняются между открытиями и сменой языка в текущей партии; новая партия/Load сбрасывает их.
- Следующая ограниченная итерация B2 — непрерывная добыча и отмена незавершённого цикла при уходе. Нужны Core/rules/checkpoint/replay и освобождение holds; не заменять это UI-выдачей товара. C затем добавляет торговлю участников и выбор общих мест лавок. P27 и hardware gates открыты.

## Непрерывная добыча — P26.6 B2 (18.10.2026)

- StartHarvesting(ItemId,Quantity1) — обычная phase3 команда. HarvestLoop повторяет одноединичные циклы в Core; StopHarvest — phase1, до завершения phase4. Старый Harvest сохраняет конечное количество для NPC/Debug. Товар появляется только при завершении, следующий цикл держит ровно свою вместимость; NoCapacity останавливает без автоматического повторного запуска.
- WASD разрешён при личной добыче: движение внутри той же клетки/соседнего ресурса того же ItemId сохраняет цикл; выход отменяет незавершённый прогресс и hold. Personal equipment привязан к клетке машины, отмена освобождает обе ссылки. MoveTo сначала валидирует путь, затем заменяет добычу движением; NoPath её не отменяет. Прочие Busy/продажа по-прежнему блокируют ход.
- Rules manual-harvest-1; World12/Session2/Envelope1 и policy прежние. Новая семантика кодируется существующим PersonalJob.Kind, поля checkpoint не добавлены. Старый baseline-1 явно отклоняется, сохраняется на прежнем пути; новый SavePath включает RulesVersion. После TickCommitted допускается только типизированный phase9 HarvestCancelled/LeftResourceArea от атомарного кванта. Нельзя ослаблять проверку на произвольный незавершённый журнал. Replay включает курс на конечной границе тика.
- OwnHarvest — read-only собственная проекция; прогресс Tick/TravelQuantum только рисуется UI. Меню/Debug/focus останавливают и сохраняют активный цикл; загрузка восстанавливает его, уход/явная остановка — нет. Проверено679tests/81exports/9replay/54comparisons и Linux Godot4.7.2; аппаратная приёмка и P27 открыты. Далее C.

## Единый баланс, рынок участников и P27 (18.10.2026)

- config/economy.json — основной профиль обычных карт. Цены/каталоги/зарплаты/найм/сроки/штрафы/спрос/выборы и правила world отдельно от npc. Не добавлять обходящие его новые экономические magic numbers в UI/политику. Самостоятельные исторические fixtures остаются самодостаточными; не ломать их ради баланса игры. RunArtifacts manifest Stage P27.
- EconomyProfile (Hosting) только разрешает данные; Core не читает файлы. NPC economics применяет concrete composition root через AlgorithmicController.WithEconomy, без зависимости Hosting от Algorithmic. Identity не перекрывается. Ошибки/дубликаты/unknown fields не игнорировать. Для нового исследования --economy явно; load/replay используют сохранённые world/controller settings. Нулевой выпуск допустим: fixed baseline targets не обязательны для research profile, но инварианты обязательны. Python verifier поддерживает пустой workforce snapshot.
- Shared IsShopSite в обычных картах, ровно по числу участников, без назначенного участника. Core quota owned-or-reserved≤1, своё reserved→owned допустимо; cancel/expiry освобождает без возврата платы. NPC предпочитает своё/ближнее доступное. Legacy ShopParticipantId только для отдельных прежних fixtures, не смешивать два режима в карте.
- PublicShops — только география; PlayerMarketView — Observe/публичные условия/собственные bids и двусторонние allocations. Не добавлять чужой inventory/maximum price. Market tablet сохраняет общую паузу/место, шлёт обычные публикацию/transfer/bid/raise/withdraw/pickup. Количество читается на момент нажатия, включая паузу; withdrawal несёт актуальный round. Оплаченный товар не телепортируется.
- World13/Rules research-economy-1/Session2/Envelope2. GZip/base64/checksum с64MiB file и512MiB decoded limit, сохранение полной истории, direct validation и atomic backup; Envelope1 текущей схемы читается, старые World12/rules не мигрируются/не перезаписываются. Профиль5000 тиков: save≈58MB/load≈11с/working set после второй сессии≈2.93GB; не выдавать это за мобильную приёмку.
- P27 сохраняет software/hardware границу. Облачный gate, профиль, RU/EN market UI и парная проверка профилей — в отчёте/evidence. Следующий ограниченный шаг — Windows EXE/Android и текущая реальная Gate D по P27_LOCAL_ACCEPTANCE.md, затем второй месяц с учётом измеренных ограничений. Исторический P23/P25 не заменяет новый запуск.

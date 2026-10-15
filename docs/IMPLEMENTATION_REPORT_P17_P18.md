# Очередь логистики и городской спрос

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.

## Этап1: завершение P17

База — merge03237d8 после PR16, рабочее дерево чистое. Полная спецификация, архитектура, решения и план прочитаны в предыдущей итерации и повторно сверены с актуальной неизменённой базой; очереди/спрос/инварианты перечитаны. Ветка feat/logistics-queue-capital-demand.

Durable Queue содержит входящие, собственные исходящие и контрактные задания; QueueLogisticsJob upsert по каноническому ключу без ручной товарной квоты. Прежние Set-команды заменяют primary задание своего направления. SetLogisticsEnabled отдельно останавливает/возобновляет направление; Enabled каждого job позволяет поставить его на паузу. Начатый Trip.JobKey связан с сохранённым заданием и завершается после stop.

Планировщик выбирает выполнимые задания: ближайший contract deadline, вход при включённом производстве и нехватке следующей партии, собственный output, optional input refill. Равенства — oldest LastServed, начальное равенство разрешается seed offset; успешный prepare обновляет DispatchCount/LastServed после atomic reserve. Чтение readiness/cost не расходует случайность. Приоритеты восстанавливаются из сохранённых контрактных сроков, рецепта, запасов, flags и истории обслуживания, без отдельной расходящейся копии deadline.

Очередь хранит намерения, не товар/денежные holds. Выбранный рейс резервирует прежние stock/destination/return/payroll и общую contract quota. Один slot/group на предприятие сохранён. Очередь ограничена64 заданиями для bounded snapshot; повторные задания не размножаются. World schema9/Rules logistics-7, прежние World/Rules без migration.

Проверены441/441 tests,16 ordinary scenarios,13 ordinary save/load pairs + controlled late-return pair, независимые45 exports/27 byte comparisons; отдельный queue command replay и byte compare. Linux Godot restore/build/import/smoke/strict verify:62 hashes и load12/34/55/60. Все процессы bounded. Исходные money/item ожидания прежнего экономического цикла сохранены перед обновлением platform hashes. Проверены I01–I09/I12–I17 применительно к P17 и S6: обязательства, exclusive cargo, source return place, реальные пути/зарплаты, существующий controlled late-return/crew/wage gate; политика I10–I11 относится к P19/P20.

Новые21 тест проверяет реальные13+7 обязательств рейсами10+3+7, затем собственный output10; равные deadlines и seed winners; priority enabled/paused production; независимый direction stop; R/I/U при ручной частичной приёмке; два транспорта на один остаток13; queue/active refs/rotation corruption; каждый следующий hash/ledger/event после direct load. Первый запуск тестов ошибочно пересёкся со сборкой и смешал assemblies; исключён из gate, последовательные build/test повторены. Исправлены старая проверка urgency при paused production и runtime IDs новой competing fixture. Полные сведения — verification/p17-p18/p17-gate.json.

P17 полностью зелёный в границах IMPLEMENTATION_PLAN. Аппаратный device gate P06 остаётся отдельным открытым условием. После этого gate разрешён переход к P18 текущим указанием пользователя.


## Этап2: городской выпуск, спрос и цены P18

После зелёного P17 gate добавлен Core CapitalDemand. При CapitalDemandSettings null старые фиксированные regression заказы сохраняются; новая нормальная конфигурация autonomous-demand.json использует периодический генератор с прежними NPC без изменения их правил/политики. capital-demand-cycle.json — отдельная cash0 recovery fixture с двумя raw/product цепочками, без бесплатного хозяйства и без залога за будущий товар.

Период снимает delta фактического manual/automatic Harvested и finished Produced, RecipeInput+ConstructionInput, Exported и физического запаса. G считается по неизменным BasePriceMinor; WIP не готовый выпуск, внутренние переносы/перепродажа не новый выпуск. Gross/consumption/stock change опубликованы разными research полями. Сглаживание (previous+G+carry)/2 сохраняет carry; target=max(bootstrap,floor(smoothed×11/10)).

Новый бюджет публикуется в tick0 и затем только в phase6 при Tick%PeriodTicks=0. Конфиг по умолчанию:period60/bootstrap20000minor/coefficient11/10. Минимум1 каждого raw item гарантирован в пределах bootstrap, остальные q распределены по нормализованным товарным весам5000..15000. Целочисленный residue заполнен воспроизводимо, OrderedBase<=TargetBase и ошибка<minBase. Это стартовое техническое правило, не доказанный оптимальный баланс. Фактический MonetaryBudget=Σq×order price отдельно от базового ориентира.

Common triangle9000..11000bps/600ticks, item8000..12000/240ticks, noise9800..10200 генерируются отдельным private demand stream. PriceScaleBasisPoints default10000 позволяет проверить рост цены без подмены выпуска. Подписанная цена неизменна. Observation содержит текущую цену всех товаров, включая q0, и последние16 опубликованных периодов цен; private cycles/random/seed/будущие цены туда не входят. Полный demand checkpoint/research state не используется как controller prompt.

Spot и future используют один CapitalOffer/Remaining/BudgetHold. Перед новой эмиссией используются свободные refunds в CapitalBudget; существующие hold/escrow не являются free. Phase7 выводит неиспользованный незаключённый остаток explicit DebitExternal/CapitalDemandRetired. Заключённый контракт сохраняет свой deadline и бюджет, даже когда исходный offer истёк. Window cutoff ограничен inclusive offer expiry; приёмка/закрытие окна происходит до retirement. Совместные исходные fixed CapitalOffers+генератор запрещены конфигурацией, чтобы исключить второй bootstrap бюджет.

World schema10/Rules capital-demand-1, Session2/Envelope1. Сохранены flow baseline, smoothing carry, period records, рыночные фазы/PRNG, все сгенерированные definitions/created/expiry/released и прежние runtime windows/contracts/holds. Строгий restore не проигрывает мир: constructor временное состояние заменяется saved state и проверяется references/ledger/NextId/quotas/funding/random/history/hash. World9 queue-only и предыдущие версии без migration, прежние saves не перезаписываются. Новое capital-demand.json включено в14 побайтных export files.

35 новых P18 тестов проверяют фиксированный G при изменении рыночного multiplier, истинные raw+finished output и отдельное потребление/запас, WIP, odd smoothing carry, coefficient11/10, bootstrap при cash0/нулевом выпуске, три границы harvest19/20/21, seeded waves с постоянным стоимостным ориентиром, private projection и q0 published prices, inclusive order expiry/late rejection, общий spot/future budget и reuse refund следующего периода, corruption phases/PRNG/бюджета/order и direct load в середине периода/добычи/на границах. Отдельный NPC integration восстанавливает policy plans вместе с demand state и сравнивает world/controller hashes/command tape/observations.

Сценарии:queue226ticks; cash0 demand185ticks; live autonomous-demand1000ticks с3 NPC. В отдельном проверенном пилоте NPC дали84plank/37bread, экспорт46plank/30bread,51 оплаченный spot settlement; это проверка работы нового генератора с прежними алгоритмами, не заявление о завершении P21 или балансе игры. Будущие доходы/новые political/LLM/UI decisions не добавлялись.

Первый целевой P18 test ожидал refund к tick20, хотя его fixture contract закрывался21; срок fixture уточнён до8 тиков, refund теперь происходит19 и проверяет reuse на20. Ошибка тестового ожидания не скрыта; итоговый suite запускается после исправления. Отчёт начальных environmental/race failures P17 сохранён выше, финальная проверка использует последовательные build/test и готовые assemblies.

## Граница завершения

P17 завершён до P18 отдельным green gate. P18 оценивается по полному окончательному набору, а не наличию модуля. Фактические итоговые коды/числа и Linux Godot report записываются в verification/p17-p18/final-gate.json. Windows и Android hardware в этой среде не запускались; Android device gate P06 открыт. P19, политика, LLM и UI не реализовывались. Следующий пункт плана — **P19: election scheduler, current worker blocks, strict majority**.


## Фактический окончательный gate

| Проверка | Реальный результат |
| --- | --- |
| Locked restore / Release build |exit0,0 warnings/errors |
| xUnit |476/476 passed,0 failed/skipped;420 прежних+21 P17+35 P18 |
| Runner |18 ordinary scenarios и controlled late-return fixture, exit0 |
| Direct save/load |15 ordinary pairs+1 controlled pair; все14 export files совпадают |
| Replay |queue226 и demand185: команды воспроизведены и14 файлов совпадают |
| Independent checks |53 exports; money/item/capacity/population/contracts/queue/demand/funding проверены |
| Byte comparison |33 comparisons×14 files, включая capital-demand.json |
| Godot Linux |client locked restore/build, import/smoke/strict verifier exit0;62 hashes, load12/34/55/60 |
| Requirements |SHA-256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376 прежний |

Полная команда `python scripts/check_headless.py --output artifacts/headless-p17-p18` реально выполнена с установленным SDK10.0.401/runtime10.0.12; внешний wrapper ограничил весь gate600s, вложенные restore/build180s, tests120s, scripted runner30s, autonomous300s, verifiers15s. Последняя сборка проверена до запуска tests/runner. Отдельный финальный Godot4.7.2 .NET smoke выполнен после проверки прежних экономических чисел и явного обновления platform fixture. Полные журналы/большие research traces находятся вне git; компактные metadata и размеры опасных saves — verification/p17-p18/final-gate.json.

Итог: **P17 и P18 завершены по фактическому gate**. Следующий пункт IMPLEMENTATION_PLAN — **P19**. Отдельный Android device gate P06 по-прежнему открыт; данная проверка не считается запуском на телефоне или Windows hardware.

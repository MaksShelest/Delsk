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

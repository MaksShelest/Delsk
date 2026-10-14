# Отчёт реализации P15

Автор: Шелестов Максим Евгеньевич. Дата: 14.10.2026.

Выполнен только P15 из согласованного плана, от merge P14 `89c604b534bed18a21021f884a1861de99bdf7f6`, отдельная ветка `feat/pickup-p15`. Source requirements v0.3 не изменялись. Полный объём P16–P27 сохранён; автоматического merge нет.

## Реализовано

- CollectPurchase использует прежний allocation ID, покупатель/позиция/idle/capacity проверяются в Core. Товар реально переносится из seller shop в buyer carrier в атомарной transaction; расчёт из funded escrow пропорционален партии. Quantity/source/price договора не назначаются клиентом.
- Несколько рейсов обычным MoveTo/Transfer. Q=C+R+X, active stock hold и escrow только на R. Fulfilled без штрафа; Expired освобождает остаток продавцу, fine/refund оплачиваются один раз. Inclusive deadline: arrival/local receipt раньше expiry phase7. Полученное в пути не отзывается и не дублируется.
- Startup default TTL120 от allocation/fine5% остатка. Штраф округляется вниз, Int128 защищает промежуточное произведение. Не добавлены cancellation/delivery/auto-route или отдельные работники.
- Direct save/load сохраняет новые counters/deadline/settlement, pending command, escrow и реальные пути/holds. Reverse hold validator и ledger-by-escrow расширены. Own projections по allowlist для buyer/seller, research exports полные.
- RunArtifacts: market/summary/final-state расширены; новый scenario.json позволяет видеть фактические параметры прогона. Python только независимо сверяет C# результат. Планы/инструкции Rider/Godot и следующий P16 обновлены.

## Фактически проведённые проверки

Locked restore, Release build: exit0,0 warnings/errors. Standalone xUnit: **205/205**, failures0/skipped0,16.418s (MTP через dotnet test отдельно проверяется в remote CI). Это33 новых случая к172 P14: четыре поездки с capacity3 для full receipt, partial/zero collection, fine rounding/overflow, wrong buyer/location/busy/quantity/capacity, inclusive t−1/t/t+1 и arrival on deadline, duplicate receipt, privacy, structural corruption, queued receipt, direct load/replay.

Полный `scripts/check_headless.py`: **8 сценариев/5 save-load пар**,42.269s на этой среде. Все11 файлов каждой пары сравнивались побайтно: state/ledger/events/commands/metrics/labor/controllers/decisions/agents/market/scenario. Все18 завершённых выгрузок прошли independent money/item/capacity/population/settlement verification. Cut points: cycle55/equipment101/autonomous350/market44/pickup47. Дополнительно unit tests сравнили каждый последующий tick после pickup cut31/35/36/40/46/47/48/50. Старые контрольные денежные/товарные числа сохранились, включая autonomous1000ticks.

Новый scripted S3 pickup: allocation31, Q10 wood по500minor; receipt36 и46 по3; carrier3 заставляет два физических рейса; departure47/expiry48/arrival50/unload51/final52. Paid3000/Fine100/Refund1900, C6/R0/X4. Seller13100/buyer6900, escrow0, total20000; buyer6/seller4 physical wood. Save на47 = **103641 bytes**. Final hash `c3813a288358b4de2d222e4329f7c03057cfed210de8a65cec5f44dfa505f18c`.

Godot4.7.2 .NET/Linux: реальный client build/import/headless smoke и проверка логов прошли. **62/62** tick hashes равны C# runner, lifecycle pause/resume проверен, direct saves restored12/34/55/60. Current cycle hash `09b178043488d1e9e7acf9ad8c138c2ba602d5f4f4de3761c6fa3abf2d30f2d6`. Expectations обновлены после independent balances, из-за DTO/config/rules shape. SDK10.0.401/runtime10.0.12, TFMnet10.0 не менялись.

Две промежуточные проверки были неуспешны: analyzer xUnit2031 требовал корректный Assert.Single overload; два corruption tests ожидали generic error вместо более раннего OrphanStockReservation. Тесты исправлены без обхода validator, весь набор повторён успешно. Полные stdout/stderr/exit/hard timeout журналы сохранены отдельно; компактные доказательства — [checks.json](verification/p15/checks.json).

## Совместимость и ограничения

World schema3/Rules pickup-1, Session2/Envelope1. World1/P13 и World2/P14 отклоняются явно, миграция не реализована; прежние files/branches сохранены. Новый Godot user path содержит pickup-1. final-state/scenario inspection/config не являются loadable world snapshot.

Новые APK/Windows EXE не экспортировались, телефон не проверялся; device gate P06 открыт. Godot сцена всё ещё technical62-tick playback без human market UI. NPC baseline пока не выбирает auctions/pickup (P21), поставочные обязательства P16/авторейсы P17/политика/модель ещё не реализованы. Полная игра без модели — Gate C/P21; ручная проверка ядра/runner доступна сейчас.

## Следующая ограниченная задача

P16: buy offers/принятие будущей поставки участнику/столице, funded buyer escrow и полный destination capacity hold, supplier bond5%, manual physical partial receipt, inclusive deadline180, funded fine/refund/bond release без долга. Сразу checkpoints/ownership/exports и full/partial/zero/dedup/deadline/load/replay tests. P17 авто-логистику, политику, модели и UI не добавлять в ту же итерацию; полный текст задания в IMPLEMENTATION_PLAN.

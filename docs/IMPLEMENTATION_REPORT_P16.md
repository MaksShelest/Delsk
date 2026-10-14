# Отчёт реализации P16

Автор: Шелестов Максим Евгеньевич. Дата: 14.10.2026.

Выполнен только P16, от merge P15 `814573cab138f8fe90610e189eb918371293bb29`, отдельная ветка feat/delivery-p16. Source specification v0.3 неизменна. P17–P27 сохранены в плане, automatic merge не выполняется.

## Реализовано

- CreateBuyOffer/AcceptDeliveryQuantity: funded buyer budget и запросы объёма будущей поставки без требования наличного товара. Supplier резервирует реальный залог. One request/window; edit/withdraw, failures сохраняют прежний hold. Fixed price, proportional allocation по всем requests с largest remainders/seeded ties.
- Participant full destination capacity проверяется всем closing batch; если нет места, ни один contract не подписывается, supplier holds освобождаются, buyer budget остаётся на незаключённый остаток. Offer remainder expiry/cancel не отменяет существующий contract.
- Capital spot/future используют один существующий order/Remaining/BudgetHold, один phase6 batch, не две эмиссии. Bootstrap наличная продажа с cash0 сохранена. Один actor не получает дополнительные заявки двумя способами. Price берётся из заказа, не повышается поставщиком.
- DeliveryContract: escrow + отдельный SupplierBond, fixed parties/item/Q/destination/deadline, Active/PartiallyDelivered/Fulfilled/Expired. DeliverContract принимает физический свободный carrier stock supplier у destination, participant получает реальный stock вместо capacity hold, capital — export sink. Money выплачиваются только за принятое.
- Full receipt возвращает весь bond; inclusive deadline refund unused price buyer, fine из bond buyer (capital: CapitalCompensation), остаток bond supplier. Goods в личном carrier не удаляются при expiry. Partial/expiry/dedup не списывают повторно. R/I0/U=remaining: до P17 carrier не назначен конкретному contract.
- Checkpoint/ref/hold/ledger validation/NextId/hash/allowlist расширены вместе; own contracts только стороны, researcher full state. Новые delivery.json и scenario.json входят в exact continuation checks. Numeric workers/population остаются агрегатами.

Default ContractTTL180/fine5% и достаточный bond из первоначального DECISIONS сохранены. Новое стартовое допущение OfferTTL180 от publication, window5 от первой request, capped at expiry. Нулевые суммы после floor не создают платежей; Intermediate products/supplier sums Int128, balances long checked. Signed-contract cancellation нового правила не получила: cancel/withdraw только незаключённый объём.

## Фактически проведённые проверки

Locked restore/Release build прошли. Итоговый standalone xUnit **249/249**, errors0/failures0/skipped0,17.179s. Это44 новых случая: escrow/bond/full capacity/future promises, participant/capital full/partial/zero receipt, shared spot/future quota, proportional allocation/reversed submission order, edit/withdraw/cancel/offer expiry, NoCapacity whole batch, stale window, wrong rights/location/busy/qty/stock/money/overflow, received/late goods, exact deadline arrival, zero rounded bond, structural corruption, privacy, pending receipt/dedup, direct load/replay. До новых тестов прежние205 тоже реально прошли.

`scripts/check_headless.py`: **10 сценариев/7 save-load пар**,46.150s на этой среде. Все12 экспортов пары совпадают побайтно: state/ledger/events/commands/metrics/labor/controllers/decisions/agents/market/scenario/delivery. Все24 завершённые выгрузки прошли independent money/item/capacity/population/settlement verification. Cut points: cycle55/equipment101/autonomous350/market44/pickup47/delivery37/capital-delivery3. Participant unit cases дополнительно сравнивают каждый последующий tick после cut1/3/7/14/17/21/34/37/41/46/47; mixed capital spot/future window также direct restored. Это direct load настоящего состояния, не подмена replay.

Participant S3: allocation7/deadline47/final48, две physical поездки carrier3 после добычи3+3. Q10 по500minor, D6/E4, Paid3000/Refund2000/Fine100/BondReturned150; initial bond250. Supplier12900/buyer7100, total20000; real buyer stock6. Save37 во втором outbound = **80591 bytes**. Hash `0e2dfdc495e6272116cb5dc7403326c3a708b6c5c92c20ba35d60fc237aa987c`.

Capital S3: allocation6/deadline46/final47, отдельный public terminal5,1; те же Q/D/E и финансовые counters. Supplier12900, CapitalBudget2000, CapitalCompensation100; только первоначальные external5000, total15000. Real Exported6. Save3 в volume window = **25200 bytes**. Hash `8a8a91cd50203ef560b178243f26ca0d8108250d0f32a6efada2d8aef158b767`.

Прежние численные результаты сохранены: cycle wood10→plank5/export5, cash112500/wages2000/treasury500; autonomous1000 cash NPC2/3/4=93475/70200/97058, plank81/bread50 produced, population28/unemployed21. Новые canonical hashes изменились из-за DTO/config/window shape: cycle `9c2625d09795285ccb67b3b196874b4d5b5c1a2077e262385694c73dd6a3582f`, autonomous `d85bb49a1e601d94b610bf05605ab8e322322ef898f636c5146f368a3ef0d9c3`.

Godot4.7.2 .NET/Linux: actual client build/import/headless smoke и log verifier прошли; **62/62** tick hashes совпадают с runner, lifecycle pause/resume, direct restores12/34/55/60 проверены. Platform expectations обновлены после independent balance checks. SDK10.0.401/runtime10.0.12/net10.0 не менялись.

Промежуточный набор241 имел4 failures в capital cases из-за invalid fixture (terminal на private shop plot). Исправлены test data, land rules не обходились; итоговые249 и весь headless набор повторены успешно. Полные stdout/stderr/exit/hard timeout logs сохранены отдельно; компактные доказательства — [checks.json](verification/p16/checks.json). Remote CI публикуется в PR отдельно после фактического результата.

## Совместимость и границы

Current World4/Rules delivery-1, Session2/Envelope1. Реальный load P15 pickup checkpoint новым runner вернул expected exit1/UnknownWorldSchema. World1–3 явно отклоняются без миграции, прежние saves/ветки сохраняются; Godot user path новый по RulesVersion. Проверочная утилита по умолчанию пишет artifacts/headless-p16, чтобы не перезаписывать saves прежнего этапа. final-state/scenario inspection/config не заменяют checkpoint.

Новые APK/Windows EXE не экспортировались, Android device не проверялся. Godot сцена остаётся technical playback, human game/contract UI ещё нет. NPC baseline пока не выбирает participant auctions/contracts (P21), real model не подключена. Automatic cargo/return/queues/независимые stops — P17, demand/elections/hidden support — далее по плану. Full algorithmic Gate C/P21 ещё не достигнут.

## Следующая ограниченная итерация

P17a: transport system/slot предприятия, покупка/характеристики и numeric Logistics group, funded physical round trip существующего contract, exclusive R/I quotas и destination/return place, payroll и direct save during outbound/return. P17b затем обязательно добавляет queues/upgrades/inbound/output automation, независимые stops и late return. Весь P17/месячный объём сохраняется; подробное задание — IMPLEMENTATION_PLAN.

# Отчёт первой части логистики P17a

Автор: Шелестов Максим Евгеньевич. Дата: 14.10.2026.

Выполнен P17a от merge P16 `d5c671897a35c0ed2e30f0f6863f1f9734580d76`, отдельная ветка feat/logistics-p17a. Source specification v0.3 не изменялась. P17b и весь месячный объём сохранены; auto merge не выполняется.

## Реализовано

- BuyLogistics создаёт систему предприятия/один slot/cargo inventory и числовую Logistics EmploymentGroup N0, за плату казне при физическом присутствии владельца. Группа участвует в общем population/caps/salary³/gradual hiring/readiness/payroll/work order. Объектов работников/их wallets/AI нет.
- SetLogisticsContract выбирает собственное обязательство supplier; quantity не вводится. Prepare phase4: min свободного output/contract U/cargo volume, ETA check, exclusive stock R и полный future salary hold. Phase2 load следующего барьера R→I, реальные source→cargo goods и source return capacity. Cargo двигается по fixed grid path; Source/Destination/Price из authority.
- Phase3 receipt использует тот же ReceiveDelivery P16: I→D, supplier paid per received unit, destination hold становится stock/экспортом, source return place released. Fulfilled financial contract не удаляет машину: она возвращается empty с тем же funded payroll. Manual receipt ограничен U, generic Transfer managed cargo запрещён; cargo stock само тоже зарезервировано.
- Actual N×committed wage оплачивается на travel ticks из upper capacity budget, unused salary hold возвращается after final return tick. Active readiness Cost0 не занимает тот же бюджет повторно. Wage pending до end+1, overflow предварительно проверен. Stop отключает новые trips, уже начатый завершается и оплачивается.
- Snapshot5 включает system/group/immutable trip path/progress/stock/cargo/return/wage holds, R/I refs и wages; validation/NextId/hash/own allowlist/research exports обновлены вместе. Новый logistics.json входит в byte continuation. В чужом controller нет private route/cargo inventory.

Default price20000/cargo volume10/minimum+capacity crew2/wage25/move1 сохранены из DECISIONS. S6 fixture только ускоряет labour step1/mobility1 и использует population4, startup workforce0: найм реально происходит, обычные defaults не заменены тестовым балансом.

## Фактически проверено

Locked restore/Release build прошли. Последний standalone xUnit **281/281**, failed0/skipped0,19.403s;32 новых случая к249 P16. Проверены economy full roundtrip, physical positions/R/I/holds, salary budget/free cash0/insufficient cash/minCrew, purchase rights/location/no free N/duplicates, private observations, managed cargo, manual quota before arrival, repeat assignment, wage-after-return, route stop, exact deadline/return after financial closure, known-late refusal, unused payroll refund, structural corruption и direct load/replay.

Полная `scripts/check_headless.py`: **11 scenarios/8 save-load pairs**,52.869s на этой среде. Все13 файлов пары совпали побайтно: state/ledger/events/commands/metrics/labor/controllers/decisions/agents/market/scenario/delivery/logistics. Все27 завершённые выгрузки прошли independent money/items/capacity/population/settlement checks. Cuts: cycle55/equipment101/autonomous350/market44/pickup47/delivery37/capital-delivery3/logistics69. Unit cut45/66/67/68/69/70/71/72/73/74 сверяет каждый следующий tick; pending wage/unused reserve тоже restored.

S6 настоящий цикл: harvest20 wood→production10 plank→transport delivery10→empty return. Purchase45; contract allocation52; route66/hire2; prepare67/load68; physical positions3,0 на69 и4,0 на70; receipt71; return74; final75. Logistics payroll50×6=300minor, production wages500, total WagesSink800; Treasury21000; supplier108200/buyer70000; total200000. Population4 сохраняется, final unemployed4, все hold balances0. No teleport/no repeated payment, destination реальный stock10. Save69 = **240902 bytes**; final hash `8ec7ac9b5e1a97ff16349ea99ea059ee0693abee9152294292f0f2370a934eed`.

Отдельные fixed examples: initial supplier23300 → free cash0 после funding полного рейса, но crew остаётся и рейс завершается; при23299 hire/work отказаны NoMoney. CapacityCrew3/Population2: upper reserve450, actual wages300, остаток150 released. ContractTTL19: receipt точно71/deadline71, return завершается74 после финансового Fulfilled; TTL18 заведомо поздний рейс не отправляется, source goods10 остаются, expiry корректен.

Прежние regression численные результаты и autonomous1000 сохранены. Current cycle hash `0bef029702cc667580d7b3a202ca2137e7f4bb0aeb3aa4f70ac1c39113ff5070`, autonomous `7bfbe71a7ed057a236c33f9e7603395ba695a41b35ba19fe0ac4f6b631fa9bfe`; shape/config/rules изменились, поэтому platform expectations явно обновлены после independent checks.

Godot4.7.2 .NET/Linux actual client build/import/headless smoke и log verifier прошли; **62/62** tick hashes, pause/resume и full load12/34/55/60. SDK10.0.401/runtime10.0.12/net10.0 не изменялись. Remote CI результат публикуется в PR после фактического выполнения.

Первая build отказала CS0136 (duplicate local/pattern variable), исправлено. Один baseline вызов стартовал со старыми assemblies после failed build: он исключён из текущего evidence. Свежий build/baseline249 и итоговый281 действительно выполнены. Это не выдаётся за успешную первую сборку. Полные bounded stdout/stderr/exit logs сохранены отдельно; компактные доказательства — [checks.json](verification/p17a/checks.json).

## Совместимость и оставшийся объём

Current World5/Rules logistics-1, Session2/Envelope1. Реальный P16 save load новым runner отказан expected exit1/UnknownWorldSchema. World1–4 не мигрируются, старые files/branches сохранены, новая Godot/output path по версии. final-state/config exports не являются checkpoint.

**P17 целиком не готов.** P17b: upgrade, incoming/outgoing queues, general own destinations, полнота independent inbound/output/production stops, multi-trip scheduling/fairness, controlled late cargo/physical return и workforce perturbation regression. Basic returning-cargo primitive есть, но полной проверенной late-route системой не объявляется. NPC пока не выбирают новые transport/market/politics goals (P21). UI P26/реальная модель/научные серии отсутствуют; новых APK/Windows EXE export/device tests не проводилось, Android device gate открыт.

## MVP и исследовательская программа

[RESEARCH_READINESS.md](RESEARCH_READINESS.md): ориентир~60% functionality (55–65% субъективная оценка, не code/time measure); complete algorithmic system после P21, real LLM pilots P22/P23, основные repeated series/statistics P24/P25. Godot UI для вычислительных экспериментов не обязателен. Предлагаемая основная тема — architecture/evaluation гибридных NPC под resource/latency ограничения; дополнения — delays/fallback, permitted history, political cooperation. Темы/гипотезы ещё не подтверждены результатами.

Следующая ограниченная задача P17b с оставшимися обязательными gates; при необходимости разделить её на вертикальные части, не сокращая полный P17. Подробное задание — IMPLEMENTATION_PLAN.

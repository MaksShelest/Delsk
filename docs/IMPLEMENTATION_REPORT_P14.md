# Итерация P14: предложения, заявки и дефицитные аукционы

Автор: Шелестов Максим Евгеньевич. Дата:14.10.2026.

## Объём

От пользовательского merge P13:00c2b1417d00cd5c587571d043e526427a613845, отдельная ветка feat/market-p14. Реализован только P14; физическая выдача/TTL/refund/fine остаются P15, NPC bidding policy — P21. Спецификация0.3 не изменена, SHA-256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376. Месячный объём сохранён.

## Что сделано

- Core MarketContracts/Market/MarketValidation без новых проектов/frameworks: публикация из own shop stock, public catalogue, общий collecting window, bid cash holds,2 configurable auction rounds при shortage, minimum raise/initial maximum, withdrawals, partial pay-as-bid allocation и canonical seeded ties.
- Все заявки окна участвуют вместе. Достаток закрывается по исходной цене, shortage сначала переводит **всех** в auction; цена/объём лота не меняются. Одна запись на buyer; до auction можно atomically заменить запрос, в auction нельзя вступить/изменить quantity/max/rejoin после withdrawal. Failed raise/edit сохраняет старый hold. Cutoff включительный; MarketRound отклоняет устаревшие raises/withdrawals.
- Stock/money атомарны: исходный lot hold заменяется индивидуальными pickup holds, выделенная цена переводится в ContractEscrow, unsold/unused money свободны. Allocations имеют status AwaitingPickup: товар всё ещё в seller shop, seller ещё **не получил выручку**, buyer inventory не пополнился. Это подготовка PickupContract для P15, не выполненная физическая сделка.
- Public projection содержит current bid/quantity/window/round/MarketRules, private maximum/hold только own buyer; allocations видны buyer/seller. В controller observation вложенные коллекции immutable. Full market.json/inspector остаются research exports.
- World schema2/Rules market-1 сохраняют offers/bids/allocations/tie PRNG, queues/results и все новые ownership links. Reverse checks исключают orphan/missing stock и money holds, неверный escrow/source, mismatch allocated total. Session2/Envelope1 продолжают P13.
- Scripted market-window-v1: реальная добыча/путь/разгрузка, затем S2. Новый --script market; market.json/summary/ledger/events и headless/CI расширены. P13 autonomous сценарий сохранён; он пока использует столицу, а не новые аукционные решения.

## Фактические локальные проверки

| Проверка | Результат |
| --- | --- |
| Locked restore/Release build |Exit0,0 warnings/0 errors |
| xUnit standalone |**172 passed**,0 failed/0 skipped: прежние148 и24 market cases |
| Headless regression |7 scenarios,4 checkpoint/load пары; полный набор38.064s,exit0 |
| Exact continuation |cycle@55,equipment@101,autonomous@350,market@44: побайтно world/events/commands/ledger/metrics/labor/controllers/decisions/agents/**market.json** |
| Независимый verifier |Money/item flows/capacity/pop, market escrow/claim quantities/aggregated reservations passed |
| Market boundary/rights tests |Полный/partial/empty, shortages, multiple holds, failed edit/raise, minimum raise/1-round config, cutoff/final round inclusive, stale rounds/rejoin, private maximum, foreign shop/self bid, reserved goods cannot resell/load, dedup/no repeat pay, order reversal/equal-seed allocation,16 seeds без fixed-ID preference |
| Load tests |Collecting/round1/round2/allocated claim и pending raise; corrupt escrow reference rejected; prior hold tests сохранены |
| Linux Godot |Реальная Debug сборка/import/headless scene/log verifier exit0:62 hashes и actual load@12/34/55/60, lifecycle adapter checked |
| Старый World1 файл |Ожидаемый exit1/UnknownWorldSchema; файлы не менялись, migration нет |
| Источник/diff |Spec SHA прежний, diff --check без ошибок |

В первом общем наборе C# тесты/сценарии/save-load прошли, Python verifier ошибся, читая numeric allocation ID как объект. Исправлен; полный набор повторён успешно. Это не ошибка экономики, не скрытый пропуск проверки.

Компактная запись: [verification/p14/checks.json](verification/p14/checks.json). Большие журналы/сборки не коммитились. Remote CI статусы смотреть в PR; перечисленные выше проверки действительно выполнены локально.

## Контрольный сценарий S2

51 ticks; publish36/cutoff41, auction rounds41–46/46–51. Лот15 wood физически в own shop, requests buyer2/3/4:6/8/3. Prices в minor units500/600/700 (5/6/7 денег).

| Buyer |Requested|Allocated|Unit price|Escrow|Cash after |
| --- | --- | --- | --- | --- | --- |
|2|6|4|500|2000|8000|
|3|8|8|600|4800|5200|
|4|3|3|700|2100|7900|

Escrow8900+seller10000+buyers21100=initial40000. Source stock15/reserved15; buyer stocks0, exporter sink0. Seller cash10000 не изменился. У buyer2 лишний hold1000 освобождён. Snapshot@44 —83602 bytes. Market world hash3eddb2be0a40f8ae86dcf9ff60e50fef7953603b942a97334ade85307fea56a4. Это scripted fixture с подготовленной лавочкой, не обычный старт с бесплатной собственностью.

## Совместимость и ограничения

Новые DTO/action/config/rules изменили canonical hashes. Числа прежних fixtures и автономного seed42 не изменились; platform expectations обновлены **после** проверок денег/товара/численности. Новый cycle/Godot hash207b582725541ea3f1a9ea28ebe77a32e28c129d852dade7b5ffab30bff83a06.

World1/P13 saves не загружаются этой версией; автоматической миграции нет. Их файлы сохранены, предыдущий код доступен в feat/autonomous-p13. Для новой партии использовать новый save path. Godot filename теперь содержит RulesVersion (platform-checkpoint-market-1.json), старый файл не заменён/не удалён. Snapshot Envelope/Session version сами по себе не обещают совместимость старого World.

Не сделаны: physical pickup/partial collection/deadline/refund/fine P15; future delivery P16; auto logistics P17; demand/waves/elections/support P18–P20; новая NPC market strategy P21; UI P26/реальная модель/серии. Allocations сейчас остаются AwaitingPickup; до P15 их нельзя получить или закрыть по сроку. Seller early cancellation не добавлена. Новых APK/EXE нет, реальный Android gate открыт. Длинная партия/масштабный рынок/мобильная производительность не измерялись.

## Запуск и следующий шаг

Из корня после restore/build:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --output artifacts/market
```

Результат в artifacts/market: summary.json/market.json/ledger.jsonl/events.jsonl. [Запуск/сохранение/параметры](HEADLESS_TESTING.md), [Rider/Godot](LOCAL_SETUP.md).

Следующая ограниченная задача — P15: тот же allocation ID превращается в pickup lifecycle Q=C+R+X. Несколько физических CollectPurchase с присутствием buyer и вместимостью, payment per collected unit, inclusive TTL, refund/fine только на невывезенную часть, no repeat settlement и snapshots на границах/пути домой. P16/P17/политику/LLM/UI одновременно не добавлять. PR не объединяется автоматически.

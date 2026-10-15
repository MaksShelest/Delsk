# Что уже можно тестировать и когда появится минимальная версия

Автор: Шелестов Максим Евгеньевич. Статус после P17a, 14.10.2026.

| Уровень проверки | Готовность | Что получит тестировщик |
| --- | --- | --- |
| Экономическое ядро в коде | **Доступно сейчас**, P00–P16/P17a | Rider/.NET,281 xUnit-тестов, общий C# runner; добыча, производство, земля, запасы, числовой найм/зарплата, техника и сохранения |
| Экономическая симуляция с3 самостоятельными NPC | **Доступна сейчас**, P13 | Две цепочки, самостоятельные покупка/стройка/найм/добыча/сбыт, цели/планы и точное продолжение из checkpoint;1 бездействующий слот человека |
| Полный игровой цикл утверждённого прототипа без модели | После **P17b–P21 / Gate C** | P14 рынок/аукционы, P15 выдача и P16 поставочные договоры уже есть; физическая автодоставка, выборы/скрытые договоры и алгоритмы для них |
| Минимальная ручная игра в Godot | **P26**, зависит от P12/P21 | Ввод человека, карта/карточки, те же правила и сохранения. Текущая сцена — отдельный технический playback62 тиков |
| Полный исследовательский объём первого месяца | **Gate D / P22–P27** | Локальная модель/fallback, серии сравнений/анализ; аппаратные/телефонные проверки отдельно |

Первая автономная тестовая версия экономики уже доступна **без графики**. Она не объявляется завершённой игрой: конечные заказы столицы из наличия ещё не заменены полным рынком/генератором спроса. Транспортом служит личный инвентарь NPC с настоящим путём; автоматическая техника добывает на месте, первый контрактный рейс P17a готов; остальные автоматические перевозки — P17b. Android device gate остаётся открыт. Весь месячный объём §15 сохраняется.

## Проверка сейчас

Получить `feat/logistics-p17a`, открыть Delsk.sln в Rider; [локальная настройка](LOCAL_SETUP.md). После restore/build:

```powershell
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Для стандартных JSON пилот дал3 предприятия и3 машины,81 доску/50 хлеба выпущено,80/37 продано. Population28=unemployed21+employed7. Наличные NPC2/3/4:93475/70200/97058 minor units. Это контрольное описание конкретной версии/seed, не условие выигрыша и не результат любого баланса. Резервы и товар в пути/складах сохранены; отказы при конкурентной покупке земли попали в trace и привели к новым планам.

Смотреть summary.json → agents.csv/labor.csv → decisions.jsonl/ledger.jsonl; файлы лежат в `artifacts/run-42`. [Подробно: почему Python, параметры, проверки и продолжение](HEADLESS_TESTING.md). Старые fixtures остаются быстрыми регрессиями; они не заменяют автономную партию.

[Отчёт P13](IMPLEMENTATION_REPORT_P13.md). P14 предложения/аукционы проверены отдельным scripted сценарием, NPC bidding ещё P21. P15 физическая выдача частями/сроки/возврат/штраф реализованы. P16 поставочные обязательства уже реализованы. P17a уже реализован; следующая ограниченная задача — **P17b**, полный P17 ещё не готов.

## Проверка рынка P14

Ветка feat/logistics-p17a; после сборки `--scenario scenarios/market-window.json --script market --output artifacts/market`. Ожидается succeeded/51 ticks, allocations4/8/3 при price500/600/700 minor, escrow8900. Source shop всё ещё содержит15 reserved wood, buyer inventories пусты, seller cash10000. Это allocation, **не выдача**. market.json/summary/events/ledger показывают реальные резервы/денежные потоки. P14 World2/Rules market-1 не загружает P13 World1; прежние saves доступны с прежним кодом, migration нет. [Отчёт P14](IMPLEMENTATION_REPORT_P14.md).


## Выдача P15

Ветка feat/logistics-p17a, запуск `--scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup`. Проверено52 ticks:6 из10 получены двумя поездками,4 освобождены при expiry; seller3100 дохода (3000+100 штраф), buyer1900 возврата. Общий C# набор205 tests,8 scenarios/5 save-load pairs. NPC bidding/pickup goals всё ещё P21; ручной ввод человека P26. World3/Rules pickup-1 отклоняет World1/2 без миграции, прежние файлы оставить для прежнего кода. [Отчёт P15](IMPLEMENTATION_REPORT_P15.md).


## Поставочные договоры P16

Ветка feat/logistics-p17a, `--scenario scenarios/delivery-lifecycle.json --script delivery --output artifacts/delivery`; capital variant — capital-delivery.json/script capital-delivery. Contracted10, Delivered6/Unfulfilled4; Paid3000/Refund2000/Fine100/BondReturn150, общего money/item balance нарушения нет.249 tests,10 scenarios/7 exact save/load pairs; [отчёт P16](IMPLEMENTATION_REPORT_P16.md).

Personal carrier физически возит товар с ручной приёмкой, future obligation не телепортирует товар. Automatic transports/queues/независимые delivery stops — P17 (P17a/P17b в плане). NPC market/contract goals — P21; Godot human UI — P26. Current World4/Rules delivery-1 не загружает World1–3 без migration; старые files/branches сохранены. Device gate P06 остаётся открыт.


## P17a и исследовательская готовность

Первый full physical transport cycle доступен: logistics-cycle.json/script logistics,75 ticks. Numeric crew/stock+quota/cargo+return-place/payroll/return/save/load проверены;281 tests/11 scenarios/8 load pairs. Full P17b и autonomous transport goals P21 ещё не готовы. [Отчёт P17a](IMPLEMENTATION_REPORT_P17A.md).

Инженерный ориентир MVP~60% (не measured code/time progress); методика оценки/предложенные темы — [RESEARCH_READINESS.md](RESEARCH_READINESS.md). Complete algorithmic headless experiments после P21; real LLM pilots P22/P23, основные series/statistics P24/P25. Godot human UI P26 не нужен для вычислительных экспериментов. Реальная модель/научные результаты и Android device test ещё не проведены. Current saves World5/Rules logistics-1, old World1–4 files остаются прежнему коду.

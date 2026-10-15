# Что уже можно тестировать и когда появится минимальная версия

Автор: Шелестов Максим Евгеньевич. Статус после завершения P17/P18, 15.10.2026.

| Уровень проверки | Готовность | Что получит тестировщик |
| --- | --- | --- |
| Экономическое ядро в коде | **Доступно сейчас**, P00–P18 | Rider/.NET,476 xUnit-тестов, общий C# runner; добыча, производство, земля, запасы, числовой найм/зарплата, техника и сохранения |
| Экономическая симуляция с3 самостоятельными NPC | **Доступна сейчас**, P13 | Две цепочки, самостоятельные покупка/стройка/найм/добыча/сбыт, цели/планы и точное продолжение из checkpoint;1 бездействующий слот человека |
| Полный игровой цикл утверждённого прототипа без модели | После **P17b–P21 / Gate C** | P14 рынок/аукционы, P15 выдача и P16 поставочные договоры уже есть; физическая автодоставка, выборы/скрытые договоры и алгоритмы для них |
| Минимальная ручная игра в Godot | **P26**, зависит от P12/P21 | Ввод человека, карта/карточки, те же правила и сохранения. Текущая сцена — отдельный технический playback62 тиков |
| Полный исследовательский объём первого месяца | **Gate D / P22–P27** | Локальная модель/fallback, серии сравнений/анализ; аппаратные/телефонные проверки отдельно |

Первая автономная тестовая версия экономики уже доступна **без графики**. Она не объявляется завершённой игрой: конечные заказы столицы из наличия ещё не заменены полным рынком/генератором спроса. Транспортом служит личный инвентарь NPC с настоящим путём; автоматическая техника добывает на месте, первый контрактный рейс P17a готов; остальные автоматические перевозки — P17b. Android device gate остаётся открыт. Весь месячный объём §15 сохраняется.

## Проверка сейчас

Получить `feat/logistics-queue-capital-demand`, открыть Delsk.sln в Rider; [локальная настройка](LOCAL_SETUP.md). После restore/build:

```powershell
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Для стандартных JSON пилот дал3 предприятия и3 машины,81 доску/50 хлеба выпущено,80/37 продано. Population28=unemployed21+employed7. Наличные NPC2/3/4:93475/70200/97058 minor units. Это контрольное описание конкретной версии/seed, не условие выигрыша и не результат любого баланса. Резервы и товар в пути/складах сохранены; отказы при конкурентной покупке земли попали в trace и привели к новым планам.

Смотреть summary.json → agents.csv/labor.csv → decisions.jsonl/ledger.jsonl; файлы лежат в `artifacts/run-42`. [Подробно: почему Python, параметры, проверки и продолжение](HEADLESS_TESTING.md). Старые fixtures остаются быстрыми регрессиями; они не заменяют автономную партию.

[Отчёт P13](IMPLEMENTATION_REPORT_P13.md). P14 предложения/аукционы проверены отдельным scripted сценарием, NPC bidding ещё P21. P15 физическая выдача частями/сроки/возврат/штраф реализованы. P16 поставочные обязательства уже реализованы. P17a уже реализован; следующая ограниченная задача P17 — **P19 — выборы и числовые блоки**, полный P17 ещё не готов.

## Проверка рынка P14

Ветка feat/logistics-p17a; после сборки `--scenario scenarios/market-window.json --script market --output artifacts/market`. Ожидается succeeded/51 ticks, allocations4/8/3 при price500/600/700 minor, escrow8900. Source shop всё ещё содержит15 reserved wood, buyer inventories пусты, seller cash10000. Это allocation, **не выдача**. market.json/summary/events/ledger показывают реальные резервы/денежные потоки. P14 World2/Rules market-1 не загружает P13 World1; прежние saves доступны с прежним кодом, migration нет. [Отчёт P14](IMPLEMENTATION_REPORT_P14.md).


## Выдача P15

Ветка feat/logistics-p17a, запуск `--scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup`. Проверено52 ticks:6 из10 получены двумя поездками,4 освобождены при expiry; seller3100 дохода (3000+100 штраф), buyer1900 возврата. Общий C# набор205 tests,8 scenarios/5 save-load pairs. NPC bidding/pickup goals всё ещё P21; ручной ввод человека P26. World3/Rules pickup-1 отклоняет World1/2 без миграции, прежние файлы оставить для прежнего кода. [Отчёт P15](IMPLEMENTATION_REPORT_P15.md).


## Поставочные договоры P16

Ветка feat/logistics-p17a, `--scenario scenarios/delivery-lifecycle.json --script delivery --output artifacts/delivery`; capital variant — capital-delivery.json/script capital-delivery. Contracted10, Delivered6/Unfulfilled4; Paid3000/Refund2000/Fine100/BondReturn150, общего money/item balance нарушения нет.249 tests,10 scenarios/7 exact save/load pairs; [отчёт P16](IMPLEMENTATION_REPORT_P16.md).

Personal carrier физически возит товар с ручной приёмкой, future obligation не телепортирует товар. Automatic transports/queues/независимые delivery stops — P17 (P17a/P17b в плане). NPC market/contract goals — P21; Godot human UI — P26. Current World4/Rules delivery-1 не загружает World1–3 без migration; старые files/branches сохранены. Device gate P06 остаётся открыт.


## P17a и исследовательская готовность

Первый full physical transport cycle доступен: logistics-cycle.json/script logistics,75 ticks. Numeric crew/stock+quota/cargo+return-place/payroll/return/save/load проверены;281 tests/11 scenarios/8 load pairs. Full P17b и autonomous transport goals P21 ещё не готовы. [Отчёт P17a](IMPLEMENTATION_REPORT_P17A.md).

Инженерный ориентир MVP~60% (не measured code/time progress); методика оценки/предложенные темы — [RESEARCH_READINESS.md](RESEARCH_READINESS.md). Complete algorithmic headless experiments после P21; real LLM pilots P22/P23, основные series/statistics P24/P25. Godot human UI P26 не нужен для вычислительных экспериментов. Реальная модель/научные результаты и Android device test ещё не проведены. Исторические saves P17a:World5/Rules logistics-1, old World1–4 files остаются прежнему коду.


## P17b1

Upgrade транспорта выполнен и проверен: Level1→2, cargo10→20, min/capacity crew2→3, цена30000minor+10plank из встроенного склада, ID/численность/зарплата сохраняются. Новый logistics-upgrade fixture176ticks/20delivered,308tests/12scenarios/9load pairs. [Отчёт](IMPLEMENTATION_REPORT_P17B1.md). P17b2 отправка в свой склад/лавочку до заполнения реализована, затем остальные обязательные части P17b. Full P17/Gate C ещё открыт. Исторический P17b1:World6/Rules logistics-2; старые World1–5 не мигрируются. Android device gate остаётся открытым.


## P17b2

Own Warehouse/Shop destination UntilFull реализован, actual проверки — [отчёт](IMPLEMENTATION_REPORT_P17B2.md). Новый fixture60wood→30plank/два рейса10+5/склад15/output15, internal receipt без выручки. Full storage/no money/no path дают причины ожидания; free place автоматически возобновляет маршрут. Incoming/shared queues/general production destination/full late-return остаются следующими частями. Исторический P17b2:World7/Rules logistics-3, World1–6 без migration; Android device gate открыт. Входящие поставки из своего склада реализованы следующей частью.


Подтверждение P17b2:339/339tests,13scenarios/10load pairs, Windows62hashes/load12/34/55/60; local independent check33exports/20comparisons. Own scenario180ticks. Локального SDK/Godot в Work нет, build/test/probe — actual remote CI. Подробные IDs/exit codes — verification/p17b2/checks.json.


## P17: входящие поставки

Вход из собственного Warehouse/Shop в input предприятия, независимые input/output/production switches и общий transport slot/numeric crew реализованы. [Отчёт/фактические проверки](IMPLEMENTATION_REPORT_INBOUND.md). Общая queue/fairness/полный late-return gate остаются; full P17/Gate C ещё не готов. Current World8/Rules logistics-4, World1–7 без migration, Android device gate открыт. Расшифровка технических ID в IMPLEMENTATION_PLAN; прежде всего использовать названия функций.

Фактический входящий цикл110ticks и369/369tests/14scenarios/11load pairs/Windows probe прошли; источник/evidence verification/inbound/checks.json.


## P17: просрочка и возврат

Реализованы строгая загрузка позднего груза, контроль физического возврата/резервов и зарплатной границы подготовки рейса. Изменения численности проверяются настоящим трудовым рынком; путь начатого рейса не исчезает при уходе экипажа. [Фактические проверки и ограничения](IMPLEMENTATION_REPORT_LOGISTICS_RETURN.md). Аварийная фикстура создана намеренным ответвлением checkpoint, не обычной игровой командой или replay от начала. World8/Rules logistics-5; старые правила без миграции. Полный P17/очередь/fairness/следующее производство остаются; Android device gate открыт.

Проверено:393/393теста;14обычных сценариев/11пар сохранения и аварийная фикстура с12-й парой; Windows62хеша. [Подтверждения](verification/logistics-return/checks.json).


## P17: следующее собственное производство

Реализована физическая доставка output→input другого own Enterprise с совместимыми рецептами, finite capacity, независимым stop и точным load. Own-production fixture212ticks:4trips,30plank→7crate+16plank остатка, без продаж/эмиссии. [Отчёт/результаты](IMPLEMENTATION_REPORT_OWN_PRODUCTION.md). Current World8/Rules logistics-6; старые Rules без migration. Общая очередь/nearest deadline/fairness и оставшиеся quota cases ещё P17. Android device gate открыт.


## Завершение P17 и P18

Общая очередь с deadline/urgency/fair rotation/quota/Stop/Resume и strict save/load проверена до перехода к P18. Городской валовый выпуск/потребление/stock change, сглаживание, периодический спрос1.1/bootstrap, волны цен и приватный PRNG добавлены в Core. `autonomous-demand.json --script autonomous` использует прежних3 NPC с новым генератором; `capital-demand-cycle.json --script capital-demand` показывает восстановление cash0. [Отчёт и результаты](IMPLEMENTATION_REPORT_P17_P18.md).

Current World10/Rules capital-demand-1, old saves без migration. Следующий пункт P19. Полный Gate C ещё требует выборов/политических договоров/P21; реальное устройство P06 остаётся непроверенным.

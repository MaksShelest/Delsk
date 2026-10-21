# Фактическая готовность Delsk

Актуальное оформление20.10.2026 после PR#46: [планшеты v1](IMPLEMENTATION_REPORT_TABLETS_V1.md). Scoped тема/пять производств/биржа/игрок/справка, реальные handlers/access/pause; Linux native RU/EN/responsive gates пройдены. Экономика прежняя, PR45 open/draft не включён. Аппаратная визуальная приёмка остаётся открыта.

Актуально20.10.2026, пакет8 от слитого PR#43: World18/production-recipes-1/Session2/Envelope2. [Весь план по пакетам](NEXT_STAGE_SUMMARY.md), [новая лаборатория](RESEARCH_LAB.md), [измерения/отрицательные результаты](RESEARCH_RESULTS_PACKAGE8.md), [gate и передача](IMPLEMENTATION_REPORT_PACKAGE8.md). P58 программно закрыт. P59/P60 целиком не закрыты: Hard не показал выигрыша, full-history5000 требует значительной памяти/времени; долгосрочный ROI, реальная модель/WindowsEXE/Android-device не проверены. Следующий шаг закрывает эти gates, не добавляет новый пакет.

Исторические срезы ниже сохраняют состояние на свою дату. Уточнение границ: пакет4=P43–P46, пакет5=P47–P50, пакет6=P51–P53/P56.

Текущий срез19.10.2026: пакет3 P37–P42 реализован после PR#38. World16/Rules physical-automation-1, профиль presentation-economy-v4.773 ordinary/779 Developer tests,90exports/11replay/60comparisons; дополнительно third-map900/load/replay и Linux Godot81 physical tick hashes/оконный UI. [Отчёт и честные границы](IMPLEMENTATION_REPORT_PACKAGE3.md). Следующий пакет4 P43–P47, без автоматического перехода. P32/P33, аппаратные gates и ограничения длинной истории остаются открыты. Нижние срезы — история.

Актуально19.10.2026 после пакета2: P36/P54/P55 реализованы, World15/Rules stopped-elections-1. [Отчёт и ограничения](IMPLEMENTATION_REPORT_PACKAGE2.md), [Developer и headless](DEVELOPER_TOOLS.md). P32/P33 по-прежнему имеют зависимости. Следующий пакет3 P37–P42; нижние датированные срезы — история.

Автор: Шелестов Максим Евгеньевич. Актуально18.10.2026, P27.

Текущая итерация19.10.2026 — пакет1 после слитого UI01/PR#36: [дорожная карта и фактические статусы](DELSK_NEXT_STAGE_PLAN.md), [отчёт](IMPLEMENTATION_REPORT_PACKAGE1.md), [покрытие действий](ACTION_ACCESS_MATRIX.md). World14/Rules household-management-1, Session2/Envelope2. P32-сложности и P33-встречные переговоры не реализованы; зависимые части открыты. Старые World13 saves остаются прежнему коду без миграции/перезаписи. Аппаратная приёмка Windows/Android/текущая реальная модель не подменяется этим пакетом.

Далее сохранён исторический срез P27/UI01 и предыдущих итераций.

Программный объём первого месяца реализован: независимый Core, алгоритмические NPC/LLM-адаптер/исследования и P26.6 A/B/C. Завершены рынок участников/физическое получение/аукцион и свободный выбор одного общего места лавки. Единый баланс — config/economy.json; инструкция — [ECONOMY_CONFIGURATION.md](ECONOMY_CONFIGURATION.md).

Облачная часть P27 включает S1–S7, save/load/replay, Linux Godot, UI RU/EN, профиль1200/5000 тиков и A/B по трём семенам. [Отчёт P27](IMPLEMENTATION_REPORT_P27.md) содержит точные результаты и существенные ограничения длинной истории: на5000 тиках save≈58MB и load≈11с; память после двух сессий≈2.93GB. Это не подтверждение Android/60FPS. World13/Rules research-economy-1, Session2/Envelope2; старые World12/manual-harvest-1 saves отдельно без миграции.

Полная аппаратная приёмка месячного результата остаётся открыта: текущие Windows EXE, Android lifecycle/память/управление и новая живая Gate D демонстрация модели. [Локальные шаги](P27_LOCAL_ACCEPTANCE.md). Исторические P23/P25 не выдаются за новый запуск. Следующий шаг — эти gates и ограниченное развитие второго месяца с учётом профиля, а не дальнейшее расширение первого месяца скрытыми требованиями.

Далее сохранены исторические примеры и показатели предыдущих итераций; они не заменяют актуальный статус выше.

## Проверка сейчас

Получить `feat/private-support-baseline`, открыть Delsk.sln в Rider; [локальная настройка](LOCAL_SETUP.md). После restore/build:

```powershell
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Для стандартных JSON пилот дал3 предприятия и3 машины,81 доску/50 хлеба выпущено,80/37 продано. Population28=unemployed21+employed7. Наличные NPC2/3/4:93475/70200/97058 minor units. Это контрольное описание конкретной версии/seed, не условие выигрыша и не результат любого баланса. Резервы и товар в пути/складах сохранены; отказы при конкурентной покупке земли попали в trace и привели к новым планам.

Смотреть summary.json → agents.csv/labor.csv → decisions.jsonl/ledger.jsonl; файлы лежат в `artifacts/run-42`. [Подробно: почему Python, параметры, проверки и продолжение](HEADLESS_TESTING.md). Старые fixtures остаются быстрыми регрессиями; они не заменяют автономную партию.

[Отчёт P13](IMPLEMENTATION_REPORT_P13.md). P14 предложения/аукционы проверены отдельным scripted сценарием, NPC bidding ещё P21. P15 физическая выдача частями/сроки/возврат/штраф реализованы. P16 поставочные обязательства уже реализованы. P17 и P18 завершены; P19 — выборы и числовые блоки — также реализован и проверен. Следующая задача P20 — приватные договоры поддержки.

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

## Проверенные выборы P19

Scheduler/electionId, текущие группы/strict majority, выбор через общий валидатор, первый победитель и песочница реализованы. Public immutable observation показывает итоговые бюллетени, pending choices только владельцу.512tests/19scenarios/16ordinary load pairs+controlled/3replays и Linux Godot102hashes прошли. [Отчёт](IMPLEMENTATION_REPORT_P19.md). Current World11/Rules elections-1; следующий P20, затем полноценные политические utility-решения P21. UI и реальное Android-устройство остаются отдельными задачами.


## Актуальное уточнение: подготовленные карты (17.10.2026)

P26.6 часть A реализована: меню выбора двух карт до запуска мира, расширенная карта с6 клетками каждого сырья и защищённой дорогой, камера без ограничений краями, компактный HUD/иконки, узнаваемые производства и видимые машины. Спецификация0.4 §26 фиксирует все новые согласованные требования. Большие планшеты, тактическая пауза, непрерывная добыча/отмена и свободные торговые места ещё впереди (B/C). Подробности и фактические проверки — IMPLEMENTATION_REPORT_P26_6_MAPS.md.


## Актуально: планшеты и тактическая пауза — P26.6 B1

После слитого PR#32 реализован интерфейсный блок B1: биржа столичных заказов с фильтром/сортировкой/своей заявкой/отменой/результатами и выбранное собственное предприятие с прогрессом, запасами, персоналом и объяснённой логистикой. Оба планшета почти на весь экран, с крестиком и общей тактической паузой. WASD внутри клетки сохраняет планшет и запускает весь мир, выход закрывает его; Busy не снимает паузу. Спецификация0.4 §26 остаётся источником. Подробности проверки — IMPLEMENTATION_REPORT_P26_6_TABLETS.md.

B2 (повтор/отмена добычи) и C (торговля участников/общие места лавок) ещё не реализованы; P26.6 целиком не закрыт. P27/hardware gates открыты.

## UI01, 18.10.2026

Игровая область расширена, контекстные действия поверх карты, вся кнопка добычи показывает прогресс, справка открывается с паузой, биржа имеет отдельную историю. Полный экран и окно переключаются в меню. Core и баланс прежние. [Объём и остаток](INTERFACE_ITERATIONS.md), [проверки](IMPLEMENTATION_REPORT_UI01.md).

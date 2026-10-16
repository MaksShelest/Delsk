# Инструкции работы в Delsk

Автор проекта и материалов: Шелестов Максим Евгеньевич.

## Начало задачи

1. Прочитать `docs/requirements/game_specification.md` (v0.3) полностью, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/IMPLEMENTATION_PLAN.md` и более узкие AGENTS.md, если они появятся.
2. Проверить git status/diff, текущую ветку, последние коммиты и удалённые refs. Сохранять существующую работу. Не начинать повторную реализацию без проверки файлов.
3. Выполнять ограниченный объём текущего задания. В архитектурной ветке только документы; код начинается отдельным заданием/PR.
4. Новые явные указания пользователя имеют приоритет. В источнике отличать принятое правило, план, предложение и открытый параметр. Поздние §23–25 уточняют ранние варианты. Не переоткрывать согласованные вопросы.
5. P00–P24 выполнены в программных границах отчётов; Android device gate P06 открыт. Текущее состояние серии — IMPLEMENTATION_REPORT_P24.md и verification/p24. P23 — измеренный локальный `qwen3.5:4b`: реальный запрос, принятая команда, fallback и replay. P24 прогнал парные baseline/hybrid/hybrid+структурированные переговоры, replay и равенство выключенной модели baseline. Следующий пункт P25; статистика 10 seeds и UI не объявлены готовыми. Считать готовность по фактическим проверкам, а не наличию кода или документации. Ниже сохранены исторические правила/версии отдельных итераций; текущая версия World12/Rules baseline-1, Session2/Envelope1, utility-economy-v2/checkpoint2.

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
- Наём по зарплата³ с ограничением мест и общей численности. В выборах текущая численная занятость даёт один голос на работника, безработные не голосуют; победа только строго>50%.
- Политический договор доступен сторонам и исследователю. Обязательство собственного блока активируется атомарно с фактической компенсацией; один блок/выборы, без перепродажи приобретённых голосов.
- Snapshot JSON, journal JSONL, export CSV/JSON; Python только анализ. Не вводить сервер, SQL, event bus, ECS, DI framework или интерфейс для каждого класса без практической нужды.

## Реализация и изменения требований

- Держать модули небольшими, обычные классы/records и явные вызовы предпочтительнее универсальных абстракций. Создавать проекты по мере реальной необходимости, без пустых заглушек всех будущих систем.
- Баланс и контент — версионированные настройки. Стартовые допущения перечислены в DECISIONS; изменения помечать как допущения, не как согласованные исторические правила.
- Сохранять весь месячный объём §15. Не объявлять числовой найм, автоматику и выборы «потом вне MVP». При риске срока сообщить о переносе фактического завершения, а не незаметно сокращать требования.
- Не изменять `docs/requirements/game_specification.md` без явного указания. Его исходный SHA-256 указан в DECISIONS. Новые технические решения записывать отдельно.
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
- RulesVersion сейчас baseline-1. LaborSettings null включает настройки по умолчанию; исторические regression fixtures явно растягивают трудовой шаг, не использовать их баланс для обычной партии. ConstructionInput отдельно учитывает необратимый расход на стройку/уровни. Изменение формы DTO меняет хеши: после проверки старого сценария явно обновить platform fixture, сохраняя старые отчёты как историю. Не выдавать replay за load checkpoint.
- Текущие версии: World schema12, Session schema2, Envelope format1; controller utility-economy-v2/checkpoint2. World11/P19 и support-1/P20, World10/P18 без migration. World8/PR16 и World9/очередь также без migration. World1/P13, World2/P14 и World3/P15 и World4/P16 и World5/P17a и World6/P17b1 и World7/P17b2 сохранения отклоняются (миграции нет); прежние файлы/ветки сохранять. CheckpointStore выполняет direct state restore и checksum/reference/hash validation. Snapshot сохраняет существующие системы, не автоматически будущие: каждый новый модуль обязан расширять DTO/validator, hold ownership, controller state и continuous-vs-load tests. final-state остаётся inspection; не сериализовать delegates и не подменять load replay.
- P11: production/extraction входят в общий EmploymentGroups/LaborWorkplaces и единый ротируемый work order. Новая роль должна входить в population, budget, wages, private/public projections, hash/replay и snapshot P12 ровно один раз. Личные machine jobs и automatic WIP/holds при load восстанавливаются с взаимными ссылками; сами временные Func из LaborWorkplace не сериализовать.
- Найм: phase0 применяет pending wages, phase5 планирует прирост/readiness/одновременные кадровые delta. Учесть будущую оплату при readiness и один общий бюджет владельца; payroll phase4 использует старый N до перехода. Credit — единицы1/denominator, labor PRNG и LastGrowthPeriod входят в hash/save P12. Чужим контроллерам передавать только PublicEmploymentView.
- После достаточных проверок конкретного риска прекращать необязательное расширение тестов и отдавать результат.
- Клиентский проект: `src/Client.Godot/Delsk.Client.sln`, отдельно от Core solution. Godot .NET/SDK/templates ровно4.7.2, общий TFM net10.0, SDK10.0.401. Перед пересмотром читать отчёт P06; не переключать TFM только из-за устаревшего минимального требования документации.
- Godot export может вернуть exit0 при ошибке публикации C#. Проверять журнал через `scripts/verify_platform_probe.py` и состав сборки; на этой среде DOTNET_PROCESSOR_COUNT=1 нужен экспорту. В PowerShell `'-m:1'` передавать в кавычках. Runtime-locks в obj не коммитить.
- В git не класть credentials/keystore/passwords, модельные веса, кеши, builds и большие журналы. Небольшие тестовые данные и manifest допустимы.

## Git, передача в Cursor и авторство

- Отдельная ветка на задачу, небольшие коммиты и PR с объяснением проблемы, решения, проверок и оставшихся рисков. Без автоматического merge, force push, reset --hard или clean чужой работы.
- Перед push проверить diff и author/committer. В авторстве указывать только «Шелестов Максим Евгеньевич». Не добавлять иных авторов, Co-authored-by и служебных подписей инструментов. Технические описания LLM как компонента игры сохраняются.
- Не менять глобальный git config; при необходимости настройка только этого репозитория. Не придумывать личный email: использовать подтверждённый адрес либо GitHub noreply владельца.
- Cursor следует тем же инструкциям; локальное окружение не даёт права дублировать правила в UI. Передача: branch/commit, фактические статусы задач, воспроизводимая команда, непройденные gates.
- В конце задачи кратко сообщить что изменено, что реально проверено, ссылку на PR и точный следующий ограниченный шаг. Не выдавать архитектуру/план за реализацию.

## Контроллеры P13

- Agents.Algorithmic -> Core; Hosting не знает concrete policy. Наблюдение ControllerObservation immutable через вложенные ImmutableArray; никакого engine/ScenarioDefinition/raw seed/research hash/foreign stock в Decide. Future secret fields только own/bilateral allowlist.
- Hosting фиксирует все observations перед Decide/Send и даёт только own results. Единственный путь действий — обычные Send/Enqueue/Core validation; общий command budget8, текущий baseline выбирает1. Busy и sale hold не равны завершённой операции.
- Capture сохраняет settings/goals/finite plan/cursor/pending ID/timers/private PRNG/trust/counts. Session2 сохраняет controller blobs/awaiting refs/turns/samples; composition root обязан RestoreControllers до Step. Attach только tick0; не выдать command replay за восстановление памяти NPC.
- Вариативные economic coefficients пока не реализованы: settings принимают только1. В P21 политические weights/trust влияют на цену и риск поддержки. Исследовательские серии P24 не вычёркивать. Три NPC в console уже самостоятельные, human slot idle; Godot сцена по-прежнему technical playback, не NPC UI.
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
- hybrid по умолчанию off, --model-mode ollama требует explicit config и loopback endpoint. Realtime и LiveHeadlessStress ставятся только при включённом runtime; выключенная модель сохраняет headless/paced и совпадает с baseline по миру. ControlledFakeS7 не выдавать за реальную LLM. Source World12/Rules baseline-1, Session2/Envelope1 прежние, hybrid-plan-v1/checkpoint1. P24 серии: scenarios/research-series.json, отчёт IMPLEMENTATION_REPORT_P24.md. Статистика P25 не начата.

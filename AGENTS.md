# Инструкции работы в Delsk

Автор проекта и материалов: Шелестов Максим Евгеньевич.

## Начало задачи

1. Прочитать `docs/requirements/game_specification.md` (v0.3) полностью, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/IMPLEMENTATION_PLAN.md` и более узкие AGENTS.md, если они появятся.
2. Проверить git status/diff, текущую ветку, последние коммиты и удалённые refs. Сохранять существующую работу. Не начинать повторную реализацию без проверки файлов.
3. Выполнять ограниченный объём текущего задания. В архитектурной ветке только документы; код начинается отдельным заданием/PR.
4. Новые явные указания пользователя имеют приоритет. В источнике отличать принятое правило, план, предложение и открытый параметр. Поздние §23–25 уточняют ранние варианты. Не переоткрывать согласованные вопросы.
5. P00–P16 выполнены в заявленных границах; Android device gate P06 открыт. Следующая итерация — первая часть P17: transport system, numerical Logistics group и один физический рейс по договору; сверить границы P17a/P17b в плане. P16 buy offers/договоры/залог/частичная приёмка/просрочка реализованы. P15 выдача частями/TTL/возврат/штраф реализованы; сверить фактические проверки с отчётом P15. P14 окна/аукционы и ожидающие выдачи распределения проверены. P13 контроллеры/планы и autonomous runner проверены в границах отчёта. Сверять статус с планом/отчётами; fixture со штатами не доказывает динамический найм, загрузку checkpoint или автономность NPC.

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
- RulesVersion сейчас delivery-1. LaborSettings null включает настройки по умолчанию; исторические regression fixtures явно растягивают трудовой шаг, не использовать их баланс для обычной партии. ConstructionInput отдельно учитывает необратимый расход на стройку/уровни. Изменение формы DTO меняет хеши: после проверки старого сценария явно обновить platform fixture, сохраняя старые отчёты как историю. Не выдавать replay за load checkpoint.
- Текущие версии: World schema4, Session schema2, Envelope format1. World1/P13, World2/P14 и World3/P15 сохранения отклоняются (миграции нет); прежние файлы/ветки сохранять. CheckpointStore выполняет direct state restore и checksum/reference/hash validation. Snapshot сохраняет существующие системы, не автоматически будущие: каждый новый модуль обязан расширять DTO/validator, hold ownership, controller state и continuous-vs-load tests. final-state остаётся inspection; не сериализовать delegates и не подменять load replay.
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
- Вариативные economic coefficients пока не реализованы: settings принимают только1. Политические weights/trust сохранены, но не влияют на ещё отсутствующие решения. Расширение P21/P24 не вычёркивать. Три NPC в console уже самостоятельные, human slot idle; Godot сцена по-прежнему technical playback, не NPC UI.
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

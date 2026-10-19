# Как запускать и проверять экономику без графики

> Актуально19.10.2026, пакет1: World14/Rules household-management-1, Session2/Envelope2. Полный gate714tests/82exports/9replay/54comparisons; каждый export включает public-economy.json, direct load сравнивает19файлов, world-only15. check_headless.py вызывает независимый verify_public_economy.py для капитала/труда/города наряду с balance/privacy/invariant checks. [Текущий отчёт](IMPLEMENTATION_REPORT_PACKAGE1.md). Нижеследующие сведения с датами P24/P27 — история; базу брать из актуального origin, не из исторического имени ветки. Примеры runner сохраняют назначение.

> Актуально18.10.2026: единый редактируемый баланс обычной игры — [config/economy.json](../config/economy.json); [руководство](ECONOMY_CONFIGURATION.md). Для опытов используйте runner --economy PATH явно; старые примеры без этого аргумента ниже читают самостоятельные scenarios/fixtures. --validate-only true проверяет настройки, --profile true измеряет headless Step/save/load, run_economy_study.py выполняет одинаковые seeds для профилей A/B. World13/Rules research-economy-1, Session2/Envelope2. Старые rules saves отдельно без миграции. Программный месячный объём/облачная P27-проверка выполнены, аппаратная приёмка и ограничения — [отчёт P27](IMPLEMENTATION_REPORT_P27.md).


Автор: Шелестов Максим Евгеньевич. Состояние после P24, 16.10.2026.

## P22: модель необязательна

```powershell
python scripts/check_headless.py --output artifacts/headless-p22
dotnet run --project src/Research.Runner -c Release --no-build -- --script model-safety --output artifacts/s7
dotnet run --project src/Research.Runner -c Release --no-build -- --script hybrid --model-mode off --output artifacts/model-off
```

Проверено618tests/26scenarios/22ordinary load pairs+controlled/9replays/81exports/54comparisons. Direct continuation18файлов, world-only replay14. Godot Linux222hashes/14file restores. S7 использует3настоящих гибридных контроллера и контролируемые fake responses; это не реальный LLM эксперимент. Полностью отключённый hybrid совпадает с baseline по миру/командам. Подробности — IMPLEMENTATION_REPORT_P22.md.

Реальный локальный transport включается только `--script hybrid --model-mode ollama --model-config scenarios/local-model.example.json`. При включённой модели `--mode paced` помечается Realtime, headless — LiveHeadlessStress. Выключенная модель сохраняет обычные `headless`/`paced` и не называется stress-режимом. Планы/принятые обязательства/auction bid модель не задерживает; новый plan option применяется только в фиксированный apply tick. model-decisions.jsonl содержит private researcher prompt/response/resolution/latency; это не public stream. Replay с --model-mode/--model-config отклоняется, адаптер не создаётся.

Серия P25: `py -3 scripts/run_research_series.py --protocol scenarios/research-series-p25.json --output artifacts/p25-series --resume`, затем `py -3 scripts/analyze_paired_series.py --series artifacts/p25-series --evidence docs/verification/p25`. Десять семян 42–51, отчёт — [IMPLEMENTATION_REPORT_P25.md](IMPLEMENTATION_REPORT_P25.md).

Клиент P26.6: Godot .NET, F5 на `Playfield.tscn`. Обычная игра идёт, WASD задаёт курс, камера следует за человеком. Верхняя панель и нижние действия читают ту же сессию. Escape открывает меню, Debug шагает вручную. `--smoke` гоняет `PlatformProbe`. Отчёт — [IMPLEMENTATION_REPORT_P26_6.md](IMPLEMENTATION_REPORT_P26_6.md). Headless-прогон не заменяет оконную проверку.

После load все незавершённые Pending/Ready slots получают fallback в прежний applyTick с новой epoch, без нового запроса. Уже разрешённые решения не повторяются. Это правило не обещает идентичность прерванной живой модели непрерывному realtime rollout. [Процедура P23](LOCAL_MODEL_VALIDATION.md) на этой машине выполнена: реальный `qwen3.5:4b`, применённый `MoveTo`, fallback и replay записаны в [IMPLEMENTATION_REPORT_P23.md](IMPLEMENTATION_REPORT_P23.md). Адаптер отключает thinking, чтобы JSON оставался в `content`.

Далее приведены исторические показатели P21 и прежних итераций; текущие totals указаны выше.

## Текущий baseline P21

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script baseline --seed 42 --output artifacts/baseline
dotnet run --project src/Research.Runner -c Release --no-build -- --script baseline --scenario scenarios/baseline-auction.json --controllers scenarios/baseline-auction-controllers.json --output artifacts/baseline-auction
dotnet run --project src/Research.Runner -c Release --no-build -- --script baseline --scenario scenarios/baseline-politics.json --controllers scenarios/baseline-politics-controllers.json --output artifacts/baseline-politics
dotnet run --project src/Research.Runner -c Release --no-build -- --script baseline --scenario scenarios/baseline-logistics.json --controllers scenarios/baseline-logistics-controllers.json --output artifacts/baseline-logistics
python scripts/check_headless.py --output artifacts/headless-p21
```

Обычный economy стартует без готового бизнеса: три NPC развивают две цепочки, торгуют и исполняют сроки1200ticks. Остальные три сценария подготовлены для конкуренции в аукционе180ticks, политики160ticks и автологистики360ticks. Human1 бездействует, модель полностью отсутствует. Research runner дополнительно проверяет фактические критерии поведения по окончании соответствующего горизонта, включая replay.

Проверено561test,24обычных сценария,21обычная save/load пара+controlled pair,8command replays,77выгрузок с независимыми balance/invariant/privacy assertions. Direct continuation сравнивает17файлов; world-only replay14, поскольку policy trace без вызова контроллера не возникает. Полный shared gate используется также CI; каждый этап имеет hard timeout. Ошибки чтения/прогона не маскируются успешным summary.

Новые настройки: MaximumBuyPriceBasisPoints12500, SellPriceBasisPoints10000, SupportPricePerVoteMinor500, PoliticalHorizonTicks180, Trade/Logistics/Politics/FutureContractsEnabled=true, MarketLotQuantity10. Алгоритм сравнивает свободные средства/товар с holds, ETA/вместимость/срок, покупку с добычей; обязательство раньше расширения. Политика может отказаться от поддержки из-за цены или риска проиграть выборы. Состояния utility-economy-v2/checkpoint2 сохраняют основной и прерванный план, bid reviews, failed signs, cooldowns и private PRNG. World12/Rules baseline-1; старые rules/policy файлы оставлять для прежнего кода.

support.json/events.jsonl/commands.jsonl и raw controller observations — исследовательские приватные материалы, не публичный UI. public-events.jsonl содержит отфильтрованные события без условий сделок; controller allowlist — только own/bilateral support. Подробные измерения и ограничения — [отчёт](IMPLEMENTATION_REPORT_P20_P21.md). Последующие старые примеры autonomous сохранены как регрессии, их исторические показатели не являются текущими результатами P21.

## Что выполняет прогон и зачем Python

Вся игровая экономика, контроллеры и консольный runner написаны на **C#**. `Simulation.Core` считает мир; `Agents.Algorithmic` выбирает действия NPC; `Simulation.Hosting` передаёт команды, сохраняет и продолжает сессию. Godot использует это же ядро. Python не вычисляет производство, найм, деньги или движение и не нужен для одиночного прогона.

`Research.Runner` — консольная программа, которая создаёт мир по JSON, добавляет контроллеры, выполняет тики до заданного горизонта и выгружает результат. В автономном сценарии три NPC сами добывают, строят, нанимают, производят и продают. Место человека оставлено свободным: без интерактивного ввода оно бездействует. Старые `cycle`/`equipment` и другие fixtures исполняют заранее написанные команды для регрессии; это отдельный тип проверки.

**Headless** означает отсутствие графики и ожидания реальной секунды между тиками. Путь на30 игровых тиков всё равно занимает30 тиков; производство, зарплаты, переходы и окна используют те же правила. `paced` ждёт1 реальную секунду на тик. В ускоренном прогоне не происходит мгновенной передачи груза.

Python сейчас лишь удобно запускает .NET-сборку/тесты/несколько прогонов с таймаутами и независимо проверяет выгруженные файлы. Позднее P25 добавит анализ серий и графики. Можно обойтись C# runner и xUnit; второй движок экономики не создаётся.

## Быстрый запуск в Rider или терминале Windows

Получить ветку `feat/private-support-baseline` (после её merge — текущую базовую ветку); безопасное обновление — [LOCAL_SETUP.md](LOCAL_SETUP.md). Открыть `Delsk.sln`. В терминале **корня репозитория**:

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Здесь Python и Godot не используются. SDK10.0.401 закреплён в global.json; рабочее окружение не нужно переустанавливать. Rider можно использовать для отладки C# runner. Встроенная консоль напечатает `succeeded`, конечный тик, хеш мира и **абсолютный путь результата**. Указанные команды удобны для ручной проверки; облачные проверки запускают их через `run_check.py` с hard timeout. При `failed` смотреть причину и код завершения; наличие каталога само по себе не означает успех.

Исторический пилот P13:1000 тиков, seed42,3.375 секунды на этой среде до экспорта. Это измерение одного небольшого мира, не обещание скорости на другом компьютере или длинной партии. Полный месячный рынок/политика ещё не готовы; этот запуск проверяет P00–P16.

## Параметры прогона

| Где | Параметр | Значение и назначение |
| --- | --- | --- |
| CLI | `--script autonomous` | Самостоятельные NPC; остальные scripts — фиксированные проверки |
| CLI | `--ticks 1000` | **Абсолютный конечный тик**, не1000 дополнительных; предел — scenario.maxTicks |
| CLI | `--seed 42` | Seed нового мира и отдельных потоков контроллеров; менять для нового эксперимента |
| CLI | `--mode headless` | По умолчанию; `paced` для наблюдения длительностей в реальном времени |
| CLI | `--output artifacts/run-42` | Каталог файлов результата относительно текущего рабочего каталога |
| CLI | `--scenario scenarios/autonomous-economy.json` | Мир: карта, сырьё/рецепты/цены, начальные деньги, население, трудовые шаги, техника/земля, maxTicks |
| CLI | `--controllers scenarios/autonomous-controllers.json` | Начальные политики, human slot и горизонт по умолчанию; только новый автономный запуск |
| NPC JSON | `preferredInput`, `batchQuantity` | Направление производства и желаемая личная партия; фактический объём ограничивается складом/предложением |
| NPC JSON | `cashReserveMinor` | Порог восстановления/резерв для вложения; деньги в сотых долях:3000=30 |
| NPC JSON | `baseWageMinor`, `maximumWageMinor`, `wageReviewTicks` | Базовая ставка/потолок/интервал пересмотра; сама задержка найма принадлежит Core LaborSettings |
| NPC JSON | `decisionIntervalTicks`, `blockedRetryTicks` | Переоценка без готового плана и задержка повторения отвергнутой цели |
| NPC JSON | `cashWeight`, `productionWeight`, `expansionWeight` | Веса текущих экономических целей;100 — базовое значение,0 выключает соответствующий вклад |
| NPC JSON | `ownVictoryWeight`, `humanDefeatWeight`, доверие | Сохраняются, но политические решения появятся P19–P21; сейчас не меняют экономику |
| NPC JSON | `harvestCoefficient`, `productionCoefficient` | Пока разрешено только1; другое значение отклоняется, а не игнорируется. Изменяемые коэффициенты правил остаются P21/P24 |

Числа — стартовый баланс стенда, не новые игровые правила. Для исследования сделать копии двух JSON, менять один фактор, указывать новые пути и отдельный output. В manifest хранятся seed/config hash и полные состояния политик (в том числе settings); checkpoint содержит обе конфигурации. Для одинаковых версий+seed+JSON ожидается одинаковая лента решений/состояний. Новый seed не обязан дать тот же исход.

Пример другой начальной численности: скопировать scenario, изменить `population`; деньги — `participants[].cashMinor`; прирост — `labor.growthPeriodTicks`/`growthPerPeriod`; длительность производства — `recipes[].fullStaffTicks`. Не менять сроки и объёмы одновременно с политикой, если цель — измерить эффект политики. При нулевых деньгах ручная добыча остаётся бесплатной; конечный внешний спрос может закончиться (генератор спроса — P18).

## Где и в каком виде результат

Для команды выше — папка `Delsk/artifacts/run-42`. Открыть в Rider либо проводнике; CSV — таблицей в Excel/LibreOffice, JSON — в редакторе. Результаты не появляются автоматически в Godot: его текущая сцена — отдельный технический стенд62 тиков, а не интерфейс автономной партии.

| Файл | Что смотреть / проверять |
| --- | --- |
| `summary.json` | succeeded/failed, тик, счета, по каждому товару добыча/выпуск/расход/экспорт, население/безработица, группы; хеши мира и контроллеров |
| `agents.csv` | **Каждый NPC на каждом тике**: наличные, стоимость складских запасов по базовым ценам, штат, число предприятий/машин, занятость персонажа, текущая цель |
| `labor.csv` | Численность/вместимость/цель группы, ставка и привлекательность, readiness/причина; задержка найма и постепенный уход |
| `metrics.csv` | Общие потоки и tick hashes; cash относится к первому участнику (в автономном стенде это бездействующий human slot). `treasury_minor` — весь баланс казны, включая землю/технику/энергию; старое имя energy_minor исправлено |
| `decisions.jsonl` | Исследовательская трасса разрешённых наблюдений NPC, выбранных целей/действий и полученных результатов команд; обычное ожидание busy не создаёт новый план |
| `commands.jsonl` / `events.jsonl` | Фактически поданные команды и последствия; отказы, длительности пути, добычи, выпуск/стройка, кадровые шаги |
| `ledger.jsonl` | Все проводки с причиной; разделить выручку, зарплату, энергию и вложения |
| `controllers.json` | Финальные цели/планы/курсор/ожидаемая команда/PRNG/настройки/доверие NPC; состояние можно сверять после load |
| `manifest.json` | Seed, версии, hash конфигурации мира (не политики), timing policy, типы/состояния/настройки политик, runtime; Commit заполнен только если задан DELSK_COMMIT |
| `final-state.json` | Полное исследовательское состояние; **не загружаемое сохранение** |

Деньги указаны в minor units:93475=934.75. Изменение наличных не равно прибыли с амортизацией. `stock_base_value_minor` оценивает только наличные собственные запасы; WIP отдельно в final-state, в стоимость запаса не включён. Выпуск/экспорт сравнивать по каждому товару; суммы количеств разных товаров не являются денежным показателем. Общее время запуска/условное среднее не заменяют профиль CPU и включают подготовку/сохранение; RAM пока не измеряется (null+reason).

Пилот seed42 дал три оплаченных производства и три машины:81 доску/50 хлеба выпущено,80 досок/37 хлеба продано; оставшийся товар сохранён в складах/личном инвентаре. Наличные NPC2/3/4:934.75/702/970.58 против200 в начале. Это описание пилота, не доказательство оптимального баланса или интересной игры.

## Сохранение, продолжение и replay

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --checkpoint artifacts/npc.save.json --checkpoint-at 350 --output artifacts/npc-continuous
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/npc.save.json --ticks 1000 --output artifacts/npc-loaded
```

Первая команда сохраняет на350 **живой** мир и планы, затем продолжает до1000. Вторая непосредственно восстанавливает их и выполняет351…1000, не проигрывая первые350 тиков. Для автономного load конечный тик задаётся явно; seed/scenario/settings берутся из сохранения и не заменяются. `--checkpoint FILE` без checkpoint-at записывает состояние на конце прогона.

Для старых synchronous fixtures checkpoint-at сначала записывает их полную командную ленту и сохраняет recorded cursor; такие saves знают финальный тик сами. Это не способ сохранения живых NPC. Session schema2 включает контроллеры. P17a World schema5/Rules logistics-1: старые World1–4 файлы не загружаются, автоматической миграции нет; оставить их для прежней версии кода. Декодированный сеанс с политиками требует `RestoreControllers` в composition root до Step; runner выполняет это сам. Неизвестная политика/версия отклоняется, контроллеры не исчезают молча.

`commands.jsonl` можно проиграть через `--replay PATH --scenario ... --ticks FINAL_TICK`: это повтор **мира** без повторного вызова NPC. Планы контроллеров проверять через checkpoint/load, не через command-only replay. Время отсутствия не продвигает мир. Исправный предыдущий файл — `.bak`; явный выбор backup: `--recover-backup true`. После corrupt main восстановиться и сохранить в новый путь; не вращать повреждённый main поверх backup.

## Общая автоматическая проверка

Если Python установлен:

```powershell
python scripts/check_headless.py --output artifacts/headless-p17-p18
```

Она выполняет bounded restore/build,512 xUnit-тестов,19 обычных сценариев,16 обычных save/load пар и отдельную controlled late-return пару. Дополнительно сравниваются queue/demand/elections command replays. Сравнение15 файлов включает capital-demand.json/elections.json;57 выгрузок проходят независимые money/item/capacity/population/settlement/demand/election checks. Полные stdout/stderr и exit codes лежат в checks; ошибки возвращают ненулевой код.

Что это подтверждает: отсутствие отрицательных/повторных списаний, дублирования товара, потери численности, сохранение физической длительности и воспроизводимое продолжение. Что оценивать по данным отдельно: простаивание, накопление товара, окупаемость вложений, конкуренцию зарплат, повторение неудачных целей. Участнические аукционы P14 проверены; выдача P15 и поставочные договоры P16 проверены; первый рейс P17a реализован; оставшиеся P17b/выборы ещё не реализованы. Их нельзя считать проверенными по успеху набора.

Полную экономику после P21 проверять **тем же C# runner без визуального оформления**: полноценный сценарий → ограниченный горизонт/результат → проверки → несколько одинаково настроенных seeds → сравнение выпуск/исполнение/безработица/победы/ошибки. Автоматизация серий и статистика — P24/P25; графики ещё не создаются. [Фактическая граница](PROTOTYPE_STATUS.md).

## Новый рыночный сценарий P14

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --output artifacts/market
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --checkpoint artifacts/market-p17a.save.json --checkpoint-at 44 --output artifacts/market-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/market-p17a.save.json --output artifacts/market-loaded
```

Ожидается51 ticks. Seller физически добыл и привёз15 wood, publish36/cutoff41, shortage17, rounds41–46/46–51. Buyer2/3/4 requests6/8/3, final prices5/6/7 денег; allocations4/8/3, escrow89. Товар остаётся15 reserved в seller shop, у buyer пока0, seller cash100. На этом финальном тике выдача ещё не начата; её lifecycle проверяет отдельный P15 сценарий ниже. Market demo scripted; P13 NPC пока принимают наличные заказы столицы, новые bidding goals — P21.

Новый market.json содержит предложения/активные bids/allocations, summary также показывает их; ledger — MarketEscrow, events — MarketOfferPublished/MarketAuctionStarted/MarketBidRaised/MarketRoundStarted/MarketPurchaseAllocated/MarketWindowClosed. Research exports могут содержать private bid maximum; participant/ControllerObservation видит только свой maximum и двусторонние allocations.

`market.auctionRoundTicks`/`auctionRounds`/`minimumRaiseMinor` в scenario управляют раундами, TradeWindowTicks — сбором заявок. Requests/raises в API требуют MarketOfferId, при raise/withdraw — текущий MarketRound. Публикация требует own shop stock. Денежный hold основан на current price, maximum — потолок, не мгновенное списание. Failed raise/edit оставляет старый hold/bid. После закрытия выделенные деньги в escrow, surplus money/unsold units свободны.

Четвёртая checkpoint пара сохраняет44 внутри auction; market.json также сравнивается побайтно. Набор после P15 содержит205 tests,8 scenarios,5 save/load pairs. Godot остаётся технической62-tick fixture. World3/Rules pickup-1 изменяет canonical hashes даже пустого рынка; исторические P13 numbers сохранены, старые hashes/saves не выдаются за текущие. P13/P14 saves открыть соответствующим прежним кодом в отдельной копии, новые писать в новый файл; автоматического преобразования нет.


## Выдача P15, параметры и результаты

Запуск: `dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup`. Это deterministic scripted lifecycle, не автономный market NPC. Из10 wood по500 получены3+3 двумя рейсами, residual4 expires tick48 во второй обратной поездке. Финал52: seller13100/buyer6900, escrow0, Paid3000/Fine100/Refund1900, Q10=C6+R0+X4. market.json содержит исходный Q, counters, escrow/source/hold, DeadlineTick, ClosedTick и Status. ledger.jsonl содержит фактические выплаты; events.jsonl — PurchaseCollected/PurchaseExpired/PickupFinePaid; final-state — физические запасы; scenario.json — фактически использованные настройки.

`market.pickupTtlTicks` — от allocation (default120), `market.pickupFineBasisPoints` — штраф на остаток (default500=5%). В fixture TTL17/carrier3 специально проверяют просрочку. В собственной копии JSON можно менять баланс; script pickup имеет fixed контрольные числа и будет отклонять иной результат. Для новых сценариев нужен собственный command tape/fixture, а не изменение ожидаемой бухгалтерии ради зелёного результата. `--replay commands.jsonl --ticks FINAL_TICK` воспроизводит команды с теми же настройками; controller decisions при этом повторно не вычисляются.

Пятая save/load пара tick47 проверяет уже оплаченный груз в пути до expiry48. Unit tests дополнительно сравнивают каждый последующий tick после cut31/35/36/40/46/47/48/50, включая pending receipt и deadline. `scenario.json` тоже входит в побайтное сравнение; independent verifier сверяет Q/C/R/X, escrow/receipts/refund/fine и ledger по счёту договора со ставкой из JSON. Это анализ C# результатов, Python не перемещает товар и не запускает отдельную модель экономики.


## Поставочные договоры P16

`--scenario scenarios/delivery-lifecycle.json --script delivery --output artifacts/delivery` проверяет funded будущую поставку участнику; `--scenario scenarios/capital-delivery.json --script capital-delivery --output artifacts/capital-delivery` — тот же lifecycle столице. Это scripted fixtures, не новые самостоятельные NPC goals. Supplier carrier3 делает два рейса с добычей3+3 после заключения договора. Participant allocation7/deadline47/final48; Capital allocation6/deadline46/final47. Q10/Delivered6/Unfulfilled4; Paid3000/Refund2000/Fine100/BondReturn150 minor. Fine платит supplier покупателю (у pickup наоборот).

Настройки `delivery.contractTtlTicks` default180, `fineBasisPoints` default500, `offerTtlTicks` default180. В fixture TTL40 специально создаёт partial expiry. Bond финансируется по тому же проценту полной стоимости, default250 при10×500; первоначальный hold supplier на всю request, после proportional allocation излишек освобождается. Participant buyer заранее hold budget, при подписании — full destination capacity. Если не хватает места, ни один contract окна не подписывается, supplier holds освобождаются, незаключённый бюджет остаётся до expiry/cancel. Initial cash/carrier/map/window задаются в scenario.json; изменение balance может менять результат, fixed fixture assertions нельзя менять ради скрытия ошибки.

Одна supplier request на window/offer. AcceptDeliveryQuantity принимает BuyOfferId для участника **или** OfferId для столицы, Quantity и опубликованный OfferWindow; stale window отклоняется. CreateBuyOffer требует Destination own building input/item/Q/price. DeliverContract принимает ContractId/Q, не самодельный счёт/цену/destination. Все движения/приёмка/штрафы на C#; Python только запуск/анализ экспортов.

`delivery.json` содержит buy offers/requests/contracts, summary/final-state — их конечные состояния, ledger.jsonl — DeliveryFunding/Bond/Payment/Refund/Fine/BondReturn, events — SupplyQuantityRequested/DeliveryContractCreated/DeliveryReceived/DeliveryExpired. Own projections contracts доступны сторонам, research full dump не использовать как controller prompt. В P16 active Remaining=Unprovided, Prepared/InTransit0: personal stock ещё не прикреплён к договору, automatic quota/cargo будет P17.

Набор после P16:249 tests,10 scenarios/7 save/load pairs,24 проверенных выгрузки. Сравниваются12 файлов (добавлен delivery.json). Cut delivery37 — physical outbound, capital-delivery3 — window/bond holds; unit tests дополнительно cuts1/3/7/14/17/21/34/37/41/46/47 с каждым последующим tick. Capital spot/future общее окно проверено отдельным xUnit и share ровно один budget, без второй эмиссии. Default bootstrap cash0 recovery всё ещё отдельная доступная spot sale.

Current World4/Rules delivery-1; старые World1–3 files оставить для прежних веток, автоматической migration нет. Godot probe updated hashes после independent money/item checks; новый user save path по RulesVersion. Реальная ручная UI игра/авторейсы/модель/политика ещё не проверены и не готовы.


## Первый транспортный цикл P17a

`--scenario scenarios/logistics-cycle.json --script logistics --output artifacts/logistics`: supplier физически добывает20 wood, производит10 plank, покупает систему (N0), заключает delivery contract и назначает его системой. Worker groups полностью числовые; fixture ускоряет Labor step1/mobility1, group сначала нанимается. Route quota automatically10, без ручного повторного ввода. Prepare67/load68/arrival71/return74/final75, path2,0→5,0 и обратно. Logistics payroll50×6=300minor, полный wages800, supplier108200/buyer70000, total200000.

На Loading quota R10/source stock hold10; Outbound I10/cargo reserved10/source return place10; при receipt I→D/cargo empty/return hold released, после return wage hold released. Generic Transfer managed cargo запрещён, manual receipt максимум U. Actual N и committed wage оплачиваются из upper capacity budget, новая ставка после end+1; idle неоплачиваемый. ETA>deadline не отправляется.

`logistics.json` — системы/текущий trip/path/progress/holds/зарплата; final-state — real stocks; delivery.json — D/R/I/U; ledger — LogisticsPurchase/LogisticsWages/DeliveryPayment; events — prepare/load/accepted/completed; labor.csv — numerical crew/caps/readiness. Research Inspect может видеть весь cargo; чужие controllers получают только own projections. P17b queues/upgrades/все destinations/controlled late return и P21 policies ещё отсутствуют.

Current набор281 tests/11 scenarios/8 save-load pairs,27 проверенных выгрузок,13 побайтных файлов каждой пары (добавлен logistics.json). Cut69 — outbound; unit cuts45/66/67/68/69/70/71/72/73/74 плюс зарплата и unused reserve cases. Headless сохраняет длительность перевозки, ускоряется только calculation. World5/Rules logistics-1 не мигрирует World1–4; сохранять предыдущие файлы/ветки. По умолчанию check_headless пишет artifacts/headless-p17a.

[Исследовательская готовность и темы](RESEARCH_READINESS.md) описывает будущие пилоты/серии. Текущий regression подтверждает correctness этой реализации; эффект модели/новизну гипотез из него не выводить.


## Продукт в следующее собственное производство (текущая итерация P17)

`--scenario scenarios/own-production-cycle.json --script own-production --output artifacts/own-production`: настоящий наём из N0, источник производит30plank из60wood; рейсы10+5 заполняют input15, output stop позволяет downstream переработать14plank→7crate, resume доставляет10+4. Финал212ticks, logistics wages1200minor, cash73600, total200000, нет активных holds. Продукция приходит после физического пути; stop не отменяет начатую работу. Дополнительный рецепт — только тестовый контент.

Save cut170 — второй outbound с грузом5. Использовать обычные `--checkpoint FILE --checkpoint-at 170` и `--load FILE`;13 export files равны continuous. Current World8/Session2/Envelope1/Rules logistics-6, старые Rules без migration; исторические номера выше относятся к прежним итерациям. [Отчёт](IMPLEMENTATION_REPORT_OWN_PRODUCTION.md). Очередь/priorities/fairness/оставшиеся quota scenarios остаются P17; Android device gate открыт.


## Текущая очередь и городской спрос P17/P18

```sh
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-queue.json --script logistics-queue --output artifacts/queue
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/capital-demand-cycle.json --script capital-demand --checkpoint artifacts/demand.save.json --checkpoint-at 85 --output artifacts/demand
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/demand.save.json --output artifacts/demand-loaded
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/autonomous-demand.json --script autonomous --ticks 1000 --output artifacts/npc-demand
python scripts/check_headless.py --output artifacts/headless-p17-p18
```

Queue cycle226ticks:13+7 по ближайшему сроку рейсами10+3+7, затем own output10; зарплата1800minor. Save185 сохраняет активный рейс и ожидающие задания. Demand cycle185ticks:cash0→harvest2wood→physical spot sale,4 периодических бюджета и retirement старых остатков; save85 внутри периода. NPC demand350→1000 восстанавливает реальные планы/очереди/рыночные фазы, не проигрывает первые350 ticks.

capital-demand.json — researcher periods/G/base consumed/stock delta/smoothing/target/orderedBase/monetary budget/external funding и private state; не controller prompt. Observation содержит CapitalPrices и16 периодов опубликованной истории, private cycles/RNG исключены. Current World10/Rules capital-demand-1, Session2/Envelope1. Null CapitalDemandSettings оставляет исторический fixed-order fixture; новые настройки не применяются скрыто к старому балансу. Следующее P19; device gate открыт.

## Выборы P19

```bash
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/elections.json --script elections --output artifacts/elections
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/elections.json --script elections --checkpoint artifacts/elections.save.json --checkpoint-at 9 --output artifacts/elections-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/elections.save.json --output artifacts/elections-loaded
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/elections.json --script elections --replay artifacts/elections/commands.jsonl --ticks 40 --output artifacts/elections-replayed
python scripts/verify_checkpoint_exports.py artifacts/elections artifacts/elections-loaded artifacts/elections artifacts/elections-replayed
```

Fixture: numerical employment30/20/10/0, population100, period10/base1. В первом голосовании A получает32/64 и не побеждает. Во втором прямые B→A/C→B дают A52/B11/D1; tick20 first winnerA. Tick21 ContinueAfterVictory, затем настоящая ручная добыча в песочнице. elections.json содержит snapshot результатов и полный research state, а не loadable save. ControllerObservation.Elections показывает только собственный pending choice и публичные результаты.

В обычном мире Elections=null включает period1200/base1, а не выключает выборы. World11/Rules elections-1 несовместимы со старыми saves; сохранения разных правил держать отдельно. Explicit regeneration после проверенного gate: `python scripts/update_platform_fixture.py artifacts/cycle --elections-directory artifacts/elections`. Godot smoke проверяет62экономических и40выборных hashes, включая file restores9/19/20/21 перед выборами/победой/песочницей. [Отчёт P19](IMPLEMENTATION_REPORT_P19.md). Следующий этап P20, не реализован.

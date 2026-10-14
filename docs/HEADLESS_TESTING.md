# Как запускать и проверять экономику без графики

Автор: Шелестов Максим Евгеньевич. Состояние после P15, 14.10.2026.

## Что выполняет прогон и зачем Python

Вся игровая экономика, контроллеры и консольный runner написаны на **C#**. `Simulation.Core` считает мир; `Agents.Algorithmic` выбирает действия NPC; `Simulation.Hosting` передаёт команды, сохраняет и продолжает сессию. Godot использует это же ядро. Python не вычисляет производство, найм, деньги или движение и не нужен для одиночного прогона.

`Research.Runner` — консольная программа, которая создаёт мир по JSON, добавляет контроллеры, выполняет тики до заданного горизонта и выгружает результат. В автономном сценарии три NPC сами добывают, строят, нанимают, производят и продают. Место человека оставлено свободным: без интерактивного ввода оно бездействует. Старые `cycle`/`equipment` и другие fixtures исполняют заранее написанные команды для регрессии; это отдельный тип проверки.

**Headless** означает отсутствие графики и ожидания реальной секунды между тиками. Путь на30 игровых тиков всё равно занимает30 тиков; производство, зарплаты, переходы и окна используют те же правила. `paced` ждёт1 реальную секунду на тик. В ускоренном прогоне не происходит мгновенной передачи груза.

Python сейчас лишь удобно запускает .NET-сборку/тесты/несколько прогонов с таймаутами и независимо проверяет выгруженные файлы. Позднее P25 добавит анализ серий и графики. Можно обойтись C# runner и xUnit; второй движок экономики не создаётся.

## Быстрый запуск в Rider или терминале Windows

Получить ветку `feat/pickup-p15` (после её merge — текущую базовую ветку); безопасное обновление — [LOCAL_SETUP.md](LOCAL_SETUP.md). Открыть `Delsk.sln`. В терминале **корня репозитория**:

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Здесь Python и Godot не используются. SDK10.0.401 закреплён в global.json; рабочее окружение не нужно переустанавливать. Rider можно использовать для отладки C# runner. Встроенная консоль напечатает `succeeded`, конечный тик, хеш мира и **абсолютный путь результата**. Указанные команды удобны для ручной проверки; облачные проверки запускают их через `run_check.py` с hard timeout. При `failed` смотреть причину и код завершения; наличие каталога само по себе не означает успех.

Исторический пилот P13:1000 тиков, seed42,3.375 секунды на этой среде до экспорта. Это измерение одного небольшого мира, не обещание скорости на другом компьютере или длинной партии. Полный месячный рынок/политика ещё не готовы; этот запуск проверяет P00–P15.

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

Для старых synchronous fixtures checkpoint-at сначала записывает их полную командную ленту и сохраняет recorded cursor; такие saves знают финальный тик сами. Это не способ сохранения живых NPC. Session schema2 включает контроллеры. P15 World schema3/Rules pickup-1: старые World1/2 файлы не загружаются, автоматической миграции нет; оставить их для прежней версии кода. Декодированный сеанс с политиками требует `RestoreControllers` в composition root до Step; runner выполняет это сам. Неизвестная политика/версия отклоняется, контроллеры не исчезают молча.

`commands.jsonl` можно проиграть через `--replay PATH --scenario ... --ticks FINAL_TICK`: это повтор **мира** без повторного вызова NPC. Планы контроллеров проверять через checkpoint/load, не через command-only replay. Время отсутствия не продвигает мир. Исправный предыдущий файл — `.bak`; явный выбор backup: `--recover-backup true`. После corrupt main восстановиться и сохранить в новый путь; не вращать повреждённый main поверх backup.

## Общая автоматическая проверка

Если Python установлен:

```powershell
python scripts/check_headless.py --output artifacts/headless
```

Она выполняет bounded restore/build,205 xUnit-тестов,5 прежних fixtures, market/pickup и автономный прогон,5 пар save/load. Сверяет побайтно final-state/ledger/events/commands/metrics/labor/**controllers/decisions/agents**, затем независимо проверяет деньги, товар, вместимости и население. `artifacts/headless/checks` содержит stdout/stderr/exit/timeout, `headless-check.json` — успешный итог. Сбой не маскируется и возвращает ненулевой код.

Что это подтверждает: отсутствие отрицательных/повторных списаний, дублирования товара, потери численности, сохранение физической длительности и воспроизводимое продолжение. Что оценивать по данным отдельно: простаивание, накопление товара, окупаемость вложений, конкуренцию зарплат, повторение неудачных целей. Участнические аукционы P14 проверены; выдача P15 проверена; поставочные контракты/выборы ещё не реализованы. Их нельзя считать проверенными по успеху набора.

Полную экономику после P21 проверять **тем же C# runner без визуального оформления**: полноценный сценарий → ограниченный горизонт/результат → проверки → несколько одинаково настроенных seeds → сравнение выпуск/исполнение/безработица/победы/ошибки. Автоматизация серий и статистика — P24/P25; графики ещё не создаются. [Фактическая граница](PROTOTYPE_STATUS.md).

## Новый рыночный сценарий P14

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --output artifacts/market
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --checkpoint artifacts/market-p14.save.json --checkpoint-at 44 --output artifacts/market-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/market-p14.save.json --output artifacts/market-loaded
```

Ожидается51 ticks. Seller физически добыл и привёз15 wood, publish36/cutoff41, shortage17, rounds41–46/46–51. Buyer2/3/4 requests6/8/3, final prices5/6/7 денег; allocations4/8/3, escrow89. Товар остаётся15 reserved в seller shop, у buyer пока0, seller cash100. На этом финальном тике выдача ещё не начата; её lifecycle проверяет отдельный P15 сценарий ниже. Market demo scripted; P13 NPC пока принимают наличные заказы столицы, новые bidding goals — P21.

Новый market.json содержит предложения/активные bids/allocations, summary также показывает их; ledger — MarketEscrow, events — MarketOfferPublished/MarketAuctionStarted/MarketBidRaised/MarketRoundStarted/MarketPurchaseAllocated/MarketWindowClosed. Research exports могут содержать private bid maximum; participant/ControllerObservation видит только свой maximum и двусторонние allocations.

`market.auctionRoundTicks`/`auctionRounds`/`minimumRaiseMinor` в scenario управляют раундами, TradeWindowTicks — сбором заявок. Requests/raises в API требуют MarketOfferId, при raise/withdraw — текущий MarketRound. Публикация требует own shop stock. Денежный hold основан на current price, maximum — потолок, не мгновенное списание. Failed raise/edit оставляет старый hold/bid. После закрытия выделенные деньги в escrow, surplus money/unsold units свободны.

Четвёртая checkpoint пара сохраняет44 внутри auction; market.json также сравнивается побайтно. Набор после P15 содержит205 tests,8 scenarios,5 save/load pairs. Godot остаётся технической62-tick fixture. World3/Rules pickup-1 изменяет canonical hashes даже пустого рынка; исторические P13 numbers сохранены, старые hashes/saves не выдаются за текущие. P13/P14 saves открыть соответствующим прежним кодом в отдельной копии, новые писать в новый файл; автоматического преобразования нет.


## Выдача P15, параметры и результаты

Запуск: `dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup`. Это deterministic scripted lifecycle, не автономный market NPC. Из10 wood по500 получены3+3 двумя рейсами, residual4 expires tick48 во второй обратной поездке. Финал52: seller13100/buyer6900, escrow0, Paid3000/Fine100/Refund1900, Q10=C6+R0+X4. market.json содержит исходный Q, counters, escrow/source/hold, DeadlineTick, ClosedTick и Status. ledger.jsonl содержит фактические выплаты; events.jsonl — PurchaseCollected/PurchaseExpired/PickupFinePaid; final-state — физические запасы; scenario.json — фактически использованные настройки.

`market.pickupTtlTicks` — от allocation (default120), `market.pickupFineBasisPoints` — штраф на остаток (default500=5%). В fixture TTL17/carrier3 специально проверяют просрочку. В собственной копии JSON можно менять баланс; script pickup имеет fixed контрольные числа и будет отклонять иной результат. Для новых сценариев нужен собственный command tape/fixture, а не изменение ожидаемой бухгалтерии ради зелёного результата. `--replay commands.jsonl --ticks FINAL_TICK` воспроизводит команды с теми же настройками; controller decisions при этом повторно не вычисляются.

Пятая save/load пара tick47 проверяет уже оплаченный груз в пути до expiry48. Unit tests дополнительно сравнивают каждый последующий tick после cut31/35/36/40/46/47/48/50, включая pending receipt и deadline. `scenario.json` тоже входит в побайтное сравнение; independent verifier сверяет Q/C/R/X, escrow/receipts/refund/fine и ledger по счёту договора со ставкой из JSON. Это анализ C# результатов, Python не перемещает товар и не запускает отдельную модель экономики.

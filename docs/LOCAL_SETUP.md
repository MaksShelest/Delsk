# Локальная проверка: Windows, Rider и Godot

Автор: Шелестов Максим Евгеньевич. Просрочка и возврат груза (P17), 15.10.2026.

## Получить исходники

Ниже команды **PowerShell**, из выбранного рабочего каталога. Нужен Git. Новая копия:

```powershell
git clone --branch feat/logistics-late-return https://github.com/MaksShelest/Delsk.git Delsk
cd Delsk
```

Если Delsk уже есть, сначала `git status`. Сохранить собственные незакоммиченные изменения; не делать reset/clean и не копировать поверх них архив. Затем:

```powershell
git fetch origin
git switch --track origin/feat/logistics-late-return
```

Если эта локальная ветка уже существует, вместо последней строки выполнить `git switch feat/logistics-late-return`, затем `git pull --ff-only`. При ошибке о локальных изменениях остановить переключение и сохранить свою работу. После объединения PR можно перейти на актуальную основную ветку; сейчас default branch репозитория называется `feat/economic-cycle-p00-p05`, а не `main`.

## Проверить окружение и собрать ядро

Для тестирования готовности см. [PROTOTYPE_STATUS.md](PROTOTYPE_STATUS.md). В репозитории закреплены **.NET SDK 10.0.401 x64** и **Godot .NET 4.7.2**. Проверенная матрица описана в отчётах P06 и P09–P10. Сначала проверить уже установленное окружение, не переустанавливать его автоматически. В корне проекта `dotnet --version` должен вернуть `10.0.401`; если SDK отсутствует, установить эту версию рядом с имеющимися. Godot должен быть именно выпуском **.NET** с C#, стандартный редактор не подойдёт. Templates нужны для экспорта, для запуска сцены в редакторе не обязательны.

```powershell
dotnet --version
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
```

В PowerShell `'-m:1'` передаётся в кавычках. Проверять exit code каждой команды: продолжать после успешного restore/build. Тестовая сборка содержит встроенный xUnit runner; это тот же набор тестов, без необходимости поддержки конкретного test UI Rider. Альтернатива, проверяемая CI: `dotnet test --project tests/Simulation.Tests/Simulation.Tests.csproj -c Release --no-build --no-restore`.

## Rider и исследовательский runner

Открыть **Delsk.sln** для ядра, Hosting, runner и тестов. Rider Terminal открывается в корне репозитория. Команда нового сценария:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/labor-dynamics.json --script labor --output artifacts/labor
```

Ожидается `status=succeeded`, **291 тик**. На конце: население9, группы1/8, безработица0. Ставки40/20 minor units; цель8/1 пока ещё не достигнута после недавнего возобновления. В `artifacts/labor/labor.csv` видны каждый тик, численность, места, ставка оплаты, ставка привлекательности, цель, готовность и остаток мобильности. В `events.jsonl` видны изменение ставки, переходы и оплаты; `summary.json` — денежный итог. Это scripted исследовательский пример, не автономные NPC.

Для ручной проверки посмотреть:

- tick80: группы1/8 при ставках10/20;
- команда повышения принимается81, оплата по40 начинается82, привлекательность меняется101, ближайший перевод110;
- остановка первой группы202 не платит и сохраняет WIP/численность; группа уходит постепенно до0 в280;
- возобновление281; первый повторный найм290, оплата291.

Другие прогоны: `--scenario scenarios/economic-cycle.json --script cycle`, `--scenario scenarios/zero-cash-recovery.json --script recovery`, `--scenario scenarios/land-and-content.json --script development`. Указывать отдельный `--output`, чтобы сохранить результаты. Их трудовой шаг10000 и прирост0 намеренно фиксируют прежний regression-стенд; новые кадровые правила проверяются сценарием labor и xUnit. Это не рекомендуемые настройки обычной партии.

Если установлен Python, дополнительная независимая проверка: `python scripts/verify_results.py artifacts/labor`. `final-state.json` пока **не загружаемый save**, полноценное продолжение из checkpoint — P12.

## Godot .NET

Клиент имеет отдельное решение. Из корня репозитория:

```powershell
dotnet restore src/Client.Godot/Delsk.Client.csproj --locked-mode --disable-parallel '-m:1'
dotnet build src/Client.Godot/Delsk.Client.csproj --no-restore --disable-build-servers '-m:1'
```

В Godot Project Manager нажать **Import**, выбрать **src/Client.Godot/project.godot**, открыть проект. В Rider открыть **src/Client.Godot/Delsk.Client.sln**. Общие Core и Hosting подключены ProjectReference; переносить C#-файлы в отдельный новый Godot-проект вручную не требуется.

В Godot: **Editor → Editor Settings → Dotnet → Editor → External Editor → JetBrains Rider**. В Rider проверить путь к установленному Godot .NET в настройках Godot Engine. Настройка интеграции подтверждена [официальной документацией Rider](https://www.jetbrains.com/help/rider/Godot.html); [Godot C# basics](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html) описывает .NET-редактор и SDK. Версии проекта закреплены файлами global.json и Delsk.Client.csproj.

Нажать **F5**. Открывается `Playfield`: пауза, один тик, ход, добыча и карточка предприятия. `--smoke` открывает `PlatformProbe`. 3D-показ — [P26.5](IMPLEMENTATION_PLAN.md#p265). Подробности технического клиента — [IMPLEMENTATION_REPORT_P26.md](IMPLEMENTATION_REPORT_P26.md).

Ошибку ручной проверки записать вместе с `git rev-parse HEAD`, версиями SDK/Godot, сценарием и журналом; не менять SDK/TFM или ожидаемые хеши для маскировки ошибки. Android и готовый Windows EXE в этой итерации заново не экспортировались; телефонный gate остаётся открытым.

## Новая проверка техники P11

После Release build запустить:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/equipment-cycle.json --script equipment --output artifacts/equipment
```

Ожидается200 тиков: личная добыча малой/большой машиной, наём automatic экипажа, один цикл, неоплачиваемая остановка и постепенный уход, физический вывоз/продажа двух видов сырья. Подробные числовые ожидания — PROTOTYPE_STATUS.md. Текущий набор393 теста. Godot по-прежнему технический стенд; интерфейса управления техникой пока нет.

## Сохранения и общая проверка P12

[HEADLESS_TESTING.md](HEADLESS_TESTING.md) объясняет ядро/runner и даёт алгоритм проверки без графики. С Python: `python scripts/check_headless.py --output artifacts/headless-p17a`. Это10 сценариев/7 save-load пар, включая самостоятельных NPC и рынок; полная игра с выборами ещё не готова.

Godot-стенд имеет кнопки Сохранить/Загрузить/Восстановить из backup и user://platform-checkpoint-logistics-5.json. При сворачивании/фокусе сохраняет committed tick, при возврате elapsed сбрасывается; autosave30 и обычный выход. На реальном телефоне поведение ещё не проверено. Новые standalone APK/EXE в P12 не экспортировались.

## Автономная экономика P13

Обновить исходники до feat/logistics-late-return, открыть корневой Delsk.sln, restore/build как выше. Запуск из Rider Terminal без Python/Godot:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --script autonomous --ticks 1000 --seed 42 --output artifacts/run-42
```

Это три самостоятельных NPC и бездействующий human slot. CSV/JSON лежат в указанной папке проекта, абсолютный путь печатается в консоли. Настройки мира/политик и точное продолжение — [HEADLESS_TESTING.md](HEADLESS_TESTING.md). Для отладки создать .NET run configuration Research.Runner, Working directory — корень Delsk, Program arguments — те же флаги после `--` (сам разделитель в поле аргументов приложения не нужен).

В Godot ничего переносить пофайлово не требуется: обновить тот же репозиторий и собрать клиентское решение. Текущая сцена остаётся технической fixture, она ещё не отображает автономную партию. Для ручной игры нужен будущий P26; сейчас результаты NPC смотреть через runner. Python — только необязательная оркестрация/анализ.

## Рынок P14 и сохранения

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/market-window.json --script market --output artifacts/market
```

Это scripted auction,51 ticks; market.json показывает начальные allocations/escrow до выдачи. Автономный сценарий P13 остаётся доступен с --script autonomous; UI этой итерацией не добавлен.

## Выдача P15: локальная проверка

Обновить ветку `feat/logistics-late-return`, открыть Delsk.sln в Rider и выполнить restore/build из инструкции выше. Игровая экономика работает в C#, Python для этого запуска не требуется:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/pickup-lifecycle.json --script pickup --output artifacts/pickup
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/pickup-lifecycle.json --script pickup --checkpoint artifacts/pickup-p17a.save.json --checkpoint-at 47 --output artifacts/pickup-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/pickup-p17a.save.json --output artifacts/pickup-loaded
```

Ожидается succeeded,52 ticks; market.json: Collected6/Released4/Remaining0, Paid3000/Fine100/Refund1900 minor. В ledger.jsonl видны две PickupPayment по1500, PickupFine100, PickupRefund1900. Товар покупателя6, продавца4; общие деньги20000. Это две реальные поездки carrier3, expiry48 во второй обратной поездке; при загрузке tick47 путь/груз продолжаются. Для локальной полной регрессии — `python scripts/check_headless.py` (необязательный orchestration,281 тестов/8 сценариев/5 load pairs).

Godot: импортировать src/Client.Godot/project.godot .NET-редактором4.7.2, собрать Delsk.Client.sln в Rider. Он ссылается на те же Core/Hosting: копировать код пофайлово не нужно. Сцена пока technical62-tick probe; human market UI — P26.

Исторические saves P15 World3/Rules pickup-1 несовместимы с World1/2. **Не перезаписывать старый save**: новый файл для новой партии. Прежние партии открывать прежней веткой (World2: feat/market-p14; World1: feat/autonomous-p13) в отдельной копии. Автоматического преобразования нет. Godot путь итерации P15 user://platform-checkpoint-pickup-1.json не заменяет прежний.


## Поставочные договоры P16: локальная проверка

Текущая ветка `feat/logistics-late-return`. После `git fetch origin` переключить её через Rider или `git switch --track origin/feat/logistics-late-return`; если уже существует, `git switch feat/logistics-late-return` и `git pull --ff-only`. При локальных изменениях сохранить их перед переключением. Открыть Delsk.sln, restore/build как выше; Python для самого C# прогона не требуется.

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/delivery-lifecycle.json --script delivery --output artifacts/delivery
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/delivery-lifecycle.json --script delivery --checkpoint artifacts/delivery-p17a.save.json --checkpoint-at 37 --output artifacts/delivery-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/delivery-p17a.save.json --output artifacts/delivery-loaded
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/capital-delivery.json --script capital-delivery --output artifacts/capital-delivery
```

Первый результат succeeded/48 ticks. В delivery.json contract Expired, Delivered6/Unfulfilled4, Paid3000/Refund2000/Fine100/BondReturned150 minor. Supplier12900/buyer7100. В ledger две оплаты1500, возврат2000, штраф100, возврат залога150; stock buyer6, общие деньги20000. Save37 восстанавливает второй outbound путь. Capital demo47 ticks: тот же финансовый расчёт, Exported6, бюджет столицы2000 и compensation100, supplier12900; начальная внешняя эмиссия только5000.

Полный набор `python scripts/check_headless.py`:249 tests,10 scenarios,7 save/load pairs. Core .NET SDK10.0.401/TFMnet10.0 и Godot4.7.2 .NET не изменились. Для клиента открыть src/Client.Godot/project.godot и собрать Delsk.Client.sln в Rider; UI всё ещё technical probe.

Исторические P16 saves **World4/Rules delivery-1, Session2/Envelope1**. World1–3 не мигрируются; старые файлы открыть прежним кодом в отдельных копиях (P15: feat/pickup-p15; P14: feat/market-p14; P13: feat/autonomous-p13). Новая партия — новый файл; Godot user://platform-checkpoint-logistics-5.json. Код не нужно вручную переносить пофайлово, Core/Hosting уже связаны ProjectReference.


## Автологистика P17a: локальная проверка

Получить ветку feat/logistics-late-return; открыть Delsk.sln в Rider, выполнить locked restore/Release build по инструкции выше. Версии .NET/Godot прежние. Запуск C# не требует Python:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-cycle.json --script logistics --output artifacts/logistics
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-cycle.json --script logistics --checkpoint artifacts/logistics-p17a.save.json --checkpoint-at 69 --output artifacts/logistics-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/logistics-p17a.save.json --output artifacts/logistics-loaded
```

Expected succeeded/75 ticks, contract Fulfilled/Delivered10, logistics CompletedTrips1 и Trip=null. Ledger LogisticsWages300/total WagesSink800/Treasury21000, Supplier108200/buyer70000. На69 сохраняются outbound cargo10, source return capacity10, wage reserve250; actual trip prepare67/load68/receipt71/return74. Смотреть logistics.json/delivery.json/ledger/events/labor.csv. Никаких ручных копий Core в Godot не нужно: клиент ProjectReference остаётся тем же; UI пока technical probe.

Все проверки: `python scripts/check_headless.py --output artifacts/headless-p17a` (281 tests/11 scenarios/8 save-load pairs). Current **World5/Rules logistics-1**; не перезаписывать старые saves, использовать новый файл. Прежние партии — прежним кодом в отдельной копии (P16 branch feat/delivery-p16 и более ранние соответствующие ветки). Godot user://platform-checkpoint-logistics-1.json. Android device test не проведён.


## P17b1: улучшенный транспорт

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-upgrade.json --script logistics-upgrade --output artifacts/logistics-upgrade
```

Ожидается176ticks,20plank доставлено/10plank израсходовано на upgrade, supplier105050/buyer40000minor, total200000. Level2/cargo20/crew capacity3 видны в logistics.json/final-state.json/labor.csv. Это scripted fixture; NPC transport goals ещё P21. Исторический P17b1:World6/Rules logistics-2, старые World1–5 только прежним кодом; отдельные пути сохраняют старые saves.308/308 tests и Windows probe прошли удалённо, текущая Work-среда без SDK/Godot; телефонный gate остаётся открыт. [Отчёт](IMPLEMENTATION_REPORT_P17B1.md).


## P17b2: собственный склад/лавочка до заполнения

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-storage-cycle.json --script own-storage --output artifacts/own-storage
```

Ожидается180ticks, два рейса10+5 в свой склад15, ещё15plank в output, supplier74900minor, total200000. Это scripted fixture, не новая автономная стратегия NPC/P21. Полный статус сборок/тестов/сохранений — [отчёт P17b2](IMPLEMENTATION_REPORT_P17B2.md). Исторический P17b2:World7/Rules logistics-3, World1–6 без migration, старые файлы сохранять для прежнего кода. Общий headless check: `python scripts/check_headless.py --output artifacts/headless-p17b2`. SDK/Godot версия не менялась; текущая Work-среда без SDK, реальные build/test выполняются CI. Android device gate открыт.


## P17: входящие поставки на предприятие

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/inbound-cycle.json --script inbound --output artifacts/inbound
```

Сырьё сначала физически складируется человеком, затем машина едет пустой к складу и с грузом возвращается на предприятие. Input/output switches отдельны, один slot/numeric crew. После inbound10+5 production потребляет14wood и выпускает7plank; исходящий рейс возвращает продукт в склад. Проверенный цикл110ticks: supplier78050minor/общая сумма200000, input1wood/storage15wood+7plank. Actual результаты — [отчёт](IMPLEMENTATION_REPORT_INBOUND.md). Current World8/Rules logistics-4, старые World1–7 без migration; SDK/Godot не менялись, телефонный gate открыт. Полная bounded regression: `python scripts/check_headless.py --output artifacts/headless-inbound`.


## P17: контролируемая просрочка и возврат

Ветка `feat/logistics-late-return` основана на PR №14; PR №15 содержит эту итерацию. Полный запуск с тестовым аварийным снимком:

```powershell
python scripts/check_headless.py --output artifacts/headless-late-return
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/headless-late-return/late-return.save.json --output artifacts/late-return-restored
```

Нужны .NET и Python только для полного orchestration/проверки экспорта. Игровые правила и восстановление выполняет C#. Фикстура создаётся в xUnit при заданном DELSK_RETURN_ARTIFACTS; bare test run без переменной её не экспортирует. Папки late-return-at-expiry/late-return/late-return-loaded и late-return-fault.json отделяют намеренную аварийную ветку от обычных сценариев. [Фактические результаты](IMPLEMENTATION_REPORT_LOGISTICS_RETURN.md). World8/Rules logistics-5, старые Rules не мигрируются; сохранять старые файлы. Телефонный gate открыт.

# Локальная проверка: Windows, Rider и Godot

Автор: Шелестов Максим Евгеньевич. Итерация P11, 14.10.2026.

## Получить исходники

Ниже команды **PowerShell**, из выбранного рабочего каталога. Нужен Git. Новая копия:

```powershell
git clone --branch feat/extraction-equipment-p11 https://github.com/MaksShelest/Delsk.git Delsk
cd Delsk
```

Если Delsk уже есть, сначала `git status`. Сохранить собственные незакоммиченные изменения; не делать reset/clean и не копировать поверх них архив. Затем:

```powershell
git fetch origin
git switch --track origin/feat/extraction-equipment-p11
```

Если эта локальная ветка уже существует, вместо последней строки выполнить `git switch feat/extraction-equipment-p11`, затем `git pull --ff-only`. При ошибке о локальных изменениях остановить переключение и сохранить свою работу. После объединения PR можно перейти на актуальную основную ветку; сейчас default branch репозитория называется `feat/economic-cycle-p00-p05`, а не `main`.

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

Нажать **F5**. Сейчас это технический стенд воспроизведения старого62-тактового цикла: он открывается на паузе, кнопки **One tick / Run / Restart** управляют проигрыванием записанных команд. Он вызывает обновлённое общее ядро и проверяет хеши. В нём пока нет карточки зарплат, свободного управления строительством или новой интерактивной labor-сцены. **Проверять P09/P10 следует через runner и labor.csv**. Полноценный UI — P26.

Ошибку ручной проверки записать вместе с `git rev-parse HEAD`, версиями SDK/Godot, сценарием и журналом; не менять SDK/TFM или ожидаемые хеши для маскировки ошибки. Android и готовый Windows EXE в этой итерации заново не экспортировались; телефонный gate остаётся открытым.

## Новая проверка техники P11

После Release build запустить:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/equipment-cycle.json --script equipment --output artifacts/equipment
```

Ожидается200 тиков: личная добыча малой/большой машиной, наём automatic экипажа, один цикл, неоплачиваемая остановка и постепенный уход, физический вывоз/продажа двух видов сырья. Подробные числовые ожидания — PROTOTYPE_STATUS.md. Текущий набор106 тестов. Godot по-прежнему технический стенд; интерфейса управления техникой пока нет.

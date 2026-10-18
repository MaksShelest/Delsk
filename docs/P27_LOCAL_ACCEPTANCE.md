# Локальная приёмка первого месячного объёма

Автор: Шелестов Максим Евгеньевич. 18.10.2026.

Облачные проверки и ограничения — [IMPLEMENTATION_REPORT_P27.md](IMPLEMENTATION_REPORT_P27.md). Настройка экономики — [ECONOMY_CONFIGURATION.md](ECONOMY_CONFIGURATION.md). Эти шаги нужны для аппаратных gates; результаты здесь не объявлены выполненными заранее.

## Получить и собрать

До слияния PR используйте его ветку:

```powershell
git fetch origin
git switch feat/p27-research-economy
git pull --ff-only
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --nologo '-m:1'
py -3 scripts/check_headless.py --output artifacts/p27-local
dotnet build src/Client.Godot/Delsk.Client.csproj -c Debug --nologo '-m:1'
```

Если локальной ветки ещё нет, `git switch --track origin/feat/p27-research-economy`. После слияния актуальная базовая ветка проекта — `feat/economic-cycle-p00-p05`; не подменяйте её предположением о master. Команды выше не стирают незафиксированные локальные изменения; при конфликте сначала сохраните свою работу.

## Windows: редактор и готовая сборка

```powershell
New-Item -ItemType Directory -Force artifacts/p27-local | Out-Null
$delskReportPath = Join-Path (Get-Location) 'artifacts/p27-local/platform-windows.json'
godot --headless --path src/Client.Godot -- --smoke --report $delskReportPath
py -3 scripts/verify_platform_probe.py --report $delskReportPath
godot --path src/Client.Godot -- --profile-frames
```

Создайте каталог отчёта заранее либо замените путь своим существующим каталогом. Нужен именно Godot.NET4.7.2. В новой партии проверить выбор обеих карт, WASD, добычу/уход/паузы/Debug, покупку и бронь общего места лавки, запрет второго места, планшет биржи и предприятия. На лавке — положить запас, опубликовать лот, купить у другого участника, поднять/отозвать ставку, получить товар на месте, увидеть причину недоступности. Фильтр и сортировка должны переживать повторное открытие и смену языка.

Для готового Windows EXE нужны установленные .NET export templates4.7.2; сохранённые presets не менять. Создайте каталог, выполните существующий preset:

```powershell
New-Item -ItemType Directory -Force artifacts/p27-windows | Out-Null
$delskExePath = Join-Path (Get-Location) 'artifacts/p27-windows/DelskProbe.exe'
godot --headless --path src/Client.Godot --export-debug 'Windows Probe' $delskExePath
```

Предпочтительнее передать абсолютный путь к EXE. Проверить лог на managed errors, наличие комплекта рядом с EXE и запустить его отдельно от редактора; пройти те же игровые шаги. Запуск редактора или одного Core-теста не подтверждает работоспособность готового EXE.

## Android

Использовать существующий Android preset и инструкцию [Client.Godot/README](../src/Client.Godot/README.md), без изменения пакета, подписи и настроек экспорта. Сборка APK в облаке или локально не заменяет запуск на телефоне.

На устройстве проверить оба режима/карты, читаемость планшетов, фактическую производительность, сохранение/возврат и отсутствие офлайн-догоняния. Свернуть приложение минимум на минуту во время пути/добычи/производства: после возврата экономический тик, груз и срок должны продолжаться с сохранённого состояния. Записать модель телефона, Android, версию сборки, FPS/максимальное время кадра и результат save/load. Полноценный Android-джойстик этой итерацией не добавлялся: управление WASD и desktop-показ не закрывают мобильную эргономику.

Начните с короткой партии, затем проверьте 1200 и5000 тиков/соответствующие saves. Длинная исследовательская история заметно увеличивает память и время загрузки; текущие облачные числа в отчёте не являются Android-бюджетом.

## Реальная локальная модель и Gate D

Транспорт/decision slots/приватность проверяются автоматическими тестами, но fake S7 не заменяет новую живую демонстрацию. Предыдущий реальный пилот P23/P25 сохранён как историческое доказательство своего commit. На текущей сборке выполнить 1–2 дилеммы с уже установленной моделью по [LOCAL_MODEL_VALIDATION.md](LOCAL_MODEL_VALIDATION.md); не переустанавливать работающую Ollama без необходимости.

Для нового парного пилота существующий протокол можно запустить в **новый** каталог:

```powershell
py -3 scripts/run_research_series.py --protocol scenarios/research-series-p25.json --output artifacts/p27-live-series
py -3 scripts/verify_research_series.py --output artifacts/p27-live-series --evidence artifacts/p27-live-series-gate.json
```

Это полноценная серия, которая может занять длительное время. Проверить имя/digest/доступность модели и модельный конфиг по локальной инструкции до запуска. `--resume` использовать только для прерванной серии той же конфигурации/версии, а не для выдачи старых результатов за текущие. Timing policy headless/paced/live не смешивать в одной метрике. Показать специализацию, числовой найм, реальный рейс, конкурентную сделку и выборы; объяснить, что решения модели проходят тот же валидатор.

## Что считать завершением

Зафиксировать результаты Windows EXE, Android lifecycle/ресурсов и живого Gate D на текущей версии. Только после этого обновлять аппаратный статус и объявлять полную приёмку месячного результата. Программный объём и облачная проверка P27 завершены отдельно; наличие этой инструкции само по себе не закрывает gates.

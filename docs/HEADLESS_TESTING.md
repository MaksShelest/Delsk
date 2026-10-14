# Как проверять экономику без графики

Автор: Шелестов Максим Евгеньевич. Состояние после P12,14.10.2026.

## Что означает «тестировать ядро локально»

Simulation.Core — обычная C#-библиотека с состоянием мира и правилами. Simulation.Hosting управляет сессией, сохранениями и записанными входами. Research.Runner запускает их из терминала. Эти проекты не требуют Godot, сцен, моделей, видеокарты или Python для расчёта экономики. Rider удобен как редактор/отладчик; его Terminal запускает те же команды .NET.

Сейчас runner обслуживает scripted fixtures: заранее написанные сценарии используют настоящие команды, оплату, резервы, добычу и движение. Сохраняемый recorded playback содержит ленту и курсор. Это ещё не самостоятельные NPC и не ввод человека с клавиатуры во время партии. P13 добавит алгоритмических контроллеров; P14–P21 — оставшиеся торговые/политические системы. Полный объём экономики сможет запускаться тем же способом без графики по мере реализации, не потребует отдельного переписывания правил.

Графика отвечает за отображение/ввод, а не за бухгалтерию. Headless просто вызывает Step быстрее, без ожидания реальных секунд. Расстояние30 тиков остаётся30 тиками, работники/цены/плата/дедлайны не пересчитываются в другой масштаб. Скорость прогона зависит от мира и объёма журналов, минуту на любую партию заранее не обещаем.

## Алгоритм проверки на Windows

1. Получить ветку `feat/checkpoint-p12`; подробное безопасное обновление копии — [LOCAL_SETUP.md](LOCAL_SETUP.md). Открыть **Delsk.sln**, а не клиентское решение. Проверить `dotnet --version`: SDK10.0.401 x64 закреплён в global.json.
2. В терминале корня репозитория выполнить restore/build и тесты:

```powershell
dotnet restore Delsk.sln --locked-mode --disable-parallel '-m:1'
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers '-m:1'
dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none
```

3. Запустить один сценарий, например технику:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/equipment-cycle.json --script equipment --mode headless --output artifacts/equipment
```

Ожидается succeeded/200 ticks. `--mode headless` — режим по умолчанию. `paced` специально ждёт1 секунду/тик для сравнения правил времени; он не подходит для быстрого перебора.

4. Прогнать **все готовые системы и восстановление одной командой**, если установлен Python:

```powershell
python scripts/check_headless.py --output artifacts/headless
```

Скрипт выполняет bounded restore/build,132 теста,5 сценариев и две пары save/load. Каждая дочерняя команда имеет hard timeout и отдельные logs. Он сравнивает continuous и loaded: final-state, каждый tick hash, commands/events/ledger/metrics/labor, затем независимо проверяет деньги, товар, capacity и population. Сбой возвращает ненулевой код и сохраняет результаты. Python здесь оркестрация/проверка файлов, а не вторая экономика.

5. Посмотреть `summary.json` и `metrics.csv`: статус, ticks, cash, затраты, выпуск/экспорт и численности. `labor.csv` показывает текущий/целевой штат и ставки, `ledger.jsonl` — платежи, `events.jsonl` — действия/причины. `headless-check.json` фиксирует успешную проверку набора. Успех инвариантов подтверждает целостность расчёта, а не интересность/баланс игры; их оценивать отдельно по поведению участников.

## Какие сценарии уже есть

| Script / JSON | Проверяемая область | Ожидаемые ticks |
| --- | --- | --- |
| cycle / economic-cycle.json | Добыча→производство→ручная перевозка→продажа |62|
| recovery / zero-cash-recovery.json | Выход из cash0 |32|
| development / land-and-content.json | Земля/стройка/upgrade/склады/две цепочки |465|
| labor / labor-dynamics.json | Wage³,delay,gradual transfer,idle/rehire |291|
| equipment / equipment-cycle.json | Личная/automatic машина,minCrew,buffer,физический вывоз |200|

Короткие cycle/recovery/development сохраняют исторический баланс длинным трудовым шагом. Найм смотреть в labor/equipment. Это набор P00–P12; он пока не проверяет несуществующие NPC, полноценные аукционы, поставочные договоры или выборы.

## Проверка продолжения

Сохранить на55 во время перевозки и закончить прогон:

```powershell
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/economic-cycle.json --checkpoint artifacts/cycle.save.json --checkpoint-at 55 --output artifacts/cycle-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/cycle.save.json --output artifacts/cycle-loaded
```

При checkpoint-at runner сначала получает полный план существующей synchronous fixture, затем проигрывает его и сохраняет реальный мир+курсор на указанном tick. Поэтому сохранение самодостаточно для продолжения recorded script. Это не сериализация C#-стека функции и не controller NPC. При `--checkpoint FILE` без checkpoint-at записывается итоговый мир; если в нём нет активного recorded плана, для продолжения указать `--ticks FINAL_TICK`.

`--ticks` — **абсолютный конечный tick**, не число добавляемых тиков. Для load можно указать62; без флага используется конечный tick сохранённого плана. Для обычной synchronous fixture произвольный --ticks запрещён: иначе можно было бы получить пустые тики без действий. Новые seeds/config начинают новые эксперименты; при load менять конфигурацию/seed нельзя.

Файл содержит checksum,версии,полный текущий мир и session trace; предыдущий исправный файл — `.bak`. Повреждённый main не загружается молча. Явное восстановление: `--load artifacts/cycle.save.json --recover-backup true --ticks 62`; runner сообщает путь/тик backup. Если main повреждён, Save откажется вращать его поверх good backup; после восстановления записать в новый путь. `final-state.json` остаётся inspection export, загрузка работает с файлом `--checkpoint`, а не с ним.

## Когда экономика будет дописана

Алгоритм тот же: собрать/проверить тесты → выбрать полноценный сценарий с контроллерами → headless прогон до заданного горизонта/результата → проверить инварианты и logs → повторить при разных seeds и начальных настройках → сравнить ошибки/исполнение/безработицу/победы/расходы. Сценарии должны включать дефицит, partial/expiry, простой, сохранение и политическую сделку.

Уже можно повторять готовые fixtures с `--seed N` и разными output каталогами; для смыслового исследования нужны полноценные NPC и правила последующих задач. Общая автоматизация серий/статистики — P24/P25. Не приводим команду несуществующего полного NPC/game сценария. [Граница готовности](PROTOTYPE_STATUS.md): ближайшая автономная экономика — P13, полный алгоритмический игровой цикл — Gate C/P21.

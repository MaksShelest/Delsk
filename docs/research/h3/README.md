# H3: воспроизведение и границы доказательств

Автор: Шелестов Максим Евгеньевич. 02.11.2026, Europe/Samara.

База — H2 merge `b87bc90a84845f509cf256bb227dbcf2952a43ad`, целевая ветка
`feat/economic-cycle-p00-p05`. Работа — `feat/vnext-h3-compact-acceptance`,
[PR77](https://github.com/MaksShelest/Delsk/pull/77), без auto merge.
Текущий отчёт — [IMPLEMENTATION_REPORT_H3](../../IMPLEMENTATION_REPORT_H3.md).
Сначала сохранить свои изменения; команды ниже не требуют reset/clean.

## Инструменты и короткие проверки

.NET SDK10.0.401, Godot4.7.2 .NET, Python3.12+, net10.0. Обычный клиент не
содержит probes; native checks создают отдельные stages. Тяжёлые локальные
проверки выполнять последовательно. Процессы проверяются по exit, deadline,
логам и содержимому результата, а не только по наличию файла.

```bash
dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
dotnet build src/Research.Runner/Research.Runner.csproj -c Release --no-restore --disable-build-servers -m:1
python scripts/build_compact_map.py --check
python scripts/check_map_designs.py --output artifacts/h3-maps
python scripts/check_headless.py --phase tests --suite h3 --output artifacts/h3-unit
python scripts/check_h3_prepared.py --fixtures artifacts/h3-unit/f2-fixtures --output artifacts/h3-prepared
python scripts/check_developer.py --suite h3 --output artifacts/h3-developer
python -m unittest discover -s tests/Checks -v
```

Сами scripts используют `scripts/run_check.py` и прежние deadlines. Для прямых
команд restore/build применять180s, отдельных ordinary tests120s, Developer180s;
runner600/900s. Ограничения checkpoint64MiB/512MiB не повышены.

## Настоящая сцена

```bash
python scripts/check_world_proportions.py --godot /path/to/godot-mono --maps compact-valley small-river large-districts --output artifacts/h3-world
python scripts/check_world_proportions.py --godot /path/to/godot-mono --maps compact-valley --motion --asset-cache artifacts/h3-world/assets --output artifacts/h3-motion
python scripts/check_city_experience_ui.py --godot /path/to/godot-mono --case compact --output artifacts/h3-city
python scripts/check_h2_roads.py --godot /path/to/godot-mono --maps compact-valley --fixtures artifacts/h3-unit/h2-fixtures --output artifacts/h3-roads
python scripts/check_hud_god_ui.py --godot /path/to/godot-mono --output artifacts/h3-hud
```

На Linux нужен настоящий display server; используемый здесь Xorg dummy и
llvmpipe не являются аппаратным GPU/FPS тестом. Native input проверяется
введёнными keyboard/mouse/touch events. Motion180кадров отражает6активных секунд,
но время записи PNG не является временем кадра реального приложения.
`--save PATH --natural-save --maps compact-valley` позволяет отдельно проверить
настоящий ordinary checkpoint; prepared funded snapshots так не обозначать.

## Естественная пользовательская траектория

```bash
dotnet restore tools/H3Acceptance/H3Acceptance.csproj --locked-mode --disable-parallel -m:1
dotnet build tools/H3Acceptance/H3Acceptance.csproj -c Release --no-restore --disable-build-servers -m:1
python scripts/run_check.py --name player --timeout 900 --log-directory artifacts/h3-player-checks -- dotnet tools/H3Acceptance/bin/Release/net10.0/H3Acceptance.dll artifacts/h3-player compact-valley
```

Третий аргумент helper — checkpoint той же траектории; использовать новый output
и сохранять все предыдущие попытки. Это scripted human через обычный Send с3NPC,
не ручное прохождение интерфейса и не независимая экономическая реплика.
Стратегия заработка сырым деревом намеренно проста, не оптимальна. Все деньги,
материалы, таймеры и XP действительные. Подготовленная многорейсовая доставка и
работы дорог проверяются отдельно и не выдаются за продолжение этой экономики.

## Отдельная новая экономическая когорта

[study-plan.json](study-plan.json) зафиксирован до результатов в `ec0886b`.
180ordinary миров:3карты×3/6/10NPC×3сложности, n5 для3/10NPC и n10 для6NPC;
12extended миров:3карты×Medium/Hard×6NPC×seed142/143. Ordinary seeds42–51 и
extended142/143 различаются. Один мир/seed — единица статистики, NPC и его
сегменты не независимы.18planned paired contrasts, Holm, карты раздельно.

```bash
python scripts/study_h3.py --matrix all
python scripts/study_h3.py --case ordinary-compact-valley-6-Medium-42 --output artifacts/h3-study/world --segments 3
```

Начальный сегмент/continuation использует настоящий `OrdinaryNewGame.Resolve`,
Movement1 и maxTicks1000000 без research horizon override. Frozen DLL, inputs,
worker и atomic journal сохраняются. После прерывания повторить с тем же output;
`--retry-failed` только явно для зафиксированной неудачи. При переносе сохранять
весь runtime/inputs/trajectory/segments и относительные пути. Не пересобирать
runtime продолжения. Успешный сегмент учитывается только после accounting и
public/privacy audits; pending/failed/censored не превращаются в успех/нулевой
срок. Ordinary5000 и extended batch14400 не доказывают126000/35часов.

Исходный Actions run — [41355595793](https://github.com/MaksShelest/Delsk/actions/runs/41355595793).
Raw artifacts сохраняются90дней. Не скачивать целиком десятки гигабайтов ради
статистики. `h3-collect.yml` читает выбранные исходные ZIP entries по HTTPS ranges,
проверяет их ZIP CRC, требует все192world journals и оба аудита каждого успешного
сегмента, затем сохраняет компактные observations в ветке H3. Полный digest ZIP
этим не проверяется. `collect.json` с `{"run":41355595793}` запускать после
завершения исходного run; сохранение summary не перезапускает симуляции.

```bash
python scripts/analyze_h3.py --observations docs/verification/h3/economy/observations.json.gz --output artifacts/h3-analysis
```

Возобновление отдельным bounded batch: `resume.json` с исходным run и
`phase:"extended"`, затем push. Workflow восстанавливает исходный commit и
артефакты; не подменяет отсутствующий мир новой партией. Поздние продолжения
сохранять отдельным run и явно связывать с прежним journal. РазвитиеII–V,
окупаемость, задания и преимуществоHard заявлять лишь по фактической экспозиции.

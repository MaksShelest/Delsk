# Воспроизведение I1

Автор: Шелестов Максим Евгеньевич. Основа: b96c59ca / PR78.
Измерения и границы — [отчёт I1](../../IMPLEMENTATION_REPORT_I1.md),
сохранённые доказательства — [verification/i1](../../verification/i1/README.md).

SDK 10.0.401, `DOTNET_PROCESSOR_COUNT=2`, Python 3, Godot 4.7.2 Mono.
`dotnet` должен быть в PATH; для native нужен Xvfb и программный OpenGL.
Тяжёлые проверки выполнять последовательно. Каждый output должен быть новым:
исходные ошибки и логи сохраняются. Команды запускать из корня репозитория.

```sh
python scripts/run_check.py --name restore --timeout 180 --log-directory work/checks -- dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
python scripts/run_check.py --name build --timeout 180 --log-directory work/checks -- dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
```

Полная регрессия состоит из disjoint suites `previous roads billing services
runs npc ux h2 h3 i1`. Для каждого SUITE выполнить обе команды с отдельным output:

```sh
python scripts/run_check.py --name ordinary-SUITE --timeout 1000 --log-directory work/checks -- python scripts/check_headless.py --phase tests --suite SUITE --output work/ordinary/SUITE
python scripts/run_check.py --name developer-SUITE --timeout 1000 --log-directory work/checks -- python scripts/check_developer.py --suite SUITE --output work/developer/SUITE
```

Внешние 1000 секунд ограничивают orchestration; внутренние прежние границы
120/180 секунд сохранены. Только новые длинные I1 partitions имеют свои
180/240/300 секунд. Проверять TRX: отсутствие failed/skipped и пересечений testId
между suites. Developer дополнительно обязан отклонить сборку Release с exit1.
В опубликованном локальном прогоне использовались внешние 780 секунд для обычных
старых suites, 600 для Developer/previous и 1000 для I1; точные argv в архиве.
Новый CI-шаг I1 Developer также ограничен1000 секундами: локально он занял
836,705s. Новая ordinary I1 часть CI сохраняет780s; прежние шаги не менялись.

Suite I1 создаёт настоящие фикстуры и 72 измерения: персонаж/пять моделей,
четыре покрытия, три сохранённых профиля. Дополнительно содержит midquantum
сохранение для native и пять сохранений машин с грузом посреди маршрута.

```sh
python scripts/run_check.py --name speed-report --timeout 30 --log-directory work/checks -- python scripts/report_i1_speeds.py --input work/ordinary/i1/i1-fixtures/measurements.jsonl --output work/speeds
python scripts/run_check.py --name i1-runner --timeout 600 --log-directory work/checks -- python scripts/check_i1_runner.py --unit work/ordinary/i1 --output work/i1-runner
python scripts/run_check.py --name retained-runner --timeout 780 --log-directory work/checks -- python scripts/check_headless.py --phase runs --unit-artifacts work/ordinary/previous --output work/headless
python scripts/run_check.py --name i1-native --timeout 900 --log-directory work/checks -- python scripts/check_i1_native.py --fixtures work/ordinary/i1/i1-fixtures --output work/native --godot /absolute/path/to/Godot
python scripts/run_check.py --name ci-tools --timeout 30 --log-directory work/checks -- python -m unittest discover -s tests/Checks -v
python scripts/run_check.py --name research-lab --timeout 60 --log-directory work/checks -- python -m unittest discover -s tests/Research.Lab -v
```

Runner проверяет полные load/replay exports и независимые бухгалтерские аудиты;
он не пересчитывает Core в Python. Native копирует исходники в owned stage,
проверяет ordinary AssemblyAudit **до** добавления probe, импортирует ассеты,
проверяет source_md5, выполняет прежний platform smoke и оконный I1 probe.
Четыре PNG просматриваются отдельно: восстановленная сцена, движение, справка
RU и EN. Эти проверки не являются приёмкой физических Windows/Android устройств.

При изменении исходников создавать новые outputs. Не продолжать старые журналы
на другом бинарном коде и не заменять исторические H2/H3 результаты новой когортой.

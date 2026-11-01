# Воспроизведение H2

Автор: Шелестов Максим Евгеньевич. Новая инженерная когорта; H1 не переписывается.
SDK10.0.401, DOTNET_PROCESSOR_COUNT=2, обычная Release сборка; тяжёлые локальные
процессы последовательно. Пределы180s restore/build,120s тестовая часть,
600s обычный runner /900s late runner и64/512MiB сохранений не повышать.

## Исходные миры и длительное продолжение

В GitHub Actions run40954434118 сохранены оригинальные manifests/части восьми
`h1-resume-long-…` bundles. Скачать все части одного case и manifest в общую
папку (в Actions download-artifact применяется merge-multiple). Все имена,
байты и SHA проверяются, исходные runtime/journal не переписываются:

```sh
python scripts/restore_h1_for_h2.py --bundle work/bundle --output work/original
python scripts/run_check.py --name restore --timeout 180 --log-directory work/build-checks -- dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
python scripts/run_check.py --name build --timeout 180 --log-directory work/build-checks -- dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
python scripts/verify_h2_late.py --checkpoint work/original/checkpoints/10800-a1.save.json --runtime src/Research.Runner/bin/Release/net10.0 --output work/h2-result
```

Последний путь брать из trajectory.json:lastCheckpoint, а не всегда10800.
Скрипт фиксирует checkpoint/DLL/helper SHA, копии Python tools и стадии в journal.json. Повтор той же
команды после остановки продолжает успешные стадии, проверяя runtime и helpers; ошибки
сохраняются, для исправленного кода нужен отдельный output. Полный результат:
+1800, load/+30, continuous/+1830,31 точный экспорт, world replay,
3accounting/3public/2full-history audits, lossless gzip полного JSONL.
Исходная матрица — [.github/workflows/h2-late.yml](../../../.github/workflows/h2-late.yml).

## Парные измерения прежних правил

Подготовить отдельную исходную реализацию с одними измерительными callbacks:

```sh
python scripts/prepare_h2_baseline.py --output work/h2-baseline
python scripts/run_check.py --name baseline-restore --timeout 180 --log-directory work/checks -- dotnet restore work/h2-baseline/src/Research.Runner/Research.Runner.csproj --locked-mode --disable-parallel -m:1
python scripts/run_check.py --name baseline-build --timeout 180 --log-directory work/checks -- dotnet build work/h2-baseline/src/Research.Runner/Research.Runner.csproj -c Release --no-restore --disable-build-servers -m:1
python scripts/measure_h2_performance.py --h1-root work/h1-retained-resume --before-runtime work/h2-baseline/src/Research.Runner/bin/Release/net10.0 --after-runtime src/Research.Runner/bin/Release/net10.0 --output work/paired --replicates 3
```

`--h1-root`: папки long-CASE/world/checkpoints, восстановленные из оригинальных
bundles; все8 перечислены в paired-summary.json. Перед измерениями зафиксировать
копии обоих runtime. Скрипт отказывает при подмене SHA в продолженном журнале.
Сравниваются одинаковые saved controllers и старый divisor80; никакого Resolve
новой партии. Протокол: Medium42 Small10800→10900, Large12600→12700, n3 временных
повторов, не новых миров. Все повторы, включая медленный первый Small, сохранены.

`baseline-instrumentation.patch` не содержит оптимизаций/новой скорости: только
host Stopwatch/GC, callbacks фаз/инвариантов, статистику архива и runner entry.
Первичный frozen deep-baseline-runtime был снят до добавления детальных
invariant callbacks; полные Core0–9 уже измерялись. Reconstructed baseline
добавляет эти callbacks; его семантика отдельно проверяется теми же hash/exports.
Битовые SHA первичных DLL и каждого входа приведены в performance/paired-summary.

Для чистой повторной загрузки использовать сохранения из парного run на одном
и том же конечном тике. Три раза запустить с новым output каждую команду:

```sh
python scripts/run_check.py --name warm-before-1 --timeout 600 --log-directory work/checks -- dotnet BEFORE/Research.Runner.dll --measure-session BEFORE.save.json --operation step --output work/warm-before-1
python scripts/run_check.py --name warm-after-1 --timeout 600 --log-directory work/checks -- dotnet AFTER/Research.Runner.dll --measure-session AFTER.save.json --operation step --output work/warm-after-1
```

Заменить BEFORE/AFTER на зафиксированные папки. Параметр ticks отсутствует —
игра не продвигается. Повторить с `--operation export`,900s и отдельными папками,
затем сравнить реальные exports и независимые аудиты:

```sh
python scripts/verify_postg2_continuation.py work/export-before/exports work/export-after/exports
python scripts/verify_results.py work/export-after/exports
python scripts/verify_public_economy.py work/export-after/exports
python scripts/verify_h2_history.py work/export-after/exports
```

`phases.json` содержит интервалы, wall/RSS/allocations и оба хеша; вложенные
Core/invariant, sampling/hash и policy/history нельзя складывать как независимые
времена. Преобразование Session3→4 сравнивается отдельно с повторной загрузкой4.
Экспорт остаётся полным и большим; gzip применяется после byte-equivalence.

## Новое движение и реальные кадры

`python scripts/check_headless.py --suite h2 --phase tests --output work/h2-unit`
проверяет36 ordinary tests с прежним120s пределом каждой части. Дополнительно
действующий check_npc_g1.py на обеих картах/3/6/10NPC делает новую1800-tick
непрерывную игру, checkpoint900/load и replay; это отдельная Movement1 когорта.

RoadSceneTests создают настоящие Core fixtures. Запустить с переменной
DELSK_H2_EVIDENCE=work/road-fixtures; затем в работающем X11:

```sh
python scripts/check_h2_roads.py --godot /path/to/Godot --fixtures work/road-fixtures --output work/native --verify
```

24 viewport PNG, native-check, исходные SHA и ordinary AssemblyAudit сохраняются.
Probe внедряется только в отдельную копию; обычный клиент его не содержит.
Хеши и bounds дополняют ручной просмотр каждого вида/скина.

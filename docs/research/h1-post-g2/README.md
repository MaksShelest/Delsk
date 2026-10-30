# H1 после дополнительной G2: протокол и продолжение

Автор: Шелестов Максим Евгеньевич. Основа 3cc17e8; это отдельная когорта
map-adapter-2. Предварительный план committed507ec3e до основных наблюдений.
Историческая когорта adapter1 остаётся в docs/research/h1.

Реальная C# симуляция OrdinaryNewGame, G1 coherent policies, observer без
экономических команд. Не Python модель экономики. Все обычные партии идут
до 5000, 120 независимых миров; 6NPC n10, 3/10NPC n5 на условие. 12 заранее
объявленных контрастов, парные seed, exact sign/Holm12 и bootstrap медианы.
Цензурирование, отсутствующие результаты и неудачные попытки сохраняются.
Восемь long миров до 126000 — разведочные n2, Medium/Hard,6NPC, обе карты.
Отличается только разрешённый горизонт и соответствующий finalTick; это
отдельная когорта, которую нельзя объединять с ordinary.

Запуск после locked restore/build Research.Runner Release net10.0:

```bash
python scripts/study_postg2_h1.py --matrix ordinary
python scripts/study_postg2_h1.py --case ordinary-small-river-6-Medium-42 --output work/study/ordinary-small-river-6-Medium-42
python scripts/study_postg2_h1.py --case long-small-river-6-Medium-42 --output work/study/long-small-river-6-Medium-42 --segments 8
python scripts/analyze_postg2_h1.py --study work/study --output work/analysis
```

Journal atomically commits только завершённый audited checkpoint. Бинарники,
исходные входы, протокол и helpers проверяются SHA; пути относительные,
OS lock исключает одновременных writers. SIGTERM просит остановку между
ограниченными сегментами; принудительное убийство оставляет попытку interrupted.
После этого нужен явный --retry-failed; предыдущая неудача сохраняется.
Не увеличивать 600/900s,64/512MiB checkpoint и 2MiB входных планов.
Полные checkpoints, runtime и исходные экспорты хранятся отдельно от тонкой
сводки. Decisions сжимаются без потерь и проверяются исходным SHA.

Workflow h1-post-g2 запускается при открытии PR с start.json: независимые
workers, max12 ordinary/max4 long, без изменения производительности Core.
Каждый long worker ограничен восемью сегментами; ready не означает достигнутые
35 ч. Blocked timeout/save — сохранённый результат исследования, а не успешная
партия. Ошибка accounting/privacy оставляет красный job.
Artifacts h1-full-CASE-aN содержат переносимый каталог мира; h1-metrics-CASE-aN
дают компактный журнал/аудиты/метрики; h1-post-g2-summary — общую сводку.
Срок GitHub хранения 90 дней: для долгого продолжения скачать полный artifact
до истечения, распаковать и checkout его sourceCommit. Нельзя подставлять новый
runtime. Повторить worker с тем же case/output; он продолжит только из последней
проверенной границы. Для blocked нужен явный retry. Workflow_dispatch resume_run
использует исходный commit и сохранённые artifacts; доступность dispatch новой
workflow зависит от её присутствия в default branch. Локальное продолжение
работает до merge и не требует dispatch.

Проверка инфраструктуры отдельно: actual30+30 с переносом каталога совпал
с continuous60 по 31 экспорту, включая gzip decisions. Это не экономическая
реплика и анализатор не принимает smoke-case как наблюдение основной когорты.
Свидетельство: verification/economy-h1-post-g2/continuation-smoke.json.

Числа баланса до результатов не меняются. Без длинного наблюдения нет
подтверждённой ROI/Hard superiority/времени II–V. Блокер фиксируется для H2,
не устраняется повышением лимитов и не заменяется расчётной маржой.

## Воспроизвести итоговые сводки без повторной симуляции

```bash
python scripts/analyze_postg2_h1.py --observations docs/verification/economy-h1-post-g2/observations.json.gz --output work/reanalysis
python scripts/render_postg2_h1.py --analysis work/reanalysis --output work/plots
```

Это повтор агрегации фактических измерений, не новые игровые партии.
Format2 gzip сохраняет общий frozen fingerprint один раз; строки всех 128 миров,
сегменты и неудачные попытки остаются. JSON значения восстанавливаются полностью;
форматирование исходного JSON меняется. Manifest фиксирует оба SHA.
Первоначальный main run40921991720/source0f89a63 завершился 120ordinary и 8blocked
long. Поздний сбор durable evidence только копирует/checks/bundles, не запускает
новых игровых тиков и не подменяет исходный runtime.

Контроль прогноза транспорта воспроизводится после нулевого cash diagnostic
(используется его canonical-resolution до вмешательства cash=0):

```bash
python scripts/diagnose_economy_h1.py --output work/zero
python scripts/probe_transport_h1.py --canonical-root work/zero --output work/transport
```

Восемь отдельных соседних MoveTo исполняются настоящим Core. Python проверяет
только входные соседние клетки и читает события; пути/экономика не симулируются.

Полный resume bundle содержит последнюю проверенную save, original runtime,
inputs, journal и компактные прежние результаты/аудиты. Части 24MiB имеют отдельные
SHA, manifest содержит общий SHA ZIP и SHA каждого файла. Сначала скачать все
части и manifest, проверить SHA частей, соединить в указанном порядке в resume.zip,
проверить archiveSha256, распаковать в новый owned каталог и проверить files[*].sha256.
Вернуться к sourceCommit; worker проверит собственные frozen hashes и последний
checkpoint. --segments0 проверяет journal/входы/бинарники без игровых тиков;
для фактического продолжения blocked нужен --retry-failed. Известный save/time
блокер сам собой при повторе не исчезает. H2 потребует отдельной версии бинарника
и объявленной когорты, а не скрытой подмены в этом journal.

Полные прежние raw exports, включая failed output, остаются в исходных artifacts
h1-full-CASE-a1 до 27.01.2027 17:30:34 UTC. Bundle сохраняет состояние продолжения,
не заменяет архив всех исторических raw traces. Срок для durable evidence artifacts
также 90 дней; локально скачанные bundles переживают этот срок хранения GitHub.

Автоматическое восстановление после распаковки artifact ZIP с частями/manifest
в один каталог (исходный sourceCommit, .NET нужен только для дальнейших тиков):

```bash
python scripts/restore_postg2_resume.py --bundle work/bundle --output work/restored
python scripts/study_postg2_h1.py --case long-small-river-6-Medium-43 --output work/restored --segments 0
```

Restore проверяет SHA частей/ZIP/всех файлов, пути, исходные frozen inputs/DLL/helpers
и оставляет запись restoration.json; новых тиков/deserialize не делает. Реальный
late load этих же байтов уже выполнен исходными следующими сегментами; здесь
проверяется перенос. После явного --retry-failed запускается новое продолжение,
которое сохраняет прежнюю ошибку и соблюдает исходный бюджет.

Повторный сбор audit metadata проверяется verify_postg2_collection.py:128 миров,
411 исходных пар passing audits и неизменные измерения. Первый pattern download
дал 96 миров и был отклонён;32 недостающих artifacts получены по явным IDs.
Collection v2 использует полный пагинированный список/явные IDs и обязательную
сверку с исходной сводкой. Этот вторичный partial не считается новой когортой.

Для построения SVG требуется matplotlib3.10.8; анализ/worker/restore используют
стандартную библиотеку Python. Все полные научные выводы и открытые вопросы —
в IMPLEMENTATION_REPORT_H1_POST_G2.md; P77 не объявлен закрытым.

# H1 после дополнительной G2: протокол и продолжение

Автор: Шелестов Максим Евгеньевич. Основа 3cc17e8; это отдельная когорта
map-adapter-2. Предварительный план committed507ec3e до основных наблюдений.
Историческая когорта adapter1 остаётся в docs/research/h1.

Реальная C# симуляция OrdinaryNewGame, G1 coherent policies, observer без
экономических команд. Не Python модель экономики. Все обычные партии идут
до5000, 120 независимых миров; 6NPC n10, 3/10NPC n5 на условие. 12 заранее
объявленных контрастов, парные seed, exact sign/Holm12 и bootstrap медианы.
Цензурирование, отсутствующие результаты и неудачные попытки сохраняются.
Восемь long миров до126000 — разведочные n2, Medium/Hard,6NPC, обе карты.
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
Не увеличивать600/900s,64/512MiB checkpoint и2MiB входных планов.
Полные checkpoints, runtime и исходные экспорты хранятся отдельно от тонкой
сводки. Decisions сжимаются без потерь и проверяются исходным SHA.

Workflow h1-post-g2 запускается при открытии PR с start.json: независимые
workers, max12 ordinary/max4 long, без изменения производительности Core.
Каждый long worker ограничен восемью сегментами; ready не означает достигнутые
35ч. Blocked timeout/save — сохранённый результат исследования, а не успешная
партия. Ошибка accounting/privacy оставляет красный job.
Artifacts h1-full-CASE-aN содержат переносимый каталог мира; h1-metrics-CASE-aN
дают компактный журнал/аудиты/метрики; h1-post-g2-summary — общую сводку.
Срок GitHub хранения90дней: для долгого продолжения скачать полный artifact
до истечения, распаковать и checkout его sourceCommit. Нельзя подставлять новый
runtime. Повторить worker с тем же case/output; он продолжит только из последней
проверенной границы. Для blocked нужен явный retry. Workflow_dispatch resume_run
использует исходный commit и сохранённые artifacts; доступность dispatch новой
workflow зависит от её присутствия в default branch. Локальное продолжение
работает до merge и не требует dispatch.

Проверка инфраструктуры отдельно: actual30+30 с переносом каталога совпал
с continuous60 по31 экспорту, включая gzip decisions. Это не экономическая
реплика и анализатор не принимает smoke-case как наблюдение основной когорты.
Свидетельство: verification/economy-h1-post-g2/continuation-smoke.json.

Числа баланса до результатов не меняются. Без длинного наблюдения нет
подтверждённой ROI/Hard superiority/времени II–V. Блокер фиксируется для H2,
не устраняется повышением лимитов и не заменяется расчётной маржой.

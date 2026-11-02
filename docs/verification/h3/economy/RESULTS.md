# H3: фактические экономические результаты

Автор: Шелестов Максим Евгеньевич. Сформировано `scripts/report_h3_outcomes.py`.

Собрано **192/192** исходных миров; отсутствует 0. Обычных конечных точек t5000: **180/180**.

Это отдельная Movement1 когорта. Независимая единица — мир/seed. NPC одного мира коррелированы; продолжения, re-audit и checkpoint re-export не добавляют реплик. Обычные и extended результаты не объединяются. Тик равен секунде активной симуляции. First factory означает построенный производственный корпус, не первый проданный товар и не окупаемость.

## Обычные партии: t5000

Число предприятий и цензурированные NPC ниже относятся только к полным конечным точкам. Медиана времени рассчитана среди наблюдавшихся построек, пропуски не заменены нулём. Изменение капитала — медиана по мирам, внутри мира используется медиана NPC; денежные значения в монетах.

| Карта | NPC | Сложность | Полных миров / план | Первых корпусов / NPC | Цензурировано | Медиана tick среди построивших | Медиана Δкапитала |
|---|---:|---|---:|---:|---:|---:|---:|
| compact-valley | 3 | Easy | 5/5 | 15/15 | 0 | 2603 | 652.2 |
| compact-valley | 3 | Medium | 5/5 | 15/15 | 0 | 2603 | 613.09 |
| compact-valley | 3 | Hard | 5/5 | 15/15 | 0 | 3693 | 767.3 |
| compact-valley | 6 | Easy | 10/10 | 41/60 | 19 | 3169 | 667.63 |
| compact-valley | 6 | Medium | 10/10 | 42/60 | 18 | 3183 | 693.92 |
| compact-valley | 6 | Hard | 10/10 | 33/60 | 27 | 4055 | 688.26 |
| compact-valley | 10 | Easy | 5/5 | 25/50 | 25 | 3226 | 673.46 |
| compact-valley | 10 | Medium | 5/5 | 28/50 | 22 | 3697.5 | 681.7 |
| compact-valley | 10 | Hard | 5/5 | 19/50 | 31 | 4058 | 615.03 |
| small-river | 3 | Easy | 5/5 | 10/15 | 5 | 3722.5 | 526.7 |
| small-river | 3 | Medium | 5/5 | 10/15 | 5 | 3722.5 | 539.21 |
| small-river | 3 | Hard | 5/5 | 0/15 | 15 | — | 481.09 |
| small-river | 6 | Easy | 10/10 | 49/60 | 11 | 4017 | 546.71 |
| small-river | 6 | Medium | 10/10 | 50/60 | 10 | 4035 | 544.3 |
| small-river | 6 | Hard | 10/10 | 2/60 | 58 | 4894 | 498.44 |
| small-river | 10 | Easy | 5/5 | 21/50 | 29 | 3779 | 501.61 |
| small-river | 10 | Medium | 5/5 | 21/50 | 29 | 3784 | 472.15 |
| small-river | 10 | Hard | 5/5 | 7/50 | 43 | 4652 | 490.72 |
| large-districts | 3 | Easy | 5/5 | 1/15 | 14 | 4878 | 292.38 |
| large-districts | 3 | Medium | 5/5 | 1/15 | 14 | 4878 | 292.38 |
| large-districts | 3 | Hard | 5/5 | 0/15 | 15 | — | 292.38 |
| large-districts | 6 | Easy | 10/10 | 7/60 | 53 | 4685 | 351.7 |
| large-districts | 6 | Medium | 10/10 | 7/60 | 53 | 4685 | 351.7 |
| large-districts | 6 | Hard | 10/10 | 0/60 | 60 | — | 374.28 |
| large-districts | 10 | Easy | 5/5 | 28/50 | 22 | 4790 | 427.06 |
| large-districts | 10 | Medium | 5/5 | 28/50 | 22 | 4790 | 427.06 |
| large-districts | 10 | Hard | 5/5 | 0/50 | 50 | — | 394.22 |

## Заранее объявленные парные сравнения

Hard−Medium и Easy−Medium, один seed на пару. Δ — разность медиан NPC capital change в монетах. Bootstrap95% и двусторонний sign test взяты из исходного H1 анализа; Holm применяется ко всем18 сравнениям. n10 при6NPC — primary, n5 при3/10NPC — exploratory. Это исход ранней экспозиции, не доказательство зрелого промышленного преимущества.

| Карта | NPC | Сравнение | Полных пар / план | Медиана Δ | Bootstrap95% Δ | Holm p |
|---|---:|---|---:|---:|---|---:|
| compact-valley | 3 | Hard−Medium | 5/5 | 114.66 | 33.88…220.86 | 1 |
| compact-valley | 3 | Easy−Medium | 5/5 | 0 | -54.47…54.32 | 1 |
| compact-valley | 6 | Hard−Medium | 10/10 | 12.29 | -63.13…54.17 | 1 |
| compact-valley | 6 | Easy−Medium | 10/10 | -3.2 | -23.2…19.67 | 1 |
| compact-valley | 10 | Hard−Medium | 5/5 | -66.67 | -88.36…30.95 | 1 |
| compact-valley | 10 | Easy−Medium | 5/5 | 0.65 | -39.33…25.29 | 1 |
| small-river | 3 | Hard−Medium | 5/5 | -39.62 | -76.31…-23.43 | 1 |
| small-river | 3 | Easy−Medium | 5/5 | -0.07 | -12.69…1.03 | 1 |
| small-river | 6 | Hard−Medium | 10/10 | -46.37 | -73.6…-25 | 0.0351562 |
| small-river | 6 | Easy−Medium | 10/10 | 0 | -1.56…4.09 | 1 |
| small-river | 10 | Hard−Medium | 5/5 | -0.18 | -33.93…37.66 | 1 |
| small-river | 10 | Easy−Medium | 5/5 | 0 | -16.96…39.02 | 1 |
| large-districts | 3 | Hard−Medium | 5/5 | 0 | 0…0 | 1 |
| large-districts | 3 | Easy−Medium | 5/5 | 0 | 0…0 | 1 |
| large-districts | 6 | Hard−Medium | 10/10 | 0 | 0…9.4 | 1 |
| large-districts | 6 | Easy−Medium | 10/10 | 0 | 0…0 | 1 |
| large-districts | 10 | Hard−Medium | 5/5 | -14.32 | -63.32…-8.27 | 1 |
| large-districts | 10 | Easy−Medium | 5/5 | 0 | 0…6.25 | 1 |

## Длинные партии: последние принятые точки

Цель —126000, а не конец bounded batch. Показаны только прошедшие оба аудита checkpoints. Неуспешные/незавершённые попытки сохраняются в journals и observations. Статус ready означает необходимость следующего сегмента.

| Исходный мир | Статус | Принятый tick | Корпусов /6 | Физические II/III/IV/V | XP production / quest | Транспорт / дороги / service orders |
|---|---|---:|---:|---|---:|---|
| extended-compact-valley-6-Hard-142 | ready | 16200 | 4/6 | 0/0/0/0 | 759/0 | 0/0/0 |
| extended-compact-valley-6-Hard-143 | ready | 16200 | 6/6 | 0/0/0/0 | 1000/0 | 0/0/0 |
| extended-compact-valley-6-Medium-142 | ready | 16200 | 4/6 | 0/0/0/0 | 663/0 | 0/0/0 |
| extended-compact-valley-6-Medium-143 | ready | 16200 | 6/6 | 0/0/0/0 | 1043/0 | 0/0/0 |
| extended-large-districts-6-Hard-142 | ready | 16200 | 1/6 | 0/0/0/0 | 56/0 | 0/0/0 |
| extended-large-districts-6-Hard-143 | ready | 12600 | 4/6 | 0/0/0/0 | 80/0 | 0/0/0 |
| extended-large-districts-6-Medium-142 | ready | 16200 | 6/6 | 0/0/0/0 | 205/0 | 0/0/0 |
| extended-large-districts-6-Medium-143 | ready | 7200 | 6/6 | 0/0/0/0 | 51/0 | 0/0/0 |
| extended-small-river-6-Hard-142 | ready | 16200 | 6/6 | 0/0/0/0 | 708/0 | 0/0/0 |
| extended-small-river-6-Hard-143 | ready | 16200 | 6/6 | 0/0/0/0 | 946/0 | 0/0/0 |
| extended-small-river-6-Medium-142 | ready | 16200 | 6/6 | 0/0/0/0 | 681/0 | 0/0/0 |
| extended-small-river-6-Medium-143 | ready | 5400 | 5/6 | 0/0/0/0 | 127/0 | 0/0/0 |

## Реальные денежные потоки и ограничения ROI

Ниже — медиана **мировых сумм NPC** последних принятых extended checkpoints каждой карты. Разная длительность и censoring запрещают трактовать их как равный сравнительный горизонт. Goods включает импорт; импорт указан также отдельной подкатегорией и второй раз не прибавляется. Operating receipts исключают cash capex; они не означают прибыль/окупаемость конкретного предприятия.

| Карта | Миров | Сбыт, монеты | Goods / импорт | Зарплаты | Cash capex | Operating после road debt |
|---|---:|---:|---:|---:|---:|---:|
| compact-valley | 4 | 12980.56 | 1342.25 / 848.75 | 4330.36 | 3165 | 7486.56 |
| small-river | 4 | 7433.57 | 1204.75 / 748.75 | 2924.8 | 2830 | 2940.27 |
| large-districts | 4 | 2914.91 | 700.5 / 625 | 252 | 1880 | 1964.17 |

## Задания и технологические рубежи

В последних принятых точках ordinary: заданий принято 0, завершено 0; максимальный XP одной отрасли одного NPC —108, порогII —600. Каждая партия учтена здесь один раз; длительности различаются.

В последних принятых точках extended: заданий принято 0, завершено 0; максимальный XP одной отрасли одного NPC —375, порогII —600. Каждая партия учтена здесь один раз; длительности различаются.


Полные snapshots содержат imports/pickups/физические остатки, rejection reasons, worker-ready samples, production batches, фактические receipts и trace/ledger reconciliation. Worker/idle samples внутри мира зависимы. Наблюдаемая атрибутируемая окупаемость не подменяется статической маржой или экстраполяцией будущих продаж. Если paybackTick отсутствует, результат остаётся цензурированным. Prepared delivery/road fixtures и scripted human отсутствуют в этой независимой когорте.

Источник: [observations.json.gz](observations.json.gz). Исходные артефакты, frozen fingerprints и audit errata находятся рядом в [source-artifacts](source-artifacts.json) и [collection-verification](collection-verification.json).

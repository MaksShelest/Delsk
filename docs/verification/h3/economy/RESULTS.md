# H3: фактические экономические результаты

Автор: Шелестов Максим Евгеньевич. Сформировано `scripts/report_h3_outcomes.py`.

Собрано **192/192** исходных миров; отсутствует 0. Обычных конечных точек t5000: **144/180**.

Это отдельная Movement1 когорта. Независимая единица — мир/seed. NPC одного мира коррелированы; продолжения, re-audit и checkpoint re-export не добавляют реплик. Обычные и extended результаты не объединяются. Тик равен секунде активной симуляции. First factory означает построенный производственный корпус, не первый проданный товар и не окупаемость.

**Предварительный срез:** часть ordinary endpoints неполна. Complete-case сравнения могут быть смещены отказами аудитора при развитии/стройке. Эти p-values не являются окончательным выводом о сложности.

## Обычные партии: t5000

Число предприятий и цензурированные NPC ниже относятся только к полным конечным точкам. Медиана времени рассчитана среди наблюдавшихся построек, пропуски не заменены нулём. Изменение капитала — медиана по мирам, внутри мира используется медиана NPC; денежные значения в монетах.

| Карта | NPC | Сложность | Полных миров / план | Первых корпусов / NPC | Цензурировано | Медиана tick среди построивших | Медиана Δкапитала |
|---|---:|---|---:|---:|---:|---:|---:|
| compact-valley | 3 | Easy | 5/5 | 15/15 | 0 | 2603 | 652.2 |
| compact-valley | 3 | Medium | 5/5 | 15/15 | 0 | 2603 | 613.09 |
| compact-valley | 3 | Hard | 5/5 | 15/15 | 0 | 3693 | 767.3 |
| compact-valley | 6 | Easy | 7/10 | 28/42 | 14 | 2943 | 711.04 |
| compact-valley | 6 | Medium | 6/10 | 24/36 | 12 | 2943 | 693.92 |
| compact-valley | 6 | Hard | 8/10 | 26/48 | 22 | 4052 | 677.84 |
| compact-valley | 10 | Easy | 4/5 | 22/40 | 18 | 3526 | 669.26 |
| compact-valley | 10 | Medium | 4/5 | 23/40 | 17 | 3732 | 687.47 |
| compact-valley | 10 | Hard | 4/5 | 16/40 | 24 | 4094 | 609.91 |
| small-river | 3 | Easy | 5/5 | 10/15 | 5 | 3722.5 | 526.7 |
| small-river | 3 | Medium | 5/5 | 10/15 | 5 | 3722.5 | 539.21 |
| small-river | 3 | Hard | 5/5 | 0/15 | 15 | — | 481.09 |
| small-river | 6 | Easy | 3/10 | 15/18 | 3 | 4053 | 547.38 |
| small-river | 6 | Medium | 5/10 | 26/30 | 4 | 4035 | 549.34 |
| small-river | 6 | Hard | 9/10 | 2/54 | 52 | 4894 | 494.75 |
| small-river | 10 | Easy | 5/5 | 21/50 | 29 | 3779 | 501.61 |
| small-river | 10 | Medium | 5/5 | 21/50 | 29 | 3784 | 472.15 |
| small-river | 10 | Hard | 4/5 | 5/40 | 35 | 4652 | 476.56 |
| large-districts | 3 | Easy | 5/5 | 1/15 | 14 | 4878 | 292.38 |
| large-districts | 3 | Medium | 5/5 | 1/15 | 14 | 4878 | 292.38 |
| large-districts | 3 | Hard | 5/5 | 0/15 | 15 | — | 292.38 |
| large-districts | 6 | Easy | 6/10 | 2/36 | 34 | 4593.5 | 312.21 |
| large-districts | 6 | Medium | 6/10 | 2/36 | 34 | 4593.5 | 312.21 |
| large-districts | 6 | Hard | 10/10 | 0/60 | 60 | — | 374.28 |
| large-districts | 10 | Easy | 4/5 | 22/40 | 18 | 4790 | 425.59 |
| large-districts | 10 | Medium | 4/5 | 22/40 | 18 | 4790 | 422.47 |
| large-districts | 10 | Hard | 5/5 | 0/50 | 50 | — | 394.22 |

## Заранее объявленные парные сравнения

Hard−Medium и Easy−Medium, один seed на пару. Δ — разность медиан NPC capital change в монетах. Bootstrap95% и двусторонний sign test взяты из исходного H1 анализа; Holm применяется ко всем18 сравнениям. n10 при6NPC — primary, n5 при3/10NPC — exploratory. Это исход ранней экспозиции, не доказательство зрелого промышленного преимущества.

| Карта | NPC | Сравнение | Полных пар / план | Медиана Δ | Bootstrap95% Δ | Holm p |
|---|---:|---|---:|---:|---|---:|
| compact-valley | 3 | Hard−Medium | 5/5 | 114.66 | 33.88…220.86 | 1 |
| compact-valley | 3 | Easy−Medium | 5/5 | 0 | -54.47…54.32 | 1 |
| compact-valley | 6 | Hard−Medium | 5/10 | 45.81 | -5.7…70.53 | 1 |
| compact-valley | 6 | Easy−Medium | 6/10 | 5.96 | -5.67…19.78 | 1 |
| compact-valley | 10 | Hard−Medium | 3/5 | -53.34 | -76.86…30.95 | 1 |
| compact-valley | 10 | Easy−Medium | 4/5 | -13.76 | -39.33…25.29 | 1 |
| small-river | 3 | Hard−Medium | 5/5 | -39.62 | -76.31…-23.43 | 1 |
| small-river | 3 | Easy−Medium | 5/5 | -0.07 | -12.69…1.03 | 1 |
| small-river | 6 | Hard−Medium | 4/10 | -40.01 | -98.15…-11 | 1 |
| small-river | 6 | Easy−Medium | 3/10 | 0 | 0…14.46 | 1 |
| small-river | 10 | Hard−Medium | 4/5 | 8.23 | -33.93…37.66 | 1 |
| small-river | 10 | Easy−Medium | 5/5 | 0 | -16.96…39.02 | 1 |
| large-districts | 3 | Hard−Medium | 5/5 | 0 | 0…0 | 1 |
| large-districts | 3 | Easy−Medium | 5/5 | 0 | 0…0 | 1 |
| large-districts | 6 | Hard−Medium | 6/10 | 0 | 0…16.93 | 1 |
| large-districts | 6 | Easy−Medium | 6/10 | 0 | 0…0 | 1 |
| large-districts | 10 | Hard−Medium | 4/5 | -13.41 | -63.32…-8.27 | 1 |
| large-districts | 10 | Easy−Medium | 4/5 | 0 | 0…6.25 | 1 |

## Длинные партии: последние принятые точки

Цель —126000, а не конец bounded batch. Показаны только прошедшие оба аудита checkpoints. Неуспешные/незавершённые попытки сохраняются в journals и observations. Статус ready означает необходимость следующего сегмента.

| Исходный мир | Статус | Принятый tick | Корпусов /6 | Физические II/III/IV/V | XP production / quest | Транспорт / дороги / service orders |
|---|---|---:|---:|---|---:|---|
| extended-compact-valley-6-Hard-142 | ready | 14400 | 4/6 | 0/0/0/0 | 703/0 | 0/0/0 |
| extended-compact-valley-6-Hard-143 | ready | 14400 | 6/6 | 0/0/0/0 | 759/0 | 0/0/0 |
| extended-compact-valley-6-Medium-142 | ready | 14400 | 4/6 | 0/0/0/0 | 587/0 | 0/0/0 |
| extended-compact-valley-6-Medium-143 | ready | 14400 | 6/6 | 0/0/0/0 | 924/0 | 0/0/0 |
| extended-large-districts-6-Hard-142 | ready | 14400 | 1/6 | 0/0/0/0 | 44/0 | 0/0/0 |
| extended-large-districts-6-Hard-143 | running | 10800 | 4/6 | 0/0/0/0 | 50/0 | 0/0/0 |
| extended-large-districts-6-Medium-142 | ready | 14400 | 6/6 | 0/0/0/0 | 182/0 | 0/0/0 |
| extended-large-districts-6-Medium-143 | blocked_audit | 3600 | 0/6 | 0/0/0/0 | 0/0 | 0/0/0 |
| extended-small-river-6-Hard-142 | ready | 14400 | 6/6 | 0/0/0/0 | 648/0 | 0/0/0 |
| extended-small-river-6-Hard-143 | ready | 14400 | 6/6 | 0/0/0/0 | 745/0 | 0/0/0 |
| extended-small-river-6-Medium-142 | ready | 14400 | 6/6 | 0/0/0/0 | 645/0 | 0/0/0 |
| extended-small-river-6-Medium-143 | blocked_audit | 1800 | 0/6 | 0/0/0/0 | 0/0 | 0/0/0 |

## Реальные денежные потоки и ограничения ROI

Ниже — медиана **мировых сумм NPC** последних принятых extended checkpoints каждой карты. Разная длительность и censoring запрещают трактовать их как равный сравнительный горизонт. Goods включает импорт; импорт указан также отдельной подкатегорией и второй раз не прибавляется. Operating receipts исключают cash capex; они не означают прибыль/окупаемость конкретного предприятия.

| Карта | Миров | Сбыт, монеты | Goods / импорт | Зарплаты | Cash capex | Operating после road debt |
|---|---:|---:|---:|---:|---:|---:|
| compact-valley | 4 | 11532.88 | 1242.25 / 841.25 | 3631.13 | 3165 | 6833.97 |
| small-river | 4 | 6369.5 | 1126.75 / 738.75 | 2696.8 | 2680 | 2489.04 |
| large-districts | 4 | 2591.27 | 667.5 / 625 | 169.2 | 1535 | 1750.81 |

## Задания и технологические рубежи

В последних принятых точках ordinary: заданий принято 0, завершено 0; максимальный XP одной отрасли одного NPC —108, порогII —600. Каждая партия учтена здесь один раз; длительности различаются.

В последних принятых точках extended: заданий принято 0, завершено 0; максимальный XP одной отрасли одного NPC —321, порогII —600. Каждая партия учтена здесь один раз; длительности различаются.


Полные snapshots содержат imports/pickups/физические остатки, rejection reasons, worker-ready samples, production batches, фактические receipts и trace/ledger reconciliation. Worker/idle samples внутри мира зависимы. Наблюдаемая атрибутируемая окупаемость не подменяется статической маржой или экстраполяцией будущих продаж. Если paybackTick отсутствует, результат остаётся цензурированным. Prepared delivery/road fixtures и scripted human отсутствуют в этой независимой когорте.

Источник: [observations.json.gz](observations.json.gz). Исходные артефакты, frozen fingerprints и audit errata находятся рядом в [source-artifacts](source-artifacts.json) и [collection-verification](collection-verification.json).

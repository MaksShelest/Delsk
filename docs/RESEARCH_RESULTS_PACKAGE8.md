# P59 — измеренный Linux-пилот пакета8

Автор: Шелестов Максим Евгеньевич.20.10.2026, Europe/Samara.

Симуляция: настоящий Research.Runner, World18/production-recipes-1. Входы — docs/research/package8-{difficulties,negotiations,recovery,long-history}.json; исходный профиль не изменён. Каждый arm проходит C# preflight, каждый run независимые verify_results/verify_public_economy. Серии выполнены после a5c2cfe с рабочими изменениями profiling, затем зафиксированными13a24c6; runner manifest содержит базовый commit, а не фиктивный будущий commit. Метаданные Stage исправлены после этих замеров, без изменения World/hash. Точные input/export SHA, команды, seed, полный набор метрик и разброс — [research-study.json](verification/package8/research-study.json). Это ограниченный исследовательский пилот, не доказательство универсального баланса.

## Экономика и механизм

| Серия | Объём | Фактический результат |
|---|---|---|
| Easy/Medium/Hard | seeds42/43/44 ×3 сложности ×1200тиков =9 | Hard−Medium capital: **−91938/−48677/0**, среднее−46871,67, диапазон[−91938;0]. Hard gross finished output=0/0/0; Medium=67000/0/0 по фиксированным ценам. Никакого доказательства преимущества Hard |
| Переговоры on/off | те же3seed ×2 ×1200 =6 | Только NegotiationsEnabled отличается; trade/politics/future contracts сохраняются. Капитал попарно совпал, переговоров0 в обеих ветвях: **механизм не активирован этим пилотом**, эффективности общения он не измеряет. Активные accept/counter/direct-support paths проверены unit/integration/основными сценариями |
| Старт без денег | seeds42/43 ×1200 =2, StartingCash=0/CashReserve=0 | Все3NPC восстановились без лавочек. Seed42 остатки41600/34523/35828; seed43:11377/33733/40945. У человека0 — политика человека не запускалась. Во втором случае построено производство, но не shop |
| Длинная история | seed42/Medium ×5000 =1 | Checkpoint world/controller hash совпал после прямого decode/restore; результат ниже |

Нулевой выпуск не блокирует bootstrap; внешний бюджет и цена/фиксированный G проверяются прежними отдельными инвариантами, не бесконечным притоком к каждому tick. Ниже maximum wages этого короткого ordinary-пилота22; это отсутствие runaway в **этой выборке**, не общая гарантия. Финансовые/товарные/численные кадровые балансы сошлись во всех18run.

Выгружаются capital/cash/занятость/ставки/payroll, idle worker-ticks, finished output по base price, harvested units, completed trips, late pickup/delivery, выборы, запросы/fallback/latency. Отсутствующая latency при отключённой модели — null, не0. Операционный денежный результат = фактические поступления от продаж минус фактические закупки/оплата труда/энергия/аренда/штрафы. Escrow/funding/refund не являются доходом; это **не бухгалтерская чистая прибыль**: capex/непроданный stock/износ/альтернативная стоимость отдельно. Capital change профилируется по фиксированной публичной конвенции. Определения сохраняются в metadata.

Все новые фабрики производят в prepared food-chain/baseline-food сценариях полного gate, но это не доказательство окупаемости обычной партии. Аренда/покупка/expiry и отсутствие двойной выдачи проходят сценарии; долговременный ROI/сравнение всех цепочек и отсутствие доминирующей стратегии **не закрыты**. Измеренные нули выпуска/Hard-loss требуют следующего отдельного balance/goal-ranking исследования. Рабочий профиль не перенастраивался на малой выборке ради желаемого победителя.

## Производительность

На1200 тиках mean hosted Step5,08–6,02мс в difficulty-пилоте; включает JIT/контроллеры/Core/invariants/sampling/hash. Это не замер пользовательского устройства.

На5000 тиках:

- Mean11,804мс, p95 27,616мс; controller2,610/Core2,689/sampling+hash6,503мс в среднем.
- CPU57,144с за цикл измерения; суммарные аллокации20,506ГБ (не retained RAM).
- Save58 602 941байт, encode5,294с, decode+controller restore15,688с. Это память/сериализация, **не дисковый I/O benchmark**.
- Working set после восстановления второй сессии3 940 921 344байт; peak4 948 418 560. Не выдавать это за RAM одного мира на телефоне. Размер близок к лимиту64MiB контейнера; дальнейший рост истории остаётся существенным риском.

Новый optional profiling callback живёт только в Hosting, не сохраняется/не меняет hash/PRNG/правила. Профилирование разделило причину: больше половины hosted Step занимают inspection/sampling/hash, сохраняющие растущую историю. На этом этапе история не удалялась и схема hash/save не менялась. Нужна отдельная работа с индексами/представлением истории/измерением I/O, а не ограничение FPS или косметическое сглаживание.

Изолированный оконный Godot/Xvfb/software gallery: первоначальная геометрия52,844мс;100 неизменившихся Show-вызовов без rebuild, CPU0,0492мс/вызов; изменение одного stock-root2,513мс;60 фактически отрисованных кадров mean29,482/p95 39,215мс. Working set303 087 616байт. Это подготовленная визуальная сцена без NPC-экономики; не аппаратные60FPS/Windows/Android. GPU timers отсутствуют (null). Тестовый класс проверяется как отсутствующий в ordinary DLL/PCK.

## Воспроизведение

После bounded restore/build, из корня:

```sh
python3 scripts/run_check.py --name difficulties --timeout 420 -- python3 scripts/research_lab.py --output artifacts/difficulties --plan docs/research/package8-difficulties.json --run
python3 scripts/run_check.py --name negotiations --timeout 300 -- python3 scripts/research_lab.py --output artifacts/negotiations --plan docs/research/package8-negotiations.json --run
python3 scripts/run_check.py --name recovery --timeout 180 -- python3 scripts/research_lab.py --output artifacts/recovery --plan docs/research/package8-recovery.json --run
python3 scripts/run_check.py --name long --timeout 420 -- python3 scripts/research_lab.py --output artifacts/long --plan docs/research/package8-long-history.json --run
python3 scripts/run_check.py --name verify --timeout 150 -- python3 scripts/summarize_lab.py --lab artifacts/difficulties --lab artifacts/negotiations --lab artifacts/recovery --lab artifacts/long --output artifacts/verified
```

Хранить достаточную RAM/диск для полных исходных exports. Не запускать несколько тяжёлых профилей одновременно при сравнении CPU. В облачной серии long run частично пересёкся с окончанием коротких пилотов; это дополнительный предел сопоставимости timings, не финансовых/детерминированных результатов. Repetitions1,3 независимых seeds для основных пар: статистическая мощность мала. Реальная модель в текущей среде отсутствует (fresh localhost11434 connection refused); модельные/аппаратные выводы не делаются. **P59 полностью не закрыт**: отчёт честно фиксирует отрицательные и открытые результаты.

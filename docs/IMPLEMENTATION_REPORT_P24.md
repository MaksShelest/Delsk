# P24: парные исследовательские серии

Автор: Шелестов Максим Евгеньевич. Дата: 16.10.2026.

База — `23333f223352c46bcf8d5f5894b6c01be1cc7596`, ветка `feat/p24-research-series`. Поле commit в выгрузках равно этому HEAD: изменения P24 ещё не зафиксированы. Игровые World12/Rules baseline-1/Session2/Envelope1, сроки торговли и allowlist наблюдения не менялись. Статистика первых 10 seeds и UI не входят в этот этап. Компактные факты — [verification/p24/gate.json](verification/p24/gate.json). Полные прогоны лежат в `artifacts/p24-series` и в git не входят.

## Протокол

`scenarios/research-series.json`, `paired-one-factor-v1`. Один мир, одно семя и один горизонт внутри сравнения. Коэффициенты добычи и производства остаются ×1. Бюджет запросов общий для живых hybrid-плеч: интервал 30, apply 15, TTL 15, не больше 4 запросов за 120 тиков. Конфиг модели — прежний `scenarios/local-model.example.json`, SHA-256 `db343e0e579127d48f9d8e2fd41b13efc6ddb7f8ce8261b6e6b7729b716282f3`.

Три режима:

- `baseline` — алгоритмические контроллеры, модель выключена;
- `hybrid` — тот же мир и та же модель, `politicsEnabled=false`, поэтому в варианты плана не входят структурированные переговоры;
- `hybrid-communication` — та же модель и те же настройки, `politicsEnabled=true`. Это единственное отличие от `hybrid`: модели предлагаются существующие `BuySupport` и `SignSupport`. Свободный текст по-прежнему не исполняет договор.

Сравнения не смешивают timing-policy. Headless-равенство модели и baseline живёт отдельно от paced. Replay помечается `recorded-headless` и не входит в живую пару. Алгоритмический baseline не называется budget-matched: у него нет очереди модели.

## Что реально прогнано

Ollama уже слушал `http://127.0.0.1:11434`. Повторно модель не скачивалась. Измеренный тег `qwen3.5:4b`, digest `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04`, family `qwen35`, `4.2B`, `Q4_K_M`, Apache-2.0. Размер файла `3324173934` байт. После прогрева `/api/ps` дал `size_vram=3140495604`. RAM процесса игры не измерялась и записана как null. Прогрев `/api/chat` с `think:false` занял 5.17 с, `done_reason=stop`, content не пустой; это не игровое решение.

| Сравнение | Режим | Seed | Тики | Timing | State hash | Модель |
| --- | --- | --- | --- | --- | --- | --- |
| disabled-model-economy | baseline и hybrid-off | 42 | 120 | headless | `233e28957308aa59a28a972bcdae9fe649eaebb3e296035335be157ec6c35a4f` | выключена, миры совпали |
| disabled-model-economy | baseline и hybrid-off | 43 | 120 | headless | `342ca6cf0b1872bc2bce284185b332de1c08beb759ddc092080eac7ae0ccb50c` | выключена, миры совпали |
| disabled-model-politics | baseline и hybrid-off | 42 | 120 | headless | `1638800952f31454bb41e7d0e9d817e08a6ebbb0602797e1cc200b6b9205268b` | выключена, миры совпали; 8 предложений и 3 подписи |
| live-modes | baseline | 42 | 120 | paced | тот же `1638800952f3…` | алгоритмический pacer не изменил мир относительно headless |
| live-modes | hybrid | 42 | 120 | Realtime | `514aa7644498369be29656746b5c18fb8c4ff256830e23b61b3a7439c0511a51` | 3 запроса, 3 ModelResolved, 0 fallback |
| live-modes | hybrid-communication | 42 | 120 | Realtime | `970d55a2784ca78178f3bce0281937eedb37bf73d0039c9cae06cc0edf3dd49a` | 6 запросов, 6 ModelResolved, 0 fallback |

Живой `hybrid` на тике 0 предложил всем трём NPC `AdjustWage` и `Hold`. На apply tick 14 участники 2 и 4 выбрали `AdjustWage`; Core принял `SetWage` `s1:2:1` и `s1:4:1`. Участник 3 выбрал `Hold`. Поддержек нет: politics выключена. Latency 1264–1660 мс, p50 1495, p95 1660. Prompt tokens 833–835, completion 55–84.

Живой `hybrid-communication` на тике 0 предложил `BuySupport`, `AdjustWage` и `Hold`; все три NPC выбрали `Hold`. Следующая волна предложила `SignSupport`, и снова был выбран `Hold`. Команд модели нет. Алгоритм после Hold всё же провёл 8 предложений и 3 подписи. Мир отличается и от baseline, и от hybrid без переговоров: слот решения откладывает ещё не отправленный план. Эти подписи не являются выбором модели.

Replay всех трёх paced-плеч без модели совпал с исходным миром по world-файлам. В `decisions.jsonl` сохранены сырые observations: 66 на economy-плечо, 97 на politics baseline, 70 и 91 на живые hybrid. `public-events.jsonl` не содержит prompt, response и `ModelResolved`. Неуспешных прогонов и fallback в этой серии не было; каталог и журналы неудачного прогона серия не удаляет.

## Исправление метки timing-policy

До этой серии `--script hybrid --model-mode off --mode headless` записывал `LiveHeadlessStress`, хотя запросов не было и мир совпадал с baseline. Такая метка смешивала выключенную модель со stress-режимом живой модели. Теперь `Realtime` и `LiveHeadlessStress` ставятся только при включённом runtime. Выключенная модель сохраняет `headless` или `paced`. Три equality-плеча пересобраны и перепрогнаны уже с новой меткой; их хеши остались равны baseline. Проверка серии до исправления этот дефект поймала и после исправления прошла.

## Повтор

```powershell
py -3 scripts/run_research_series.py --output artifacts/p24-series
py -3 scripts/verify_research_series.py --output artifacts/p24-series --evidence docs/verification/p24/gate.json
```

Полный regression gate после этих правок: restore/build, 618/618 тестов, 0 падений, 81 экспорт, 9 replay, 54 сравнения. Выключенный hybrid снова совпал с baseline-economy и сохранил метку `headless`; digest в runner остался null. Журнал — `artifacts/p24-regression`, в git не входит. Godot, Android device gate P06, EXE/APK и серия из 10 seeds не выполнялись. Следующий этап — P25.

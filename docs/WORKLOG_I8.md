# Журнал DELSK I8 / P80

Автор — Шелестов Максим Евгеньевич. 18 ноября 2026, Europe/Samara.

- Ветка `feat/vnext-i8-final-acceptance`, исходный `origin/master` `2957bfedf3f0ae4faa3318f48a38282a7be64cdf`.
- Рабочая копия изначально была на `e175199`; чистая. Оба merge-коммита входят в базу. [PR #110](https://github.com/MaksShelest/Delsk/pull/110) · [PR #111](https://github.com/MaksShelest/Delsk/pull/111)
- На GitHub открыты #45 и #60. История отчётов имеет приоритет только в пределах соответствующих профилей. Текущие стартовые карты и правила берутся из I6-C8C/D, независимые миры из I7, лабораторные сценарии из R1/R2.
- Среда Linux x64, 2 CPU по квоте, 8 GiB RAM, SDK .NET 10.0.401, Godot Mono 4.7.2 в отдельном каталоге. Базовые locked restore и Release solution build завершились успешно.
- Отдельный тест `BillingPenaltyE3Tests.GraceIsInclusiveFirstPenaltyAt1001Then1000NoCompoundingAndPartialPrincipalFirst` прошёл за 13,646 с по xUnit. Полный последовательный xUnit запущен на зафиксированных базовых DLL с исходным дедлайном 780 с.
- Базовый Лесной край · Hard · seed 17 · 3 NPC, профиль `vnext-i6c8d-economic-balance-1`, достиг t720. Все три NPC приняли поддержку. У них 0 покупок участка, 0 строек, 0 выпуска. Снимок t360 сохранён. Планировщик сравнивает полную стоимость участка с доступными 17 000 minor после резерва и отбрасывает действие до вызова Core. Следующий шаг — проверить отдельно учёт субсидии участка и корпуса в прогнозе Hard, затем повторить парные опыты и сверить события и хеши.
- Рабочие журналы и тяжёлые архивы опытов хранятся вне Git под `/workspace/work/i8`. Команды и итоговые компактные свидетельства будут записаны в репозитории.

## Исправление старта NPC Hard

- Расчёт Hard использовал 60 000 minor для участка при 17 000 доступных после резерва. После покупки аналогичный барьер имели корпус и уже привезённые стройматериалы. Прогноз теперь считает остаток целевого гранта для каждой операции, а стоимость привезённых материалов участвует в оценке вложений без повторного денежного списания. Core проводит прежние платежи.
- В паре Лесной край · 3 NPC · Hard · seed 17 до t720 базовый выпуск составил 0 партий, после исправления 19. Все три NPC построились, первые партии на t340, t404, t420. Новый тест проверяет те же операции на ресурсе обычной карты, 200 монет у каждого участника, по одной проводке каждого гранта и выпуск к t500.
- Первый диагностический запуск создал checkpoint t360, но путь совпал с JSON-квитанцией `run_check.py`. Квитанция перезаписала checkpoint; его нельзя использовать для продолжения. Текущая матрица пишет `world.json` и квитанции в разные каталоги.
- Целевой xUnit `StrategicPlanningTests` завершился успешно. Параметры остальных сложностей, денежные правила и формат сохранения прежние. Следующий шаг — сравнение событий всех пар, независимый аудит и продолжение выбранных миров из отдельного checkpoint.

## Матрица, сохранения и платформы

- [PR #112](https://github.com/MaksShelest/Delsk/pull/112) открыт в `master`.
  На `66b9510` GitHub подтвердил четыре успешных прогона. [Core](https://github.com/MaksShelest/Delsk/actions/runs/44264153427) · [Godot](https://github.com/MaksShelest/Delsk/actions/runs/44264153215) · [H2](https://github.com/MaksShelest/Delsk/actions/runs/44264153181) · [H3](https://github.com/MaksShelest/Delsk/actions/runs/44264153192)
- 30 коротких экспортов, включая 24 на исправленной версии, завершились до t720
  и прошли независимый аудит. Лесной край допускает 3 NPC. 6 и 10 NPC получают
  `Map participant capacity exceeded`. Малая долина прошла 3, 6 и 10 NPC.
- 16 стартов игрока до t1200 воспроизвели обе стратегии с одинаковыми входами.
  Ручная добыча приносит деньги во всех случаях. Все четыре производственные
  цепочки на обеих картах получили оплату и покрыли прямые денежные расходы.
  Полная окупаемость и зрелые уровни остаются следующими опытами.
- Лесной край Hard 17 продолжен t720 → t3600 → t7200. Последняя точка
  [t7200](verification/i8/woodland-hard-17-t7200.save.json.gz) содержит три предприятия I,
  максимальный XP 296. Изменение наличных NPC +247,81 монеты, выпуск 761.
  Целые хеши, объёмы и совпавшие экспорты лежат в [checkpoints.json](verification/i8/checkpoints.json).
- После загрузки t720 хеш мира t840 совпал с непрерывным прогоном и replay.
  Хеш контроллеров непрерывного и восстановленного прогона совпал. Проверены
  13 одинаковых экспортов и независимые бухгалтерские сверки.
- Godot Linux 4.7.2 Xvfb/software GL, 10 I7 сценариев. Windows Release и
  Developer экспортированы из `66b9510`, состав прошёл аудит. В локальном Android
  сначала обнаружено отсутствие `javac`, затем Temurin JDK дошёл до зависимости
  Gradle, где отсутствовал доверенный корневой сертификат среды. Итоги
  платформенных прогонов финальной ветки доступны в [Checks](https://github.com/MaksShelest/Delsk/pull/112/checks).
- Базовый xUnit достиг 780 с после 1 336 завершений. Три файла не нашлись при
  переносе DLL вне дерева проекта, все три проверки прошли с восстановленными
  путями. Повтор из обычного каталога DLL после исправления Hard достиг того же
  лимита после 915 завершений, без зарегистрированных отказов. Синхронные
  Gradle и исследования делят квоту CPU, поэтому прогоны не дают сравнения
  общего ускорения. Пени завершились отдельно и внутри первого агрегата.
- Исследовательская лаборатория 91/91, Python Checks 49/49.
  Следующий шаг после I8 — продолжить опубликованный мир до II и добавить
  сопоставимую обычную траекторию игрока с импортом и автодоставкой.
- Контекст выполнения — Codex Cloud `ccarenv_b64_Y2NhcmVudl8xOGZkNDhkNGY2ZDQ4MTkxYTZhYWJhMDFiZmZkYjZmYg`,
  рабочие журналы в `/workspace/work/i8`. URL самой задачи в предоставленном
  контексте отсутствовал.

## Как повторить и продолжить

Команды из корня репозитория, SDK 10.0.401. Два исполняемых файла для пары
собираются из указанной базы и из ветки I8. Меняются только runtime и каталог
результата. Сохранения и отчётные квитанции пишутся в разные пути.

```sh
dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
runner=src/Research.Runner/bin/Release/net10.0/Research.Runner.dll
work=$(mktemp -d)
dotnet "$runner" --export-i6c8-profiles config/vnext/economy.i4-mobile-equipment.json "$work/profiles"
cp config/vnext/balance.b1.json "$work/profiles/balance.b1.json"
printf '%s\n' '{"npcCount":3,"difficulty":"Hard","seed":17,"startingIndustry":"woodworking"}' > "$work/options.json"

python scripts/run_check.py --name hard-start --timeout 180 --log-directory "$work/checks" -- \
  dotnet "$runner" --scenario scenarios/vnext/woodland-region-i6c8c.json \
    --economy "$work/profiles/i6c8d.json" --new-game "$work/options.json" --script baseline \
    --output "$work/start" --ticks 720 --checkpoint "$work/start.save.json" \
    --human-ballot self --economy-sample true --progress true --compress-controller-turns true
python scripts/verify_results.py "$work/start"

python scripts/run_check.py --name hard-one-hour --timeout 300 --log-directory "$work/checks" -- \
  dotnet "$runner" --load "$work/start.save.json" --output "$work/hour" --ticks 3600 \
    --checkpoint "$work/hour.save.json" --economy-sample true --progress true --compress-controller-turns true

python scripts/run_check.py --name human-woodworking --timeout 180 --log-directory "$work/checks" -- \
  dotnet "$runner" --study-i6c8d scenarios/vnext/woodland-region-i6c8c.json \
    "$work/profiles/i6c8d.json" woodworking 17 production 1200

gzip -dc docs/verification/i8/woodland-hard-17-t7200.save.json.gz > "$work/late.save.json"
sha256sum "$work/late.save.json"
python scripts/run_check.py --name late-continue --timeout 300 --log-directory "$work/checks" -- \
  dotnet "$runner" --load "$work/late.save.json" --output "$work/next" --ticks 8400 \
    --checkpoint "$work/next.save.json" --economy-sample true --progress true --compress-controller-turns true
python scripts/verify_results.py "$work/next"
```

Для матрицы подставить `compact-valley` или `woodland-region` в путь карты,
`Easy`, `Medium` или `Hard`, seed 17 или 42. На Малой долине использовать
3, 6 и 10 NPC. Для игрока повторить `woodworking`, `food`, `dairy`, `materials`
с обеими стратегиями `production` и `raw`. Для сопоставимого replay читать
экспортированный `scenario.json` и `commands.jsonl`. Для исследовательского
продолжения указывать абсолютный конечный тик. SHA-256 распакованного позднего
сохранения `55b5258261a3c6e18d6a8652c004806768ffbe4995d4e5c23328645a237dd40d`.

```sh
python scripts/run_check.py --name aggregate --timeout 780 --log-directory "$work/checks" -- \
  dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll \
    -noColor -parallelMode none -verbose -xml "$work/aggregate.xml"
python -m unittest discover -s tests/Checks
python -m unittest discover -s tests/Research.Lab
python scripts/check_city_experience_ui.py --case saved-worlds --display :78 \
  --godot /path/to/Godot_v4.7.2-mono --output "$work/native"
```

Финальные проверки и артефакты привязаны к истории запусков [PR #112](https://github.com/MaksShelest/Delsk/pull/112/checks).

## Проверка оптимизации наблюдений

- В измеренном горячем участке `observations` две одинаковые проекции
  публичной геометрии участков создавались для каждого участника. Существующий
  барьер теперь вычисляет геометрию один раз. Вне барьера запрос по-прежнему
  использует актуальный снимок.
- Малая долина · Hard · seed 17 · 10 NPC из одного checkpoint t720 до t840.
  Два повтора каждой версии, одинаковый SDK и `DOTNET_PROCESSOR_COUNT=1`.
  Медиана наблюдений 1 435,72 → 1 119,67 мс, аллокации 446 226 932 →
  362 284 952 байт. Хеши и конечный файл совпали во всех повторах.
  [Четыре профиля и исходные измерения](verification/i8/performance.json)
- Build Release 0 ошибок и предупреждений. Целевой xUnit 61/61.
  `H2StorageTests.BatchReadEqualsIndividualAllowlistAndDoesNotLeakCacheAcrossFailedBarrier`
  проверяет побайтовую эквивалентность проекций и очистку после отказа.
- Для воспроизведения замера подготовить checkpoint t720 Малой долины с
  `npcCount=10` по предыдущему протоколу. Для каждого runtime использовать
  `dotnet Research.Runner.dll --measure-session world.json --ticks 840 --operation save --output NEW_DIRECTORY`.

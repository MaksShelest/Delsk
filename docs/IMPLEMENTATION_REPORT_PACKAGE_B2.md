# Пакет B2: runtime карты

Автор: Шелестов Максим Евгеньевич.24.10.2026.
Основа: A2 fda7203 и полностью проверенный B1, PR58/74b0ca5 +56badfa(CI import).
Отдельная ветка feat/vnext-package-b2, B1 не merge. C не начат.

## Что реализовано

Strict C# design adapter + существующие ScenarioValidation/GridMap/экономика:
никакой второй Python/Godot симуляции. Required/closed schema, duplicate/null/
version/type/ID/bounds/references/intersections reject. Typed optional MapLayout
проверяется reader и полным Core validator, сериализуется/хешируется/restore сразу.
Source SHA отдельно от snapshot/profile/ConfigHash. Источники не изменены.
C# CLI export даёт deterministic embedded/lab snapshots; output не перезаписывается.

| Исходная геометрия | small_river | large_districts |
|---|---:|---:|
| Размер |64×48|128×96|
|2×3 промышленные контуры / entry |48|96|
| Ресурсные районы / клетки |8/200|24/600|
| Public источники / виды сырья |8/8|24/8|
| Лавки / spawn pool |15/11|15/11|
| Оси / коридоры / городская грунтовка |720/1872/327|2880/7488/1163|

Сохранены source IDs и координаты. Покупаемые Plot — только клетки контуров и
лавки (303/591), без бесплатной земли. Коридор/город/ресурсы исключены из land;
валидация запрещает их добавление. Дороги сохраняют начальное городское назначение
как metadata, без фиктивных прав/ступеней/налогов. Новых правил стройки D нет.
Civic3×3/2×3/5×5 и реальные blocked bodies/подходы; отсутствие entry/body в source
интерпретировано явно в adapter-1, см. [инструкцию](MAPS_PACKAGE_B2.md).

Обычное меню, preview, RU/EN имена, переиспользованный read-only DebugBoard в миникарте/debug и исследовательский каталог
видят обе карты и сохраняют пять старых aliases. Общий B1 setup3..10, exact11spawns,
seed rotation без World RNG. Явная A2пара, несовместимый профиль отвергается до мира.
Фактические stock/regeneration/public/покупка/аренда/объёмы/длительность — supported
A2, не полный будущий баланс. Руды/песок имеют безопасный quarry fallback вместо
пшеничных полей; milk — ферма, grain/beet — разные поля. Все8ресурсов локализованы.
Обычная3Dгеометрия без промышленных столбиков, городская грунтовка объединяется
в полосы; большой мир не создаёт12тыс. отдельных ground mesh. Метаданные не
добавляют collisions от декора. Ратуша ведёт в City/существующие политические
команды, терминал — обычная биржа, dealer — paused help о будущем E.

## Реальные проверки

- Полный ordinary suite1058tests/0skip;30новых B2 cases плюс30B1. Строгие плохие
  inputs, exact fixtures, source/snapshot equality,9seeds, всеcounts и старые5карт.
- Runtime GridMap BFS от каждой11spawn ко всем industrial entries,15stalls,
  civic entries/public sources. Исходные blocked civic bodies и реальные warehouse
  walls учтены. Warehouse topology fixture сохраняет все corridor клетки открытыми.
- В обеих картах cash0 через реальные Move/Harvest собирает каждый из8public
  ресурсов, доставляет и продаёт обычной командой. Деньги/товар не внедряются.
- Checkpoint до/во время движения, harvest и загруженного рюкзака: continuous/
  directload/replay equality. Legacy truck fixture перенесён на точные новые
  obstacles/industrial/market клетки: фактический путь/груз/возврат, loaded trip
  checkpoint и каждый последующий hash совпадают. Это тест прежнего грузовика,
  не реализация отсутствующих A2 production templates или будущего автопарка.
- Обе карты×3/6/10:64process checks,180ticks, каждыйNPC выдаёт commands; direct
  load23/continuous/replay exact, independent balances. Profile10 measured отдельно.
- source gate: два C#экспорта каждой карты точно равны committed snapshot bytes,
  Source SHA соответствует исходнику, legacy profile явно rejected.
- Native Godot обе карты×3/6/10:800×720, RU/EN selection/preview/start/последний
  Actor_N/Cancel replacement/Continue/diskload. Дополнительные count3 lifecycle:
  реальные команды до подходов, клики по City/exchange/help, quarry context,
  camera follow; RU City/exchange проверяются отдельно в том же окне.
- Лаборатория37unit; launcher/reopen/lock/shutdown и28preset×map combinations
  прошли C# validation. Реальный Chromium выбрал обе A2пары, выполнил два30tick
  опыта и проверил записи мира/manifest. Fake executor не использовался.
- Linux shared Core/Godot import/smoke:1132tick hashes,35restores и stopped final10;
  ordinary assembly audit. Старый A2save tick50 load сохранил прежний hash
   8e1a85419d83a0581dcc71e20db6a7dc9aca3ad6dfc5432bad91a6ffb6efd7a9.

Headless102exports/14replays/69pairs прошёл полностью; отдельный финальный suite1058/1058.
A1/A2process43/49 зелёные. Developer isolated1064/1064 и forbidden Release gate
прошли; первый timeout не засчитан. B1 CI после56badfa: Core и Windows platform
зелёные,17+17 HUD/ExportDebug проверки сохранены. Матрица: [PACKAGE_B_ACCEPTANCE.md](vnext/PACKAGE_B_ACCEPTANCE.md).
Финальный native gate обе карты×3/6/10 — все6exit0 и clean Godot logs;
RU City/exchange, paused dealer help и all-candidate choices проверены. Миникарта
переиспользует публичный DebugBoard без новых команд/изменения прав/времени.
Дополнительный negative preview guard не даёт несовместимому --economy сломать
каталог/Start и сохраняет выбор совместимых старых карт.
Bounded restore/build180s, suite120s, import120s, smoke30s, chunk300s. Full
headless780s агрегирует отдельные bounded процессы; multi-window bilingual
native150s включает несколько страниц/физическое движение/два старта на softwareGL,
не заменяет30s smoke. Первая Developer попытка180s прервана при конкурирующих
тяжёлых jobs; изолированный повтор1064/1064 прошёл, таймаут успехом не считался.
Evidence/logs/TRX/JSON: /workspace/work/delsk-b2; воспроизводимые scripts/tests
в репозитории. Логи/бинарные captures не добавлены в Git.

## Асимметрия и измерения

9seed/11actors: actor1 получил7разных spawn на малой и6на большой карте.
До всех public sources+15stalls+terminal, суммарные min/median-of-medians/max:
2/10/57 и2/36/125 клеток. Геометрия во всех seeds/counts одинакова. Seed rotation
не означает одинаковую экономическую выгоду мест или доказанный баланс.

Первый measured180tick/10NPC (одновременная нагрузка других gates):

| Показатель | прежняя малая B1 карта | small_river | large_districts |
|---|---:|---:|---:|
| Average/p95,ms |13.68/20.69|37.34/95.95|83.49/173.27|
| Checkpoint bytes |1,154,005|5,012,737|9,786,172|
| Working set bytes |230,486,016|824,578,048|2,118,369,280|
| Encode/decode,ms |229.95/448.28|369.33/867.88|754.81/1735.13|

Все checkpoint hashes совпали. Это ограниченные облачные измерения с JIT,
controllers/Core/invariants/sampling и памятью restored session, не контролируемый
hardware benchmark. Большой roster/map/history существенно дороже; не скрывать
эту границу и не обещать hardware FPS/длительные20–35часов. Startup отдельно в
summary новой CLI версии; финальный отдельный180tick замер: startup484.01ms, average87.45/p95183.47ms,
checkpoint9,786,172bytes, encode1332.01/decode2065.87ms, working set2,158,817,280bytes.
State hash1a3ff432ae4d7c05c652077d3d5f3201408c135a50bf9f1832f9dbcdf4a31307
и controller hash совпали с прежним запуском. Отсутствие параллельных локальных
gates не делает облачный хост контролируемой hardware средой.

B2 закрывает согласованную runtime-часть P68 на supported A2, совместно с B1/P62.
Полный будущий баланс/отрасли D, стройка/XP/C, транспорт/дороги/счета E не закрыты.
Windows/Android устройства, звук/аппаратный FPS, реальная LLM и длительный баланс
не проверены. Первым следующим ограниченным шагом может быть C после отдельного
задания; автоматически он не начинался.

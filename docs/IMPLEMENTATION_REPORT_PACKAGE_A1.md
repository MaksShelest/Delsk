# Пакет A1 — read-only каталог и структурная CLI-проверка

Автор: Шелестов Максим Евгеньевич.24.10.2026 (Europe/Samara).

**A1 выполнен; A2 запланирован и не начат.** Это не закрытие всего пакета A,
P61/P63 или доказательство баланса B1.

## Найденное состояние и граница

Локальная исходная ветка062381e была чистой. После fetch основная ветка3a75917
включает PR51–54: нашу лабораторию, исследовательское пространство/observer/две
карты PR52, актуализацию0.6 PR53 и точное задание A1/A2 PR54. Статусы PR проверены
на GitHub: все четыре слиты. Реализации каталога/нового CLI до A1 не было.
Существующие EconomyProfile.Read/Load/Apply, общий recipe executor, строгий
SimulationJson, Research.Runner и xUnit-проект сохранены. Новая ветка:
`feat/vnext-package-a1`, база3a75917.

Мир **18**, Rules **production-recipes-1**, Session **2**, Envelope **2** прежние.
Профиль **presentation-economy-v6**,9 товаров/10 рецептов. В A1 он и все сценарии
не изменены. Каталог не подключён к игре, конфигурации контроллеров или хешу мира;
никаких заглушек будущих механик не добавлено.

## Контракт и источник

`config/vnext/catalog.b1.json`: format_version=`delsk-vnext-catalog-1`,
id=`vnext-b1`, status=`catalog_only`, source_sha256 и три секции:

| Секция | Записей | Соответствие B1 |
| --- | ---: | --- |
| items | 34 | Все шесть исходных полей без изменения значений |
| recipes | 23 | Все восемь исходных полей, включая input map/работу/услугу |
| industries | 6 | id/name/materials без изменения; coins → construction_price_minor ровно ×100 |

Перевод монет в minor — смена единицы, не новая формула цены. Остальные секции
B1 сознательно остаются A2. Source SHA-256 точных байтов B1:
`4b12d705acd4c7f4e9d44f540eb3f600edd75b983b1ffdd52c71a15e8a42c9d2`.
SHA-256 поставленного каталога:
`62bf82eb8ed11e0cc72415c6fef026d8a3e305ee7f0849debdefb2115a70a0a8`.

`extract_vnext_catalog.py` только механически выбирает/переносит JSON. Числа не
переписаны в C#; Python не реализует проверку экономики. Повторная генерация даёт
те же байты, `--check` обнаруживает stale source hash/данные без записи файлов.
Для двух файлов .gitattributes закрепляет LF; проверена фактическая Git-фильтрация
с core.autocrlf=true. Reader всё равно считает digest реально прочитанных байтов,
не нормализует текст. Каталог hash и runtime ConfigHash имеют разные назначения.

## C# и диагностика

В существующем Hosting: VNextCatalog*.cs, sealed records с ImmutableArray и
ImmutableSortedDictionary (Ordinal). File reader отказывает до чтения слишком
большого файла, повторно ограничивает чтение на случай роста: **1MiB**, глубина32.

Контракт закрытый: обязательные поля, duplicate decoded JSON keys на любой глубине,
unknown/null/type/version/status; ID `[a-z][a-z0-9_-]{0,63}`, names1..160 непустых,
source resource/import/production, 64hex SHA. Категории ID раздельные. Коллекции
непустые, inputs1..8 различных товаров. Объём/цены/количества/работа1..Int64.MaxValue,
XP/услуга0..Int64.MaxValue, ступень1..5. Дроби/экспонента/строки не превращаются
в целые. Проверяются все ссылки.

Checked Int64: произведения количества на объём/цену, суммы входной партии и
строительных материалов, полный справочный строительный набор с денежной ценой,
услуга×работа, выход×XP. Переполнение отклоняется. Порог70, вместимости ступеней,
граф/самозамыкание, доступность bootstrap и маржа **здесь не проверяются**.

Fail-fast диагностика воспроизводима: code/path/message. Главные коды:
InvalidJson, TooLarge, IoError, DuplicateKey, UnknownField, MissingField, NullValue,
InvalidType, InvalidInteger, UnsupportedVersion, InvalidStatus, InvalidSha256,
InvalidId, InvalidName, InvalidSource, EmptyCollection, InvalidQuantitySet,
OutOfRange, DuplicateId, UnknownReference, Overflow. Невалидный каталог наружу не
возвращается. Standalone reader проверяет синтаксис source SHA, а не утверждает
сверку с непрочитанным B1: фактическая согласованность проверяется отдельным
извлечением и независимым xUnit-сравнением всех полей выбранных секций.

## CLI

После текущего Release build из корня:

```sh
dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --validate-vnext-catalog config/vnext/catalog.b1.json --output artifacts/vnext-a1
```

Только две пары аргументов, в любом порядке, без дубликатов. Ранний return стоит
перед `new ModelServices`, созданием мира/сессии/контроллеров. Нет Step, HTTP, Godot,
браузера, применения EconomyProfile или записи серии. Общий прежний parser и
execution body не менялись. Добавлены только отдельная dispatch-строка и команда.

Exit **0** — структура корректна; **2** — любая ошибка нового CLI/ввода/структуры/IO.
На безопасном output пишется только `catalog-validation.json`: succeeded/failed,
scope=structure, formatVersion/catalogId, sha256 точных байтов, counts, diagnostics.
Если заголовок/данные ещё не разобраны или oversized файл не читался, неизвестные
метаданные/digest — null. Ошибка аргументов или недоступный/непустой output:
stderr/exit2, без ложного сообщения о сохранённом отчёте. Готовый отчёт появляется
атомарно с запретом перезаписи; чужие результаты/series не удаляются.

## Реально выполненные проверки

Все команды ограничены через scripts/run_check.py; SDK10.0.401/net10.0, прежние lock.

```sh
python3 scripts/run_check.py --name a1-restore --timeout 180 -- dotnet restore Delsk.sln --locked-mode --disable-parallel -m:1
python3 scripts/run_check.py --name a1-build --timeout 180 -- dotnet build Delsk.sln -c Release --no-restore --disable-build-servers -m:1
python3 scripts/run_check.py --name a1-tests --timeout 120 -- dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll -noColor -noLogo -parallelMode none -result-trx artifacts/a1-tests.trx
python3 scripts/run_check.py --name a1-cli-gate --timeout 180 -- python3 scripts/check_vnext_catalog.py --output artifacts/a1-cli
```

- Restore/build exit0,0 warnings/errors на финальной сборке.
- **947/947 xUnit**,0 failed/skipped/not-run;50 новых cases. Малый fixture и34/23/6,
  независимое сравнение B1 по всем полям/source SHA, strict cases/все ссылки,
  значения/суммы/overflow, immutable/Ordinal, точные digest/BOM/round-trip,
  размер/глубина/ошибочный UTF-8/IO и явная граница A2. Прежние EconomyProfile,
  save/load, рецепт/WIP/privacy tests входят в полный набор.
- **43 реальных bounded process checks** в CLI gate: два byte-identical извлечения,
  stale source whitespace, valid/small/bad-reference/missing/oversized100MiB
  sparse файл,21 запрещённый simulation/model/research flag, missing/duplicate,
  сохранность чужой папки/файла/отчёта. Успехи exit0, ожидаемые отказы exit2.
- Существующий normal CLI: baseline с неизменным economy/seed42/120ticks,
  checkpoint59, direct load до120, replay без модели.4 полных runner export,
  3 continuation/world-only сравнительные пары. Контроллерная память и полные
  committed exports при load совпали, world-only replay совпал.
- SHA исходного профиля, B1, каталога и **всех** сценариев после gate прежние.
  Старые Core/версии/Save API/права/ExportDebug не редактировались.
- `core.autocrlf=true` Git-фильтр сохранил точные исходные байты B1.

Краткое evidence и образец настоящего CLI отчёта: [verification/package-a1](verification/package-a1).
Большие временные логи/артефакты — /workspace/work/delsk-a1, не коммитятся.
CI получает extraction check и реальный catalog CLI gate; существующий полный
headless gate остаётся. Локально он целиком не повторялся: dispatch изолирован,
общий parser/правила не изменены; полный xUnit и профильная direct-load/replay
регрессия проверены. Godot/Android/GPU/реальная модель здесь не запускались.

## Остаток A2 / остановка

A1 завершён по проверенному read-only результату. Следующее отдельное задание:
остальные числовые секции B1; явный адаптер поддерживаемых частей к EconomyProfile;
семантический граф/начальные комплекты≤70/вместимости/зависимости/bootstrap;
legacy→vNext mapping; отдельный выбранный runtime-профиль и изолированный headless
smoke на прежнем executor, с регрессией. Версии менять только при реальном runtime
изменении; старые saves не перезаписывать. Будущие цехи/XP/снос/машины/дороги/услуги
остаются своими пакетами. Полный P61/P63 и аппаратные/model/balance gates не закрыты.

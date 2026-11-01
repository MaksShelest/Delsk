# H2: журнал работы

Автор: Шелестов Максим Евгеньевич. Основа29cb97d (PR75 уже слит владельцем).
Задание H2 включает движение, грунтовку и измеренную производительность; H3 не начат.

## Хранение и профиль прежних правил

- Зафиксированы отдельные baseline/instrumented runtimes и исходные late H1 bundles.
  Фазовые измерения не входят в сохранения/хеш и не меняют игровое время.
- Полная история наблюдений хранится lossless DAG с разделением неизменных частей;
  экспорт JSONL пишет прежние наблюдения напрямую. Выборки контроллеров и труда
  сохраняются целиком с дедупликацией. Session4 читает Session1–3; World28 пока прежний.
- Индексы полного журнала/предложений заменяют повторные линейные просмотры;
  проверки не отключались. Canonical SHA потоковый, JSON-семантика прежняя.
- Шесть H2Storage tests прошли: actual H1 Session3→4→продолжение, golden60,
  replay, полные наблюдения, приватность batch, четыре вида повреждений архива.
- Ошибка первоначальной сборки CS0121 (неоднозначный SelectMany) исправлена
  явной lambda; исходный failure сохранён в checks. Лимиты проверок не повышались.

## Уточнение вывода H1

В H1 microprobe сопоставил Scenario.MoveTicksPerCell=1 с фактическим движением.
Экономический G1 DTO уже возвращал4 на грунтовке (ControllerContracts.cs).
Вывод о подтверждённом расхождении1vs4/5 ошибочен. Остаётся проверить конкретные
смешанные пути и транспорт; это не подтверждает причинное объяснение задержек H1.

## Физическое движение и native

- Все32 H2 tests прошли по отдельным неизменённым120s partition deadlines;
  asphalt/highway fixtures разделены на самостоятельные классы, не увеличены лимиты.
  Дополнительно original RoadTrafficE2 и NpcCheckpointG1 прошли.
-100клеток:400→160 пешком/обычные машины,800→320Тракт,500→200пешком вне дороги.
  Построенные40/60:12клеток24→10/17→7, Вектор45:22→9. ETA совпало,
  road units за100клеток прежние. Услуги1/5рейсов72/160тиков, включая handling.
- Закрывающийся объезд, встречный поток, торможение/exit hold, extraction иfreight
  прошли с новым движением, save/load/replay/реальным грузом и начислениями.
- Native Playfield:24PNG, обе действующие карты/classic/cozy/close+overview,
  прямой/построенный угол/перекрёсток/работы. После просмотра сглажено слияние
  колеи; все сцены пересняты. ordinary AssemblyAudit до включения isolated probe.
- Profile/regeneration: World/NPC всех11derived profiles byte-semantic exact;
  design JSON меняет только дату и80→32, новые provenanceSHA. Старый savedconfig
  не перезаписывается, old acceptedquote/deadline exact.
- Late large Hard42 уже прошёл12600→14400 с save/readback за262.8s.
  Остальные инженерные продолжения/экспорт/аудиты выполняются; это не новый n H1.

## Дополнительная точность направления

- Найден случай свободного старта против направления на узкой дороге: оценка
  должна включать обязательный манёвр. Общий oriented search хранит клетку+курс,
  допускает законный возврат через исходную клетку после разворота. Старые правила
  этого поиска не получают. Публичный navigation snapshot разделяется внутри
  барьера; исключён повторный квадратичный просмотр коридора для каждого NPC.
-3Directional tests прошли: оба варианта кармана23=23тика, услуга32=32.
  Новый ArrivalTick задания учитывает оставшийся путь; несколько сервисных рейсов
  суммируются по фактическим направлениям с ограниченным циклом состояний.
- Resumable late verifier smoke30→40→42:31exact exports, world replay,3accounting,
  3public и2full-history audits; полная JSONL сжимается только lossless после сверки.
- LateLargeHard43 также12600→14400/save/readback за263.48s. Прежние frozen
  performance/legacy runtimes сохранены; новая геометрия движения — отдельная когорта.

01.11.2026,13:40UTC — финальные проверки и исправление смены покрытия

- Native `native-final`: все4 реальные сцены прошли,24 PNG, оба скина,
  три масштаба/выбор клетки/save-load. Источники текущей сборки зафиксированы.
- Найден и воспроизведён дефект: завершение грунтовки под пешеходом в середине
  ребра меняло знаменатель progress и приводило к InvalidPathProgress при load.
  Movement1 теперь запоминает время уже начатого ребра; следующие ребра получают
  новое покрытие при входе. ETA обновляется перед инвариантами. TimedEdges
  отсутствует в старых мирах/хеше; старый golden848deb54 сохранён. Replay/save
  во время изменения покрытия прошли. H2 suite расширен до36 тестов.
- Первый CI4370485: все8 late продолжения/replay exact прошли; три small
  остановились позже в новом full-history аудите с KeyError6090/6243/6532.
  Пустые наземные склады удаляются при строительстве. Аудитор больше не требует
  их наличия в финальном мире: проверяет историческую землю/момент покупки,
  владельца, вместимость, привязку ID и отсутствие занятого здания в наблюдении.
  Это не независимая реконструкция каждого выделенного ID; полная история
  дополнительно сверяется exact continuous/restored. Ошибки CI не скрыты.
- ci-tools выявил две ошибки инструментов: старая минимальная fixture billing
  не имеет models (новая ветка проверки скорости должна быть только movement1);
  тест разбиения не учитывал новый отдельный H2 suite. Исправлено,30 Python
  corruption/partition/cache тестов прошли. Старые suites/границы не ослаблены.

01.11.2026,13:52UTC — все36 H2 tests и полный старый экспорт

- Финальная ordinary H2 suite:36/36,13 частей с120s пределом; ошибок/пропусков0.
  Первоначальный ошибочный вызов отсутствующего run_test_partitions.py (--help)
  сохранён как h2-final36 exit2; настоящие результаты — final36/unit-check.json.
- На Small10900 все31 экспортных файла до/после оптимизации совпали побайтно,
  оба набора прошли независимые accounting/public/history. Новая схема хранения
  не меняет наблюдения и действия. Повторные загрузки4/полные экспорты и Large
  выполняются отдельно от первой конверсии3.
- Для воспроизведения добавлена baseline-instrumentation.patch поверх29cb97d
  и prepare_h2_baseline.py. Только измерительные callbacks/runner; никаких
  правил, индексов или новой скорости. Сборка подготовленной baseline прошла.
  Первичный frozen runtime имел Core0–9, но ещё не детальные invariant callbacks;
  эта разница происхождения прямо указана в README. Реконструированная версия
  отдельно проверяется против исходных old-rule world/controller хешей.
- Историческая ground-проверка уточнена: ID сохраняет связь с владельцем/plot,
  но после сноса допустим новый ground ID на той же земле в другой момент;
  внутри одного наблюдения дубль запрещён. Participant.accountId также сверяется.

01.11.2026,14:15UTC — длинные проверки и контракты дорожных fixtures

- CI73c42a4:8/8 retained late миров прошли все14 стадий,31-file continuation,
  world replay,3accounting/3public/2history audits каждый. Windows/UI оба pass.
- Полная E2 runner цепочка выявила ошибку рефакторинга тестов: CallerMemberName
  после выделения CheckFreight/CheckExtraction изменил имена fixture файлов.
  Старый check_road_e2.py ждёт прежние имена. Восстановлены явным nameof(Fact),
  H2 fixtures получили собственные имена. Игровые правила/проверки не менялись.
  В CI добавлен always-print bounded E2 stdout/stderr; исходный failure сохранён.
- Дополнительные тёплые1800тиков: Small10900→12700,147,14s/0,87GiB peak,
  Large14400→16200,259,38s/1,54GiB. Реальный save/readback прошёл, envelope
  4,78/5,34MB, decoded32,79/36,97MB. Это отдельные storage/growth probes;
  дополнительный replay/audited horizon16200 пока не заявлен.
- Повторное выполнение нового durable tool с тем же output прошло:14 стадий,
 97 helper SHA/copies, контрольные30→40→42, всё31 exact и все8 аудитов.

01.11.2026 — завершённые проверки H2

- b16b095/run41313520862:8/8 late worlds, все24 accounting/24public/16history
  audits,31-file continuation и world replay. Actual interval/peak/world/controller
  SHA выгружены из CI logs в verification/h2/late; разные hosted workers не
  подставляются в локальный n3 speedup. Первая конверсия достигала8,03GiB peak.
- E2 после фикса имён:27unit +51 bounded processes,35exports,24comparisons pass.
- Required verify теперь зависит и от h2-engineering-unit. Старые needs и
  deadlines сохранены. Функциональные953inputs отделены от report-only changes.
- Оставшиеся ordinary5000/первая legacy-конверсия/20–35ч/полный JSONL явно
  перечислены в отчёте. H3 не начат, автослияние не включалось.

01.11.2026,17:50UTC — bounded Developer history read

- Финальная проверка production callers нашла ControllerTurns.ToArray().TakeLast
  в DeveloperTools. API ControllerTurnsTail выбирает диапазон до декодирования;
  видимые записи и полная сохранённая история прежние. Model safety использует
  metadata, поскольку наблюдения при этой проверке не читаются.
- На Large12700: load+tail/SHA16,12s, tail7,66s, peak1,25GiB. Независимый streaming
  verifier подтвердил256 точных observation SHA/actor/tick против2,85GB полного
  экспорта. Это не заявленный paired speedup Developer UI или аппаратный FPS.
-7 storage tests прошли, включая whole-save equality до/после чтения. Первый
  новый тест сравнивал разные load epochs; исправлен baseline самого теста,
  правило Epoch+1 при load сохранено. Исходный failed TRX/log сохранён.
- Первый Developer запуск имел неправильный namespace и0tests (не pass).
  Правильный Simulation.Tests.DeveloperToolsTests:19passed,0fail/skip.
- Python corruption/cache/partition/audit suite:32passed. Все3 CI workflows
  на1b42948 завершились success до этого read-side изменения. Повторяются
 37H2/native и model safety, функциональные fingerprints обновляются отдельно.

01.11.2026,18:15UTC — fractional road save regression

- Дополнительный highway45 mid-quantum test сначала корректно встретил
  TurnRequired: ручной противоположный курс требует явного разворота. Setup
  исправлен обычным TurnVehicle, правило управления не обходилось.
- Затем получен реальный NotCommittedTick при112 единицах BillingDistance:
  межквантовый predicate оставался≤80. Исправлен только Movement1 на cap модели/
  покрытия×2,5 с округлением вверх. TrafficCellEntered хранит nominal speed≤80;
  разрешён также для нового unbilled E2, где денежного события вообще нет.
  Legacy predicate80, owner/type/phase/command/amount guards сохранены.
- Highway45: progressFraction500000, сохранение,13квантов exact continuation,
  возврат через законный разворот, ETA/полный replay прошли. Отдельный unbilled
  manual cell entry→save/load также pass; новый38й test включён в H2 suite.
- Все38 ordinary H2 tests прошли в полном13-part запуске без skips/failures.
  Native повторяется на окончательном BillingValidation source; правила денег,
  производства и обработки не менялись. Полный CI1b42948 ранее success во всех
  трёх workflows, последняя корректировка будет собственным новым коммитом.

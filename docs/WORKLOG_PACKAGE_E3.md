# Восстановление и работа над E3

27.10.2026, Europe/Samara. Автор: Шелестов Максим Евгеньевич / MaksShelest.

- Задание: только E3/P71, без услуг доставки. Новое явное правило пользователя
  заменяет прежние 500/500: grace1000 inclusive, первая пеня1001, период1000.
- Старт clean на2ca4b12, feat/vnext-e2-roads. Fetch показал merge PR67 в
  основной ветке: d9e17e308ea325118e4af665152c2899cbd97549. Этот merge содержит
  финальный E2/2ca4b12 и проверенный runtime25f6689; diff между merge и E2 пуст.
  Проверены actual Core40483124762 и Windows40483124741, оба success на25f6689.
- Новая ветка feat/vnext-e3-accounts от d9e17e3. Исходная работа переиспользована:
  общий RoadBody/TrafficTravel, вклады каждой ступени, MoneyAccount/EconomyTransaction,
  holds/escrow, phases, session playback, планшет и privacy allowlists.
- Подготовленные SDK10.0.401, MonoGodot4.7.2, caches/Xorg используем прежние.
  Runtime skill прочитан, сетевой policy enforced; секреты не читались.
- Правила E3 — отдельный профиль vnext-e3-accounts-1 только новых designed games.
  World26 с optional Billing, limited25–18 только без Billing. Старые resolved
  configs/hash не заменяются. Метаданные generated profiles пересобраны после
  изменения источника; E1/E2 runtime-правила остаются прежними.
- Начисление сохраняет целые units 1/(800×10000×10000)копейки, class/share из
  typedBALANCE, источник покрытия — исходная клетка физического ребра. Остаток
  кванта, отброшенный E2 на центре клетки, не тарифицируется. Временный голый
  объезд без ступеней бесплатен. Добывающая техника — базовый лёгкий множитель.
- Каждый120tick все целые копейки общего накопления участника включаются в
  один счёт. Распределение методом наибольшего остатка сохраняет подписанные
  (в том числе отрицательные) дробные остатки за каждым получателем.
  Это предотвращает потерю10% города при множестве маленьких поездок.
- Оплата использует один атомарный clearing с нулевым остатком; это не второй
  кошелёк участника. Principal first, largest-remainder по открытым требованиям,
  invoice-specific tie rotation. Каждое требование ограничено своей суммой.
- Автоплата после поступлений/освобождения резервов, перед пенями, из frozen
  free budgets. Взаимные receipts доступны автоплате следующего активного тика.
  Пени20% открытогоprincipal с integer basis-point remainder; без капитализации.
- Неудачные ранние сборки сохранены в /workspace/work/e3/checks: build-initial
  (nullable buyer type), e3-build-tests (неверный вызов TickScheduler и var
  declaration). Это compile failures, не пройденные проверки.

Работа продолжается; финальные результаты фиксируются только после настоящих gates.

- e3-accounts-1: timeout124 после ошибки тестового направления (против facing
  без разворота). Направление исправлено; timing cases вынесены в отдельные
  исчерпывающие partitions с прежним120s. Не считать этот запуск pass.

- Intermediate unit-2:18/18 ordinary прошли; исходники затем расширены signed
  rounding, escrow receipt и traffic cases, поэтому это ещё не финальный gate.
- Первичный push7031b01 пропустил изменённый исходный BALANCE при выборочном
  staging;5484a86 добавляет1000/1000. Проверять CI на последнем полном snapshot,
  не на7031b01. DraftPR68 открыт без merge.
- Research.Lab первый локальный вызов без сетевого разрешения:7 socket errors
  (localhost запрещён sandbox). Повтор с штатным network permission:37/37,
  /workspace/work/e3/checks/research-lab. Auditors/PythonChecks22/22.

- unit-3 остановлен на новом receipt test: автопомощник E2 уже начал физически
  освобождать долго занятый проезд; LeaveVehicle закономерно отклонён в движении.
  Fixture ждёт законного завершения манёвра перед выходом; busy guard не ослаблен.

- b09b72812f2ed565b6c83599bc24e418f271699d: полный CI E3 ordinary22/22;
  unit CI40526926126 job122268907581 с настоящими counters в logs. E2 ordinary/
  Developer и previous ordinary/Developer прошли на том же snapshot; Core runners
  продолжаются. Windows40526926355/job122268907559 success. Это пока не итоговый
  combined verify. PR68 остаётся draft.
- unit-4:22/22 locally,562.182s/780, все8partitions без skips/overlap.
  Receipt fixture после выхода обнаружил истёкшие первоначальные столичные
  заказы (подготовленный period10000 при TTL180). Этот fixture явно сохраняет
  один первоначальный funded order до10000; новые деньги/спрос вручную не выдаются.
  Повтор прошёл; это prepared source/finance evidence, не natural progression.
- Native1: настоящий MonoGodot4.7.2, RU800×720/EN1100×800, view/no debit,
  paused pending commands,0.01principal, actual cash, disk restore, auto toggle,
  paid state; strict logs прошли. Источник /workspace/work/e3/native-1.
  После visual inspection уточнены только scoped colours и paper-host probe;
  повтор native запланирован, первый проход не выдаётся за итог этих новых bytes.
- Runner E3: /workspace/work/e3/runner-1,600s outer deadline, ordinary tapes из
  unit-4/e3-fixtures. На время этой записи ещё выполняется; не считать pass.
  Нужны final audit, actual World25 continuation, native2, exact source digests
  и окончательные Core/Windows CI, затем итоговый report/plan/ready PR без merge.

- Runner1 завершён успешно:48process checks/33exports/22exact pairs,
  585.429s/600. Deadline не повышался, все individual процессы<=120s.
- CI b09 runner обнаружил исторический unconditional capacity assertion в
  verify_results: активный C1 forecast ошибочно проверялся как обычный hold.
  Core StageDelivery/CapacityForecast и следующий inventory assertion уже
  различали эти случаи. Исправлена только ошибочная строка аудита: forecast
  требует действующие Storage rules, normal contracts по-прежнему требуют hold.
  Все3 escrow continuation exports после исправления прошли обе независимые
  проверки; полный Runner1 тоже прошёл. No gameplay waiver/deadline increase.
- Independent billing audit теперь отклоняет missing billing.json у E3,
  неожиданный E3 export/payment у старых профилей, начисление вне дороги;
  новые corruption mutations прошли вместе со всеми прежними Python22checks.

- Итог79e8589: Core40531261453 все12jobs success, включая required verify и
  actual E3 counters22ordinary/22Developer/48processes33exports22pairs.
  Windows40531262344/job122284378968 success. Код после79e8589 не меняется.
- Native2:139.025s/600,RU/EN actual mouse controls/partial/diskload/auto/paid,
  strict logs.662точных native inputs и probe SHA совпали с79e8589.
  Свежий legacy native:1132hash checks/35restores, strict logs passed.
- Actual old World25 freight save1176→1568: exact compared files и audit passed,
  оригинал retained, Schema25/SHA в save25-compatibility. No Billing backfill.
- Подготовлены849точных входных SHA,367financial inputs совпали с unit snapshot,
  полный список process records и финансовый пример из настоящего журнала.
  Далее только отчёт/статусы/evidence, push и ready PR68 без merge/F.

# F1 — восстановление и рабочий журнал

Автор: Шелестов Максим Евгеньевич.

Основа `06ccbe7c6e3e1cf25de8283b290ef72edd131d51`, основная ветка
`feat/economic-cycle-p00-p05`, содержит финальный E3 (`79e8589`, PR68).
Проверены успешные Core 40531261453 (12 jobs) и Windows 40531262344.
Ветка `feat/vnext-f1-delivery-offers`. Исходное рабочее дерево чистое.

Явное задание пользователя разрешает только F1/P72: каталог, расчёт, резервирование,
10 активных тиков, принятие/отказ, escrow, сохранение и интерфейсы. F2/P73 не начинать.
Принятое состояние не запускает рейс. До погрузки допускается полная отмена.
Новый профиль F1 для новых игр; старые resolved profiles/миры не подменяются.

Проверки и исправления в процессе:
- core-build-1: failure CS1503 (тип reverse stock holds), CS8602 (nullable requote);
  исправлены, core-build-2 success.
- tests-build-1/2: failure в новых тестах (StorageOf/AuthenticatedActor и xUnit2031),
  исправляются до финального gate. Исходные логи сохранены `/workspace/work/f1/checks`.
- Подготовленные деньги/география/ресурсы тестов не являются natural progression,
  долгосрочным балансом, ROI или проверкой устройств.

- services-tests-1:20 запусков,14pass/6fail — неверные ожидания теста (wood имеет
  volume2, а не1) и попытка позднего AttachControllers; исправлены тесты.
- services-tests-2 и unit-3:20/20pass, pure quote/hash, save/replay, inclusive10,
  pause, refund, reprice, carrier policy.
- native-1: ordinary client build прошёл; probe остановился на неназванной полосе
  ответа. Добавлено стабильное имя узла; исправление проверяется native-2.
- unit-4: build failure в новой подготовке300volume (with initializer), исправлен.
- unit-5:24 запуска,22pass/2fail — в новой fixture третьего заказчика отсутствовало
  третье место лавки; во второй fixture покупатель шёл к грузу после подписания и
  закономерно терял допустимый срок. Исправлены исходные условия/порядок подготовки,
  игровые сроки не повышались. Финальные24 ещё проверяются.

- unit-6:24/24 ordinary pass, реальные300volume→5рейсов и два разных заказчика,
  paid-pickup reuse, no-money atomic rejection. Core и24 Developer также прошли
  в первом CI-снимке30a98b4 (финальный CI ещё ждёт следующих правок).
- native-2: RU/EN прошли все реальные клики/диск/save/queued pause/refund;
  после визуального просмотра улучшены подписи координат, пустые списки и описание ETA.
- Python first:1failure у старого assertion про объединение трёх suites;
  расширен на четвёртый services с сохранением disjoint/exhaustive и старых9 partitions.
  Lab first:7socket PermissionError без network entitlement; повтор с loopback доступом.
- Добавлены независимые corruption tests цены, числа рейсов, дублей lease/accept/funding,
  ранней выплаты и утечки public event. Runner audit расширяется на новые реальные fixtures.

- Native-3: CS0108 — formatter Position скрывал Control.Position; переименован
  Coordinates. Проверка сборки не считалась успешной; сохранены process record и
  build logs в attempts/native3. Изменено ошибочное UI-пояснение про overflow:
  переполнение запрещает новые заказы, не отменяет уже обещанную поставку.
- При финальной сверке §2 выявлен обязательный paused response barrier. Добавлен
  обычный authenticated envelope на текущем quantum только для решений по уже
  эксклюзивной заявке (не для создания/конкуренции). Tick/physics/production/wages
  не продвигаются, общий8command budget сохраняется, samples cash обновляется.
- unit-7:24pass/1fail — новый save поймал NotCommittedTick; добавлена узкая проверка
  linked committed service control events. Незавершённые экономические фазы по-прежнему
  нельзя сохранить. unit-8:25/25pass, включая partial-quantum save/retry/replay.
  Финальный прогон также закрепляет assertion cash sample и общий command budget.

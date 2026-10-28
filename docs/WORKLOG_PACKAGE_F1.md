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

- unit-9 выявил CS1061 в новом budget guard: поле результата называется AppliedTick,
  исправлено. CI f589453 также остановился на этой ошибке, не считается pass.
- unit-10: bounded build timeout124 на180.039s, игровых assertions не было.
  Deadline не повышен; выполняется отдельная bounded подготовка tests build и
  повтор обычного полного gate. Ограничение/исходные process records сохранены.
- NPC публикует свободную пустую unbound технику обычной командой; при плане своей
  логистики исключает ServiceReserved. Реальные рейсы/оплата частей остаются F2.
- Independent audit дополнительно суммирует service stock/money commitments,
  чтобы несколько машин не могли скрыть нехватку совокупных резервов.

- unit-11:26/26 pass после bounded retry, deadline180 прежний.
- В финальной проверке поставочного согласия выявлен двойной прогноз: объём уже
  включён в underlying DeliveryContract. Quote теперь исключает текущую часть из
  «других ожидаемых», не создаёт второй incoming/capacity hold. DTO чужого получателя
  больше не предлагает его как доступный источник без paid-pickup права.
- unit-12:27/27 pass (14 lifecycle/12 rights/1 competition). Новые F1 classes
  разделены на естественные disjoint partitions с прежним120s each/780s aggregate;
  previous9/E2/E3 partitions и deadlines не менялись. Подтверждены quota/consent,
  prepared quantity, ручной double-delivery rejection и полный возврат quota.

Финальный runtime/test/CI снимок ba0f348a1c5cb3ff52428b6b38b219dbc75b9ced.
- Final runner:76process/54exports/36pairs,182.764s при600, все independent audits pass.
- Final native: RU/EN реальные callbacks, immediate paused responses и disk save,
 61.655s;642 inputs matched stage, no missing inputs.
- Actual World26 сохранение E3 до первой пени: t1120→3124, все exports exact;
  независимые деньги/капитал pass. Старый resolved config не включает Services.
- Legacy native первый вызов использовал ошибочный --reportfile: игра прошла,
  verifier не нашёл требуемый файл и failed. Повтор с правильным --report прошёл
  строгий verifier:1132 hashes/35 restores и frozen voting boundaries.
- Python24/Lab37 pass. Весь runtime/test/CI source закреплён852 SHA256,
  финансовые82 unchanged; новый world E3 отличается только Services.
- Итоговые CI checks и mergeable state фиксируются непосредственно перед final.

Итог: Core40628448975 все15jobs success, включая final verify и отдельные
F1/E2/E3 unit/Developer/runner gates; Windows40628448996 success на ba0f348.
CI F1 runner также76/54/36, отчёт совпал с local. Current runtime/test/CI852 SHA
не изменены после проверок. Только итоговые docs/evidence следуют отдельным коммитом.
PR69 готовится к review, merge не выполняется. F2 не начат.

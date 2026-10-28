# F2 — рабочий журнал

Автор: Шелестов Максим Евгеньевич. 28.10.2026 (Europe/Samara).

Основа25b6a38 основной feat/economic-cycle-p00-p05 — merge PR69, содержит полный
F1/8cd9e08 и проверенный runtime ba0f348. Начальное дерево чистое. Ветка
feat/vnext-f2-delivery-runs. Явное задание F2/P73 разрешает работу вопреки
историческому «F2 не начинать»; G1 не начинать. Автоматического merge не будет.

Переиспользовать один Vehicle.CargoInventory, E2 TrafficTravel/quanta и реальные
погрузочные средства, E3 monetary transaction/holds, F1 order/quote/escrow/lease,
товарные pickup/delivery contracts и C1 overflow. Новое execution state opt-in,
старые resolved profiles/saves не переключать. Новые игры используют отдельный
F2 профиль; World28 ограниченно читает старый27 без execution state.

План реализации:
- Физический run отдельно от immutable F1 agreement. Manual owner driving +
  обычные at-location Pickup/Unload, NPC через те же команды. Numeric automation
  только с настоящей группой capacity1/headcount0 при создании, общая labour allocation
  и фактическая зарплата за рабочие тики; без бесплатного водителя/телепорта owner.
- Shared source/cargo/return holds; Q=source remaining+loaded total+released,
  cargo=loaded-delivered-returned, одна машина/один груз. Каждая фактическая выгрузка
  создаёт отдельный pro-rata service payment; cumulative integer division,
  последний остаток по полной Q. Late20% только late gross, с переносом дробных BP.
- Cancel stops future loads; carried cargo physically finishes or returns. Service
  deadline сам груз не удаляет; customer force-close после deadline+1200 (>).
  Underlying goods expiry использует прежний money settlement, перевозимый остаток
  возвращается/довозится по действующим правам. Unused service escrow refund once.
- Расширить endpoints: стационарное storage добывающей техники как authorised point,
  own import pickup как source-only; capital terminal как destination только backed
  delivery contract. Ground/backpack/vehicle cargo не сторонние service points.
  Возврат paid foreign-source cargo — в согласованный собственный товарный inventory
  заказчика, без обратной фиктивной продажи/изъятия денег у продавца.
- Защитить source/destination/return points от сноса/несогласованного движения;
  accepted receipt допускает C1 forecast overflow. Своя terminal/mining logistics прежняя.
- Проверить реальные команды, partial/late payment/cancel/overflow/save in transit,
  native RU/EN UI, независимый аудит и все старые required suites/Windows.
  Heavy local gates serial, deadlines не повышать, failures сохранять.

Существенная совместимость: у старых F1 товарных сделок TTL120/180; они не будут
скрыто продлены. Для нового F2 профиля минимумы §16 — pickup1200/delivery2400.
Сроки услуги и основной сделки различаются; истечение основной сделки не даёт
права уничтожить перевозимый груз или автоматически выплатить service fee.

Промежуточные проверки и исправления (сохранять как failures, не считать pass):
- core-build-1: ошибочная вставка optional return-hold в ExpireDelivery и неверное
  имя терминального свойства; исправлено. tests-build-1: xUnit2012, исправлено.
- execution-smoke-1: InvalidOverflowProvenance при пятом рейсе. Добавлена проверка
  service/return provenance по фактическим receipts/events; smoke-2: 3/3 pass.
- settlement-1: 5/5 pass (late, cancel return/finish, force-close boundary/pause).
- unit-1/points-2: неверная prepared geography частного ресурса (нужен реальный
  public cell в district); unit-2: helper Single для двух wood sites. Исправлены
  fixtures без ослабления map validation.
- unit-3: реальная блокировка грузовика stationary extractor на одной клетке.
  Добавлен frozen adjacent access stop, без исключения collision/push/телепорта.
- После review добавлены ручной takeover числовой машины между центрами клеток,
  остановка numeric handling без работников, paid foreign cargo return/expiry,
  раздельные goods/service payouts и independent corruption checks.

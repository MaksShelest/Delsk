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

Итог 28.10.2026, Europe/Samara:
- Native first build: отсутствовал using Simulation.Hosting в partial panel;
  исправлено. Нативный UI повторно проверен после исправления устаревшей фразы
  «груз ещё у источника» во время разгрузки. Final71.722s,677 matched inputs.
- unit-5: в новом receiver-case владелец ушёл к warehouse перед SetWage;
  правильный physical Move к машине добавлен, location policy не ослаблялась.
- unit-6:27/27 pass. После review добавлены30 итоговых случаев: partial receipt
  cancel keeps earned fee, Trakt rejects crew, return==recipient pays real delivery.
- Runner-1:112 process checks/save-load comparisons passed; independent accounting
  остановлен новым строгим правилом return==recipient, exit1,563.101/600s.
  Это не timeout и не pass. Исправлено без повышения deadline; runner-2:
  116checks,84exports,56pairs,548.948/600s, все независимые аудиты пройдены.
- Final ordinary30/30, CI ordinary30/30 иDeveloper30/30 на96ebac6. Core run
  40643810656 все18jobs success, Windows40643810551 success. Python28/Lab37 pass;
  первая Lab-попытка без loopback permission дала7 PermissionError, затем все37
  прошли в подготовленной сети. Никакие tests/partition/deadlines не ослаблены.
- Реальный F1 World27 accepted.save.json b42292d0… t258→288 load/replay точны,
  old profile не переключён. Source869, native677, old financial82 unchanged.
- Локальный Git push не имел действительной авторизации. Использован имеющийся
  GitHub connector: create_tree/commit/ref с expected SHA. Remote и local trees
  побайтно совпадали; локальные refs синхронизированы только после проверки tree.
  PR70 draft до финальной проверки, затем ready; auto merge не используется.
- Существенная граница: товары сохраняют подписанный срок. В F2 profile минимумы
  1200/2400, динамическая формула товарного договора из §16 не заменяется скрытым
  продлением при выборе машины. Too-long service rejected по F1 ETA/rights check.
- Итоговые docs/evidence после runtime96ebac6 не меняют869 проверенных source inputs.
  F2/P73 завершён, G1 не начат. Следующий шаг — review PR70.

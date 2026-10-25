# C1: хранение, склады и временное переполнение

Автор: Шелестов Максим Евгеньевич.25.10.2026, Europe/Samara.

Исходная база —45fae36, актуальное merged состояние с PR58/B1 и PR59/B2;
начальное рабочее дерево чистое. Фактически существовали строгие normal inventories,
объёмный ручной перенос, товарные права/holds, склад200, прежние buy-delivery contracts
и Freight. Хранения на пустой земле, timed warehouse upgrades и overflow не было.
Повторно производство/рынок/маршруты/контроллеры не переписаны.

## Реализация

- Optional C1 rules; сохранённый ground inventory70 для собственной пустой клетки,
  общая вместимость разных товаров. Только прежний ручной backpack↔inventory transfer.
  Земля не получает статуса Building/trading/automatic delivery endpoint.
- Склад занимает одну клетку;200/300/400. Цены100/250/600 монет, реальные материалы
  и60/120/180 активных тиков строго из typed BALANCE_VNEXT. Одна tx фиксирует оплату
  и расход свободных материалов. Same-inventory identity при постройке/улучшении;
  операции/резервы и запасы сохраняются, вложения до завершения учитываются как WIP.
- Новые C1 buy-delivery contracts содержат capacity forecast вместо невозможного
  normal hold. Старые normal holds, escrow, supplier bond и оплата за фактический receipt
  сохранены. Разгружается и ручная поставка, и принятый Freight; normal room заполняется
  без вытеснения holds, остальное получает собственный OverflowLot/1200ticks.
- Сроки партий независимы; события сразу/600/120; обычное место спасает излишек
  в expiry/id порядке. Ручной rescue использует прежний VolumeWork и hold рюкзака.
  Если место освободилось во время rescue, его незавершённая часть отменяется со снятием
  hold, товар спасается в normal. Ранее уже перенесённое не дублируется.
- Только оставшийся излишек истекает явным sink/Lost. I05 учитывает normal+overflow−loss;
  спрос/капитал разделяют потери, внутреннее потребление и выпуск. Обычные выкладка,
  производство и собственные автоназначения не получают обход вместимости.
- Владельцу доступны реальные количества, normal holds, прогноз поступлений, риск,
  сроки и ход стройки; RU/EN карточки подключены к обычному Submit/AccessFor.
  Собственное HUD предупреждение; overflow/rescue события не раскрываются через
  public stream. Research journal сохраняет полную историю.
- World19, limited reader18 только без C1 config/state. Rules production-recipes-1,
  Session2/Envelope2 прежние; null/default новые поля не меняют legacy hash.
  Сохраняются стройка, партии/предупреждения, ручной перенос и его holds.
- `economy.c1-storage.json` воспроизводимо генерируется C# из проверенного A2+typed full
  balance, с отдельным payload SHA. Две новые карты/лабораторные presets используют C1;
  прежние5/defaultv6/A2 bytes и старые resolved saves сохранены.

## Проверки

Результаты запускаются ограниченными процессами через `scripts/run_check.py`:
restore/build180s, ordinary tests120s, Developer tests180s, отдельные runner30–300s,
Godot import120s/smoke30s, native150s. Aggregate gate ограничивает несколько таких
процессов; это не безграничный запуск.

После последнего исправления:1077/1077 ordinary,1083/1083 Developer,37/37 Python;
общий headless102exports/14replays/69exact-pairs. C1process24checks/14exports/
13exact-pairs,2293ticks. A1:43 process checks; A2:49. Negative Developer Release
проверка ожидаемо отклонена. Полные фактические результаты — в
[verification/storage-c1/checks.json](verification/storage-c1/checks.json).
C1 покрывают19 новых тестов: объём смешанного ground/права/торговля, все реальные
уровни/вложения/сроки, оплата после unload/normal holds, нормальные stock reservations,
автоматический грузовик, отдельные сроки/запрет повторного обещания, частичный rescue,
expiry во время переноса, одновременное освобождение capacity, предупреждения/пауза,
save/load/corrupt snapshots/replay, loss на границе периода спроса, production output cap.

Отдельный process gate проверяет точные bytes генерации C1 profile, физический
2293-tick сценарий, шесть checkpoint cuts (34/37/1084/1087/1690/2289), exact exports
continuous/saved/loaded/replay, товарные/денежные инварианты и public privacy.
Источники plank/steel/beam/concrete в этом сценарии явно подготовлены для теста.

Нативный Godot/Xvfb: реальные мышиные кнопки ground deposit/build, pause queue,
обычный disk Continue посреди стройки, warehouse overflow/rescue и собственное HUD
предупреждение. EN1100×800/RU800×720, проверены screenshot layout и clean logs.
Все2293 хеша C1 тиков совпали с отдельным настоящим runner. [RU ground](verification/storage-c1/ground-ru.png),
[EN overflow](verification/storage-c1/overflow-en.png). Проверочные классы
живут только в изолированном stage и запрещены AssemblyAudit в обычной сборке.

Реальный ранее созданный A2 World18 WIP-save загружен на tick50 с неизменным хешем
`8e1a85419d83a0581dcc71e20db6a7dc9aca3ad6dfc5432bad91a6ffb6efd7a9`.
Обе новые карты проходят real CLI3/6/10 NPC,60ticks, сохранение/replay/учёт/profile,
по32 process checks; A1/A2 CLI regression и exact B2 source snapshots также проверены.
Лабораторный браузер реально запускает по опыту на каждой новой карте с C1 profile.

## Ограничения и граница

C1 механизмы реализованы и проверены, но **обычная полная строительная прогрессия
на новых картах не доказана и ещё не обеспечена**: A2/C1 не содержат полных
производственных шаблонов/импорта plank/steel/beam/concrete. Это зависимость от D,
а не основание выдавать бесплатный материал или подменять требование древесиной.
Подготовленные sources/рюкзак2000 — только fixture, не новый ordinary balance.

C2/снос/возврат60%, новые цеха/XP/машины/доставка как услуга и новая NPC-стратегия
не начинались. В C1 нет операции уменьшения capacity: собственный начатый маршрут
защищён прежним normal hold. Случай внезапного shrink через снос не заявлен проверенным.
Чужие оплаченные pickup товары остаются в normal с существующим stock reservation;
overflow не используется как скрытый collateral. Нового совместного multi-owner
warehouse механизма не добавлено.

Windows/Android устройства, длительный баланс и полная натуральная цепочка материалов
не проверены. Linux/native/software gates не объявляются аппаратной приёмкой.
P67/P74 и весь пакет C целиком не закрываются этой итерацией. Следующая отдельная
итерация — C2 по явному заданию; автоматически к ней не переходили.

Использование: [STORAGE_C1.md](STORAGE_C1.md).

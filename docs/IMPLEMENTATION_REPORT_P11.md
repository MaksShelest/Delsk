# Отчёт P11: добывающая техника

Автор: Шелестов Максим Евгеньевич. Дата: 14.10.2026.
Ветка: `feat/extraction-equipment-p11`. База — пользовательский merge PR №4 `be5296c`. Исходная спецификация0.3 сохранена дословно, SHA-256 соответствует DECISIONS.

## Результат

Реализована одна законченная задача P11. Город продаёт малую/большую добывающую машину; доступны покупка конкретного уровня и upgrade. Техника размещается на узле, имеет конечный buffer. Личный режим занимает участника и даёт ускоренную добычу без экипажа/зарплаты; автоматический — числовую группу Extraction в общем рынке труда с production-группами. Покупка/upgrade не создают население.

Фактический персонал, отложенные ставки, мобильность, общий бюджет и обработка работы обеих ролей используют общие механизмы. minCrew допускает работу только при нужном N; пустой готовый экипаж может нанимать. Добывающий WIP и его место сохраняются на Off/возобновлении; режим Personal/upgrade при незавершённом автоматическом цикле отклоняется. Одна машина не работает в двух режимах одновременно. Автоматический buffer забирается обычным физическим Transfer; владелец может уйти, машина продолжает работу у своего узла.

Добавлены EquipmentTypes, команды BuyEquipment/UpgradeEquipment/SetEquipmentMode и параметр EquipmentId у Harvest, каталог/projections/hash/invariants, новый runner script `equipment`, fixture и23 новых тестовых случая. RulesVersion=`equipment-1`. Полные допущения — DECISIONS §10.

## Фактические проверки

| Проверка | Результат |
| --- | --- |
| SDK/locked restore/Release build | SDK10.0.401, успех,0 warnings/errors |
| xUnit, встроенный test assembly runner | **106/106 passed**,0 failed/skipped; ранее83 |
| Новый equipment сценарий | **200 ticks**, exact replay,18 единиц добыто/продано |
| Прежние cycle/recovery/development/labor | **62/32/465/291 ticks**, прежние денежные/товарные ожидания сохранены |
| Независимый Python verifier пяти прогонов | Деньги, товарные потоки, capacity, население каждого тика и settlements подтверждены |
| Godot .NET4.7.2 client restore/build/import + Linux smoke | Успех, все62 хеша технической fixture совпали; искусственная pause/resume проверка |
| Источник требований/diff | Исходный SHA-256 сохранён, git diff --check успешен |

Малые метаданные фактически запущенных команд, timeout/exit codes, test counters и hashes: [verification/p11/checks.json](verification/p11/checks.json). Windows/Core CI этого PR публикуются с фактическим результатом в PR; таблица не выдаёт их за уже выполненные локальные проверки. Tests запускались как `dotnet tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll`, не как локальный `dotnet test`.

Проверены: деньги/места/no population creation, оба уровня и неполная личная партия, присутствие и права, actor/machine lock, command dedup, rollback вместимости/overflow, minCrew/найм с0, задержка ставки, NoMoney, buffer full/разгрузка, WIP pause/resume/no extra goods, gradual departure/no idle wages, upgrade ID/stock/N/wage preservation, общий бюджет production+extraction, приватные DTO/copy catalog, wrong configs, replay и elapsed scheduler с теми же ticks/hashes. Человеческий интерфейс и реальное устройство этими тестами не проверяются.

## Числовой сценарий

Начальные cash100000 каждому, population7, каталожная ставка25 minor/worker/tick. Первый покупает малую машину15000, лично добывает4 wood за4 ticks; upgrade60000, лично6 wood за2 ticks. После найма5 автоматических работников большой агрегат даёт6 wood за2 оплаченных интервала, зарплата250. Off сохраняет людей в момент команды, затем они постепенно уходят. Владелец забирает6 из buffer, перевозит все16 wood в терминал, получает8000. Второй покупает малую машину15000 и лично добывает/перевозит/продаёт2 grain за600.

Итог200 ticks: first cash32750, second85600, Treasury90000, WagesSink250, CapitalBudget7400; сумма216000 равна initial200000+external16000. Добыча/экспорт wood16 и grain2, складских остатков нет. N обеих групп0, unemployed7, population7. NoWorker/individual wallet не создавались. Exact hash нового прогона: `28bd7a7004a3baf4cb5c348952d4a112996757df8a77343828f5329985f889fc`.

## Что ещё не выполнено

Полное сохранение/загрузка — **P12**, самостоятельные контроллеры — **P13**, рынок/контракты/автоматическая доставка/политика — **P14–P21**; эти задачи сохранены в плане. `final-state.json` остаётся inspection export; replay идёт из initial scenario и записанных команд. Текущий Godot только playback прежнего цикла, без карточки техники/зарплат. Новый equipment script не запускался как интерактивная Godot-сцена.

Новые APK/Windows EXE не экспортировались; эта версия на телефоне не запускалась. Android device gate открыт. Windows Godot CI, если успешен, подтверждает именно62-тактовый technical replay/компиляцию общего ядра, а не выполнение equipment сценария на телефоне или готовом EXE. Rider здесь не запускался; инструкция локальной проверки сохранена.

## Готовность и следующий шаг

**Ядро уже можно тестировать локально в коде.** [PROTOTYPE_STATUS.md](PROTOTYPE_STATUS.md) разделяет нынешний стенд, ближайшую автономную тестовую экономику после P12/P13 и полный игровой/исследовательский объём. Это ориентиры по зависимости задач, без обещания календарной даты.

Следующая итерация — **только P12**: полный versioned checkpoint/load, atomic file/backup, восстановление завершённого tick без offline catch-up, всех queues/dedup/holds/PRNG/N/wages/credits/личных machine links/WIP/windows/plots/session; отказ при corruption/unknown version/invariants. Проверить continuous vs resumed на пути, личной/автодобыче, production WIP, найме/зарплатах, торговом окне и брони. Затем P13 даст первый новый прогон с3 автономными NPC. Не добавлять эти контроллеры одновременно с хранением.

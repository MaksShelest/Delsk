# D3 — производственный контент и режим бога

Автор: Шелестов Максим Евгеньевич.26.10.2026, Europe/Samara.

Основа28a41a7 — слитый PR64/D2. В ней уже работали C1/C2 хранение/снос,
D1 параллельные цеха, D2 общий отраслевой опыт/задания/платные корпуса и отдельное
оснащение. Каталог34/23 присутствовал, но предприятия предлагали только начальные
рецепты; конечного импорта, полного открытия и локальных V2/V3 не было.
Developer XP уже существовал в D2, но изменение запрещалось на неполном travel quantum.

Восстановление подтвердило прежнюю ветку `feat/vnext-package-d3`, все изменения,
сводку и результаты. До новой разработки весь результат сохранён и отправлен
коммитом `ac9e706` с WIP-маркировкой; открыт один draft [PR65](https://github.com/MaksShelest/Delsk/pull/65).
Восстановленный незавершённый снимок не выдаётся за финальный gate.

Реализовано:

- Отдельный checked D3-профиль для новых партий двух vNext-карт: шесть отраслей,
  34 товара,23 V1 и их46 V2/V3. Старые profiles/карты не подменены. CLI генерирует
  профиль из прежней A2-основы и typed BALANCE, проверяет provenance/SHA.
- Открытие по общей XP-технологии и локальное платное оснащение. Реальные
  ингредиенты, service/payroll, общий finite input/output, версии и WIP используют
  прежний production executor и EconomyTransaction. Нет второй экономики.
- Конечные внешние запасы,125% fixed-reference price, масштаб состава, расписание
  и two-package cap. Покупка оплачивает внешнего поставщика, товар остаётся в
  собственном terminal pickup до реальной передачи и перевозки. Imported отдельно
  от Produced/G и учтён в изменениях запасов. Асфальтовый bootstrap заканчивается
  после первого фактического корпуса материаловIV; покупной товар сохраняется.
- Восемь типов добывающей техники, малая/большая; прежние права, числовой персонал,
  движение и handling, без бесплатного найма. Полный автопарк/дороги не добавлялись.
- Модернизации V2/V3 отдельно для каждого цеха, реальные деньги/материалы/120 тиков,
  защита повторных команд, старые рецепты и условия WIP. C2 ждёт проект и учитывает
  его реальные вложения один раз; отдельной отмены модернизации нет.
- Реальные RU/EN Import/Workshop UI, доступ и прежнее управление временем;
  NPC обычными командами оплачивает импорт, снабжение и модернизацию. Специализация
  рассматривает все начальные рецепты; при недоступной муке выбирается зерно→мука,
  а не неисполняемый хлеб. Общая оптимизация стратегии не входит в D3.
- Режим бога переиспользует D2 XP API. Деньги/товары/XP применяются в открытом
  окне на текущем travel quantum без Step; резервы/ёмкости и запрет вмешательства
  в замороженные выборы сохранены. Квант записывается в audit и replay.
- World23, limited22/21/20/19/18, сохранённый resolved config, explicit validation,
  hash/replay и researcher production-content.json. Own immutable observation;
  private import receipts/модернизации не попадают в public stream.

Проверки и их точная область фиксируются в `docs/verification/production-d3/`.
Сохранённые green результаты повторно проверены по exit metadata/TRX и входам;
изменённые сценарии и финальные сборки проверяются заново. Логи и большие выгрузки
остаются в `/workspace/work/delsk-d3`, не включаются в сборку или Git.

| Проверка | Подтверждение |
| --- | --- |
| Восстановленный ordinary gate |1191;102exports/14replay/69pairs, manifests сохранены |
| Финальные D3 unit/integration |38passed; ordinary1197 в совокупности с явно указанным reuse; все23 V1 исполняются; локальные V2/V3, imports/cutoff, NPC, дедупликация, сохранение/replay; текущий TRX в final-gate.json |
| Developer | recovered1203, затем свежий полный1208 и negative Release gate; developer-latest.json |
| Независимый аудит | import_audit.py сверяет stock/schedule/cap/цены/Actor/command/ledger/physical pickup/flows/privacy;15 corruption cases и контроль отсутствия future-cutoff |
| Обычный старт | compact control map:200 монет/пусто → ручной заработок → платная земля/импорт/стройка60 → первый выпуск tick2094; save/replay exact |
| Реальные vNext-карты | small3NPC300/large10NPC180, actual harvest/sales/imports, checkpoint/controller continuation и world replay exact; npc-maps.json |
| Native UI | четыре EN1100×800/RU800×720 сценария с настоящими mouse controls/командами, оплатой/120ticks/V2↔V1/физическим pickup; screenshots и native-recovered.json |
| God mode |20Debug +20standard ExportDebug, реальный D3-профиль, money/stock/XP на frozen quantum5, audit и cleanup; не визуальная/hardware приёмка |
| Godot Linux |1132runner hash comparisons,35restores +frozen voting10; linux-recovered.json |
| CI |отдельный D3runner job, обязательный verify требует core+developer+production; Windows serial import только в disposable checkout с восстановлением project.godot bytes |

Существенные уточнения и ограничения:

- §5 требует200 стартовых монет, хотя пример рассчитан от350. Применено200;
  лишние150 не выдаются. «КомбинатIV» для окончания асфальта — первый реальный
  завершённый корпус материаловIV, не только XP; снос достижение не отменяет.
- Prepared recipe/modernization fixtures намеренно дают источники материалов,
  начальные корпуса/персонал и ускоренный XP. Это не доказательство natural endgame.
  Compact ordinary-start сохраняет экономические числа, но имеет контрольную
  географию и выключенный modal ballot. Естественная лестницаI→V на обеих полных
  картах, длительный баланс, аппаратные Windows/Android, FPS/audio и реальная LLM
  не проверены и не объявляются выполненными.
- После сбоя Developer base был повторно найден успешным, а оставшиеся части
  завершены с revalidated TRX. Ранние timeout/failed audit не считаются pass.
  Import audit первоначально ошибочно приравнивал весь ExternalOut к retired
  demand; новый независимый расчёт сохраняет прежние funding checks и отдельно
  доказывает import payments. WIP Windows import завершил assets, но завис на
  Mono-editor exit; исправление CI не принимает timeout за success.
- Дороги, поставки полного автопарка, счета/услуги и общий стратегический баланс
  следующих пакетов не реализовывались. Пакет E автоматически не начинать.

Инструкция: [PRODUCTION_D3.md](PRODUCTION_D3.md). Этапы и воспроизводимые
команды: [WORKLOG_PACKAGE_D3.md](WORKLOG_PACKAGE_D3.md), final-gate.json.

# D2 — контекст и этапы

Автор: Шелестов Максим Евгеньевич.26.10.2026 Europe/Samara.

## Этап1: аудит и выбранная граница

Основа29cb58b — слитый PR63/D1; дерево было чистым. Ветка feat/vnext-package-d2.
D1: настоящие независимые партии/группы/оснащение60ticks, без XP/quests/expansion.
C1 StorageConstruction и C2 cancellation/salvage переиспользуются. Не начинать D3:
новое задание явно переносит туда полный контент и модернизации рецептов, несмотря
на старые пометки D1. Старые5карт/config/точные hashes сохранять.

План реализации:
1. XP по actor+industry, production grants только за actual completion; задания
   по уже подписанным финансируемым delivery contracts, только фактический приём,
   two slots/1200review/5,15,30,60batches и50/100/200/500XP, first once.
   Привязка к договору предотвращает выдуманный спрос/двойное обещание бюджета.
2. Явные контуры и права будущего платного выкупа; timed initial production build
   с ground70, расширение материалами из input; уровни клеток, фактические затраты,
   безопасные completion/cancellation через существующий C1/C2 executor.
3. Profile/UI/NPC, snapshot/hash/replay/exports, meaningful tests, gates и PR.

Решения: XP не требует дополнительного production count. Вторая фабрика получает
только общую известную технологию, физически начинает с I. B1проект оплачивает
реконструкцию всех затронутых клеток до целевого Lcell; до завершения старый корпус
остаётся прежним. Новые места оснащаются прежней D1командой за полную цену.
Задания не создают товаров или финансирование; купленные товары допустимы.
Натуральные полные цепочки/импорт и V2/V3 не входят в текущий D2.

Артефакты проверок хранить /workspace/work/delsk-d2. SDK env:
source /workspace/work/delsk-tablets-v1/env.sh. Каждая проверка через run_check.py.
Ordinary xUnit120s, Developer180s; тяжёлые suites изолировать, иначе timeout.
После каждого крупного этапа дописать реальные результаты и следующие действия.

## Этап1: реализован и целевой gate пройден

IndustryDevelopment.cs/DevelopmentContracts.cs: production XP строго actual
completion, preflight overflow до EconomyTransaction; grants dedup source+ID.
Задания привязаны к подписанному funded delivery contract, одна привязка на
contract, baselineDelivered исключает прошлую выдачу. FirstDelivery один раз,
дваслота,5/15/30batch50/100/200;60batch500 только explicit public project с
минимум3600remaining ticks. Деньги/спрос не создаются. Полные auditchecks на
capture/restore; на обычном tick нет history×events scan. Own immutable DTO.

World22 optional DevelopmentState/config; limited21 читает без D2fields. Прочие
18/19/20 guards сохранены. ControllerDevelopment ownonly, public events private.
16/16 D2tests прошли (4.614s/120), build0warnings. Порогиactual599..40000,
торговый путь12реальных поставок→600XP без производства, purchased goods через
actual market pickup, partial/expiry/cancel/two slots, rewardtiers и save/replay,
тамперduplicate grant, сложениеproduction+quest и сохранениеXPпослесноса.
Промежуточный сбой12поставок был StaleOfferWindow в test helper; исправлен чтением
актуального окна (правило Core не ослаблялось).

Следующий этап: Level.Footprint, реальные контуры/claimed rights и paid land,
extension существующего StorageConstruction DevelopmentProject, C2 per-cell
salvage/cancel, profile D2. UI/NPC/gates ещё не выполнены. D3 не реализовывать.

## Этап2: физические проекты и права — целевой gate28/28

EnterpriseExpansion.cs: реальные Level.Footprint1/3/4/5/6 и80/120/160/200/240,
платный BuyExpansionLand с базы, claim всего контура после начала проекта;
прочие участники не покупают/не бронируют, свой другой объект тоже не строится
в назначенном контуре. До старта чужие ownership/reservations защищены.

StorageConstruction расширен optional DevelopmentProject. Один прежний executor
AdvanceStorageConstruction выполняет warehouse + initial production60ticks +
расширение120/180/300/480. Initial материалы из ground70; расширение из input,
цена/материалы из typed BALANCE. До completion старый корпус/capacity прежний.
Каждая новая клетка проекта держит весь GroundCapacity как development:JOB hold:
нельзя положить туда груз, который был бы потерян при присоединении. Reverse hold
ownership в CheckpointValidation. Hold освобождается той же tx при completion/
C2cancel. Initial build создаёт первый N0/off цех; расширение не добавляет линий/
людей, лишь места; outfit прежний D1.

DevelopmentBuilding сохраняет полный contour + реальные DemolitionCell levels.
C2 quote использует реальные cells; отмена upgrade оставляет старый корпус,
возвращает60% project materials в input/overflow. Fee относится ко всей площади
отменяемого проекта/target level, как прежняя C2 warehouse cancellation. Receipt
разрешает one receiving inventory для many project cells. Отмена/снос снимают
неиспользованные contour rights, купленная земля остаётся своей. Все реальные
вложения учитываются Constructed/Upgraded/Outfit command journal, без double.

PrepareEnterpriseExpansion/CancelExpansionPreparation remote: finish WIP и stop
новых партий, без закрытия товарных обязательств/логистики. Сохраняется Preparing,
снятие после успешной оплаты проекта/сноса, отмена не auto-resume. Не пользоваться
C2подготовкой к сносу ради обычного upgrade. Зарплата/инвентари прежние.

Own DTO DevelopmentObservation/DevelopmentProjectView — immutable (не выдавать
StorageConstruction.Materials[] контроллеру). Quests offer review windows из
Tick/1200, stale OfferWindow reject; активное задание хранит contract deadline,
не перезапускается при review. Новый funded contract может дать выбор в текущем
review window. Explicit project60batches500XP требует объявленного CapitalOfferId
в rules.PublicProjectOffers и>=3600remaining; не создаёт финансирование/заказы.

VNextDevelopmentProfile.Create(A2,balance) -> D1+C1+XP/уровни/контуры. Генератор
--export-development-profile добавлен; файл пока в /workspace/work/delsk-d2/generated/
economy.d2-development.json (68k). Его надо скопировать config/vnext и подключить
в client/default designed maps/lab. Старые A2/C1/D1 профили не менять. В D2активны
ТОЛЬКО D1базовые рецепты; advanced content/V2,V3 остаются D3 по последнему user.

Tests:16XP +11geometry +1prep =28, последний d2-project-gate15.368s/120,build0warnings.
Normal XP tests используют реальные xp/unit1/2, в т.ч.12реальных поставок→600XP без
производства. Physical project fixture ЯВНО accelerated XP40000/plank и labor1tick,
prepared material sources/personal2000: изолирует геометрию, не normal balance.
Source profile costs/caps/thresholds отдельно проверены against BALANCE. Исправлены
ошибки ожиданий теста: реальная начальная woodworking цена17000,30wood+3steel;
снос после II+outfit даёт15plank+5steel+18wood (не ранний неверный15000/20wood).

Промежуточные commit ccad0c0(XP) и следующий stage2. Осталось:
- Общая технология в SetRecipe/Workshop validation/output compatibility: current
  code ещё использует physical b.Level, надо при Development использовать global
  Experience tech + поддержанный активным контентом IndustryRecipeAvailable; D3
  рецепты не активировать в profile.
- Подключить PlayerView.Development, UI development/quests/initial-ground builds
  и cancellation через реальные команды. C2 quote provider уже существует.
- NPC: quests при own funded contracts, prepare/land/materials/upgrade, initial
  ground deliveries несколькими рейсами (20capacity), обычные команды/allowlist.
  Старый NPC BuildProduction из backpack отключить ТОЛЬКО для D2.
- При необходимости Developer XP только conditional Developer tools/audit, никакой
  обычной AddXP команды. Изучить Interventions.cs/DeveloperTools/SessionResearch.
- Runner D2 сценарии, development.json export + compare list, checkpoint/replay/
  privacy, полный bounded gates, native RU/EN/размеры, Godot hash/audit, docs/PR.
- Старые actual D1World21 checkpoints есть /workspace/work/delsk-d1/process-exact.
- Новые поля по умолчанию omitted; ControllerBuildingLevel ConstructionTicks
  заполняется ТОЛЬКО при Development, чтобы старый C1/D1 observation hash не менять.

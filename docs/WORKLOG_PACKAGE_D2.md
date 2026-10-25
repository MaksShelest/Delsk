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

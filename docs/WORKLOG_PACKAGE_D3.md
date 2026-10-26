# D3 — восстановление работы

Основа: 28a41a7, PR64 слит; чистая копия. Ветка feat/vnext-package-d3.
Пользователь разрешил только D3, пакет E не начинать.

Этапы: (1) checked D3 profile, все рецепты/отрасли, конечный импорт и техника;
(2) платные локальные модернизации V2/V3 на прежнем исполнителе, save/hash/права;
(3) UI/NPC и безопасные немедленные Developer-вмешательства;
(4) bounded gates, отчёт, commit/push/PR без merge.

Повторно использовать D1 production/персонал/WIP, D2 XP/контуры/проекты, C1
инвентари/переполнение, C2 фактические вложения/снос. Старые профили не менять.
Подготовленные состояния отделять от естественного старта без бизнеса.

Текущий статус: изучены AGENTS, спецификация §§5–9,16,19–20, результаты D2,
основные Core/Hosting/UI механизмы. D3 пока не реализован и не проверен.
Решения: конечные внешние запасы у терминала, оплаченный товар в собственном
пункте выдачи; перенос через прежний timed Transfer, без телепорта в рюкзак.
V2/V3 — обычные RecipeDefinition с явным соответствием базе/версии; общая
технология плюс отдельная оплаченная модернизация каждого цеха.

## Этап 1–2: реализация, до итогового gate

Добавлены VNextProductionProfile (отдельный default новых designed maps, прежние
профили сохранены), полный каталог V1/V2/V3 через прежний production executor,
конечные платные imports с pickup у терминала и прежним timed Transfer; 8 типов
добывающих машин с 2 уровнями, без бесплатного найма. Imports отдельно от G.
Локальные модернизации 120 ticks, money/materials один раз; общая XP-технология
не оплачивает второй цех. Подготовка/снос ждут проект, возврат учитывает его
фактические материалы. World23 limited22/21/20/19/18, optional fields omitted.
UI ImportPanel/WorkshopPanel, NPC ordinary decisions/staging/recipe selection.
God edits допускаются на сохранённом travel quantum, без шага экономики; квант
в research audit/replay. Обычных intervention API не добавлено.

Первые meaningful tests: все23 V1 исполнились, 30/33 прошли в промежуточном
запуске. Три failures были в fixtures: два сравнения не подавали rejected input
в копию, economic-start упирался в human election dialog. Исправлено; это ещё
не финальный pass. Длительный набор делится на D3 и D3-start без пропуска тестов.
Команды/logs/fixtures вне Git: /workspace/work/delsk-d3. Client Debug build прошёл.
Следующее: окончательные focused tests, native ProductionProbe, God partial-quantum
UI, полный headless/developer gate, runner D3/C1/C2/D1/D2, Linux smoke/hash, отчёт.
Нельзя объявлять пакет закрытым до фактического gate.

Противоречие §5: 200 стартовых монет vs пример, вычисленный от350. Применено
обязательное правило200, лишних150 не выдавать. Compact ordinary witness имеет
простую контрольную географию и отключённый modal ballot, экономические числа
не ускорены. Prepared recipe/modernization cases не доказывают natural endgame.

## Восстановление после отключения,26.10.2026

Проверено повторным доступом к файловой системе: прежняя рабочая копия и все
изменения доступны, HEAD28a41a7, ветка feat/vnext-package-d3, commit/push ещё
не делались. Рабочие процессы dotnet отсутствуют. Сводка и каталоги проверок
/workspace/work/delsk-d3 сохранены. Не начинать повторную реализацию.

Подтверждённые результаты скопированы в docs/verification/production-d3:
1191 ordinary (1067+55+36+32+1),102exports/14replay/69pairs; native RU/EN4flows;
20 God Debug +20 standard ExportDebug; Linux1132hashes/35restores+frozen10.
После полного ordinary gate добавлен один meaningful NPC V2/V3 test, его
отдельный финальный запуск прошёл. Это пока не новый полный suite1192.

Developer: первый base suite завершился timeout180 при параллельной нагрузке;
финального developer-check.json ни у первого, ни у повторного запуска нет.
Повторить последовательно, не увеличивая deadline. Новых игровых assertion
failures по имеющимся журналам не обнаружено, но это не Developer pass.

D3runner: 8cases/32exports/24save-load comparisons выполнились; итоговый
verify_results.py упал на старом предположении, что ExternalMoneyOut состоит
только из CapitalDemandRetired. Добавить строгий отдельный учёт ImportPurchase
с проверкой stock/receipts/цены/flow. Само падение аудита пока не устранено.

Короткие actual-map NPC: small3NPC300ticks checkpoint150, large10NPC180ticks
checkpoint90 прошли, с explicit --human-ballot self. Есть сбор/продажи/импорт,
полное развитие не доказано. Их load/replay и итоговые audits ещё впереди.

Первый recovery commit — намеренно НЕЗАВЕРШЁННЫЙ D3 для сохранности вне среды.
Осталось: import audit, Developer, необходимые итоговые regressions/assembly
audit, docs/отчёт и PR. ПакетE запрещён. Не объявлять D3 закрытым по этому commit.

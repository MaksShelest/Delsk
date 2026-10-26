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

История первоначального анализа: изучены AGENTS, спецификация §§5–9,16,19–20, результаты D2,
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

## Этап 3: сохранность и оставшиеся gates

ac9e706 отправлен в origin/feat/vnext-package-d3; draft PR65 открыт. Сверено
через git ls-remote: коммит доступен вне среды. Это намеренно незавершённый
снимок; subsequent commits завершают D3, PR один, merge запрещён.

Подтверждён прежний base Developer1078 exit0+TRX, источник/assembly старше
начала проверки и не изменялись. Остальные4части выполнены последовательно:
55+36+33+1, итого1203; forbidden Release ожидаемо отвергнут. Источник изменён
после этого только для найденных ниже случаев: нужен свежий финальный gate.

Import audit: добавлен независимый import_audit.py, проверяет конечные запасы,
периоды/лимит2пакета, цену/Actor/команду/внешний платёж, private receipt,
физическую выдачу и current flows. 15 corruption cases + прочие проверки;
реальные32выгрузки/24пары повторно прошли, checked profile дважды совпал.
Реальные small3NPC300/large10NPC180 продолжены из checkpoint и replay,
контроллеры exact, независимый audit/capital passed. Это короткие прогоны,
не полная натуральная индустриализация обеих карт.

Найдены реальные недочёты первоначального D3: §14 требует прекращать
bootstrap-пополнение асфальта после первого реального материалов-комбинатаIV;
покупки/остатки сохраняются, снос не перезапускает поставки. Исправлено и
проверено actual UpgradeBuilding + save/load/replay. Общая XP без корпусаIV
не прекращает bootstrap (явная интерпретация термина «комбинатIV»).
NPC начальная специализация теперь рассматривает все рецептыI (зерно→мука),
выбор рецепта не удерживает выгодный, но недоступный хлеб вместо доступной
муки. Новые meaningful NPC tests выполняют настоящие команды и выпуск.

WIP Windows CI40350309350: импорт ресурсов завершился, Monoeditor не вышел
за120s; artifact показывает EditorSettings/shutdown_adb_on_exit. Для disposable
CI checkout только: serial importer + явные editor/import/quit, finally
восстанавливает точные project.godot bytes. Таймаут/ошибки не игнорируются.
D3runner вынесен в отдельный bounded production job; required verify требует
core+developer+production, тесты не вычёркиваются ради deadline.

## Этап 4: итоговый локальный software gate

- Latest production tests38 прошли; в совокупности ordinary1197 с явно
  указанным reuse прежних base/integration/D2/compact-start. Последний compatibility
  guard сохраняет прежнюю D2 affinity, D3 использует всеV1; selected policy tests
  с явными -method выполнены отдельно (нулевой запуск class+method не считается).
- Свежий полный Developer1208,12Python checks(+15 corruption subcases),37Lab.
  Запрещённый Developer Release ожидаемо rejected; ordinary Debug и ExportRelease
  построены и AssemblyAudit проверил5assemblies, Release не содержит Debug entry.
- D3runner заново:9cases/39exports/30pairs/58process checks. Все independent
  balance/privacy/capital audits passed. Дважды генерируемый profile exact.
- Native RU/EN повторён с текущими Import/Workshopпанелями и runtime. Реальный
  ordinary compact start2094, полный Linux smoke1132hashes/35restores+frozen10
  повторён. God20+20 evidence переиспользован: обработчики/applies не менялись.
- Full-map3NPC300/10NPC180: new/load/replay +independent audits/capital exact.
  До естественного полного развития эти короткие прогоны не дошли.
- Evidence: docs/verification/production-d3/final-gate.json; usage PRODUCTION_D3.md,
  report IMPLEMENTATION_REPORT_PACKAGE_D3.md; решения61/архитектура/access обновлены.
- CI на итоговом commit ещё должен запуститься после push. Старый WIP Core/Developer
  green, Windows import timeout не выдавать за final pass. Финальный PR65 один,
  E не начат, auto merge запрещён.

Воспроизводимые основные команды (SDK/env согласно global.json):

```sh
python scripts/check_headless.py --output NEW_HEADLESS
python scripts/check_developer.py --output NEW_DEVELOPER
python scripts/check_production_d3.py --fixtures NEW_HEADLESS/d3-fixtures --output NEW_D3
python -m unittest discover -s tests/Checks -v
python -m unittest discover -s tests/Research.Lab -v
python scripts/check_development_ui.py --probe ProductionProbe --godot GODOT --xvfb XVFB --fixtures NEW_HEADLESS/d3-fixtures --output NEW_NATIVE
python scripts/check_hud_god_ui.py --godot GODOT --import-cache src/Client.Godot --output NEW_GOD
```

Все игровые процессы в скриптах bounded. Локально тяжёлые gates запускать
последовательно, чтобы не повторять исходный CPU/memory contention.

## Windows CI follow-up после cc74ea8

Serial importer+--import/--quit всё ещё завис на pre-loop shutdown (CI40357580894,
job121694060588 exit124). Это не pass и не меняет зелёный local game gate.
CI теперь запускает обычный editor startup с serial import и --quit-after30,
а не выход внутри import initialization. Перед smoke отдельно проверяются все
96 importable resources: sidecars, cached files, source_md5, byte identity;
verify-only не пишет ресурсы. Missing sidecar/partial import — failure.
Новый unit test доказывает readonly и отказ на недоимпортированном ресурсе.
Время120s неизменно, source project.godot восстанавливается exact finally.
Локальный lifecycle запуск проверен; окончательный Windows result — только CI.


## Этап 5: финальный аудит порядка replay

- Windows CI40359230362 на5b20470 прошёл целиком: обычный shared-core smoke,
  Release audit и20+20god UI. Ранние import timeout не переобозначены в success;
  исправлен жизненный цикл editor startup, затем независимо проверены96imports.
- Дополнительный воспроизводящий тест выявил настоящий MissedResearchBarrier/
  несовпадение hash: ordinary input до/после god edit наquantum5 и final heading9.
  Начальный запуск2/2failed сохранён в recovery-checks/replay-repro.*.
- Replay теперь использует один существующий persisted playback cursor для
  ordinary inputs и heading; изменения god mode блокируют его на точном
  AfterCommandCount/quantum. Обычные команды записывают существующее optional
  полеTravelQuantum. Старые записи безquantum читаются; их tape bytes/identity
  не подменяются. Курсор сохраняется прежним Session2, отдельной очереди нет.
- Step/Runner и AdvanceTo воспроизводят quanta, когда это требуется tape;
  final boundary включает последний heading после последнего god edit.
  Добавлены ordinary mixed-input/checkpoint и4Developer cases, включая legacy
  quantum0 и восстановление посреди playback. Production/World schema прежние.
- После изменения Hosting нужны заново Developer, обычные unit/runner/replay и
  Godot shared-core regression. Прежние native production/god handler evidence
  остаются применимыми: обработчики и ordinary execution внеplayback не менялись.
  Итоговые результаты ниже/final-gate; не считать промежуточные runs итоговыми.


Итог replay follow-up: свежие bounded locked restore/build +1198ordinary,
1213Developer; после уточнения fixture реальной зарплаты ещё28selected passed.
Release negative gate expected rejection. Client Debug rebuild иLinux probe
повторены:1132hashes/35restores+frozen10, строгий log verifier passed.
5b20470 Core/Developer/Production/verify/Windows jobs все success; новый push
заново запускает CI. Итоговую проверку GitHub смотреть на текущем HEAD PR65,
не подменять её этим предыдущим snapshot. Все641D3fixturecommands quantum0,
нетheadings, поэтому прежние runner39exports/30pairs применимы; CI повторит их.

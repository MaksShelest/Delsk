# E1: приобретаемый автопарк, автосалон и использование транспорта

27.10.2026, Europe/Samara. Автор: Шелестов Максим Евгеньевич / MaksShelest.
Ветка `feat/vnext-e1-fleet`, [PR66](https://github.com/MaksShelest/Delsk/pull/66)
в `feat/economic-cycle-p00-p05`. Автоматического слияния нет.

Основа `ce8f0e3` содержит финальный D3 `a616649`. В начале проверены чистый Git,
удалённая основная ветка, исходники и результаты D3: GitHub Core40362781390 и
Godot40362781514 — success. Использованы готовые SDK10.0.401/Godot Mono4.7.2;
первый запуск устаревшей DLL с нулём тестов не принят за проверку.

## Результат

Реализован software-объём **E1/P69**, с использованием прежних экономики,
Freight/Logistics/EmploymentGroup, физических inventory, UI и контроллеров.
Новый профиль `vnext-e1-fleet-1` применяется только при создании двух новых
designed-map партий/лабораторных presets. Старые пять карт, D3 и сохранённые
resolved profiles не подменяются. [Инструкция игроку](FLEET_E1.md),
[решение62](DECISIONS.md), [журнал восстановления](WORKLOG_PACKAGE_E1.md).

| Модель | Монеты | Скорость максимум | Вместимость по объёму | Назначение |
| --- | ---: | ---: | ---: | --- |
| Спутник-20 | 1800 | 60 | 20 | Легковой, ручная передача10 |
| Муравей-70 | 2500 | 34 | 70 | Грузовой трицикл, лёгкий комплект35 |
| Егерь-70 | 4500 | 52 | 70 | Рабочий пикап, лёгкий комплект35 |
| Вектор-200 | 9000 | 45 | 200 | Грузовик, тяжёлый комплект100 |
| Тракт-Д1 | 12000 | 10 | 70 | Только щебень/асфальт/бетон; лёгкий комплект35 |

Начальные6 экземпляров находятся на четырёх согласованных клетках показа.
Поставка — один обзор каждые500 активных тиков, max6. Сначала свободное место,
затем замена одного непроданного с возрастом>=2500, без pending-заявок; если
подходящего места нет, обзор ждёт следующего интервала. Купленная техника
не заменяется, стоянку освобождает физическим выездом. Модель поставки учитывает
разнообразие/последние продажи, отдельный seeded PRNG сохраняется.

Покупка на входе автосалона резервирует настоящие деньги. Общий включительный
пяти-тиковый барьер распределяет последний экземпляр между всеми заявителями
по seeded fairness; вызов первого NPC не даёт привилегии. Победитель получает
одну машину, проигравшие — освобождение holds. Оплата поступает в существующую
казну по принятому для техники пути; отдельный внешний денежный сектор не вводился.

Игрок владеет несколькими машинами, выбирает свободную, находит, садится/выходит,
управляет и передаёт груз. Один CargoInventory хранит реальную позицию и товары.
При личной езде actor синхронизируется с ним; при автоматической работе тот же
кузов подключается к прежнему Logistics.Trip/queue/holds/crew. Нельзя одновременно
ехать лично, грузиться, монтировать комплект и выполнять рейс. Stop сохраняет
текущий рейс. Старый BuyLogistics в E1 требует выбора приобретённого автомобиля.
Прежние добывающие машины и логистика в старых профилях сохранены.

Скорость учитывает actual dirt20/offroad8, человек20/16, делитель80. Юридический
коридор ещё не дорога. Сохраняются subcell offsets/heading и quantum cursor.
Визуальная форма/скин не определяет характеристики. Топлива нет.

Погрузка начинает работу со следующего целого тика, считает VolumeWork и целые
предметы. Кузов даёт только водительские10; комплект35/100 оплачивается деньгами,
материалами и60тиками. Площадки10/40/100 имеют настоящий числовой персонал либо
оплачиваемую service III, монтаж120/180 и затраты из BALANCE. Два независимых
средства суммируются:35+40=75. Один оператор/площадка не учитываются дважды,
простой не оплачивается. Монтаж/материалы учтены при C2 подготовке и сносе.

Планшет «Автопарк и автосалон» показывает stock/цену/скорость/объём, заявку,
покупку, местонахождение/занятость, управление, передачу, комплекты, площадки
и назначение автоматике. NPC использует те же команды и persisted plan.
Полные будущие поставки/PRNG/чужие private terms ему не передаются.

World schema24 сохраняет fleet/stock/windows/holds/PRNG/монтаж/водителя/кузов/
обработку/пути, восстанавливает взаимные ссылки и canonical hash. Limited23–18
принимает только отсутствие новых полей. RulesVersion/Session2/Envelope2 прежние.
Независимый researcher `fleet.json` и `fleet_audit.py` дополняют обязательные
проверки денег, goods flow, населения, капитала и публичной приватности.

## Реальные проверки

Машинные итоги и source digests: [final-gate.json](verification/fleet-e1/final-gate.json).
Полные логи/fixtures находятся вне Git; небольшие exit records сохранены рядом
с manifest. Ни один timeout или нулевой тест не считается pass.

- Ordinary: **1219/1219** тестов, девять исчерпывающих непересекающихся partitions,
  без skips. Из них21 E1,644 прежних физических и132 controller/model тестов.
- Отдельный локальный headless runner gate — exit0 за491s в прежнем780s
  лимите:102exports/14replay/69comparison pairs, включая прежние добычу,
  логистику/возврат/expiry, экономику, политику и controller safety.
- E1 runner: **34 успешных процесса,22 exports,16 exact comparisons** continuous/
  checkpoint/load. Заявка, погрузка, Freight load, монтаж комплекта/площадок,
  фактические75volume/tick, граница замены2500. Все22 выгрузки прошли независимые
  fleet/economy/public audits; два экспорта профиля совпали byte-for-byte.
  Отдельный CI fleet job121991382989 на `c62352b` также success: свежий полный
  запуск без переиспользования локальных результатов первоначального аудита.
- Python checks15/15, включая11 намеренных повреждений fleet-аудита; Lab37/37.
- Native Linux Godot Mono: реальные RU800×720/EN1100×800 mouse inputs и3D,
  purchase hold→allocation→find→board→whole-tick load→save→exact continuation→leave.
  Ровно6 физических моделей, mounted actor скрыт, груз после выхода сохранён;
  strict Godot logs exit0. Probe только в isolated stage, запрещён ordinary audit.
- Обычная Debug-сборка клиента:0 warnings/errors. Windows Godot CI40443103227
  на `c62352b` — success: ordinary AssemblyAudit,1132 shared-core hashes/35
  restores, ExportRelease audit,20 native HUD/God checks Debug и ещё20 для
  стандартного ExportDebug, cleanup boundary. Это облачный Windows runner,
  не проверка Windows/Android устройства.
- Полный Developer gate в CI40443103322 — exit0 за438s: все partitions и
  ожидаемый запрет Developer Release. Локальный повтор достиг600s после семи
  завершённых partitions; это отдельный failure124, не полный локальный pass.
  Принят именно завершённый CI gate на тех же исходниках.
- Полный Core CI40443103322 на `c62352b` — **success**, включая обязательные
  unit/core/developer/production/fleet/verify. Сохранены прежние102exports,
  14replay/69comparison pairs, A1/A2 CLI, ordinary3/6/10, карты, C1/C2/D1/D2
  process gates и полный D3 runner/import audit. Последующий commit с отчётом
  меняет только документы/evidence; точные724 runtime/test/config/tool/CI
  digests сохранены для сопоставления с проверенным code snapshot.

Первый общий headless запуск прошёл все1219 unit тестов, затем достиг780s в
runner-фазе. Это aggregate failure124, а не pass. Unit и runner разделены на
два обязательных CI jobs с прежними deadlines и exhaustive partitions; локально
переиспользованы завершённые TRX/fixtures. Первый E1audit обнаружил legacy
предположение любого cargo10/crew2: verifier исправлен на проверку реальной
купленной модели и общей cargo identity, весь audit повторён на22 exports.
Другие промежуточные неуспешные попытки/исправления перечислены в worklog.

## Воспроизведение

Использовать pinned SDK/Godot из проекта, обычную доступную NuGet сеть и новый
output для каждого runner/native gate. Большие gates выполнять последовательно.

```bash
python scripts/run_check.py --name unit --timeout 780 -- python scripts/check_headless.py --phase tests --output artifacts/unit
python scripts/run_check.py --name headless --timeout 780 -- python scripts/check_headless.py --phase runs --unit-artifacts artifacts/unit --output artifacts/headless
python scripts/run_check.py --name fleet-e1 --timeout 600 -- python scripts/check_fleet_e1.py --fixtures artifacts/unit/e1-fixtures --output artifacts/fleet-e1
python scripts/run_check.py --name developer --timeout 600 -- python scripts/check_developer.py --output artifacts/developer
python scripts/check_development_ui.py --probe FleetProbe --godot "$GODOT" --fixtures artifacts/unit/e1-fixtures --xorg /usr/lib/xorg/Xorg --output artifacts/fleet-native
```

Для native допустим установленный Xvfb вместо `--xorg`; это только display stage.
Обычный AssemblyAudit дополнительно проверяет отсутствие Developer/тестовых probes,
а ExportRelease — отсутствие Debug menu. CI сохраняет прежние C1/C2/D1/D2/D3 gates
и требует `unit/core/developer/production/fleet` в общем `verify`.

## Граница и ограничения

Завершение E1 не означает весь пакетE или весь vNext. **E2 не начинался**:
дорожные работы, строительство покрытий, встречные разъезды/новый помощник
движения отложены по заданию. Тракт в E1 приобретается, едет и возит допустимые
материалы; дорожное предприятие относится к E2. Счета/P71 и сторонняя доставка/
несколько заказных рейсов P72–P73 не вводились.

Сохранено прежнее ограничение одного автоматического logistics executor на
предприятие; личный парк может содержать несколько машин. NPC проверен на реальной
покупке/назначении и restore плана, оптимальность экономической стратегии не
заявляется. Скорости40/60 требуют будущего реального покрытия E2.

Подготовленные деньги/материалы/география в focused fixtures не доказывают
естественную покупку всех моделей с200монет или длительную окупаемость. Нативные
облачные RU/EN и Windows CI не заменяют Windows/Android device/lifecycle/FPS/
сенсорный ввод, долгий баланс P77, полную P80 приёмку и актуальную живую LLM.
Эти прежние ограничения сохранены, механики E1 ими не подменяются.

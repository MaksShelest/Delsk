# H3 — рабочий журнал

Автор: Шелестов Максим Евгеньевич. 02.11.2026, Europe/Samara.

Основа: `b87bc90a84845f509cf256bb227dbcf2952a43ad`, свежий HEAD
`origin/feat/economic-cycle-p00-p05`, merge PR76/H2. Локальный `work` на
29cb97d сохранён. Рабочая ветка `feat/vnext-h3-compact-acceptance`.
SDK global.json 10.0.401; Godot .NET/SDK/templates 4.7.2; TFM net10.0.

## План и границы

1. Новая самостоятельная карта «Малая долина»: 32×27, 24 контура 2×3,
   восемь компактных ресурсных районов, публичные и частные клетки,
   G2 civic geometry, защищённые коридоры, обычное меню/описание/превью.
   Старые карты 16×9 и 16×13 слишком малы для полного состава и геометрии;
   новая площадь 864 против 3072 у «Речных кварталов». Не уменьшать объекты.
2. Обычный новый старт: Movement1/H2 и полный G1 economic profile, никаких
   бесплатных производств/машин. Горизонт новых ordinary-партий 1 000 000
   активных секунд (277ч46м40с), без подмены сохранённых конфигураций.
3. Проверить доступ/максимальную застройку, 3/6/10 NPC и сложности, точные
   продолжения опасных состояний и весь пользовательский экономический путь.
4. Новая отдельная экономическая когорта всех трёх карт; ограниченные
   возобновляемые сегменты, исходные неудачи и frozen inputs. H1 не переписывать.
5. Native Godot/RU/EN/оба скина/масштабы, PNG; доступные Windows/Android
   сборки; матрица доказательств и открытых hardware/LLM/late-balance gates.
6. Небольшие коммиты, PR без auto merge. Не повышать прежние deadlines,
   checkpoint limits; не менять цены/XP без результатов.

## Восстановленный контекст

- H2 World29/Session4 читает World28/Session1–3; полная история lossless.
- Делитель32 только новые старты; таймеры экономики — прежние целые секунды.
- H1 вывод «1 против 4/5» отозван: diagnostic читал Scenario, не DTO.
- H2 поздние 8 миров прошли прежние рубежи, но 20–35ч/I–V/ROI/Hard
  superiority не доказаны; их нельзя считать новой Movement1 выборкой.
- Доступ к сети в exec требует explicit network capability; соединение через
  штатный proxy с сохранёнными CA. Содержимое credentials не читалось.

## Ход

- Проверены clean status, remote heads, PR76 и ancestry; создана отдельная ветка.
- Подтверждены установленный SDK10.0.401 и Godot4.7.2 .NET.
- Найден технический ordinary maxTicks5000; research126000 не меняет UI.

Этот журнал промежуточный; выполнение и фактические числа — в итоговом отчёте.

### Первый законченный этап — ec0886b / PR77

- Карта и horizon закоммичены и опубликованы; PR77 draft, auto merge выключен.
- 11 compact-map и4 horizon/legacy/sandbox tests прошли; SDK/client/runner build.
- Начальная H3 когорта: workflow41355595793,180ordinary +12extended.
  Не перезапускать её из-за документации/дополнительных проверок; PR trigger
  ограничен opened. Для продолжений необходимы исходные checkpoints/runtime.
- Native proportions прошёл на всех трёх vNext-картах,11участников,
  оба скина,36PNG, точный saved layout. Linux/Xorg dummy/llvmpipe.
- Первый horizon test120s timeout сохранён: три полных idle5000 прогонов
  перенесены в фактическую natural study; software test проверяет profile,
  sandbox и exact continuation. Пределы не повышены.
- В scripted player helper выявлены ожидание зарезервированного леса,
  неполный проданный остаток в рюкзаке и выбор не-anchor клетки. Core правильно
  отклонил эти команды. Попытки a/b/d сохранены; e продолжает реальный b-save.
- Локальная build-c ошибка обращения к internal Hash привела к запуску старого
  helper DLL; процесс остановлен (-15), не включать этот повтор в результаты.
  Исправлено публичной JSON сверкой config и set -e перед build/run.
- Natural scripted player/e: первая фабрика t3767, production XP, платный
  склад t5604; ordinary horizon5000 реально пересечён. Включены3Algorithmic
  NPC иSelfBallot человека, без Developer. Это одна dependent trajectory,
  со сценарной ошибкой покупки лишней земли; не оценка оптимального игрока.
- Android SDK36/build-tools36.1.0/platform-tools и JDK21.0.12.1 подготовлены
  локально; существующий JRE/системный toolchain не заменялся. Export templates
  Godot4.7.2.stable.mono проверены по version.txt. Аппаратная приёмка открыта.

### Совместимость и полный prepared gate

-19/19 ordinary H3 tests прошли в полном disjoint suite. Новый Large service
  case первоначально занял119,57/120s: сохранён pass и сам исходный timing.
  Убраны только повторные полные replay одного и того же длинного префикса
  в новых H3 intermediate snapshots: каждый restore/+3 сохраняется, один
  полный replay остаётся в конце. Старые F2 callers replay=true неизменны.
  Новый Large case73,87s. Deadline120 не повышался.
- check_h3_prepared:41actual processes,21exports,18exact comparisons31files;
  все3карты, pending/escrow/loaded cuts и полный future command tape до оплаты
 5рейсов. Accounting/public privacy прошли для всех экспортов.
- Native natural save t5604 впервые обнаружил UnknownControllerVersion для
  self-ballot-v1. Исправлен только Godot composition root через существующий
  SelfBallotController.Restore. Повтор native-natural-b прошёл: прежние world/
  controller hashes, обе косметики и3точных следующих тика. Это настоящий
  дефект совместимости клиента; обычный ручной human ballot не подменяется.
- Native motion:180PNG/60travel quanta/6активных секунд, start(11,1)→(11,5).
  Запись заняла144,863s из150: это покадровая запись с PNG, не FPS benchmark.
  MP4 смонтирован в30fps/6s.12compact roadPNG показывают реальные straight/
  turn/junction и оплаченный working paver/bypass; проверены визуально.
- Scripted player/e достиг t12579/95760minor и остановился на собственном
  ограничении200sell attempts, а не на runtime/save/game limit. Сохранён
  last-safe checkpoint; helper продолжает ту же trajectory с явным tick budget.
  Raw-only earning после остановки фабрики — заведомо неоптимальная стратегия;
  это не калибровка сроков или full ROI. Первый политический итог был NPC3/t2400;
  после него экономика продолжала работать, modeVictory не выдаётся за modeSandbox.
- Прямая загрузка File Service ZIP в executor запрещена destination policy403.
  Обхода нет: collection выполняется штатным GitHub Actions API и HTTPS ranges
  к original GitHub artifact storage; verified compact evidence сохраняется в
  отдельном docs/verification/h3/economy commit без перезапуска симуляций.

### Этап3: воспроизводимые сцены, natural save и целевые bundles

- Сохранены native city3/6/10NPC RU/EN640/800/360 и исходные manifests,
  natural-loaded обеих косметик, по обзору остальных2карт, motion metadata.
  Исходный natural checkpoint5604(2,5MiB) и его digest сохранены в player/;
  это тот же файл, который читал исправленный Playfield, не реконструкция.
- H3Acceptance resume после уже купленного транспорта теперь пропускает новую
  покупку и позволяет отдельно закончить replay/export прежней trajectory.
  Работающий g продолжает прежний binary; это изменение применяется к следующему
  явному build, не скрывает работающие результаты.
- Добавлены предварительный IMPLEMENTATION_REPORT_H3 и research/h3/README:
  матрица требований, exact tools/команды, естественные/prepared/VM/device границы.
- build_h3_targets.py создаёт pristine isolated source без probes, проверяет
  Debug/ExportRelease metadata, стандартный Developer ExportDebug, настоящий
  self-contained Windows runtime и состав APK. Test signing key остаётся только
  в ignored staging. Отдельный H3 targets workflow сохраняет bundles90дней,
  запускает оба EXE и actual natural save на Windows VM. Итог не заявлен заранее.
- Повторный ls-remote: target остаётся b87bc90, H3 remote4175455; чужая работа
  не заменялась. Папка /codex/.../output отсутствует и readonly в managed runtime;
  готовые файлы выдаются штатными GitHub artifacts/PR, публикация Site не нужна.

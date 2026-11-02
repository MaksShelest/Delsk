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
- Player/g: paid-vehicle t19868, cash10406minor после настоящей цены180000.
  Milestone encode/decode hash прошёл, checkpoint8,7MiB сохранён. Дальнейший
  полный prefix replay не уложился в оставшиеся259s из900s общего процесса;
  process124/905,117s после5s grace, исходный running milestone JSON сохранён.
  Это не успешный final replay/export. Отдельный bounded replay из paid-vehicle
  checkpoint ещё нужен; лимит не поднимался.
- Windows platform41360866991 оба jobs success: actual shared hashes/release
  metadata, native HUD/Developer и расширенный city UX всех3карт RU/EN.
  Это hostedVM, не физический ПК и не proof живой LLM.
- Windows target run41361599014: ordinary/release restore/build/audits прошли,
  export-developer превысил240s, artifact13105122829 сохранён как диагностика,
  не успешный bundle. Новый driver явно завершает editor через --quit и печатает
  вложенные последние логи при failure, как существующий native import workaround.
  Повтор обоснован отказом;240s не повышался, игровой код тот же. Обычные Windows
  gates41360866991 остаются независимым успешным доказательством.
- Android/a cold export превысил240s на первом Gradle dependency/configuration
  build. Явный retry в том же owned stage/cache, отдельные logs/output b:
  Developer110,186s/240, Release прошёл тот же240s gate. CRC всех entries,
  подписи apksigner, manifest API24/target36/ARM64 и Release metadata audit
  прошли. Developer102310949bytes, Release95307498bytes; SHA в targets/android.
  Release не debuggable и не содержит Simulation.Developer/Debug UI. Это не
  device execution. Удалён только generated Gradle build/build cache после
  проверки APK для освобождения диска; исходники/APK/логи/keys/deps сохранены.
- Windows target41362439343: явный --quit исправил завершение обоих экспортов;
  bundle shape/runtime и actual published Release audit прошли. Developer EXE
  реально выполнил1132shared hashes/restores в Windows VM. Release EXE exit1
  через1,5s; не объявлен runtime pass. Отдельная диагностика скачивает именно
  этот retained bundle по digest EXE и печатает ошибку без пересборки/новых миров.
- Compact active road bypass усилен настоящим save/load+3 иполным replay
  непосредственно во время работы. Новый ordinary class1/1прошёл, старые H2
  callers не получают callback и сохраняют прежнюю последовательность.
- Developer H3 первый restore отказал NU1900 из-за отсутствия network grant в
  этой exec cell. Ошибка сохранена в developer-h3; новый developer-h3-b использует
  обычный разрешённый сетевой доступ, не отключает vulnerability audit.
- Developer large service original180s отказ сохранён. Новые независимые partitions:
  текущая подготовка до loaded645, расчёт оставшихся рейсов до972, полный replay
  исходного tape0→972. Использованы **исходные** saved cuts ordinary19, gzip/digests
  сохранены; текущая генерация/settlement обязаны совпадать по миру и tape.
  Никаких новых бесплатных данных внутри действий: начальное финансирование
  по-прежнему явно prepared. Старые H2/F2 callers и180/120s не менялись.
- После исправления compile-only VehicleView→OwnedVehicle (set-e не дал запустить
  stale binary), ordinary21/21 и Developer21/21 прошли. Developer полный replay
  Large96,62s/180, подготовка106,526s/180; forbidden Developer Release ожидаемо1.
  Новый H3 Developer job включён в обязательный Core verify, старые suites сохранены.
- Сравнены48fixture файлов: всё byte-equal, кроме финального Large checkpoint.
  Независимое декодирование показало единственное отличие /epoch после restore;
  world/tape и все остальные Session4 поля совпали. Epoch намеренно меняется для
  инвалидирования старых callbacks; файл не объявляется побайтно равным.
- Тот же Windows Release bundle13106117244, полный Actions ZIP digest8470fe0771…,
  EXE digest проверен: fresh Windows VM diagnostic41363547781 прошёл1132hashes,
  все35file restores, process8,453s/90. Пересборки не было. Первый exit1 остаётся
  сохранён; следующая read-only диагностика читает исходную ошибку и фактический
  runtime availability report, не повторяет экспорт/симуляции.
- Collector дополнен bounded4-way чтением независимых ZIP metadata, duplicate
  entry protection и37Python checks. Явный allowPartial пишет отдельный
  economy-intermediate с missing cases; окончательный economy по-прежнему требует
  completed source run и192journal. Trigger только по push marker, чтобы PR diff
  не перезапускал collection на каждом обновлении документов.

### Этап4: найденные late/export/audit дефекты и их исправление

- H/19871: late checkpoint загрузился,3следующих тика совпали по world иNPC
  controller hashes. Полный replay прошёл обязательный world-hash assertion и
  дошёл до RunArtifacts.Export. На decisions.jsonl кончилось место: process-6,
  870,541s/900. Не объявлять весь H успешным. Сохранены исходные process/error,
  partial trace2881224704bytes losslessly gzip сSHA/CRC; полный источник — save.
- Добавлен **opt-in** потоковый gzip в RunArtifacts.Export: default raw прежний,
  алгоритмы/таймеры/Core/схемы/64/512MiB не меняются. SHA/число исходных bytes
  считаются при записи, completion marker появляется послеflush. Новый regression
  сравнил каждый legacy export byte и полный private history raw↔gzip;
  ordinary120s иDeveloper180s прошли. H3 теперь22cases, прежние21 сохранены.
- Read-only export из сохранения19871 без симуляции/повторного replay завершён
  за40,548s/900. Полная история4649008380bytes →428687299gzip, SHAaa432a6c…,
  весь исходный partial prefix совпал. Денежный/public/privacy snapshots прошли.
  В derived re-export performance поля обнулены вnull: это не timing симуляции.
- Python auditor пропускал paid unfinished StorageConstruction/Development WIP.
  Core PublicEconomy.cs:82 учитывает его сC1. Реальный ordinary save3707:
  17000cash +реальные wood/steel материалы =39500minor WIP. На одном итомже
  re-export original auditor1, corrected0 иaccounting0. Новая сверка использует
  квитанции started/materials/completed/cancelled, не balancing delta; duplicate/
  wrong-owner/unknown closure отвергаются. Цены/XP иCore капитал не менялись.
- Interim source collected153worlds,39missing,34blocked audit,119complete ordinary,
  299NPC first factories вcomplete endpoints. Это НЕ final study: audit censoring
  исключает развивающиеся миры иможет смещать complete-case сравнения. Original
  отказы сохраняются, не переименованы вуспех. Запущенявный audit-erratum pilot
  дляодного исходного case сfrozen DLL/input checks, backup original journal,
  re-audit original exports ипродолжением тойже партии. Новыхреплик нет.
- Windows original Release fault точно DeveloperSessionUnsupported: technical
  PlatformProbe делил save paths сDeveloper. Все7probe paths теперь имеют тотже
  developer suffix, чтоordinary Playfield. Самзащитный запрет Release читать
  Developer save остаётся. Существующие пользовательские saves не тронуты.
- Actual Windows runtime report: Ollama runtime=null/unavailable,
  inferencePerformed=false. Живая модель недоступна, hardware/LLM gate открыт.
- Android CI41365831107 выявил standalone --install-template exit0 безgradlew
  вfresh stage (local check видел ужесуществовавший template). Исправлен bootstrap
  через exact official android_source.zip иmatching build version; следующий
  target run проверитfresh stage. Cold240s ивсе прежние limits сохраняются.

### Этап5: возобновление после ограничения сервиса, 02.11.2026

- Локальный85cfbd1 был сохранён, но предыдущий push остановил automatic approval
  review usage limit. После явного «Продолжай» обычный push успешно выполнен;
  обхода review не было. Основа target по-прежнему b87bc90.
- Исходный study41355595793 завершён:193jobs (matrix+192worlds),144ordinary
  completed,36ordinary+2extended audit failures,8extended bounded batches,
  2extended hosted35min interruptions. Пример LargeHard142 имеет audited12600
  ипрерванную следующую попытку; отменаhost не считается экономическим pass.
- Pilot41408105572/job125154703166 прошёл наисходныхDLL/inputs. Original
  public-audit1 приt3600 остаётся; derived re-audit0 обоихcheckers иoriginal
  simulator продолжил3600→5000 за84,225s/600, обаfinalaudits0. Artifact13124706084.
  Это таже партия, не новаяреплика. Сохранён compact proof audit-pilot.json.
- Полная коллекция исходных192worlds запрошена41408274423 безсимуляции.
  Сводка дополнительно сохраняет activeAttempt иотдельно маркирует erratum
  audits; complete-case difficulty comparisons припропусках явноprovisional.
- Следующийbatch явно выбирает35остальныхordinary и12исходныхextended,
  maximum1segment/600–900s; прерванныйhost attempt сохраняется, продолжение
  допускается только спредыдущегоaudited checkpoint. Никакогоretry настоящих
  timeout/checkpoint-limit outcome. Partial raw exports передновой записью
  архивируются прежнимlosslesshelper сSHA/readback; это неполнаяистория,
  неуспешныйsegment. Whole-job35min и64/512MiB прежние.
-40Pythonchecks повторно прошли послеисправленияcollector metadata.
- Fresh targets41408105541, Core41408105487/41408112872 ещё выполняются.
  PR77 описание обновлено пофактической реализации/открытымgates, остаётсяdraft.
- Полная исходная коллекция успешно сохраненаCIкоммитом0688841 иполучена
  fast-forward:192/192, missing0,144complete ordinary,344NPCfirst factories
  вcomplete endpoints. Проверка journals уточнилаlate checkpoints:9миров
  audited14400 (включаяLargeHard142, чейjob отменён позже приархивации),
  LargeHard143 running/lastSaved10800, twoaudit-blocked1800/3600. Поэтому
  cancellation job неравна границе последнегоaccepted checkpoint.
- Fresh targets41408105541: Androidjob125154750997 success (официальныйfresh
  template/Gradlebootstrap, обаexports240s/подписи/manifest). Artifact13124263830,
  ZIP SHA57904f94498762b11c3eb3931d9831846fed03b45fbbd2201ed019b78411ca71.
  Windowsjob125154751074 обаbundles экспортировал, audited ивыполнил подряд
  smokeDeveloper0/smokeRelease0 втойжеVM. Artifact13124995843/ZIP
  8253c15683d96aa08d85654b9a76dee0f550e44aa85e5ddeca773d9b00847119.
  Последующийnativecapture отказал вPython read_text(project.godot): cp1252
  нечитаетрусскийUTF8. Это harnessdefect дооткрытиясцены, неуспешныйnativepass.
  ВсеtextIO check_world_proportions теперьявноUTF8; diagnostic повторяеттолько
  native-load5604, читает retained85cf manifests/hashes безповторногоexport.
- CI scheduling: Core/platform concurrency наветку отменяет устаревшие проверки
  следующих revisions, сохраняя актуальный gate. H2 late теперь source-sensitive
  push +PRopened вместоcumulative-PR synchronizes: реальные измененияsrc илиего
  worker/verifier/workflow по-прежнему запускают все8миров; записиcollect/audit
  отчётов больше не повторяют H2. Составsuites, budgets, oldchecks прежние.
- Скопившиеся pre-concurrency Core/platform/H2 runs занимают очередь;47actual
  audit continuations ещёнеполучилиrunner. Добавленаоднократная cleanup automation
  сявнымallowlist толькоэтойH3ветки/трёхregressionworkflows/после07:42UTC.
  Онаоставляетновейшуюrevision каждойпроверки ипочтизаконченныйCore41408105487;
  неумеетотменять study/audit/collection/targets/diagnostic. Старыеотмены/logs
  останутсянаGitHub; новыйconcurrencyпредотвращаетповторнуюочередь.
- General runner теперь принимает явный boolean --compress-controller-turns true
  дляновыхзапусков/load, включая failed/cancelled export; default raw прежний.
  Build32,74s/180 безwarnings/errors. Две90-tick CLI проверки дали24byte-equal
  exports, exact618921raw private-history bytes→56019gzip, SHA835a2954…;
  TRUE нормализуется, неверныйzip отвергнутexit1 доworld. Frozen researchDLL
  неподменяются. Repro/proof runner-gzip-cli.json, сохранённый позднийworld
  по-прежнемуимеетнулевуюstep export-only командуH3Acceptance.
- Readable RESULTS generator используетexistingworld-level statistics/18Holm
  contrasts иlatest accepted checkpoints; NPCtimes маркируются descriptive,
  costs/QuestXP/уровни/ROI сохраняютcensoring. Collector выдаётRESULTS рядом
  сисходнымиobservations, безновыхреплик. Исходный срез доerratum: maxNPC
  industryXP108ordinary/321extended при600дляII, QuestAccepted/Completed0/0.
  Это наблюдение, непричинныйвывод иневыполнение позднихцелей.
- Устранён дополнительный риск stale completion/mixed raw+gzip при повторном
  output: preflight/API отвергают каталог старого архива или compressed export
  до записи, сохраняя каждый прежний файл. Обычный raw→raw путь прежний; progress
  текущего запуска допустим. H3Export regression усилен проверкой immutable
  previous artifacts в ordinary/Developer, число cases остаётся22.
- Сначала ошибочно вызван dotnet test: SDK IPC pipe запрещён в sandbox (exit-6),
  это не результат теста. Перешёл на штатный прямой xUnit DLL запуск проекта:
  ordinary1/1 прошёл за2,422s/120; Developer1/1 — в180s. Builds58,81/52,68s
  без warnings/errors; исходный IPC отказ сохранён. CLI --progress+gzip прошёл
  с exact history, reuse destination отвергнут до simulation при target999999.
- Самостоятельные полные CI jobs source85cf подтверждены: ordinary22+prepared
  job125154658457, Developer22 job125154658760, обаsuccess (ci-gates.json).
  Остальные Core downstream jobs ещё в очереди. Новый target marker revision5
  включает текущий shared Hosting guard/runner CLI и UTF8 native harness.
- Windows UTF8 diagnostic41409156706/job125157966666 успешно завершён:
  реальные natural save5604/оба оформления в Playfield,150s unchanged; retained
  EXE/APK hashes source85cf проверены без пересборки. Manifests85cf скопированы
  из фактически проверенных JSON в targets/*-85cf-packages.json. Диагностический
  artifact13125556684, SHA28e993edd759514408509ee3fe77335810783970e5d64ece9a286a3e06dd3f77.
- Читаемый исходный RESULTS.md сформирован из192original journals; пока144/180
  complete, все сравнения явно provisional. Не принимать это за corrected final.
- В08:21UTC API ещё показывает6успешных case+select в audit batch41408949052,
  41queued; финальные targets41412052805 и новые Core/H2/platform тоже ждут
  runner. Cleanup остаётся queued. Готовые85cf packages/H3 gates иWindows
  native retry от этого не отменяются. Чужие branches не отменялись.
- Collection revision3 явно объединит original+pilot+доступные endpoints текущего
  batch как interim, source chains сохраняются. Она не создаёт миры и не ждёт
  «зелёного» заменой таймаутов. Окончательная collection потребует завершённых
  sources и ручного перевода allowPartial=false после проверки всех outcomes.
- Обновлённый API подтвердил8/8success в H2 retained regression41408112964
  source85cf (последнийjob завершился08:11:15UTC); ci-gates.json дополнен.
  Это совместимость старых retained миров, не дополнительные репликиH3.
- На08:27UTC остаются41queued в audit batch иqueued финальные targets/select,
  collector иcleanup. Незапущенный cleanup job не имеет логов (BlobNotFound),
  поэтому он не объявлен выполненным. Доступные средства не дают прямой cancel
  Actions; GH CLI не имеет действительного token. Сохранён allowlisted cleanup
  дляпервого доступного runner; чужие ветки и research outcomes не затрагиваются.
  Следующий этап после выдачи runners: проверить все47outcomes, собрать original+
  pilot+batch, продолжитьтолько ready extended сисходнымиDLL доцели илиреального
  первого failure/limit, затем full collection иокончательная матрица/PR.

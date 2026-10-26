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

## Этап3: подключение UI/NPC и native gate

D2профиль теперь config/vnext/economy.d2-development.json, embedded и default
только двух designedmaps; лабораторные presets D2. OldA2/C1/D1 files неизменны.
PlayerView.Development own immutable; DevelopmentPanel в карточке фабрики и
«Моё хозяйство → Опыт и задания». Initial ground staging UI, текущие XP/уровень,
стоимость/материалы/сроки, отдельные land buttons, prepare/cancel/explicit target.
C2подтверждение/quote для отмены проекта прежние. Общая технология в SetRecipe/
workshop validation/output compatibility; активный profile всё ещё D1базовые.

DevelopmentGoals используют только ControllerObservation/обычные команды;
первоначальная стройка несколькими20volume рейсами, remote accept own funded
quests, prepare/land/input/paid upgrades. План/цель/курсор прежние. Найден реальный
NPC deadlock: Fetch выбирал сам target ground как источник и перекладывал его
материалы обратно; target исключён. Нельзя выбирать первый геометрически
непригодный legacy plot до проверки контура. Tests фиксируют обе причины.

Developer IndustryExperience как новый kind прежнего research intervention,
не ActionKind; exacttotal, отдельная ResearchXp поправка, без fake production.
Lowering XP не должен запереть уже выбранный поддержанный рецепт. Audit/hash/
replay/nonclean package, ordinary conditionalRemove прежние. НовыйDeveloper test
ещё ожидает полныйDeveloper gate. Public Industry events скрыты, own projections.

Последний целевой gate33/33 за16.412s/120; ещё добавлены2 meaningful cases
(new ground goods block withoutcharge +NPC actual paid tier); полный gate будет35.
Fixtures генерируются tests при DELSK_D2_EVIDENCE в отдельной workspace folder,
не добавлять saves/commands в обычный клиент. Full headless создаёт d2-fixtures.
Новый RunArtifacts development.json, сравнивается verify_checkpoint_exports.
check_development_d2.py прогоняет actual command tapes geometry+12trade quests,
с cuts/replay/balances/privacy/profilebyte reproduction. Пока НЕ ЗАПУЩЕН.

Native check_development_ui.py/DevelopmentProbe (isolated source, запрещён в
AssemblyAudit): успешно настоящиеPlayfield/buttons, XP40000 fixture, prepare,
2land payments, exact45000 expansion/oldshape untilcompletion, diskContinue
duringproject, real3cell/ready2places, native paidoutfit/N0, native questaccept+
actualdelivery→50XP, pausepreserved. RU800x720/EN1100x800,3screens viewed.
Logs /workspace/work/delsk-d2/native, native-check.json. Initial restore failed
NU1900 no network; rerun with permittednetwork passed. Probe firstbuild type
CheckpointLoadResult vsGameSession corrected toDecode; not counted aspass.

Теперь running scripts/check_headless.py в /workspace/work/delsk-d2/headless,
outer900/per-test120. Suite deliberately partitions old1117 andnewD2 in2processes,
all tests remain; growing suite would exceed old120 ifoneprocess. Need inspect
actual counts/results, then isolated Developer180, D2process, C1/C2/D1process,
oldWorld21exact, root Godot import/smoke/hash/audit, map-lab defaultD2. Check
normal root client after last changes. D3 не начинать. Docs usage/architecture/
decision60/actions updated; final report/evidence/AGENTS/plan/PR still pending.

Gate progress: full ordinary partitions1117/1117(110.380s) +35/35(16.536s)
passed; headless runner matrix stillrunning. Added restore proof of quest baseline,
actual receipt interval, acceptance event, technology at acceptance and explicit
city-project source. Production proof now indexed bybatchID atcapture/restore
instead of events scan pergrant (important for actual40000XP histories). New
prior-receipt tamper test; rerun D2targets/process and fullDeveloper after build.
No legacy profile path changed by this validation. Client-only help/active-quest
history and input-overflow label improvements pending final native rerun.

Presentation audit found a real integration gap: Presentation.Read used static
Type.Footprint instead of the completed building footprint. Fixed to actual
relative cells; meaningful assertion atall5tiers. Added cosmetic foundation only
for actual multi-cell production, nofreeequipment/collision. Native finalprobe
checks EnterpriseExtent afterexpansion. Latest target36/36 was before this fix;
final headless+Developer willrebuild/repeat. D2process/C1/C2/D1 scripts running on
previous validated binary; rerunD2process if necessary afterfinalbuild. No D3.
Final UX audit: D2reason codes localized RU/EN; next tier explicitly shows required
XP. Quest history keeps all active quests even after many closed tasks. Developer
has exactset andaddXP using same audited target API. No further feature work.
Final context audit: PlayContext initial production material preview still read
backpack although Core uses ground; corrected and asserted in Initial helper.
Card resolution now uses actual footprint, checked on each expanded tier (move
onto newcell→open card→return anchor). Future contour cells do not offer misleading
newBuild/foreignBuy buttons. These Hosting/UI changes occurred after final
headless build; Developer full suite and final ordinary D2/native mustrebuild them.
Core/runner economic rules unchanged. Physical evidence tape changed by these
extra ordinary moves: final fixture generation/process must use new output.
Final headless1153/1153 +102exports/14replay/69pairs passed. Developer1160/1160
151.184s/180, negativeRelease expectedreject. Last NPC guard: if its valid owned
initial site lacks unavailable materials, stop searching more lots instead of
buying another site; existing NPC test extended with second available contour
and no steel source. Final ordinary D2/native +targetedDeveloper D2+XP mustrepeat
this guard and latest context changes. No newfeature or ordinarylegacy change.

## Этап4: финальные проверки завершены

Код D2 готов, D3 не начат. Все перечисленные ниже exit0:
- Full headless-final:1153ordinary(1117+36),102exports/14replay/69pairs.
- Последний ordinary D2 после context/planner:36/36 за16.710s/120;
  полный Developer повтор1160/1160 за154.779s/180. ForbiddenRelease expectedreject.
- Process-exact:31checks/18exports/16pairs, physical1869ticks(cuts112/172/194/1860),
  trade393ticks(cuts268/271/392). Exactprofiles twice, no duplicate grants/budgets.
- C1:24checks/2293ticks, C2:16checks/10exports/9pairs, D1:18checks/10exports/9pairs.
- Native exact:186D2tickhashes/8restores, paidland/expansion/outfit,N0, questreceipt,
  actualfootprintvisual, pause/diskContinue, RU800x720/EN1100x800.3finalimages viewed.
- Root client/import/smoke/hash, legacy1132hashes/35restores; ordinary audit5assemblies.
  Separate Developer client built XPUI0warnings.37Python.2designedmaps actual
  Chromium→lab→runner afterfinalbuild, checkedrecordings withD2profile.
- Actual previousD1World21save158→353: samefinalhash +24exactexportfiles/metrics.

Evidence/commands/timings: docs/verification/development-d2/gate.json, native.json,
world21.json, othermanifests and3screens. Report IMPLEMENTATION_REPORT_PACKAGE_D2.md;
usage DEVELOPMENT_D2.md. Originalsourceprofiles/A2/C1/D1/legacyfive not modified.
Rootimport created3knownuntrackedUIDs (Appearance/SkinPreview/WorldSkins); removed
onlythose. New DevelopmentPanel.cs.uid is taskowned andkept. Git diff --check green.

Pending only git integration commit/push/PR(no merge), then final answer. Base
remote still29cb58b atlastls-remote. Commitsalreadyccad0c0/2ef6410; authorconfirmed
Шелестов Максим Евгеньевич <314023140a@gmail.com>. Do not redo code or startD3.
Natural all-tier material availability, full content/import/V2,V3, longbalance,
Windows/Androidhardware andrealLLM remain explicitlyunverified/outofscope.

## Follow-up CI после PR64

Пользователь показал2fail+1cancel и запросил исправить реальные причины. Ветка
прежняяfeat/vnext-package-d2, HEADbe695e0, чистаядоисправлений. Проверены реальные
GitHubjobs121342200117/121342200253 иpush121341991455; artifacts скачаны в
/workspace/work/delsk-d2-ci/core,windows. CorePR timeout120, WindowsHUDstageEditor
shutdown timeout120, pushполностьюпрошёлдоD2 иупёрсявобщие15minнаDeveloper.

Реализованы scripts/test_partitions.py (base/integration/D2 exhaustive+TRXchecks),
Developer отдельныйjob, aggregateverify требуетобаjobgreen (не ослаблять required
check). WindowsHUD --import-cache:96проверенныхrootassets сbytes/source_md5 и всеми
бинарями; ownDeveloperassemblies untouched, nativeDebug/ExportDebug обязательны.
Нельзя трактовать124/timeoutкакsuccess.7tests/Checks зеленые.

Найдена настоящаяD2UIутечка: _industry/_experience/_experienceCurrent создавались
вlegacyпрофиле, ноникогданеAddChild; verboseGodot показалorphanOptionButton/SpinBox/
Label иRID. Исправленаlazyconstruction толькоDevelopment!=null. NativeProbe
добавилреальныеD2XP Set/Add иlegacyabsence +deferredcleanup;20checksDebug и20ExportDebug
зелёные, теперьнетRIDerrors/старыхwarnings. Productправила не менялись.

Локальный headless /workspace/work/delsk-d2-ci/local/headless:1153/102exports/
14replay/69pairs green; partitions1062+55+36 проходятпримернопо58/58/17s. Новый
Developer run /workspace/work/delsk-d2-ci/local/developer ещёrunning. HUD full
/workspace/work/delsk-d2-ci/local/hud-full green. Evidence/docs CI_D2_FIX.md.

Осталось: дождатьсяDeveloper, updateevidence/docs,commit/push втотжеPR64, дождаться
реальныхGitHubchecks иисправитьеслипокажутновуюпричину. НикакойD3/merge. Автор
Шелестов Максим Евгеньевич <314023140a@gmail.com>. Никакихsubagentsнеиспользовать.

Local follow-up done:1160Developer(1069+55+36), allstepsgreen, negativeRelease
expectedreject. NativeHUDfull20Debug+20ExportDebug withrealD2Set/Add andlegacy
absence; noGodotlogerrors. Core1153/102/14/69,7infraPython. YAML parsed, aggregate
verifyneedsbothjobs. Readycommit/pushsamePR64; thenwaitactualGitHubWindows/Core/
Developer, noautomaticmerge. Portableevidence development-d2-ci/.

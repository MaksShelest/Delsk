# C1: восстановление работы

Автор: Шелестов Максим Евгеньевич.25.10.2026, Europe/Samara.
База45fae36 (PR58+59 merged), исходное дерево чистое; ветка feat/vnext-package-c1.
Задание только C1, C2/снос не начинать. env /workspace/work/delsk-tablets-v1/env.sh;
результаты /workspace/work/delsk-c1. SDK/Godot прежние.

Аудит: normal Inventory+capacity holds строгие; Warehouse только200/two-cell,
build/upgrade мгновенные; ground/overflow нет. Требования spec0.6§6/§13 и typed
balance.b1 из BALANCE_VNEXT: ground70; warehouses200/300/400,100/250/600coins,
plank10 / plank20+steel4 / beam6+concrete10;60/120/180ticks. Overflow1200warnings600/120.

Реализована первая Core часть (build проходил), проверок ещё нет:
- optional EconomicRules.Storage; ground inventories на owned emptyplots через
  прежний manual Transfer (backpack<->pile); market/automation по-прежнему building-only.
- StorageConstruction records, warehouse start consumes ground/warehouse materials
  и money once, завершает поtick; reuse ground inventory identity, upgrade тот жеid.
- отдельные Inventory.Overflow lots, tx copy/normal-first protected holds, manual
  TakeOverflow через прежний VolumeWork; promotion+warnings+expiry/loss sink.
- C1 новые delivery contracts forecast вместо capacity hold (старыеholds сохранены),
  actual ReceiveDelivery normal+overflow, buyer pays after receipt; no new offer onoverflow.
- World19; read18 только безC1fields, null/default omitted старыйhash. SnapshotStorage,
  SavedInventory.Overflow, Flow.Lost, statehash/physical/value/demand stock change updated.
- C1 profile generator --export-storage-profile A2 --balance FULL --output NEW;
  VNextStorageProfile leaves oldA2 bytes untouched. config/vnext/economy.c1-storage.json
  с storage-projection-1 создан, ещё не подключёнкклиенту/lab. Newmaps должны братьC1,
  old5/defaultv6 сохранить. MapCompatible теперь A2+C1, sameB1setup aggregates.

Существенное ограничение явно сообщено пользователю: A2 не исполняет полный импорт/
производство plank/steel/beam/concrete (нетDtemplates). Не подменятьматериалы/выдавать
бесплатно ради warehouse upgrade. Unit/UI fixtures могут подготовить stock обычными
командами на специально заданных источниках; не выдавать за normal-map progression.

Дальше: meaningful C1 tests/исправления; privacy filter Overflow events (ещё НЕсделан,
PublicEventsSince сейчас отдаёт всеgeneric!), проверкиconstruction/overflow corruption,
купленнаяземля atomic tx; normal oldhash regression; UI StoragePanel ground/build
warehouse/overflow warnings+pickup/progress; client/labC1profile/defaultpair; source
profilegates, runner scenario+save/load/replay, fullboundedheadless/Developer/Godot,
report/architecture/decisions, commit/push/PRбезmerge. C2 не начинать.

Этап Core+UI реализован. stage2:12/12, stage3:16/17 (fixture создавала
одноштучные лоты при1volume/tick, поэтому pickup завершался заtick); stage4
исправляет fixture bulky plank4volume толькодля теста partialtransfer/expiry.
Добавлен отдельный Lost в demand metrics/validation, construction investment WIP,
покупка ground однойtx, missing construction journal validation. UI connected C1
newmaps, groundcard/warehouse overflow/read-onlyown HUD; ещёнетnativeacceptance.
Runner storage-c1 preparedfixture+scripts/check_storage_c1.py готовыподgate.
Следующее: stage4result, C1processgate, fullheadless/Developer/native UI/Godot,
actual18oldsave, docs/report, commit/push/PR. До C2 не переходить.

Финальный edge-fix: promotion при освобождении capacity отменяет ещё не завершённую
ручную rescue-операцию (а не уже переданные units), освобождает backpackhold и спасает
lot в normal. Отдельный meaningful test;19/19C1. TakeOverflow journal не утекает через
public TransferStarted/Accepted/Completed (полный researcher остаётся).
Native user-owned warehouse/HUD, обе локали/размеры, disk Continue и2293C1hashesPASS.
Первый полный1076/1082 пройден; после edge-fix ordinary1077PASS, итоговые headless
runner/Developer ещё running при этой записи. Mapsmall+large32checks каждыйPASS,
A143/A249, old18 exacthashPASS, Chromium2C1experimentsPASS.
После зелёных финальных gates: заполнить verification/storage-c1/checks.json,
AGENTS/report; удалить3old UIDimport-only еслипоявились; commit/push/onePRmain,
не merge. Не переходить к C2. Все recovery paths /workspace/work/delsk-c1.

Итоговые gates зелёные:1077ordinary/1083Developer/37Python; headless102/14/69,
C1process24checks/14exports/13pairs; nativeownHUD/realUI/RUEN/2293hashes,
legacyGodot1132hashes/35restores/stopped10. Финальное evidence
verification/storage-c1/checks.json и2screenshots; report+usage+ARCH/DECISIONS/plan/AGENTS
обновлены. Осталось только git finalreview/commit/push/PR безmerge; C2 не начинать.

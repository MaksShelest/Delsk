# Восстановление A2 — 24.10.2026, Europe/Samara

Автор: Шелестов Максим Евгеньевич.

База19d8cf1, чистая копия A1 e5aa8de; PR55/56 вошли в историю.
Ветка feat/vnext-package-a2. Только A2, после готовности остановиться перед B1.

Этап1: полный typed B1 в Hosting + extract_vnext_balance.py, отдельный формат;
A1 не редактировался. Reader strict/System.Text.Json required ctor/unknown reject,
1MiB32depth, decoded duplicate/null/int checks; A1 subset валидируется прежним reader.
Analyzer: static tiers/capacity/derived recipes/margins + bounded finite-seed
construction/XP-source witness,15-section coverage. CLI isolated before ModelServices,
optional explicit source+catalog provenance, baseline profile export only on verified
semantic success. Supported profile:34items/23V1/8resources, no fake future buildings;
legacy nonproduction infrastructure/defaults explicit. Optional EconomyProfile.VNext
metadata is not saved world state. Versions/default game unchanged.

Проверки ещё выполняются; этот текст не объявляет A2 готовым.
Далее: meaningful strict/semantic tests и реальная подготовленная glass fixture,
WIP checkpoint/direct load/replay, actual CLI exits/output guards, A1 regression,
bounded full headless gate из-за runner/manifest изменений, итоговый отчёт/commit/PR.
Scratch/logs: /workspace/work/delsk-a2. SDK env:
source /workspace/work/delsk-tablets-v1/env.sh.

Этап2: профиль/fixture уточнены по фактическим ограничениям Core: оборудование
выключено, demand bp сокращается11/10 (Core знаменатель<=100), population/growth
остаются baseline, fixed4 явно. Metadata содержит SHA канонического payload,
редактирование с сохранённой provenance отклоняется; мир хранит прежний resolved
ScenarioDefinition. Все49 первых focused cases прошли. Добавлены ещё2 независимых
source-leaf/projection guards; финальные gates ниже ещё в работе. Полный headless
запущен, поскольку добавлен script/manifest. Реальный новый рецепт crush_stone:
3stone→5gravel,20workticks, wages2000/service40; WIP@18/direct/replay.

Этап3 — финальный локальный gate green. /workspace/work/delsk-a2/headless-verified:
locked restore/build0,998/998tests(70.951s<120),102exports/14replay/69pairs;
cli-verified49process checks/4exports/3pairs; a1-final43process checks;
a2-lab-loopback37/37. Предыдущие failure logs сохранены, не выдаются за pass:
xUnit analyzer assertions/equipment-node/demandratio/duplicate fixture resolved;
Python sandbox socket EPERM устранён разрешением loopback, код не ослаблялся.
LF attrs дополнены для baseline economy из-за exact source SHA на Windows.
Отчёт/139leaf mapping готовы; публикация/CI/PR — следующий final handoff.
A2 stop: не начинать B1 автоматически. Default/Core/старые карты не изменены.

Публикация: реализация a8aa0c267802beb2a17338531fbda4dd8a895155 отправлена в
origin/feat/vnext-package-a2; PR57 https://github.com/MaksShelest/Delsk/pull/57,
open/unmerged. Финальный handoff уточняет generic coverage (без фиксированного69
в описании маленького fixture) и trailing whitespace process script. Build и
A2 process gate повторены для этой формулировки; состояние/правила не менялись.
CI проверяется по финальному head; результаты не подменять предположениями.
Продолжать A2 не требуется после зелёного CI. Следующий B1 только по новому заданию.

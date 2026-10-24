# A1 — восстановление контекста

Автор: Шелестов Максим Евгеньевич.24.10.2026 (Europe/Samara).

## Анализ / контракт

Чистая исходная ветка062381e; fetch обнаружил слитые PR51–54. База реализации
3a75917 (документационный PR54), ветка feat/vnext-package-a1. PR52 добавил
исследовательское пространство/observer/две карты; не откатывать эти изменения.
A1 кода ещё нет. EconomyProfile и SimulationJson прежние; формат мира18,
правила production-recipes-1, Session2/Envelope2. Профиль presentation-economy-v6.

Объём: отдельный immutable catalog-only контракт в Hosting, механическое извлечение
items/recipes/industries из B1, строгая структурная валидация и ранний CLI-return
в Research.Runner до ModelServices/мира. Цена industries.coins переводится в minor
ровно ×100; остальные поля сохраняются. Source SHA — точные байты B1; catalog
digest — точные байты самого каталога. Ошибки диагностируются fail-fast в стабильном
порядке. A2 граф/вместимости/адаптер EconomyProfile/прочие секции не реализовывать.

## Следующие этапы

1. Реализовать контракт/извлечение/reader/validator/CLI и meaningful xUnit.
2. Bounded restore/build/tests, реальные CLI success/failure/flags/output и
   воспроизводимость генерации; существующие профиль/save/load/replay regression.
3. Отчёт/отметка плана, commit/push/PR без merge. Никаких изменений старого профиля,
   сценариев, Core schema/save/обычной игры; после A1 остановиться.

## Реализация / фактический gate

A1 реализован: config/vnext/catalog.b1.json (SHA62bf82eb…), source4b12d705…,
Hosting VNextCatalog*.cs, отдельный VNextCatalogCommand.Run до ModelServices.
Механический extractor и --check, immutable DTO, fail-fast strict diagnostics.
LF закреплён только для B1/catalog, симуляционный профиль/сценарии неизменны.

Финальный restore/build exit0,947xUnit (50 новых cases),43actualprocess checks:
34/23/6,малыйвалидный,битаяссылка/IO/oversize100MiB,21forbiddenflags,
защитаoutput/series/первогоотчёта, повторнаягенерация/stale provenance;
старыйпрофиль120ticks/checkpoint59/load/replay4exports/3pairs совпали.
Scratch /workspace/work/delsk-a1; компактные evidence verification/package-a1.
Первый build поймал лишний бессодержательный assert (xUnit2000), удалён;
итоговые проверки прошли. Общий parser/execution не менялся (только ранний dispatch),
полный локальный headless не повторялся. CI содержит прежний полный gate +A1CLI.
Godot/Android/GPU/реальнаяLLM здесь не запускались, форматыWorld/Rules/save прежние.

Отчёт IMPLEMENTATION_REPORT_PACKAGE_A1.md и план2.3 отмечают A1готов/A2не начат.
Перед передачей: проверка diff/авторства, commit/push/PR безmerge. A2 не выполнять.

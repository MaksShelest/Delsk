# Пакет 6 + планшет лесопилки: точка продолжения

Автор: Шелестов Максим Евгеньевич. Обновлено20.10.2026 (Europe/Samara).

## Задание и границы

Пользователь разрешил пакет6 из §12.1 DELSK_NEXT_STAGE_PLAN.md и интеграцию приложенного планшета лесопилки. В нормативном пакете6 **P51–P53 и P56** (старый краткий итог пакета5 упустил P56). Без P57+. После каждого крупного этапа обновлять этот файл и фиксировать проверяемый commit. Никаких субагентов не требуется. Один итоговый PR, без merge.

## Текущее состояние

- База: слитый PR#41, `66de3a4`, рабочее дерево до начала чистое. Ветка `feat/package6-npc-sawmill`.
- До изменений: World18/Rules production-recipes-1, Session2/Envelope2;835 ordinary/841 Developer. Полноценный utility-v2, P22 adapter/runtime/fallback уже существуют; не писать второй executor или HTTP runtime.
- Архив сохранён в `docs/design/sawmill-v1/source.zip`, оригинальный README рядом. Это дизайн с демонстрационными данными, не спецификация экономики. Подключать именные узлы к реальным проекциям/командам; demo.gd/project.godot не импортировать как игровую логику. Шрифт Nunito с OFL обязателен при переносе.
- Этап1 P51 реализован: PolicyProfile.cs, общий legacy-v2 / modular-v3 executor, Easy/Medium/Hard, canonical module versions и experience schema1 без фиктивного обучения. Исторические fixtures остаются Legacy и checkpoint2; новые сложности используют checkpoint3. Full Release:841/841,0 failed/skipped; лог checks/p51-suite.*, TRX p51-suite.trx. Проверены exact continuation всех3, reject unknown module version и различие recipe decision Easy/Medium. Hard имеет ETA cost, расширенный forecast — следующий P53.
- Следующее действие: этап2 P52, заменить legacy wage rules только у Medium/Hard экономической оценкой. P52/P53/P56/планшет ещё не реализованы.
- Реальная модель в этой среде пока недоступна: ollama/gguf не найдены в workspace, loopback11434/api/tags connection refused (bounded3с). P56 реальную приёмку нельзя объявлять выполненной по историческому Windows P23; программную интеграцию и тестовый runtime реализовать, если модель останется недоступна — оставить точную локальную процедуру.

## Этапы

1. P51: модули/3 сложности/версии и restore, затем gate и commit.
2. P52: маржа/найм/зарплата/резервы с объяснимыми решениями, gate и commit.
3. P53: bounded planning + paired measurements, gate и commit.
4. P56: подключение прежнего runtime в обычную игру, ошибки/ожидание/fallback/save-load; реальный запрос только при фактической доступности модели. Недоступную проверку явно оставить открытой.
5. Планшет лесопилки по архиву + RU/EN/права/пауза, общий bounded gate, отчёт/push/PR.

## Воспроизведение и восстановление

- Начать с `git status --short --branch`, `git log -5 --oneline`, затем этот файл и AGENTS.md. Не начинать заново законченный этап. Незакоммиченные изменения сначала изучить.
- Среда: `source /workspace/work/delsk-package6/env.sh` (копия env пакета5), SDK10.0.401; Godot4.7.2 Mono в `/workspace/.tools/godot/`. Scratch/logs: `/workspace/work/delsk-package6/`.
- Каждый build/test/runner через `python3 scripts/run_check.py --name NAME --timeout SEC --log-directory /workspace/work/delsk-package6/checks -- COMMAND ...`.
- xUnit: исполняемый `tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll`, не пустой `dotnet test`. Не запускать несколько полных suites одновременно (180с при конкуренции CPU).
- Публикация разрешена; git author/committer: Шелестов Максим Евгеньевич /314023140a@gmail.com. Не класть модели/кеши/секреты в git.

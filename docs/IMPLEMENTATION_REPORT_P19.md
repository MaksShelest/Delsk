# Выборы и числовые блоки P19

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.

База —157216c, актуальный merge PR17; исходное рабочее дерево чистое. P00–P18 уже реализованы, выборов в фактическом коде не было. Прочитаны AGENTS, исходная спецификация, ARCHITECTURE, DECISIONS, IMPLEMENTATION_PLAN и существующие Core/Hosting/runner/tests. Ветка feat/elections-p19 создана от удалённой слитой базы. Реализован только P19; P20 не начат.

## Реализация

Elections/ElectionContracts/ElectionValidation принадлежат Core. Настройки по умолчанию period1200/baseVote1, null использует defaults; shortened scenario period10 явный. ElectionId1,2,... — отдельный монотонный номер, voting tick=period×ID. Phase1 SetVoteChoice принимает только текущий ID/существующего кандидата и меняет собственную декларацию; последняя команда своего actor до/на voting tick определяет выбор. После выборов все декларации очищаются, отсутствие выбора означает self.

Phase8 после трудового шага берёт ровно один EmploymentGroups список: production/extraction/logistics, включая idle N, но не unemployed. Политический блок=base+current N; один direct ballot участника кандидату, приобретённых блоков/рекурсивного переноса нет. Int1282×candidateWeight>total; ровно50% оставляет итог без победителя и назначает следующий период/ID.

Первый winner/election/tick фиксируется навсегда, дальнейшее определение победителя выключается. Mode=Victory опубликован хосту для завершения/паузы; существующий GameSession.SetPaused управляет ticks. Core продолжает обслуживать явные Step. ContinueAfterVictory разрешён первому победителю и переводит в Sandbox с датой; другие участники получают NotAvailable. Новая команда после перехода AlreadySandbox, повтор того же commandId использует общий dedup. Экономика/договоры сохраняют обычные фазы.

Immutable ElectionResult содержит employment snapshot/population/unemployed/ballots/tally/total/winner. Полная ElectionStateView — research/checkpoint/hash, ElectionObservation — allowlist rules/candidate IDs/schedule/own choice/public results. Чужие pending declarations не раскрываются. Исследовательские VoteChoiceChanged не передаются контроллерам как journal. Политические договоры и компенсации отсутствуют.

World11/Rules elections-1, Session2/Envelope1. Direct restore сохраняет schedule/id/choices/results/first winner/sandbox и обычные очереди/dedup. Validator проверяет исторический N через исходное состояние и creation/EmploymentChanged/PopulationGrew journal, один ballot/actor, sum=base×actors+population−unemployed, tally/strict majority, sequence/schedule и journal result/victory/mode. Старые World1–10 без migration, прежние файлы не перезаписываются. elections.json добавлен к15 побайтно сравниваемым экспортам.

## Фактические проверки

- Locked restore180s, Release build180s: exit0,0warnings/0errors. Полный xUnit bounded120s:512passed/0failed/0skipped, включая36 новых P19 tests.
- Новые tests: S5 blocks31/21/11/1/total64, direct52/11/1 и отсутствие рекурсивного переноса; exact32/64; latest choice в voting tick, stale/future/unknown candidate; outflow и hiring на voting tick; production/extraction/logistics с реальными positive N и рейсом; immutable/private observation; одинаковый seed/input/events/hash и dedup; current first result после ухода всех работников; pause/load без offline ticks; два IController через общую authenticated очередь; corruption10полей checkpoint; wide majority выше long/2 и invalid config/growth overflow.
- Save/load/replay tests на cut0/1/9/10/19/20/21/39 сравнивают каждый следующий hash, события и ledger. Новое оборудование после выборов не меняет исторический snapshot. Actual CLI file save/load tick9 сравнивает полный экспорт; Godot дополнительно делает file restores9/19/20/21.
- Full headless gate:19ordinary scenarios,16ordinary save/load pairs и прежняя controlled late-return pair;3command replay (queue,demand,elections).57exports проходят независимые arithmetic checks.36пар побайтного сравнения15файлов: full state/every tick hash/ledger/events/commands/labor/controllers/decisions/agents/market/scenario/delivery/logistics/demand/elections.
- Python сверяет I01–I09 и election I10/I11 с labor.csv voting tick, публичным ballot journal и собственным integer majority calculation; Core проверяет применимые I10–I14/I16. Полный S5 с секретным договором/компенсацией и support-часть I10 относятся к P20.
- Godot4.7.2/.NET10 Linux: locked restore180s/build180s/import120s/smoke30s/strict report+log verification15s: все exit0.62экономических и40выборных hashes совпали с runner. Actual checkpoint restores12/34/55/60 и9/19/20/21; lifecycle adapter не делает catch-up.
- Platform expectations обновлены явно после прежних числовых economic regression checks и independent balances. Исходный game_specification.md SHA256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376 сохранён.

Полные stdout/stderr/TRX/exports в /workspace/work/delsk-p19/final-gate и checks, компактные metadata в verification/p19/checks.json. CI сохраняет artifacts. Первый build тестов выявил два xUnit2031 замечания к Assert.Single; исправлено, успешная сборка выполнена до тестов. Первый полный gate прошёл; после уточнения прав ContinueAfterVictory финальный полный gate повторён. Логи не заменяются предположением о результате.

P19 полностью завершён в критериях IMPLEMENTATION_PLAN. P20/политические договоры, P21 utility-политика, LLM/UI не добавлены. Android physical device pause/resume/kill и Windows EXE на устройстве локально не проверялись; P06 device gate открыт. Godot Linux smoke — технический cross-host replay, не устройство и не UI-приёмка. Следующий ограниченный шаг: P20, приватное предложение поддержки и атомарная компенсация собственного блока на конкретные следующие выборы.

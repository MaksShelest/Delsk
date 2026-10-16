# Приватная поддержка и алгоритмический baseline

Автор: Шелестов Максим Евгеньевич. Дата: 16.10.2026.

## P20 — отдельный проверенный этап

База b685a4d — актуальный merge PR18 после P19, рабочее дерево чистое. Изучены обязательные документы и фактический Core/Hosting/Algorithmic/tests. Ветка feat/private-support-baseline. P00–P19 существовали; support proposals и расширенный P21 baseline отсутствовали. Сначала выполнен исключительно P20.

SupportSettings задаёт общую публичную торговую точку и конечные отдельные секции участников; null сохраняет исторические сценарии. Товар попадает в секцию только физическим Move/Transfer из собственных запасов. ProposeSupport — предложение кандидата купить собственный блок указанного продавца за до8 материальных позиций, на текущий electionId. AcceptedAwaitingResources не связывает голос. Частичные поступления в секцию кандидата получают exclusive stock holds; их нельзя потратить во втором предложении или вручную. Подпись phase3 возможна только после согласия, полной фактической компенсации, присутствия стороны в общей точке, проверки всего места и свободного собственного блока. Одна EconomyTransaction переносит все материалы и создаёт один commitment/receipt; ошибка вместимости второго материала не передаёт первый.

Proposed/AcceptedAwaitingResources/Rejected/Expired/Signed сохраняются. Отклонение/включительный voting deadline освобождает только собственные holds; Signed отменить нельзя. На phase8 commitment переопределяет обычный выбор и исполняется независимо от победителя. Размер блока берётся из current N после phase5, купленные обещания не входят в свой блок. В S5 цепочка A→B→C даёт A52/B11/D1, не63голоса A; старый договор не действует на следующих выборах.

Приватность: полная SupportResearchState вынесена из generic WorldView в explicit InspectSupport/checkpoint/hash/support.json; researcher events имеют полный private payload. Own/bilateral immutable SupportObservation доступна только сторонам. PublicEventsSince исключает support/choice commands и private payload, использует собственную непрерывную нумерацию и не содержит command IDs. Proposal IDs имеют отдельную последовательность внутри namespace каждого покупателя: чужие сделки не сдвигают собственный ID. Ошибки чужого/несуществующего ID одинаковы NotAvailable. После выборов публичен прямой ballot без причины и условий поддержки.

World12/Rules support-1, Session2/Envelope1; старые World1–11 не мигрируются и не перезаписываются. Snapshot хранит секции/предложения/partial holds/commitments/receipts/исполнение. Reverse holds, canonical section refs, уникальный seller/election, lifecycle и full journal проверяются при direct restore. CommandEnvelope сравнивает immutable compensation по значению, чтобы JSON decode/load не ломал dedup и recorded cursor.

Отдельный P20 gate:538/538tests (26 новых),20ordinary scenarios,17ordinary save/load pairs+controlled pair,4replays,61independent exports,39byte comparison pairs по17файлам. Новые проверки включают full/incomplete/multi-material atomic sign, запрет повторного собственного блока, override/no resale/current workforce/no winner/expiry, third-party observations/public events/errors/private ID noninterference, snapshots Proposed/Accepted/Signed, decoded dedup и corruption8полей. Godot Linux:222runner hashes (62economic+40elections+120support), actual file restores12/34/55/60;9/19/20/21;17/18/19/27/99/100. Все restore/build/test/import/smoke/verify запущены с hard timeout и exit0. Полные логи /workspace/work/delsk-p20-p21/p20-final-gate и checks; компактные metadata — verification/p20-p21/p20-gate.json.

Первый targeted запуск имел3 assertion failures: record equality сравнивала ImmutableArray по ссылке при сопоставлении private event payload после load. Канонический JSON журналов совпадал; тесты теперь сравнивают его полностью. Отдельно исправлено действительное сравнение команд для decoded compensation/dedup и добавлен тест playback cursor. После усиления ID privacy полный gate повторён. P20 завершён до любых изменений P21.

## P21 — полноценный utility baseline без LLM

Политика расширена предметными модулями ObligationGoals/MarketGoals/LogisticsGoals/PoliticalGoals/Planning. Вход — только ControllerObservation и собственные результаты. Выход — обычные GameAction, общий Hosting barrier/командный бюджет/валидатор. Нет Engine/WorldState в политике, HTTP, LLM, wall clock, чужих private contracts, будущих цен или world PRNG.

Обязательства и оплаченные покупки приоритетнее расширения; политика учитывает дедлайн, путь, объём, текущий trip, стоимость сырья/производства/зарплаты и залог. Принимает один будущий supply contract/запрос за раз, отнимает существующие bids/claims/contracts из потребности, использует AvailableMinor/free stock. Торгует sell/buy offers, сравнивает покупку сырья с добычей, повышает ставку только в пределах лимита и один раз за round, физически получает покупку. Управляет зарплатой/возобновлением/неоплачиваемым простоем, приобретает добывающую технику/транспорт, настраивает contract/inbound/output jobs через существующую очередь. При нулевых деньгах сама добывает и продаёт через прежний bootstrap.

Политика оценивает материальную компенсацию по базовым ценам и текущему размеру собственного блока, отклоняет низкую цену и известный риск отдать конкуренту большинство. Собственная победа приоритетна; веса/цена/горизонт конфигурируются. Покупает поддержку только для себя, доставляет фактическую компенсацию и подписывает обычной командой. Не имеет доступа к чужим скрытым сделкам; приобретённые голоса не входят в продаваемый собственный блок.

Remote auction/wage/support/logistics decision сохраняет остаток физического плана с cursor/goal/reason и восстанавливает его после feedback. BidReviews, FailedSupport, планы/цели/cooldowns/private PRNG входят в utility-economy-v2/checkpoint2. Rules baseline-1, World12/Session2/Envelope1. Старые правила/политика явно отклоняются без миграции; старые файлы не перезаписывать.

## Итоговые фактические проверки

- Bounded locked restore/build: exit0. Полный xUnit561/561, failures0/skipped0 (42.034s);49 новых тестов к базе P19:26P20+23P21.
- 24 обычных runner-сценария: все20 регрессий и4 новых baseline; exit0.21обычная пара save/load плюс отдельная controlled late-return pair.
- 8 command-only replay;51сопоставление выгрузок. Direct continuation17файлов побайтно, включая каждый tick hash/все планы/decisions/команды/деньги/товар/выборы/private support/public events; baseline replay14world-файлов без policy state/trace, потому что контроллер не вызывается.
- Независимый Python verify_results:77выгрузок, money/item/capacity/reserves/population/contract quota/payment/escrow/bond/elections/private support/public event invariants — exit0. Core проверяет инварианты на каждом тике. Полный headless gate158.728s при внешнем лимите780s; отдельные restore/build180s, tests120s, runners30/300s, replay30/300s, balances30s.
- Godot4.7.2 .NET/Linux, runtime10.0.12: locked restore/build/import/smoke/strict log verifier exit0.222хеша (62экономика+40выборы+120support),14настоящих file restores. Fixtures обновлены явно из независимо проверенных runner exports; прежние экономические результаты/хеши сохранены. Все шесть платформенных действий имели hard timeout.
- Дополнительно проверены Proposed/Accepted/Signed snapshots, dedup decoded compensation, атомарность многоматериальной передачи, no recursive resale/current workforce/expiry/no-winner, privacy чужих IDs/errors/events/observations; решения политики при низкой/высокой цене и риске majority, восстановление прерванного контракта, сохранение auction reviews/private RNG и изменение порядка контроллеров.

CI core использует тот же полный check_headless.py, включая minimum561 tests, все сценарии/сохранения/replays/балансы. Полные логи текущей среды — /workspace/work/delsk-p20-p21/final-gate и checks; компактные проверяемые metadata/counters/hash/profile — verification/p20-p21/final-gate.json. Исходный game_specification.md не изменён (SHA256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376).

## Поведение трёх настоящих контроллеров (seed42)

| Сценарий | Фактический результат | Среднее ms/tick |
| --- | --- | --- |
| baseline-economy,1200ticks | Все3 сами создали производство;44bread/104plank выпущено,33/50экспортировано;10из10контрактов Fulfilled,27изменений зарплаты;1support executed | 5.149 |
| baseline-auction,180ticks | NPC2 добыл/загрузил/продал2лота; NPC3/4 участвовали в2дефицитных аукционах,8повышений ставки в лимите, реальная выдача;22plank выпущено/20экспортировано | 4.306 |
| baseline-politics,160ticks | 6Signed,2Rejected;3Executed по первым выборам и3Active на текущие; физическая компенсация/11голосов без рекурсивного блока | 5.021 |
| baseline-logistics,360ticks | Все3 сами купили транспорт/настроили очередь; trips NPC2/3/4=8/7/10;67plank выпущено/48экспортировано,9контрактов Fulfilled | 4.444 |

Это wall-clock среднее всего runner с контроллерами/валидацией/инициализацией, не чистый Core/p99 benchmark. Все прогоны без модели и scripted human. Economy начинается без бизнеса; auction/logistics/politics — подготовленные стресс-фикстуры, не новый утверждённый баланс. В economy один конкурентный PlotOwned rejection, в politics четыре NotAvailable при попытках конкурентов подписать уже проданный блок; попытка каждого actor/proposal однократная, повторной передачи/commitment нет. Это допустимые разрешения гонок общим валидатором, не обход правил и не зависание. В auction/logistics CommandRejected0.

При реализации пилот выявил потерю остатка доставки после удалённого решения и недостаточный приоритет готового обязательства — исправлены persisted SuspendedPlan и приоритет доставки, добавлен regression test. Для логистики инвестиция теперь возможна после подтверждённого выпуска, даже если текущая партия уже доставлена вручную: иначе один NPC мог бесконечно упускать момент покупки. Первоначальная auction fixture исправлена добавлением товара рецепта в каталог. После исправлений полный gate запущен заново и зелёный.

## Границы и следующий шаг

P20 закрыт полностью отдельным gate и commit768c33f до P21. P21 закрыт в заявленной программной области по фактическим тестам/прогонам. Программная часть Gate C готова. Не проверены Android physical-device lifecycle P06 и актуальные Windows EXE/APK на железе. Многосидовые сравнительные эксперименты/общий баланс ещё не проводились (P24/P25); real LLM/UI относятся к последующим задачам. Следующий небольшой этап — **P22**: локальный model adapter/строгий parser/bounded queue/fake-adapter fallback tests. В этой итерации P22 не начинался.

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

## Следующая часть

P21 ещё не реализован в этом промежуточном commit. Его разрешено начать после данного зелёного gate. P22/реальная LLM/UI не входят в задачу. Android physical-device gate P06 и запуск Windows EXE на устройстве остаются непроверенными.

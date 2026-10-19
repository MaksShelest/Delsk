# Пакет4: торговля участников P43–P46

Автор: Шелестов Максим Евгеньевич. 19.10.2026.

База — актуальный слитый PR#39, HEAD `5fd27c40ab6cf5cd14d34f2dd6dc6c039b618b3f`; до начала ветка/рабочее дерево чистые. P37–P42 уже имели реальные volume transfers, finite resources, obstacles, route editor и прежнюю logistics queue. Participant market/pickup/DeliveryContract/P20 и bounded hybrid runtime существовали. Встречных версий, отдельного lot source и gate обеих сторон не было. Работа выполнена в `feat/package4-participant-trade`.

Пакет4 по §12.1 — **только P43–P46**. Старое указание «P43–P47» в срезе пакета3 было ошибочным; P47 не реализован. Наличие документов не использовалось как доказательство реализации.

| Пункт | Реализовано | Доказательства |
| --- | --- | --- |
| P43 | Built shop обеим сторонам participant/public/direct/support trades; land/reservation не заменяет shop. Столица/голосование доступны без неё. L1 shop=warehouse200 через ordinary config. Прежние holds и исполнение договоров сохранены | ParticipantTradeTests, SharedShopTests, DeliveryTests, SupportTests; recovery runner; полный suite |
| P44 | Shop gate отдельно от own building input/output источника. Public lot фиксирует item/qty/source/point до заявки; запрещены carrier/machine/cargo/WIP/чужой запас. Реальные public/direct holds защищают от двойной продажи; production использует свободный остаток | Конкурентное согласие, public hold vs direct acceptance, factory input при активном production, ложные источники, save/load; прежний scarcity auction |
| P45 | Sale/Purchase/Supply/Support, reviewed version, accept/reject/counter, bounded TTL/rounds/pending count. Atomic reuse pickup/DeliveryContract/P20; поддержка подписывается только после полной фактической компенсации. Own/bilateral/research privacy, обычные NPC/model options/fallback | ParticipantTradeTests, NegotiationPolicyTests; invalid/timeout model replies; stale option; duplicate closure; compensation/sign/current ballot; runner counter→delivery→support; private namespaces/third-party equality |
| P46 | Активные buyer claims/срок, shared grid path/obstacles/точка выдачи, slider/+/−/exact, частичная выдача и несколько рейсов. Existing supplier truck исполняет negotiated future delivery | Mid-transfer и mid-trip restore, several pickup trips/replay, partial deadline сохранение полученного; оконный маршрут и получение2/4;220 Godot tick hashes |

Последний voting tick включителен для уже открытого согласия и фактической подписи до фазы выборов; новые переговоры с нулевым оставшимся сроком отклоняются. Это отдельно проверено на save/load и replay.

До accept переговоры не резервируют средства/товар. Default limits — TTL60,4 версии,16 активных на сторону, counter не продлевает исходный срок. Supply с ещё не выбранным получателем требует buyer counter с own destination перед accept. Многосторонних договоров, текста как исполнимой команды и динамического доверия нет.

Минимальная адаптация baseline: входящие предложения оцениваются по потребности/лимиту цены/деньгам/capacity/ETA, поддержка — по прежнему риску и собственной победе; возможны accept/counter/reject. Не добывает/не выставляет свой production input для спекуляции. После выпуска и при свободном бюджете может приобрести прежнюю логистику прежде новых заказов; активные обязательства по-прежнему запрещают expansion. Без LLM работает полностью; сохранённые планы/модельные options проходят обычные validators. Физические output/inbound/production остаются независимыми.

## Реально выполненные проверки

- Locked restore, Release build: pass,0 warnings/errors. Полный ordinary suite **805/805**,0 skipped. Developer Debug **811/811**,0 skipped; Developer Release намеренно отклонён.
- `check_headless.py`: **23 scripted scenarios,94 exports,12 replay,63 comparison pairs**. Direct/load сравнивают22 файла; world-only18. Покрыты baseline economy/auction/politics/logistics, model safety S7, model-off equality, frozen voting, late return, физические циклы и новый participant-trade220. Balance/item flow/capacity/population/elections/support/negotiation/privacy/public-economy audits прошли.
- Три ordinary map validation; отдельно900 ticks третьей карты, checkpoint350, continuous/save/load/replay:4 exports/3 comparisons. Реальный профиль: median9.12 ms/tick,p95 52.93,average18.64,save3,031,734 bytes,decode1537.72 ms. Включены JIT/controllers/invariants/hash; измерялось совместно с другими checks, не гарантирует FPS.
- Godot4.7.2 Mono Linux: Debug и ExportRelease, metadata audit ordinary DLL; resource PCK inspection без Developer. Изолированная Developer client сборка/smoke проверяется отдельно от обычного кеша.
- Linux Godot shared-Core replay:62 economic+40 election+120 support+10 stopped+81 physical+**220 trade ticks**. Trade restore32/33/34/35/38/51/69: counter/version/escrow/частичная выдача/AcceptedAwaitingResources/Signed; third-party observation остаётся пустым. Все hashes совпали с runner.
- Реальный оконный клиент1280×800/Xvfb/llvmpipe RU/EN: slider/+/exact согласованы; встречная версия2 превращается в3 обычной командой, своё согласие блокируется. Buyer guide показывает4,0/TTL/path; движение и выдача выбранных2/4 происходят через Core, глобальный планшет сохраняется. Строгие Godot logs проверены; unsupported VSync notice не является аппаратным FPS-результатом.

Машиночитаемые результаты и выбранные screenshots: [verification/package4](verification/package4/checks.json). Процедура: [PARTICIPANT_TRADE.md](PARTICIPANT_TRADE.md).

## Совместимость и границы

World17 / Rules `participant-trade-1`, Session2 / Envelope2, ordinary profile `presentation-economy-v5`. Старый World16 явно отклоняется и не перезаписывается; старые config не переосмысляются. Начните новую партию для новых правил. Нуждающиеся в участниках focused fixtures имеют реальные shops/plots; политические legacy enterprises явно переведены в building definitions. Материалы/цены/география ordinary maps не менялись. Нормативная спецификация0.5 сохранена, SHA256 `e416b331a8353d5f39d8ff042288ad2493d39aae2c506a40ecc3f30de56fd56c`.

У нынешнего транспорта есть supplier-contract роль; **buyer pickup роли нет**. Поэтому доставка negotiated future contract назначается прежнему грузовику, а stock claim получает ручную навигацию/выдачу. Это условная граница P46 «если роль поддерживает», не новая система fleet planner. Нет доступного публичного API уничтожения лавочки: сохранение действующих обязательств обеспечено отсутствием нового shop gate на collection/delivery/ballot execution, без выдуманной механики разрушения.

Не проверены аппаратные Windows EXE/Android/60FPS, новая живая модель и длительная партия. Ограничения полной истории P27 сохраняются. P47/P48+/сложности не начаты. P33 теперь включает встречные переговоры; P32 ещё зависит от P51–P53.

Следующий шаг — пакет5 с P47 (многокомпонентный рецепт). Перед P48+ требуется выбор владельцем новых цепочек из §3 дорожной карты.

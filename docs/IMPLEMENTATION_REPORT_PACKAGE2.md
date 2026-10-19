# Пакет2 — политика и инструменты разработчика

Автор: Шелестов Максим Евгеньевич. 19.10.2026, Europe/Samara.

Основа — `cc4f54cc3b2846c1dc8480d4d15cb118af499f2d`, слитый PR#37. Дерево в начале чистое, открытых PR не было. Пакет1 уже имел карточки/планшет, Core ActionAccess, публичные капитал/труд, зарплаты20/5 и World14. Выборы ещё автоматически считались внутри тика; специальных Developer-вмешательств/пакета и остановленной стадии не было. Факты сопоставлены с кодом, не только документацией. Ветка `feat/package2-elections-developer-tools`.

| Пункт | Реализация и доказательство | Статус |
| --- | --- | --- |
| P36 | Обычный профиль:600 тиков, фиксированный workforce snapshot, отдельные бюллетени без экономического Step, signed binding, private pending choices, one-shot tally/first winner/no-winner/договор, сохранение/восстановление/replay. Алгоритм и Hybrid обрабатывают голосование на отдельном barrier; human headless policy явная. Политический экран RU/EN автоматически открывается, результат публичен, независимые паузы сохраняются | Закрыт программно и Linux-оконной проверкой |
| P54 | Compile/package-разделение, Core/Hosting mutators и Developer-экран исключены из обычных DLL; обычные Debug/ExportRelease проверены metadata audit, Release+Developer=true отвергается. Изолированный клиент, отдельный save name/marker, ресурсный пакет без Developer. Ограниченный журнал наблюдения/feasible candidate scores/цели/причины/шага/результата и model input/reply/timing/fallback | Закрыт программно; аппаратные устройства отдельно |
| P55 | Target money/total stock через прежние транзакции, безопасный wage exponent1–5, old/new/input-ordinal audit, dirty-party exclusion, save/load/replay. Настоящие Step до тика/на N, бюджет между шагами, остановка/отмена на человеческом выборе/модели/victory, явная временная self-policy | Закрыт в описанном безопасном объёме, не универсальный редактор мира |

Реализация — обычные небольшие partial/records и один специальный проект Simulation.Developer. Core не получил Godot/HTTP/wall clock; население осталось числовыми группами. Нет новой LLM/UI-настройки модели, сложностей, переговоров, ресурсов, фабрик или P37–P42. P32/P33 по-прежнему ожидают P51–P53/P45.

## Существенные правила

Freeze открывается в phase8 после labor/expiry, экономический тик коммитится один раз. При WaitingForBallots Core Step и travel не продвигают ничего; Enqueue принимает только текущий SetVoteChoice с issued/notBefore/expires равными frozen tick. Один принятый ballot на actor, exact command dedup; invalid/duplicate/stale не создают второй голос. Signed block задаёт кандидата до и во время стадии. После последнего голоса phase9 control events однократно публикуют результат и исполняют поддержку даже без победителя. Старые договоры не переносятся. Веса берутся из снимка current N по каждой роли ровно один раз; безработные веса не добавляют.

Controller observations замораживаются до решений. Hybrid ballot пользуется прежним bounded runtime/parser; poll barrier отличается от economic tick, очередь ротируется по electionId. Невалидный/поддельный/timeout/deadline ответ даёт обычный алгоритмический голос. Restore нового epoch не повторяет запрос и не принимает старый ответ. Replay читает принятые команды. До tally третьи лица не получают candidate/terms из чужого pending ballot; public DTO содержит awaiting IDs и собственные weight/binding.

Developer API отсутствует в ordinary Core/Hosting и ActionKind. Поздний MSBuild Compile Remove расположен в Directory.Build.targets; отдельные obj/bin/locks предотвращают смешивание. Прямой Developer build исходного Godot-клиента запрещён: packager создаёт отдельную копию/кеш, имя Delsk Developer, защищает каталог маркером. Экран и ресурсы исключены из обычного пакета. Обычный технический Debug сохранился, но использует только обычные команды/права.

Вмешательства доступны только на committed boundary/quantum0 вне голосования, с абсолютной целью до10¹². Защищены money/stock/capacity holds и managed cargo. Capacity/unitVolume/произвольные каталоги не редактируются. Wage exponent — намеренно минимальный параметр1–5. Base config/hash остаётся исходным, override хранится отдельно. Stock injection не считается harvest/production/G; money проходит external ledger. Audit до1024 записей содержит old/new и точную позицию среди входов; replay проверяет old value, в том числе после бюллетеней одного tick.

Journal256 записей, до16 feasible candidates и ограниченные тексты; не новый неограниченный dump. Полная прежняя research session history остаётся в checkpoint, её ограничения P27 не решены этим экраном. Forward делает до8 реальных Step за кадр,8ms проверяется между атомарными вызовами; не обещает жёстких8ms для одного тяжёлого Step. Отмена не откатывает мир. После load forward автоматически не возобновляется. Временная self-policy выбирается явно для одного запуска и помечается в аудите; подписанная поддержка её ограничивает.

World15 / Rules stopped-elections-1, Session2 / Envelope2. **World14 не мигрируется**, старые файлы не перезаписываются. Developer package saves отдельно и отвергается обычной сборкой. Manifest developerBuild/researchModified/cleanComparisonEligible не позволяет включать такую партию в чистые paired series. Новая обычная партия использует presentation-economy-v3/600/pauseForBallots=true; исторические fixtures сохраняют automatic legacy режим для регрессий. Headless требует явный `--human-ballot self`, сохранённый как ballot-only controller.

## Реальные проверки

Команды ограничены hard timeout, stdout/stderr/JSON exit status сохранены. Компактные свидетельства/SHA/скриншоты — [verification/package2/checks.json](verification/package2/checks.json). SDK10.0.401, runtime10.0.12, Godot4.7.2 mono, Linux.

- Полный ordinary Release gate: **734/734**,0 errors/failed/skipped;21 сценарий регрессии,4 baseline, S7/model-off и отдельный economy-profile treatment. **86 exports,10 replay,57 comparison pairs**,20 direct/16 world-only files. Exact save/load, all tick hashes/tapes/ledgers/flows, balance/privacy/invariant и независимый public-economy audit прошли.
- Полный Developer Debug gate: **740/740**,0 failed/skipped; locked restore/build и ожидаемый отказ Developer+Release. После уточнения диагностической причины/пустого candidate list повторно собраны и проверены8 DeveloperTools tests. Два набора — разные варианты сборки, их нельзя складывать как уникальные тесты.
- Новые случаи: delayed human/no hidden tick, strict majority/half, labor на границе, current workforce/double-count, signed binding/execute без победителя, duplicate/forged/stale ballot, private pending projection, save/load до и во время ожидания, replay; model success/invalid/forged/timeout/restored session/late reply; atomic stock/cash/capacity holds, safe parameter boundary, audit restore/replay/input order, stop/cancel/explicit human policy и bounded real candidate journal; absence/rejection ordinary build.
- Обычные Debug и **ExportRelease** DLL клиента/Core/Hosting/Algorithmic: metadata audit4 assemblies не нашёл intervention methods/screens/Developer refs/resources. Экспортированный обычный PCK смонтирован в пустом Godot-проекте и перечислен: основная сцена доступна, Developer paths/resources отсутствуют. Это проверка состава, не запуск Windows EXE.
- Godot Linux import/smoke:62 экономических +40 выборных +120 политических hashes с прежними4/4/6 direct file restores; дополнительно S8 final hash после10 тиков, frozen file restores3/6 и обработка бюллетеней без tick. Всё совпало с runner; verifier проверил отчёт/логи.
- Окно Linux/X11/Mesa llvmpipe1100×720: реальная обычная партия дошла599→600, ожидание человека не двинуло clock, меню/смена RU↔EN сохранили стадию, легальный голос вызвал tally без следующего tick; Developer-кнопки нет. В отдельном Developer-клиенте применён денежный target с audit, advance599→600 остановился перед человеческим голосом, cancellation и journal видимы. Снимки — из работающих клиентов, не макеты.

Найденные проблемы исправлены до приёмки: ранний Compile Remove не исключал SDK-generated item list (перенесён в targets и подтверждён metadata); S8 добавлен также в CLI allowlist; resource inspection учитывает Godot scene remap через ResourceLoader. Исправлены читаемость непрозрачных панелей и явное отображение diagnostic reason. Старые работающие системы не заменялись.

## Ограничения и ручная приёмка

Windows EXE на устройстве, Android/тач/lifecycle на телефоне, физический монитор/fullscreen,60FPS, новая реальная модель и новая длинная партия не проверялись. Полный research history по-прежнему растёт; прежние RAM/save/load ограничения P27 действуют. Fake adapters доказывают safety/protocol, не работу реальной модели. Не все варианты интерфейсного действия пройдены мышью: ядро/runner покрывает больше сочетаний, чем указанные оконные сценарии.

1. Начать обычную партию или Continue; к тику600 дождаться политического планшета. Задержать голос, открыть меню/поменять язык: clock/N/WIP/cargo не меняются. Подписанный блок допускает только своего кандидата. Голос завершается без ещё одного экономического tick; проверить публичный результат/песочницу по существующему правилу.
2. Создать изолированный Developer-клиент по [процедуре](DEVELOPER_TOOLS.md), открыть меню → Developer. Изменить target денег/товара на безопасной границе, проверить audit old/new и отказ при нарушении holds/capacity. Изменить wage exponent, сохранить/загрузить.
3. Forward до будущего tick без human policy должен остановиться на выборе; отменить. В новом запуске явно разрешить self-policy, проверить marker и дальнейшее продвижение. В обычной сборке API/экрана нет; Developer save не загружается.

Дальше — **пакет3 (P37–P42)** по сохранённой дорожной карте. P30 остаётся отдельной визуальной задачей; P56/реальная LLM в обычной игре не начинались.

# E2: дороги и согласованное движение

27.10.2026, Europe/Samara. Автор: Шелестов Максим Евгеньевич / MaksShelest.

Основа26aeaab — merge PR66, содержит финальный E1/7163598. Начальный Git чистый.
Удалённая main проверена ls-remote/fetch; E1 Core40444910806 и Windows40444910803
success именно на финальном7163598. Ветка feat/vnext-e2-roads.
Используются подготовленные SDK10.0.401/Godot Mono4.7.2/Xorg, прежняя сеть/cache.

Авторизован только E2/P70: последовательные дорожные ступени, вклад каждой,
реальная работа приобретённого Тракта/числовой штат/материалы/время, движение
и обход внутри существующего публичного коридора. E3/счета не реализуются.
До финального отчёта наличие кода или промежуточный green не означает приёмку.
Большие логи/fixtures — /workspace/work/e2; небольшие итоги будут в Git.

Промежуточная реализация: отдельный vnext-e2-roads-1, World25 limited24–18,
append-only RoadContribution, Тракт/один прежний кузов/числовая группа capacity2,
целая работа2×8/12/16, фактические материалы и зарплата. Публичные задания
используют existing treasury holds и платят один раз; E3 начислений нет.
Traffic хранит trajectory/progress, эксклюзивные клетки/переходы, удержание съезда,
ожидание/разъезд/forward turn и closing-bypass. Tick/quanta делят один бюджет.
Own immutable observation, roads.json, independent road_audit добавляются к
прежним капиталу/flows/персоналу/privacy. Интерфейс/3D/NPC ещё проверяются.

Реальные промежуточные попытки:

- build1/2 — ошибки объявления CLI и xUnit analyzer, исправлены; build3–8 exit0.
- road-tests1:1/6, выявлена несинхронизированная Facing actor при движении машины.
  Исправлен runtime, а не ожидаемый hash.
- road-tests2:5/6, добавлен отказ пропуска ступени при конфигурации маршрута.
- e2-tests3:7/11, e2-tests4:9/11. Исправлены зависание помощника на первом
  сегменте и двойное расходование travel budget при смешении quanta/Step.
- traffic-tests5:3/5. Выезд Eger из paired dealer parking выбирал путь через
  непроданную машину; добавлен законный replan вокруг статического stock.
  Replay диагностика showed только travelQuantum3 против0, все события/деньги
  совпали: проверка whole-tick Replay теперь корректно закрывает travel budget,
  отдельный save/restore среди quanta остаётся обязательным.
- Для встречного сценария начальный курс выставляется настоящим проездом,
  а не запросом запрещённого reverse. Уже уступающая машина защищена от второго
  встречного yield; разворот строит настоящий путь через свободный turn/bay.

E2 пока WIP, финальные gates/отчёт ещё не выполнены. Известные неуспешные
попытки не объявляются pass. Все изменения в отдельной ветке, E1 переиспользован.

traffic-tests6:5/5, exit0, реальные head-on/yield, обход/closing, 30/60 helper,
съезд/restore/replay и explicit turn. build9 также exit0. Новые theory20/40/60,
второй строитель и Freight+укладчик добавлены и ещё проходят проверку.
Сохраняется WIP checkpoint в Git; это не финальная приёмка.

Следующий recovery snapshot: shared traffic также охватывает прежнюю добывающую
машину через вычисляемый RoadBody, без второго inventory/vehicle. Автоматический
переезд Тракта оплачивает фактическую группу; закрытие объезда хранит фактических
Users, а не все будущие маршруты. Дубли quanta/Step устранены в общем Hosting.

build11 failed (shadowed locals), build12 passed; road-tests9 launch до готового
Debug assembly failed, не test pass. road-tests10 9/10: неверная карта4участников/
2лавки в новой electoral fixture. build13 failed (имя ResourceNodes/analyzer в
новом тесте), build14 passed. e2-unit-1 остановлен правильным map validator:
добавленные места лавок отсутствовали в Layout. Фикстура исправлена целиком.
E2-unit-2 уже прошёл history1/1 и roads10/10; traffic/freight ещё выполняются.
Python Checks18/18. WIP CI40467554767 failure (unit/developer), platform
40467554757 success; это не финальная приёмка. Новые E2 suites выделены
в отдельные required ordinary/Developer jobs, previous+roads дают все partitions;
прежние individual/aggregate deadlines сохранены, старые тесты не исключены.

E2-unit-2: history1/1,roads10/10,traffic11/11 passed; freight2/3 passed
(реальный Freight и NPC task). Mining fixture ошибочно задавала public+owner,
отклонена ResourceValidation до запуска. Исправлены права/география ресурса.
Добавлен отдельный тест создания грунтовки в пустом публичном коридоре:
допустимый обход может иметь грунтовые концы, готовое полотно предпочитается.
Дорожные работы не должны требовать уже готовую дорогу с двух сторон для
первой грунтовки. Closing test теперь явно проверяет фактического Users при
completion. Финальные gates ещё впереди.

E2-unit-3: не принята (3 встречных narrow failures на промежуточном выборе
бокового конца). После предпочтения готового полотна E2-unit-4 прошла
history1/1,roads11/11,traffic11/11; Freight/NPC2/3, ошибка только mining map.
Малый native/copy stage ещё не является полной приёмкой. Checks18/18,
Research.Lab37/37. На4c74712 предыдущие ordinary/Developer и Windows jobs
success; новый road job покаfailure из-за fixture, не выдаётся за accepted.

Mining attempts15/16/17: исправлены законная public/private география,
актуальное extract_wood имя из каталога и направление реального проезда;
checkpoint17 подтвердил штатное NoOutputSpace в полном источнике, а не
движение: прежний ExtractionRoute переезжает при depletion. Тест теперь
использует ограниченный собственный источник слева и собственный ресурс справа.
Mining18:1/1 pass, actual bypass + restore inside + subsequent real output,
одна старая Equipment/Storage, без нового Vehicle. Добавлена отдельная
проверка минимального1 рабочего/половинной мощности, полная повторная suite
и runner audit ещё ожидаются. NativeRoadProbe запущен в обычном isolated stage.

8bec5d6: итоговая ordinary E2 suite27/27, без skips/overlap, локально326.8s
в780s. На том же code snapshot CI previous ordinary/Developer, road ordinary/
Developer и Windows success. Native1 passed RU/EN/one extraction body,
132.1s, strict logs;654 stage/runtime files совпали. Runner1 ещё выполняется.

Нативный скриншот выявил последнюю неточность: Cursor1/1 уже завершён, но
Reason Working/Enabled ещё жили до следующего тика. Исправляется в Core:
последняя клетка сразу завершает маршрут и освобождает машину. Добавлены
semantic/native assertions; предыдущие passes не выдаются за проверку этого
исправления, source gates повторяются. E3 по-прежнему не реализуется.

97f0538: new27/27 локально и обе ordinary/Developer suites в CI success.
Windows attempt1: build/ordinary metadata success, root editor import timeout124
на прежних120s. Один повтор натомжеSHA: import exit0, completeness exit1.
Обе попытки сохранены как failure, не pass. Добавлена отдельная обязательная
фаза нормального запуска Monoeditor --import перед прежним --quit-after30;
обе имеют неизменные120s и обязаны exit0. Финальные completeness/replay/logs,
Release/Developer/HUD gates сохранены, добавлен вывод failed check logs.
Это не принятие timeout и не изменение игрового кода. Проверка продолжается.

1084714 Windows prime --import тоже timeout124; stderr показал
EditorSettings not instantiated during early Mono editor shutdown. Не принят.
Этот способ удалён. Вместо загрузки C# проекта редактор импортирует отдельную
копию96 тех же assets с теми же import settings. Прежний reuse verifier
проверяет bytes/source_md5/полноту и переносит только реальные imports,
не managed assemblies/editor state. Обычный Mono runtime/replay, Release
metadata, Developer/HUD/cleanup проверки остаются обязательными.

Локальный эксперимент: asset editor exit0 за6.7s в120s; независимые96imports
прошли, затем C# runtime без editor cache прошёл1132tick/hash проверки и
strict logs. Checks20/20 включают защиту staged bytes/settings и чужого output.
Игровой код с97f0538 не менялся. Native-ready также passed136s; runner-ready
51process/35exports/24comparisons passed328s, unit-ready27/27 passed403s.
Настоящий E1 World24/348 checkpoint SHA0e92b388... продолжен, все выгрузки
побайтно совпали с исходным E1 manual-cargo. Финальная Windows CI ещё ожидается.

cd827d2 Windows дошёл до подготовки assets и выявил portability bug нового
helper: Path.read_text использовал Windows cp1252 для UTF-8 project.godot с
русским авторством. Исправлено явное UTF-8; boundary fixture содержит кириллицу.
Checks20/20 повторно passed. Это ошибка подготовки CI, gameplay97f0538 неизменен.
Все9 Core jobs именно97f0538 завершены success, включая production/fleet/roads/verify.

Финальный software gate: gameplay97f0538, все9 Core jobs40476826094 success;
Windows14ddbb1/40480084256 success включая ordinary/cache/runtime/release/HUD/
cleanup. Последующие изменения только asset-import CI/helper/two tests:
проверено823 input hashes,656 exact native-source inputs, никакого gameplay/
баланса/existing test изменения. Итог27 E2 tests,51runnerprocess/35exports/
24pairs, nativeRU/EN, actualWorld24exact,1132legacyhashes/35restores,Python20+37.
Все исходные failed attempts/timeout сохранены, deadlines не увеличивались.
Документы/plan/AGENTS отражают E2/P70 software, не весь vNext/длительный баланс/
устройства. Дальше только review PR67. E3 не начат, auto merge не выполняется.

Контрольный просмотр перед final docs выявил слабую проверку disabled route:
неверные Stops/level при Pause без активного batch могли попасть в состояние
и упасть на invariant. Guard теперь одинаков для Start/Stop, пустой маршрут
допустим только disabled. Добавлены реальные rejected-команды в существующий
atomic protected/materials test. Это единственный последующий gameplay delta,
отказ invalid input; валидные сценарии/баланс не меняются. Приёмка обновляется.

В той же проверке atomic stop перемещение CancelTrafficRoute поставлено до
замены Route/Cursor/Level: отказ VehicleMoving не меняет прежнее намерение.
Personal test теперь проверяет это настоящим travel quantum и rejected Stop.
Количество E2 cases27 сохранено, существующий сценарий усилен, не исключён.

Completed status теперь сохраняется и на следующих disabled тиках вместо
UserPaused; добавлены post-tick assertions в automatic test/native probe.
Финальный code freeze, новые gates выполняются на этом снимке. Дальнейший
объём ограничен E2; подготовленные ранее runner/native/CI records остаются
историей и не подменяют свежие подтверждения этого снимка.

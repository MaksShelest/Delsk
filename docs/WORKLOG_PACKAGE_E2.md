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

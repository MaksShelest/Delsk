# E1: восстановление и ход проверки

27.10.2026, Europe/Samara. Автор: MaksShelest.

Основа `ce8f0e3` — merge PR65, содержит финальный D3 `a616649`.
Подготовленный checkout был на чистой ветке `work`; создана отдельная
`feat/vnext-e1-fleet`. Удалённая `feat/economic-cycle-p00-p05` проверена через
`git ls-remote`: также `ce8f0e3`. Финальные PR65 workflow runs на `a616649`:
Core 40362781390 и Godot 40362781514 — success. Старый отчёт D3 содержал
pre-push статус; он не подменял фактическую проверку GitHub.

Среда: подготовленные `/workspace/.tools/dotnet` 10.0.401 и Godot .NET 4.7.2.
Обычный `godot` в PATH — 4.6.3 без Mono, не используется. Пакеты из имеющегося
NuGet cache; locked restore выполнен с разрешённой сетью. Первый запуск готовой
тестовой DLL дал 0 тестов D3: это устаревшая сборка и не считается pass.
Все последующие проверки собираются из исходников. Большие логи и fixtures
хранятся в `/workspace/work/e1`, не включаются в репозиторий.

Реализованы отдельный профиль E1, конечный автосалон, общее окно с денежными
holds, личное управление, один грузовой inventory машины и интеграция прежнего
Freight/Logistics executor. Площадки имеют отдельные числовые группы, комплекты
и площадки требуют реальных денег/материалов/монтажа. World24 с limited23–18,
own immutable projections, researcher fleet.json, hash/save/replay.

Промежуточные проверки:

- `build-e1-6`: успешная сборка Core/Hosting/Algorithmic/LLM/Runner/tests.
- `e1-tests-6`: 11/11, TRX и exit0; 95 секунд. Включает 2500/500 границы,
  сравнения сохранений по стадиям Freight, ручной груз, персонал и оплату.
- Дополнительные 14 тестов `e1-tests-7`: 12 passed, 2 failed. Выявлены ошибки
  prepared fixture: комплект материалов + 35 древесины превышал вход80;
  противоположный курс запрашивал торможение. Fixture исправлена: 30 древесины
  и поворот через перпендикулярный курс. Этот запуск не объявляется итоговым.
- `client-build-2`: обычная Godot Debug сборка успешна, 0 warnings/errors.
- Native FleetProbe находится только в tests и изолированном stage;
  ordinary AssemblyAudit запрещает его. Начат bounded RU/EN mouse-control gate.

**На момент этой записи E1 ещё не принят:** нужны свежие итоговые unit suites,
runner/audits, native checks, Developer/ordinary regression и итоговый отчёт.
E2, дорожные работы, новый помощник, счета и сторонние услуги не реализуются.

После промежуточных запусков:

- Нативный gate прошёл: два настоящих RU/EN mouse-control сценария, сохранение
  в погрузке, exact continuation, одна 3D-модель на экземпляр, mounted-actor hidden;
  strict Godot log verifier exit0. Использован уже установленный Xorg/dummy,
  установка Xvfb не понадобилась. Evidence `/workspace/work/e1/native/`.
- Большой старый base partition истёк на120s без утверждений failures в журнале.
  Разделён на явные physical/models/base группы без исключения тестов и без
  увеличения deadlines. `headless-checked` подтвердил292base, integration/D2,
 38D3 и compact-start, затем остановился на одном новом NPC harness test.
- Новый harness не вызывал обязательный `IController.CommandSubmitted` и повторял
  уже принятую MoveTo. Исправлен именно harness; настоящая политика не обходится.
  `npc-fixed`:1/1 с реальной покупкой/назначением и восстановлением plan checkpoint.
- Новый полный `headless-ready` запущен на исправленных исходниках. CI расширен
  отдельным bounded E1runner job и обязательным fleet результатом в verify;
  прежние core/developer/production gates сохранены.

Уточнение Developer: новый свободный кузов Тракта теперь проверяет допустимый
материал **до** изменения stock через существующий God API. Обычная сборка этой
проверки/API не содержит. Добавлены два Developer regression: неверный товар не
меняет мир и аудит; разрешённый щебень сохраняется и replay совпадает. Это
защита действующего инструмента от частично применённого невалидного состояния,
не новый публичный обход правил.

Итоговая проверка после исправления harness:

- `headless-ready` выполнил все 1219 ordinary тестов: 292 base,55 integration,
  36 D2,38 D3,1 D3-start,20 E1,1 E1-stock,644 physical,132 models. Все девять
  TRX без skips/overlap, каждый процесс exit0. Общая команда затем достигла780s
  уже в runner-фазе; aggregate exit124 сохранён и **не считается pass**.
- Unit и runner фазы теперь отдельные обязательные CI jobs с прежними
  per-process/aggregate deadlines. `verify` требует обе вместе с Developer,
  D3 и E1. Локально переиспользуются только подтверждённые завершённые unit
  результаты и реальные fixtures; все runner checks выполняются отдельно.
- E1 runner: все30 процессов исполнения/сравнения прошли:22 exports,16 exact
  continuous/save/load comparisons по покупке, ручной и автоматической загрузке,
  комплекту, площадкам, обработке75volume/tick, замене ассортимента. Первый
  общий audit выявил старое предположение verifier о любом грузовике10/crew2.
  Исправлен verifier: приобретённая машина сверяется с fleet.json/моделью и
  её настоящим cargo inventory; старые проверки оставлены. Повторный audit
  всех22 exports и public-economy exit0. Два воспроизводимых E1 profile exports
  также exit0, результат сверяется byte-for-byte с checked config.
- Python checks15/15, включая11 намеренных повреждений fleet-аудита.
  Research Lab37/37 с разрешёнными localhost sockets; первый network-denied
  запуск сохранён как неуспешный, не принят за проверку.

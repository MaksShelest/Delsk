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

На code snapshot `c62352b` GitHub `unit` и `developer` уже success
(Core run40443103322); Developer gate реально выполнил полный набор и
отрицательную Release build за438s в прежнем600s лимите. Windows run40443103227
success: ordinary metadata,1132 shared-core hashes/35 restores, Release audit,
20 HUD/God checks в Debug и ещё20 в стандартном ExportDebug, cleanup.
На предыдущем8215894 Windows run40441604092 завершился на проверке полноты
import-cache exit1, не на игровых assertions. Следующий свежий checkout
прошёл ту же проверку без изменения import скриптов/deadlines. Причина того
одиночного сбоя не изолирована; не считать его успешным. Выгрузить его artifact
в локальную среду через доступный download не удалось (403), статус/steps/log
получены GitHub connector. Старые Core runs при следующих pushes cancelled,
не являются доказательствами полного gate.

Native source сравнен с текущим checkout:216 одинаковых C# файлов; различия
только в четырёх NPC planning/estimate файлах, HelpOverlay и conditional God
intervention guard. FleetPanel/PresentationStage/Fleet/handling/transfer handlers
совпадают byte-for-byte. Для последующих NPC/Developer изменений используются
свежие unit/CI проверки; native pass не выдан за их тест.

Локальный повтор Developer достиг aggregate600s (exit124), успев завершить
309base/55integration/36D2/38D3/1start/20E1/1stock. Остальные local partitions
не засчитываются; после deadline проверено отсутствие оставшегося test process.
Не запускать этот полный набор заново только ради локального green: тот же
полный Developer gate уже реально success в CI40443103322/121987852578.
Локально запущен отдельно `headless-runs --phase runs` с подтверждёнными
`headless-ready` unit artifacts; CI runner-фаза также уже success, дальнейшие
старые C1/C2/D1/D2 process gates ещё проверяются.

Финал CI на `c62352b`: **все** unit/core/developer/production/fleet/verify
success, Core40443103322; Windows40443103227 success. Финальный `verify` реально
требует все пять suites. Новый полный E1job прошёл без локального reuse.
CI source724 digests и snapshots сохранены в docs/verification/fleet-e1.
Дальнейший итоговый commit содержит только документы/evidence.

Локальный `headless-runs --phase runs` завершён exit0 за490,514s:102exports,
14replay/69pairs, полный independent balances/public economy audit. Этот
runner результат вместе с1219 завершёнными unit tests и свежим полным CI
закрывает software regression; исходный aggregate timeout остаётся failure.
E1 завершён; E2 и следующие пакеты не начинались. Итоговый отчёт и небольшие
manifest/exit records сохранены в Git, большие run folders не коммитятся.

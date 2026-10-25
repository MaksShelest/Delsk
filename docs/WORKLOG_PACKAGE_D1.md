# D1 — восстановление работы

Автор: Шелестов Максим Евгеньевич. 25.10.2026.

## Этап 1: проверка основы и границы

Основа85955d7 — слитый PR62/C2, рабочее дерево было чистым. Ветка
feat/vnext-package-d1. C1/C2 переиспользуются. Фактическое производство — один
EnterpriseState/EmploymentGroup/Batch, общий ротируемый AdvanceWork, реальные
EconomyTransaction и output holds. Параллельных цехов нет.

D1: явные правила цехов, независимые партии/штат/управление, общие конечные
input/output, кривая0/25/45/65/85/100, услуга2minor/полный рабочий тик,
числовое перераспределение только существующего штата, сохранение/replay,
оплаченное оснащение готового места60тиков. Новые места/XP/контуры/V2/V3 — D2.
Не превращать старый одноячеечный legacy upgrade в бесплатный второй цех.
Для параллельного сценария допустима явно подготовленная физическая площадка;
обычная начальная фабрика имеет только одно оснащённое место.

Старые resolved профили и точные legacy/C1/C2 hashes должны сохраняться:
новые DTO optional/default-omitted, новый режим включается явным профилем.
Никаких индивидуальных работников или второго производственного/логистического
исполнителя. После каждого крупного этапа дополнить этот файл фактами/gates.

Следующий этап: Core/сохранение/сценарии, затем UI/NPC и полный gate.

## Этап 2: реализация и первые проверки

Core использует прежний EnterpriseState/Batch/EmploymentGroup для первого цеха
и тех же состояний для дополнительных, с явной parent-ссылкой и общими input/output.
Workshops.cs: команды монтажа/переноса, persisted least-recent-start fairness;
Batch.Terms сохраняет рецепт/ставку/услугу; остановка/NoMoney атомарны.
World21 limited20/19/18 без новых полей; null/default omitted для legacy hashes.
C2 учитывает все партии/группы и фактические материалы монтажа; установка блокирует
снос до конца60тиков, новый монтаж при подготовке запрещён.

Первый targeted gate:15/15; обычный Core и Godot client build без warnings.
Добавлены дополнительные проверки границ монтажа/одновременного завершения,
денег/найма/многотоварной логистики/NPC. Они ещё ждут запуска на финальном коде.
Добавлены D1-профиль из checked A2+BALANCE,6первичных фабрик с1местом,
WorkshopPanel и отдельный native probe, runner workshops-d1 и workshops.json.
Новые карты используют D1 при new game; saved config/старые5карт не подменяются.
Натуральный импорт всех материалов/расширение/XP по-прежнему не реализованы.

## Этап 3: полный gate и исправление runner

1116/1116 ordinary (113.811s/120), headless весь прошёл (421.386s/780):
102exports/14replays/69pairs;37Python. D1process18checks/10exports/9pairs,
353ticks, cuts46/105/158/194; profile generation exact twice. C1регрессия24checks
успешна. Native actual Playfield RU800x720/EN1100x800 успешно проверил2карточки,
start/stop одного/WIP/staff transfer/recipe guard/diskContinue; экраны просмотрены.
Дополнены локализация новых товаров/отраслей и справка.

Найден runner-only fallthrough: вставленный if D1 разорвал C2 else-chain, после
C2 вызывалась старая equipment fixture. Исправлено явным else if. Повторный C2
полностью зелёный, D1process повторно зелёный, final targeted21/21. Старая actual
World20/C2save794 продолжена до1996 с точным прежним hash и всеми metrics rows.

Первая параллельная Developerпопытка достигла180s timeout (не pass). Сейчас
запущена изолированная d1-developer-final; проверять её metadata, не объявлять
1122pass до финального результата. Осталось: финальный nativepolish (добавлена
nativeоплата монтажа), smoke/hash/audit, browser map preset, docs/evidence/Git/PR.

## Финальный просмотр

Добавлен22-й случай:5оплаченных линий на явно подготовленных6клетках, штат5
перераспределён1/1/1/1/1 без создания людей; общий outputhold10, одновременный
выпуск10 и restore. Первый build нового теста выявил nullable warning-as-error,
исправлено явным non-null fixture reference; затем1117/1117 ordinary (109.322s).

Уточнена безопасная граница зарплаты D1: pending pay/attraction не объявляются
действующими до завершения WIP; batch terms всё время прежние. Legacy ветка
не меняется. Убран недостижимый conditional в legacy input-room. Targeted22/22
на этом финальном Core прошли; полный ordinary d1-boundary-tests выполняется.
После него не запускать тяжёлые проверки параллельно с Developer tests.

## Этап 4: финальная приёмка и передача

Все18закреплённых gate metadata успешны в verification/workshops-d1/checks.json.
Финальный Core:1117/1117 ordinary109.379s/120,1123/1123 Developer138.798s/180,
22D1;37Python. Полный headless102exports/14replay/69pairs. После finalCore отдельно
повторён D1process18checks/10exports/9pairs/353ticks. C1 24checks, C2 16checks.
Nativepolish прошёл оплату монтажа через actual UI и Continue до его окончания;
параллельные партии/Stop/перенос/recipeguard/Continue WIP, RU800/EN1100. Images
просмотрены. Новый defaultD1 проверен actual small-river/3NPC native setup/start/
replacement/Continue; обе новые карты также через Chromium/lab30ticks каждую.
Linux legacy1132tickhashes/35restorepoints, ordinary audit5assemblies clean.
Rootclient повторно собран после последних UI правок, без warnings.

Исходник финального native stage совпадает с текущими changed C# файлами.
Три известные untracked UID от rootGodotimport удалены; WorkshopPanel.cs.uid
остаётся tracked материалом новой панели. Длинные logs/builds вне git.
Report и WORKSHOPS_D1.md содержат границу: natural расширение/XP/import — D2,
prepared3/6cell fixture не означает natural progression. D2 не начинать.
Осталось только финально проверить diff/автора, commit/push/один PR без merge.

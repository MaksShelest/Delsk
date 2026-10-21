# Переработка планшетов — контекст и этапы

Автор: Шелестов Максим Евгеньевич.20.10.2026 (Europe/Samara).

База `77d40e9`: PR#44 и#46 слиты; PR#45 открыт Draft (e195be1), не часть актуальной базы. PR45 содержит отдельную карту и аудит, редактор F04 не закончен и не восстанавливается в рамках этой задачи. PR46 добавил ResourceInventoryHud с положительными physical goods/парными цепочками и Developer-only окно из Debug; сохранить partial hooks/паузы/audit. Ветка feat/tablets-design-v1, исходное дерево чистое.

Архив delsk_tablets_godot_v1.zip (205files,48scenes) — референс представления. В нём demo.gd/фиктивные данные, не игровые инструкции. Никакой замены project.godot/экономики/прав из архива. Исходник/лицензии сохранить, reuse одинаковые Nunito/старые approved sawmill/bakery. Живые данные только через прежние PlayerView/обработчики и валидаторы.

1. Общий светлый каркас и производства: общая тема/иконки/адаптивность, milk/cheese, beet/sugar, multi-input pastry; безопасная общая привязка к HouseholdObjectCard actions, сохранение всех подробных действий/routes/recipes.
2. Биржа/планшет игрока/справка: крупные группы экранов с единым стилем; существующие фильтры/цены/аукционы/выдача/приватность/переговоры/город. Справка11тем, краткие шаги+полный текст, прежние pause/restore semantics. Developer/HUD не перекрашивать глобальной темой игры.
3. Реальные Godot probes: состояния stock/reservations/WIP/pending wage/no truck/independent directions, market/negotiations/access, RU/EN1440/1280/800 и узкие окна; normal+Developer gates/audit/hash/runner по риску. Обновить docs, commits/push/один PR без merge.

Scratch /workspace/work/delsk-tablets-v1 (env.sh готов). Godot4.7.2/.NET10.0.401, только bounded scripts/run_check.py. Не считать static DTO screenshot живой экономической приёмкой; новые вмешательства/тестовые классы только isolated stage и вне ordinary audit/PCK. По завершении каждого этапа дополнять этот файл результатом/командами/оставшимся. Старые Hard/model/device/history gates остаются открытыми.

## Этап1 — общий каркас и производства

Подключены scoped TabletDesign/TabletColumns, SVG+theme архива, reuse3идентичных Nunito/OFL без дублей. ProductionTabletPresenter расширен на dairy/sugar-factory/confectionery; исходные3сцены адаптированы/узлы именованы; demo.gd/данные не подключены. Все реальные actions по-прежнему HouseholdObjectCard/AccessFor/Send. Multi-input stock показывает каждый компонент, free/reserved отдельно и общий input volume; recipe L2, pending wage/no-truck/current crew/production independent отлогистики. При нестандартном recipe или ширине<980 — native подробная карточка, без потери команд.

Общий TabletPanel светлый, родной clock/feedback footer, не меняет TabletState. В production режиме внутренние Help/Close маршрутизируются в прежний общий host; общий заголовок скрывается, чтобы убрать дубль. В компактном режиме возвращается. HUD и Developer theme глобально не менялись.

Client Debug restore/build exit0. scripts/check_tablet_design.py: новый TabletDesignProbe только isolated stage. Реальный command-driven FoodChain построил/произвёл/продал товары;3производства×RU/EN×1440/800 проверены нативными контролами/скриншотами. На паузе click не меняет tick/enabled до Step; pending buttons disabled; фактическое переключение и pending wage подтверждены. Все invariants/log checks exit0 (production-layout-final). Первоначально исправлено имя свойства в test probe и выявленные при просмотре clipping/NoTransport layout/двойной заголовок. Старые failed scratch сохранены.

Этап2 уже начат в рабочем дереве: HelpOverlay краткие шаги/full text и PlayerTablet overview/cards/foldouts пока не проверены. Не считать их готовыми; далее объединить5страниц биржи поверх прежних Capital/ParticipantMarket и провести расширенный живой probe. Не включать в этап1 commit незаконченные экранные файлы.

## Этап2 — текущая реализация/проверка

Рабочие изменения после3ce8f94: Playfield.Tablets соединяет embedded Help/Close/header с общим host (обязательно сохранить это изменение, оно не было в первом commit). ExchangeTablet объединяет5страниц поверх CapitalTablet/ParticipantMarketTablet; прежние handlers/amount/price/auction/pickup/source продолжают работать. Все публичные лоты из PlayerView доступны на своей странице; MyOffers использует реальную собственную лавку, новые remote кнопки дополнительно проверяют прежний AccessFor. Page nav не меняет TabletState/позицию/паузу. UI row/card tokens scoped, не переписывают HUD/Developer.

PlayerTablet: собственные stats/cards/Open/Walk/Back, equipment foldout;4старые вкладки/публичный рейтинг/город/труд/own-bilateral talks/contracts/support/pickup сохранены. Детальные действия старой HouseholdObjectCard доступны. HelpOverlay:11RU/EN тем с3краткими шагами+полным прежним текстом; исправлена устаревшая фраза «аренда не реализована». Help продолжает безусловно приостанавливать мир и возвращать прежний режим при закрытии — демо-кнопка продолжения времени сюда не переносится. VotingPanel тоже получил scoped kit colors, без правок выборов.

У3производств Recipes открывает native live comparison levels; mirror-кнопки делегируют существующим recipe:ID actions, locked/current/WIP/pending guard не обходится. Native compact columns/flow/input sizes вместо уменьшения текста. Небольшие подписи/NoTransport/duplicateHeader уже просмотрены на реальных screenshots.

Первый pages-build поймал nullable Equipment, исправлено `??[]`; тестовый Market method исправлен на MarketWindow. Первый pages-probe прошёл RU/EN4вкладки+11help на1440/800/640, затем остановился на invalid replay horizon — в **probe**, потому что подавалась вся будущая command tape. Исправлено: для cut33/38/51 используются только SubmittedAtTick<=cut. Сейчас pages-probe-live запускается (session82720), stdout/checks в /workspace/work/delsk-tablets-v1. Не считать все secondary screens проверенными до его завершения; currenttablets-check.json может быть старым, всегда смотреть process exit/log metadata. После проверки добавить rendered active auction и реальную интеграцию host/pause/HUD/Developer, bounded final gates.

ProbePages лежит отдельным partial tests/Client.Probes/TabletDesignProbe.Pages.cs; packager-copy скрипт обновлён на TabletDesignProbe*.cs. Только isolated stage, AssemblyAudit исключает TabletDesignProbe. Сцены/скрипты demo/archive не подключены к приложению; исходный zip только docs/design. Native controls test использует реальные command-driven FoodChain и ParticipantTrade/replay fixtures, не рисует выдуманную экономику.

Этап2 реализован: screens-behavior exit0,21 broad native check groups (реальные GameSessions/обычные команды; точное число читать tablets-check.json). Подтверждены3factories×RU/EN+compact, WIP/output capacity/direct save-load/resume equality,4player pages/11help topics на1440/800/640,5exchange pages нареальных cut33/35/38/51,partial paid pickup/deadline,rendered active auction,capital sale queued→reserved→settled в RU/EN. При Capital test сначала fixture стояла не у терминала — исправлен настоящий Move, а не обход фильтра. При WIP test исправлены typed BuildingId/EnterpriseId сравнения в test helper. Все release-root game changes остаются presentation-only.

PR45 дополнительно fetched read-only как origin/pr45-review:4квартала только geometry/catalog/resource+targeted tests/docs, ни одной части F04 editor нет. Ветка45 не merge/cherry-pick. PR46наbase77d включает bb3f288 UTF8 staging fix, правила/HUD/godmode сохраняем.

Перед этапом3: связывание header events теперь bound к конкретному TabletPanel, а не mutable playfield field (избегает позднего resize старой карточки). RecipeCards добавлены в дерево даже при generic view — нет orphan Node у warehouses/equipment. Эти узкие lifetime fixes требуют общей host/regression проверки. Следующий этап: current repo client build/import +старые sawmill/bakery real Playfield probes, HUD/God17checks, ordinary/Developer tests/audit/platform hashes/PCK, затем reports/PR. Не считать user device/real-model/Hard/history gates закрытыми.

## Этап3 — итог проверок и передача

Все gates зелёные: engine-gate491s (headless345.7s,880ordinary/886Developer,102exports/14replay/69pairs), client-gate128.5s (Debug/ExportRelease/audit/17HUDchecks/ordinary+Developer1132hashes×2/35restores×2), screens-final51.6s21groups с live recipe locks, host5groups+sawmill4+bakery6.12Python/syntax. PCK повторён после исправления include_filter новой лицензии, pack inspection exit0, no Developer/probes/demo. Исходная спецификация SHA прежняя.

Host probe сначала ошибочно искал текст × у иконочного Close, требовал видимость пустого HUD и отмечал expected absence как failure — исправлен тестовый поиск, приложение не обходится. Фактические Help callbacks/Close/paused→running restore/Menu pause/RUEN800 работают. Старые probes обновлены для иконочного Close, добавлен проверяющий Godot error logs шаг. Таблица gates/маленькие отчёты/5actualscreenshots сохранены docs/verification/tablets-v1; большой scratch в /workspace/work/delsk-tablets-v1 не коммитить.

Далее только review diff, commit, bounded push и один PR без merge. Не начинать F04/баланс/другой пакет. До публикации PR URL ещё нет. Отчёт IMPLEMENTATION_REPORT_TABLETS_V1.md описывает literal3scene import/native adaptation, реальные проверки, команды и открытые hardware/realmodel/Hard/history gates. На commit включить generated .uid/.svg.import как принято в репозитории, не .godot/obj/bin. После PR записать URL/HEAD и пушнуть короткую передачу.

## Этап4 — продолжение21.10.2026 (Europe/Samara): Alt+Enter

Предыдущая публикация фактически успешна: PR#47 открыт, mergeable, не merged, head0a1ea34, base77d40e9. Последняя попытка дописать publication notes не исполнилась из-за лимита approval reviewer; исходники/проверки/evidence и push сохранены, ничего восстанавливать заново не требуется. Повторный fetch подтвердил обе refs без новых коммитов.

Новый запрос владельца: Alt+Enter для fullscreen с заполнением экрана без полей. Используем прежний ToggleFullscreen/настройку display.cfg. Обработчик в _Input до GUI/контекстного Enter, не в _UnhandledInput (иначе поля/справка поглощают сочетание). Echo/release не переключают повторно. Учитываем ExclusiveFullscreen при выходе, headless/embedded guard. Canvas_items aspect expand вместо default keep: адаптивный viewport заполняет любую пропорцию без letterbox и без неравномерного растяжения шрифтов. Экономика/пауза/планшеты прежние. Далее actual keyboard+native window tests с разными пропорциями, focused field, Maps/Play/Menu/Help, preference restore, builds/smoke; добавить в тот же PR47 без merge.

Этап4 завершён21.10.2026: fullscreen-window7groups exit0, настоящий xfwm4 через dbus-run-session/EWMH под Xvfb; первоначальные no-WM/hidden-form fixture failures не засчитывать. Actual1440×1024 fullscreen/window sizes1280×720/1000×800/800×600, focused input/plainEnter/echo/release/keypad, persistence host recreation, paused world, Help/Menu/ENPlayer подтверждены. Скриншот full window просмотрен, без полей. Повторён client-gate Debug/ExportRelease/audit/import,17HUDchecks,ordinary+Developer1132hashes/35restores каждый,PCK export+inspection/logverify.21screen groups и5host+4sawmill+6bakery повторно зелёные после синхронизации актуального project.godot в reuse-stage. Метаданные fullscreen-summary.json отделены от прежнего engine gate20.10 (880/886/102/14/69 не выдавать за новый запуск).

PR47 остаётся единственным PR задачи, base77d40e9/headдоэтогопродолжения0a1ea34; не merge. Реализация/тесты готовы, далее commit/push follow-up и обновление PR description. Открытые Windows/Android/realmodel/Hard/history gates прежние.

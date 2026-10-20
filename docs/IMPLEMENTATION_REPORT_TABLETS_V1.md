# Переработка игровых планшетов · 20.10.2026

Автор: Шелестов Максим Евгеньевич. Дата Europe/Samara. Ветка `feat/tablets-design-v1`, база `77d40e9`.

## Исходное состояние и границы

Проверены AGENTS, спецификация, архитектура, решения, планы, история и фактические presenters/tests. Дерево перед началом чистое. После прошлой итерации PR#44 слит; PR#46 слит в `77d40e9`, его ResourceInventoryHud и изолированный Developer god-mode сохранены. PR#45 повторно проверен: **open/draft, не слит**; его head `e195be1` fetched только для чтения. Отдельная четырёхквартальная карта и незаконченный F04 editor сюда не переносятся.

World18 / production-recipes-1 / Session2 / Envelope2, правила экономики, контроллеры, публичные/двусторонние проекции и TabletState прежние. Спецификация не менялась: SHA-256 `e416b331a8353d5f39d8ff042288ad2493d39aae2c506a40ecc3f30de56fd56c`.

Архив пользователя [сохранён целиком](design/tablets-v1/source.zip) вместе с исходным README. Это визуальный референс (48 сцен), а не новые игровые требования. Demo.gd, demo project и примерные данные не подключаются. Идентичные Nunito переиспользованы из лесопилки с OFL. Происхождение SVG и адаптации указаны в `tablets_v1/LICENSE.txt`; дополнительная лицензия не выдумана.

## Реализовано

| Группа | Адаптация к игре |
|---|---|
| Общая рамка | Светлая кремовая тема, зелёные акценты, округлые карточки, Nunito, настоящие Help/Close/время/feedback. Тема ограничена планшетами, HUD и Developer не перекрашены. |
| Производства | Три архивные сцены молочного/сахарного/кондитерского производств подключены к общему ProductionTabletPresenter; лесопилка и пекарня переиспользуют прежние оформленные сцены и обработчики. |
| Состояния производства | Фактический рецепт/уровень, все компоненты выпечки по ID, WIP, реальный штат, текущая/отложенная зарплата, свободный/зарезервированный физический товар, вместимость, улучшение, отсутствие транспорта, независимые поставки/отправка/производство. Сравнение рецептов/уровней использует настоящий каталог и прежние guarded actions. |
| Биржа | Пять страниц: столица, лоты участников, покупки, собственные предложения, общая история. Те же Capital/ParticipantMarket presenters, суммы/источники/заявки/аукцион/выдача. Собственная лавка отделена от места открытия; remote actions проверяют прежний AccessFor. |
| Игрок | Четыре раздела, собственные показатели и карточки хозяйства, Open/Walk/Back, публичные участники/город/занятость, собственные и двусторонние переговоры/контракты/поддержка/получение. Сохранены техника, переносы и маршруты. |
| Справка | 11 тем на RU/EN, три кратких шага, полный текст правил, широкая навигация или компактный выбор. Справка по-прежнему останавливает мир и возвращает прежний режим времени. |
| Вторичные формы | Существующие native controls переноса, маршрутов, переговоров, контрактов, поддержки, техники и голосования используют общий стиль/адаптивные колонки и раскрываемые секции; экономические команды не дублируются. |

На узком окне производственная иллюстрация заменяется полноценными компактными controls; текст не уменьшается до нечитаемого размера. Рецепты/подробности и длинные списки доступны прокруткой. Часть вторичных архивных макетов адаптирована как native формы, **не все 48 демонстрационных сцен импортированы буквально**. Неподдерживаемые Core действия (например, отмена опубликованного лота) не добавлены ради макета.

## Реально выполненные проверки

Все длительные процессы ограничены `run_check.py`, exit/logs проверены; краткие метаданные и отчёты — [verification/tablets-v1](verification/tablets-v1/gate-summary.json).

- Bounded locked restore/build основной solution; **880 passed**, 0 failed/skipped. Developer restore/build: **886 passed**, 0 failed/skipped. Developer+Release отрицательный build ожидаемо отклонён.
- Полный `check_headless.py`: 24 scripted и 5 baseline scenarios, **102 exports, 14 replay, 69 comparison pairs**; save/load, полные строки ledger/events/commands/labor, balance/invariant/privacy и model-safety/off проверки.
- Client Debug/ExportRelease restore/build, аудит пяти ordinary assemblies: Developer и TabletDesignProbe отсутствуют. Godot import и обычный/изолированный Developer Linux smoke: в каждом **1132 tick hashes**, остановленные выборы с final tick10 и **35 восстановлений** совпали с runner.
- Новый isolated native `TabletDesignProbe`: **21 группа**, включающая три производства RU/EN, реальные recipe locks/current selection, queued/pending actions, зарплату, WIP/output hold и direct save/load/resume; четыре раздела игрока и все 11 тем справки при **1440×1024, 800×600, 640×800**; пять страниц биржи, активный аукцион, частичное получение/срок, столичная продажа queued→reserved→settled. Это подготовленные command-driven игровые сессии, не фиктивные экономические DTO.
- Реальный Playfield host: **5 групп** Help/Close, paused/running restore, помощь из четырёх вкладок, Menu pause, RU→EN/800×600/HUD. Прежние sawmill/bakery probes: ещё **10 групп**, включая приобретение транспорта, независимость операций, pending wage и восстановление звука/речи после перезапуска сцены.
- PR46 HUD/Developer: **17 native checks**, реальные транзакции, capacity rejection, reserved physical goods, actor switch, Escape/Close и Debug pause.
- **12 Python tests**, syntax новых/изменённых проверочных скриптов. Экспорт ordinary PCK и фактическая инспекция: новые три сцены/тема/иконки/лицензия, прежние производства/шрифты/звук есть; Developer/пробы/demo/source.zip отсутствуют. Исправлен include_filter лицензии для обоих presets. Проверены журналы Godot, а не только exit0.

В ходе проверки исправлены clipping, повтор заголовка, отображение NoTransport, contrast input controls, привязка header к конкретной рамке и lifetime recipe nodes. Первоначальные ошибки тестовых fixtures (будущая tape за replay horizon, позиция вне терминала, поиск иконочного Close по тексту, намеренно скрытый пустой HUD) устранены в проверках без обхода игровых валидаторов. Неуспешные промежуточные запуски не считаются pass.

## Воспроизведение

После установки SDK10.0.401 и Godot4.7.2 Mono, из корня репозитория:

```sh
python3 scripts/run_check.py --name headless --timeout 900 --log-directory artifacts/tablets/checks -- python3 scripts/check_headless.py --output artifacts/tablets/headless
python3 scripts/run_check.py --name developer --timeout 420 --log-directory artifacts/tablets/checks -- python3 scripts/check_developer.py --output artifacts/tablets/developer
# Client solution отдельно: locked restore, Debug/ExportRelease build, Godot --editor --import.
python3 scripts/run_check.py --name tablets --timeout 600 --log-directory artifacts/tablets/checks -- python3 scripts/check_tablet_design.py --godot /path/to/godot-mono --xvfb /path/to/Xvfb --output artifacts/tablets/native
python3 scripts/run_check.py --name host --timeout 110 --log-directory artifacts/tablets/checks -- python3 scripts/check_sawmill_ui.py --mode tablets-host --godot /path/to/godot-mono --xvfb /path/to/Xvfb --output artifacts/tablets/host
# Аналогично --mode sawmill и --mode bakery; HUD/Developer: scripts/check_hud_god_ui.py.
```

Native probe компилируется только в isolated stage; `--reuse-stage` допустим после первого stage, обычный клиент его не содержит. Инспекция PCK воспроизводится `tests/Client.Probes/tablet-pack.gd`: запускать Godot `--headless --path` с отдельным пустым project.godot, `--script /absolute/path/tablet-pack.gd -- --pack /absolute/path/release.pck --report /absolute/path/pack.json`. Для Mono/Xvfb нужен loopback. Логи проверять `verify_platform_probe.py`; экспорт отдельно инспектировать. Godot/Xvfb — software-rendered Linux, Dummy audio.

## Ограничения и следующий шаг

Не проверены Windows EXE на устройстве, Android/touch/lifecycle, аппаратные FPS/динамики и пользовательская визуальная приёмка. PCK с preset Windows Probe не является запуском Windows EXE. Существующие пробелы real-model/Hard efficacy/длинной истории из пакета8 не закрываются оформлением планшетов. PR45 остаётся отдельным draft.

Рекомендуется ограниченная визуальная приёмка этих экранов на машине разработчика и целевом размере окна; затем отдельное решение по PR45/F04 и прежним открытым аппаратным gates. Этапы и контекст продолжения — [WORKLOG_TABLETS_V1.md](WORKLOG_TABLETS_V1.md).

# Debug/Developer/Release ·21.10.2026

Автор: Шелестов Максим Евгеньевич. Europe/Samara. Базаa621431 (слитый PR47), веткаfix/debug-developer-builds. Перед началом дерево чистое, история/AGENTS/Developer-код и документация проверены.

## Причина и результат

Режим бога уже был реализован в PR46: Меню→Debug→«Режим бога», деньги/товары через существующие Developer-транзакции, реальные резервы/вместимость и пауза. Обычный Debug/export-debug не включал DelskDeveloper. Кроме того, packager передавал флаги только первому MSBuild; последующий Godot ExportDebug без них мог собрать ordinary DLL и потерять кнопку.

Теперь staged Directory.Build.props сохраняет оба Developer-флага. Добавлен --export-windows/--godot: изолированный import и debug export с timeout/log validation, результат windows/DelskDeveloper.exe и сопутствующие файлы. Developer Release по-прежнему запрещён. Отдельный export lock учитывает отсутствие Editor references; ordinary ExportDebug использует прежний release lock. Root Developer-флаг не включён.

В ordinary Debug есть пояснение, как получить режим бога. Menu Debug и OpenDebug ограничены DEBUG, в Release/ExportRelease нет кнопки/точки входа. Экономика, Core/Hosting API, save/PRNG и игровые правила не изменены.

## Фактические проверки

- Locked restore/build ordinary Debug, ExportDebug и ExportRelease, без warnings/errors. Ordinary metadata audit во всех вариантах: no Developer assembly/API. Release --no-debug-ui audit подтверждает отсутствие OpenDebug; отрицательная проверка того же audit на Debug ожидаемо отвергается (exit134, без timeout).
- Реальные RU/EN меню ordinary Debug и DLL ExportRelease: Debug entry/hint есть только в Debug; в Release нет Debug/God-mode entry. Release DLL загружена в отдельном Godot stage, ordinary cache не перезаписан. Скриншоты просмотрены.
- check_hud_god_ui.py:17native checks Debug и17повторно из **ExportDebug assemblies**, собранных без CLI DelskDeveloper/DelskDeveloperPackage. Подтверждены кнопка из Debug,123.45coins→12345minor, настоящая древесина в рюкзаке, capacity rejection atomic, выбор участника, закрытие/пауза и прочие прежние HUD инварианты. Подмена cache на ExportDebug только внутри isolated test stage.
- Staged Developer ExportRelease без ручных флагов ожидаемо отвергнут (exit1, текст forbidden in Release/ExportRelease). Python syntax, Godot error logs проверены.
- Ordinary Linux smoke/hash:1132tick hashes, stopped elections final10,35file restores совпали с runner. Полный Core unit/runner suite здесь повторно не запускался: изменение сборки/UI, логика симуляции не менялась.
- Windows CI дополнен Release build/audit и выполняет расширенный Debug/ExportDebug native probe. Его результат не заявляется заранее.

Команды/exit/timing и компактные отчёты — [verification/debug-builds](verification/debug-builds/gate-summary.json). Восстановление этапов — [WORKLOG_DEBUG_BUILDS.md](WORKLOG_DEBUG_BUILDS.md). Пошаговая команда Windows/маршрут — [DEVELOPER_TOOLS.md](DEVELOPER_TOOLS.md).

## Ограничения

Matching Windows .NET export templates в текущем окружении не установлены; полный --export-windows и запуск .exe на Windows не проверялись. Managed ExportDebug и настоящий UI из этих assemblies проверены в Linux Godot. Не выдавать это за Windows hardware acceptance. Вместимость не увеличивается: для большого запаса выбрать подходящий склад; ввод задаёт итоговое общее количество, не добавку. Деньги вводятся в монетах.

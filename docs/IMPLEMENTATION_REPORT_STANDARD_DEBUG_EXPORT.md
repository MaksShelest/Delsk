# Стандартный debug-экспорт Godot ·21.10.2026

Автор: Шелестов Максим Евгеньевич. Europe/Samara. База9b825ea (PR48 merged), чистое дерево. Веткаfeat/standard-godot-debug-export. Новый запрос владельца: никакой обязательной Python-команды, режим бога включается стандартной галочкой Godot.

Root Directory.Build.props выбирает DelskDeveloper=true по умолчанию при ExportDebug, одинаково во всех references; client допускает этот экспорт из исходного проекта без Package flag. Остальные конфигурации по умолчанию ordinary. Явный Developer Release по-прежнему запрещён. Editor Debug hint указывает на галочку. Optional CLI сохранён, экономические правила/Core source/save/hash не изменены.

## Готовый workflow

Основной `src/Client.Godot/project.godot` → Проект → Экспорт → Windows Probe → Экспортировать проект. **Export With Debug включена:** в игре Menu→Debug→God mode. **Выключена:** Release без Debug/God/API. Python и отдельный source stage не нужны. Matching Godot.NET templates и SDK — обычные prerequisites .NET-проекта. Подробности [DEVELOPER_TOOLS.md](DEVELOPER_TOOLS.md).

## Проверки

- Locked restore/build исходного ExportDebug без DelskDeveloper/DelskDeveloperPackage flags и без isolated-package bypass. Затем ExportRelease без clean, metadata audit пяти ordinary DLL с --no-debug-ui, editor Debug build/audit.
- Native17checks Developer Debug и ещё17из **ExportDebug с исходным root props** (forced packager props заменены исходными перед build). Подтверждены реальная кнопка из Debug, деньги/древесина, вместимость/atomic rejection, actor switch, pause/close. DLL ExportDebug загружены в отдельном test cache, обычный кеш не менялся.
- Реальные self-contained Windows managed publish ExportDebug/ExportRelease, стандартные флаги Config/RID/Platform, без Developer flags.
- Реальный Godot --export-debug и --export-release Windows Probe из оригинального проекта: созданы Windows PE .exe, PCK и managed runtime bundle. Release повторно экспортирован **в ту же папку**, аудит результата не содержит Developer DLL/API/OpenDebug.
- Этот сценарий первоначально выявил остаточные DLL/PDB stock exporter. Добавлен включённый project.godot GDScript editor-addon: только own Windows managed directory, guard по Client.dll/deps/libraries, удаление двух известных Developer файлов после release. Debug, неудавшийся release с прежним debug manifest и соседние файлы не затрагиваются. Отдельный native filesystem probe проверяет четыре такие группы на настоящих publish manifests. Addon исключён из экспортируемых ресурсов.
- Ordinary Linux smoke:1132tick hashes/35restores и stopped elections final10 соответствуют runner. Полный Core suite не повторялся: Core logic не менялась. Logs проверены, а не только exit0. Windows CI native probe теперь также проверяет исходный props, без forced flags.

Точные команды, SHA/размеры EXE и отчёты — [verification/standard-debug-export](verification/standard-debug-export/gate-summary.json). Восстановление — [WORKLOG_STANDARD_DEBUG_EXPORT.md](WORKLOG_STANDARD_DEBUG_EXPORT.md).

Windows Mono templates4.7.2 получены из official pinned release: Range-загрузка только нужных четырёх x64 EXE/console entries (~72MB), не всего1.2GB архива. Шаблоны/выходные EXE/кеши не коммитятся. Это позволило проверить настоящий экспорт, но **запуск .exe на Windows и аппаратная приёмка здесь не выполнены**. Native controls проверены Linux Godot.

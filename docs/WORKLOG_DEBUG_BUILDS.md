# Debug/Developer/Release —21.10.2026, Europe/Samara

Автор: Шелестов Максим Евгеньевич. Базаa621431 (PR47 merged), чистое дерево; веткаfix/debug-developer-builds. Запрос: в Debug .exe не видны деньги/ресурсы, Debug не должен появляться в Release.

Этап1: фактически God-mode есть в Developer/Playfield.Developer.cs и DeveloperPanel.cs, вход Menu→Debug→God mode,17native checks прежние. Обычный Debug не содержит intervention API, что соответствует прежней архитектуре; просто export-debug исходного проекта их не добавляет. Обнаружены два пробела: menu.debug/OpenDebug без compile guard; stage packager передавал флаги только CLI, поэтому следующий Godot ExportDebug без них терял Developer. Не переписывать Core-транзакции/не делать cheats обычному клиенту.

Этап2: DEBUG guard скрывает кнопку/entrypoint в Release/ExportRelease; обычный Debug показывает понятную подсказку про Developer. Изолированный stage закрепляет DelskDeveloper/DelskDeveloperPackage в собственном Directory.Build.props. Backend обычного репозитория неизменён, Release по-прежнему отвергает Developer. Добавлена --export-windows/--godot команда с --export-debug, bounded import/export/log checks. Требуются matching Mono Windows export templates; в этой среде их наличие ещё не подтверждено, не обещать реальный Windows запуск.

Далее gate: обычные Debug/ExportRelease build+menu/metadata, Developer native17checks, ExportDebug без CLI flags и реальная кнопка/транзакции, negative Release, smoke/hash и документация с Windows командой. После зелёного gate commit/push/отдельный PR без merge. Следующие экономические этапы не начинать.

Этап3: gate зелёный.17nativechecks Debug +17actualExportDebug без CLI flags, root ordinaryDebug/ExportDebug/ExportRelease builds, release entrypoint auditpositive/debugauditnegative, RUENactualmenus/hint,1132hashes/35restores, DeveloperRelease explicitflagsнепередавались: expected forbidden exit1. В первом ordinaryDebug menu probe тест искал отсутствующую в2D Debug кнопку «Меню» — заменён реальным Escape; повторный menu-debug-final exit0. Не менять приложение ради test fixture.

Новый lock сгенерирован реальным NuGet ExportDebug restore, не вручную. Smoke и проверки logs прошли. Полный WinEXE export/run не запускать без matching templates, не считать пройденным. Проверен вывод DLL ExportDebug. CI расширен release metadata step и повторным probe этих DLL.

Далее краткий evidence, diff review, commit/push и отдельный PR без merge. Пользователю дать точную команду --export-windows с путём GodotMono_console.exe, путь windows/DelskDeveloper.exe и Menu→Debug→God mode, итоговые значения/выбор хранилища/capacity. Не запускать следующие пакеты экономики.

Опубликовано: PR https://github.com/MaksShelest/Delsk/pull/48 открыт без merge. Веткаfix/debug-developer-builds, implementation4d5f7ab. Локальные gates зелёные; user Windows EXE/новый GitHub CI отдельно. Продолжение этой задачи — только приёмка Developer EXE на машине пользователя по DEVELOPER_TOOLS.md, не новые пакеты.

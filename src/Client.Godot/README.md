# Платформенный стенд P06

Автор: Шелестов Максим Евгеньевич.

Граница версий на 17.10.2026: обычный запуск открывает `Playfield`. Escape ведёт в меню, Debug показывает ту же партию плоской картой. `--smoke` сохраняет технический `PlatformProbe`. P26.6 ещё не завершён. [Актуальный статус](../../docs/PROTOTYPE_STATUS.md).

Нужны Godot **4.7.2 .NET** и SDK **10.0.401**. `godot` ниже обозначает путь к исполняемому файлу .NET редактора. Команды из корня репозитория:

```bash
dotnet restore src/Client.Godot/Delsk.Client.csproj --locked-mode --disable-parallel '-m:1'
dotnet build src/Client.Godot/Delsk.Client.csproj --no-restore --disable-build-servers '-m:1'
godot --headless --path src/Client.Godot --import
godot --headless --path src/Client.Godot -- --smoke --report /absolute/path/platform-probe.json
python scripts/verify_platform_probe.py --report /absolute/path/platform-probe.json
```

При запуске `PlatformProbe` стенд открыт на паузе. Кнопки воспроизводят записанный сценарий; эта сцена не добавляет человеческий ввод произвольных игровых команд или интеллект NPC. `--smoke` проверяет адаптер жизненного цикла, выполняет те же 62 игровых тика без wall-clock ожидания и выходит с кодом 0/1. Отчёт по умолчанию — `user://platform-probe.json`. После полного обычного повторения отчёт также создаётся; искусственный lifecycle smoke в этом случае не выполняется.

Используются встроенные ресурсы исходного scenario, command tape и 62 хешей; доступ к внешнему каталогу scenarios на телефоне не нужен. Обновлять ожидания только после проверки изменённых правил: `python scripts/update_platform_fixture.py artifacts/cycle`. Не обновлять фикстуру, чтобы скрыть неожиданное расхождение.

Экспорты требуют templates **4.7.2 .NET**, настроенных путей JDK/Android SDK и существующих выходных каталогов:

```bash
godot --headless --path src/Client.Godot --export-debug 'Windows Probe' /absolute/path/windows/DelskProbe.exe
godot --headless --path src/Client.Godot --install-android-build-template --export-debug 'Android Probe' /absolute/path/android/DelskProbe.apk
```

В ограниченном облачном окружении экспорт выполнялся с `DOTNET_PROCESSOR_COUNT=1`. На Windows эта переменная задаётся через `$env:DOTNET_PROCESSOR_COUNT = '1'`; в PowerShell сохранять кавычки вокруг `'-m:1'`. Папка android создаётся из официального шаблона и не коммитится. Ключи остаются вне git. Debug-экспорт не является публикацией в магазине.

Всегда запускать через `scripts/run_check.py`, сохраняя timeout и журналы, затем проверять `scripts/verify_platform_probe.py --log-prefix artifacts/checks/<имя>`. Наличие EXE/APK и exit 0 сами по себе не подтверждают успешной сборки managed-компонентов.

После установки APK можно получить внутренний отчёт через adb:

```bash
adb exec-out run-as com.maksshelest.delsk.probe cat 'files/platform-probe.json' > platform-probe-android.json
```

Путь `user://` зависит от платформы; если указанный относительный Android-путь не найден, сначала определить его внутри `run-as`, не объявлять проверку пройденной. Для автоматического smoke на Android запускать activity с аргументами `--smoke`, только после подтверждения корректного механизма передачи аргументов этой версии Godot; такой запуск здесь не проверялся. При ручном повторении сравнить Tick/StateHash и отдельно выполнить сворачивание. [Фактические результаты и незакрытые проверки](../../docs/IMPLEMENTATION_REPORT_P06.md).

## Checkpoint P12

Smoke проверяет actual save/load на12/34/55/60, сохраняя все62 hashes. Обычный стенд имеет manual Сохранить/Загрузить/явный backup; путь user://platform-checkpoint.json. Autosave30, focus/pause и корректный выход используют общий CheckpointStore; elapsed после load/resume сбрасывается. Не путать с экспортом final-state и не заявлять device pass по desktop smoke. [Детали P12](../../docs/IMPLEMENTATION_REPORT_P12.md).

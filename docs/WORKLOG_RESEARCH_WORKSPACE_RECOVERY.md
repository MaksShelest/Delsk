# Восстановление работы: исследовательская лаборатория и карты
Автор проекта: Шелестов Максим Евгеньевич. Дата: 23.10.2026.

**Это журнал восстановления, а не завершённая реализация. Код остался в отключившейся рабочей среде и не отправлен в GitHub. Эта ветка содержит только данный документ. Не считать её готовой feature-веткой и не сливать как реализацию.**

## Причина остановки и исходная точка

Среда выполнения стала unavailable во время полной регрессии. Повторный доступ к консоли отклонён: No usable execution environment is available.
GitHub-коннектор остаётся доступен, поэтому этот журнал сохранён отдельно, чтобы не потерять состояние работы.

Рабочая копия: /workspace/scratch/80193353a16b/Delsk.
Локальная ветка: feat/research-workspace-maps.
База: 96e19671766cd882dbd825df902ee6c872c8e15e, origin/feat/economic-cycle-p00-p05.
Изменения не были закоммичены или отправлены. Сначала проверить, сохранилась ли эта рабочая копия; не перезаписывать её свежим checkout.

## Принятое задание

Переработать локальную исследовательскую лабораторию: нейтральный тёмный интерфейс в духе OpenAI/Cursor, удобная навигация вместо длинного вертикального экрана, прогресс и диагностика ожиданий, управление опытами и местом на диске, просмотр мира/решений/модельных запросов по шагам и просмотр файлов без скачивания, отдельные копии экономических и стартовых параметров, локальные модели и точка расширения под будущий API.

Сохранить «Четыре квартала» как доступную карту игры и добавить новую: ресурсы с одной стороны, промышленность посередине, город/терминал с противоположного края, торговля рядом с городом. Четыре участника: человек и три NPC. Старые карты сохранить.
Core, экономика и правила игры не меняются. Визуальный редактор карт и облачный API этой реализацией не закрыты.

## Подготовленная реализация в локальном дереве

### Лаборатория и запись
- scripts/research_lab.html заменён; новые research_lab.css и research_lab.js.
- Навигация: каталог, новый опыт (основное/участники/экономика/JSON), профили, модели.
- В опыте: прогоны, мир по шагам, события/решения, анализ, файлы.
- Каталог: поиск, размер, переименование, дублирование, архив, удаление/групповое удаление.
- Живой статус: шаг/цель/процент, elapsed, скорость, lastAdvancedAt, heartbeatAt, waiting-model/paused/exporting, число запросов.
- Пауза очереди между опытами; отдельные pause/resume/cancel текущей попытки между атомарными шагами. Отмена сохраняет частичные файлы и не считается успехом.
- Текущий мир записывается на диск, UTF-8 byte-offset индекс позволяет открыть нужный шаг. Последняя запись шага включает завершённое замороженное голосование.
- Журналы разбиты по категории, участнику и важности; исходная запись раскрывается.
- Inline JSON/CSV/JSONL/log; большие JSON текстовыми блоками, записи страницами.
- Графики из реального agents.csv: деньги, стоимость запасов, занятость. Прореживание примерно до 500 точек, исходники сохранены.
- Формы экономики по разделам + поиск; массивы рецептов/каталогов остаются в полном JSON.
- Состав 1–15 NPC, деньги/рюкзак/спавн/предпочтение/сложность/модель по участнику. В headless слот человека пассивен в экономике и голосует за себя.
- Профили Ollama, каталог установленного через /api/tags, per-NPC маршрутизация и отключённый actor. Общая очередь ModelRuntime сохраняется; параллельность скрыто не добавлялась.
- Новые гибридные опыты используют ResearchWaitAtDecision-v1: ожидание ответа не продвигает мир. Старый CLI без флага сохраняет старый тайминг.
- Авторитетная симуляция остаётся в C#, JS/Python не моделируют экономику.

Затронуты:
scripts/research_lab.py, research_lab_server.py, research_lab_presets.py;
новый scripts/research_workspace.py;
новый src/Research.Runner/ResearchObservation.cs;
src/Research.Runner/Program.cs, ModelServices.cs.

Новые CLI: --research true, --research-setup PATH, --model-wait true.
Новые файлы попытки: progress.json, world-scenario.json, world-frames.jsonl,
world-index.jsonl, live-events.jsonl, live-decisions.jsonl, model-transcripts.jsonl.
Последний содержит фактический prompt/ответ, только для исследователя.
Никакого доступа к исследовательским секретам для NPC/public.

### Карты
scenarios/four-corners-v1.json скопирован только как карта из PR45 / e195be132aa25f306c52319548088a0910f7bb81.
Не сливать старый PR45 целиком: его остальной код/документы устарели.

scenarios/city-corridor-v1.json: «Городской тракт», 27×25.
Город/столица/поддержка (24,12).
Старты (21,10), (20,11), (20,13), (21,14).
48 промышленных клеток: X 10,11,13,14,16,17 × Z 5,6,8,9,15,16,18,19.
8 торговых: X 19,20 × Z 8,9,15,16.
Частные ресурсы: лес X6 и зерно X5 по Z 7,8,9,15,16,17;
молоко X4 и свёкла X3 по Z 8,9,15,16.
Публичные ресурсы дальше слева, X1–2, по четыре каждого вида.
Дороги: горизонтально Z10,12,14 / X2..26; вертикально X8 / Z1..23,
X18 / Z3..21, X24 / Z9..15.
Равные ближайшие расстояния от всех четырёх стартов:
столица5, лавка2, промышленность5, частные лес16/зерно17/молоко18/свёкла19.
Это геометрическая проверка, не доказательство экономического баланса.

Изменены PlayMaps.cs (All/FourQuarters/CityCorridor/resource/name/restore mapping),
MapSelectionScreen.cs (каталог по PlayMaps.All), Delsk.Client.csproj (embedded resources),
Research.Lab presets (теперь пять карт), Simulation.Tests.csproj.
Новый ResearchMapTests.cs: Core validation, отсутствие наложений, равный доступ,
связность дорог, реальная добыча, продолжение checkpoint. Три теста.

### Документы
Подготовлены docs/RESEARCH_WORKSPACE.md, docs/MAPS_RESEARCH.md,
docs/maps/four-corners-v1.svg, docs/maps/city-corridor-v1.svg.
Обновлены AGENTS.md, README.md, docs/RESEARCH_LAB.md, ARCHITECTURE.md,
DECISIONS.md (решение54), IMPLEMENTATION_PLAN.md.
Исходная game_specification.md не переписана.
**IMPLEMENTATION_REPORT_RESEARCH_WORKSPACE.md ещё не создан**, хотя ссылки на него уже добавлены — закончить перед PR.

## Фактически завершённые проверки

Логи: artifacts/research-workspace/checks.
- workspace-tests-3: 894/894 ordinary xUnit.
- workspace-developer-tests-final: 900/900 Developer xUnit.
- workspace-client-build: Godot .NET клиент собран, 0 warnings/errors.
- workspace-godot-import: exit0; stderr содержит EditorSettings not instantiated ... shutdown_adb_on_exit.
  Не утверждать, что import был совсем без ошибок.
- workspace-godot-smoke: exit0, реальные DELSK_PLATFORM/ELECTION/SUPPORT/PHYSICAL/TRADE/FOOD_PASS.
- workspace-launcher-final: prepare/reopen/file lock/shutdown/Unicode paths прошли;
  20 комбинаций presets/maps, 35 arms через C# validators.
- workspace-python-acceptance-2: 31 Python-тест прошёл.
- workspace-ui-acceptance-2: настоящий браузер и runner; validation, прогресс,
  пауза/продолжение/отмена, удаление, мир на шаге30, стартовые деньги37.25,
  три участника, население230 из формы, дублирование настроек, модельные профили/bindings,
  inline files, журнал, график, ZIP, narrow640, token/Host/Origin/path guards,
  observer-on/off equality, checkpoint/load/replay. Console errors отсутствовали.

Скриншоты и gate.json лежат в artifacts/research-workspace/ui-acceptance-2.
Это промежуточная версия: последующие изменения ниже ещё требуют повторной проверки.

## Последние изменения, ещё не принятые финальными gates

После последнего успешного UI gate:
- общий выбор прогона/попытки вынесен над вкладками, а не только внутрь мира;
- таблица прогонов открывается по50 строк;
- список файлов ограничен выбранной попыткой/arm и общими файлами серии;
- validate_recording требует полную монотонную последовательность шагов и конечный кадр
  перед успешным включением в статистику;
- добавлены хеши DLL runner в metadata серии, проверка перед выполнением и сравнением;
- новый тест отказа при замене бинарников добавлен, но ещё не запускался (итого ожидается32 Python);
- ModelServices.Restore исправлен: при смешанных алгоритм/LLM bindings Setup получает
  все NPC actors, иначе конфиг disabled actor ошибочно отклонялся после load;
- Program.cs нормализует bool flags, запрещает research-setup с load/replay,
  помечает loaded research-wait timingPolicy и дополняет usage;
- денежный ввод участников проверяет точность0.01 и safe integer вместо скрытого округления.

scripts/check_research_model_routing.py написан, **не выполнен**.
Это явно обозначенная HTTP protocol fixture, не настоящая нейросеть:
две модели для actor2/4, actor3 алгоритмический; общий inference slot;
точные prompt/reply; research-wait; load со смешанными bindings; replay parity.

Одна ранняя попытка UI gate увидела world-index только до102 при progress completed120.
Отдельные прогоны с частым concurrent чтением и без чтения дошли до120; повторный UI gate прошёл.
Причина не установлена. Не объявлять это заведомо особенностью среды.
Добавлена проверка полноты записи, но её необходимо повторно проверить в реальном UI.
Не скрывать неполную запись под статусом succeeded.

## Где прервалась работа

Полный scripts/check_headless.py был запущен в artifacts/research-workspace/headless,
внешний run_check workspace-headless, timeout900.
Его обычные894 теста прошли (116.475с). Последний просмотренный лог дошёл до
model-safety-load/replay и сравнений с exit0. **Общий headless-check.json/финальный
exit code не прочитан** до отключения среды. Полный gate не считать завершённым.
После этих source-правок Research.Runner нужно пересобрать и выполнить оставшиеся gates.

В окружении были установлены вне репозитория:
.NET10.0.401 /workspace/tools/dotnet/dotnet,
DOTNET_CLI_HOME=/workspace/tools/dotnet-home,
DOTNET_PROCESSOR_COUNT=1 (для MSBuild нужен -m:1).
Godot4.7.2 mono /workspace/tools/godot/Godot_v4.7.2-stable_mono_linux_x86_64/.
QA Python Playwright /workspace/tools/qa-python;
headless shell /workspace/tools/headless/chrome-headless-shell-linux64/chrome-headless-shell.
Для браузерных gates PYTHONPATH=/workspace/tools/qa-python,
DELSK_CHROMIUM_PATH=указанный shell, DELSK_BROWSER_SINGLE_PROCESS=1.
QA сам создаёт HTTP server и браузер в одной process family.

## Следующий исполнитель

1. Проверить сохранность локальной feat/research-workspace-maps и git diff/status.
   Не уничтожать дерево и не начинать реализацию заново, если файлы на месте.
2. Прочитать AGENTS, проверить все финальные исходники и известные ограничения выше.
3. Пересобрать runner, выполнить32 Python tests, дополненный UI gate в новой пустой
   output-папке, HTTP protocol routing/load/replay gate, проверить полный headless итог.
4. Проверить оба новых сценария на горизонте600 и запись остановленных выборов.
   Не выдавать эти короткие прогоны за доказательство баланса/Hard.
5. Удалить только явно сгенерированные в этой итерации неотслеживаемые UID:
   Playfield.Appearance.cs.uid, SkinPreview.cs.uid, WorldSkins.cs.uid.
   Проверить, что они всё ещё не отслеживаются и не появились как чужая работа.
6. Закончить implementation report с точными итогами и ограничениями,
   устранить ссылки на отсутствующие документы, проверить diff --check.
7. Сохранить компактные screenshots/gates в docs/verification при необходимости.
   Не коммитить SDK, браузер, артефакты симуляций, caches и большие логи.
8. Commit/push feat/research-workspace-maps и создать PR в feat/economic-cycle-p00-p05.
   Пользователь разрешил публикацию кода; повторное разрешение не требуется.
   Автоматически не сливать. Дать пользователю действительную ссылку на PR.

Реальная LLM, Windows/Android hardware, длинный баланс/Hard/ROI и устранение тяжёлой
старой полной истории остаются открытыми. Запись новых кадров увеличивает расход диска/времени
и не исправляет старую проблему памяти.

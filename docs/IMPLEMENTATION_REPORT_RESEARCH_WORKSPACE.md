# Завершение исследовательского пространства и карт

Автор проекта: Шелестов Максим Евгеньевич. 23.10.2026, Europe/Samara.
База:96e19671766cd882dbd825df902ee6c872c8e15e (слитый PR51).
Ветка:feat/research-workspace-maps. Автоматическое слияние не выполнялось.

## Результат

Завершено последнее поручение по исследовательской лаборатории и картам.
Незакоммиченные исходники отключившейся среды не нашлись; реализация
восстановлена по сохранённому журналу и заново проверена.

| Требование | Реализация |
| --- | --- |
| Оформление в духе OpenAI/Cursor | Нейтральная тёмная тема, боковая навигация, вкладки вместо одной длинной страницы |
| История опытов | Каталог, поиск, размер, название, дублирование, архив/возврат, подтверждаемое одиночное/групповое удаление |
| Управление выполнением | Живые tick/target/percent/elapsed/speed/heartbeat/model requests; отдельные очередь и pause/resume/cancel попытки |
| История мира | Кадры каждого завершённого шага на диске, UTF-8 byte-offset index, просмотр/ползунок/воспроизведение |
| Диагностика | События, решения и prompt/reply; фильтры категории/участника/текста/проблемных слов, раскрытие raw JSON |
| Файлы без скачивания | Inline JSON/CSV/JSONL/log страницами до64KiB, предыдущие попытки, обычный download и ZIP |
| Анализ | Реальные итоговые метрики и графики CSV; downsampling сохраняет каждого actor и его конечную точку |
| Отдельные настройки | Снимки входов, формы экономики/поиск, полный JSON, профили-копии |
| Участники | Пассивный human +1–15NPC, старт/деньги/вместимость/предпочтение/сложность и модель по NPC |
| Локальные модели | Ollama profiles и /api/tags, per-actor routing с общим ModelRuntime/inference slot; old CLI preserved |
| Четыре квартала | Сценарий из PR45 подключён в игру и лабораторию; старый PR целиком не сливался |
| Городской тракт |27×25: ресурсы слева, промышленность в центре, город и торговля справа, связные дороги,4участника |

Начатые опыты не получают новые настройки профиля. Входы/DLL закреплены
SHA256; бинарник иной версии не запускается в прежней серии. Clean summary
исключает failed/interrupted/cancelled/Developer/intervened и неполные записи.
Парные различия объявляют factor/mode/model contrast и не смешивают
разные timing-группы или недекларированно изменённые стартовые условия.

## Архитектурные границы

Simulation.Core, экономический профиль config/economy.json и исходная
game_specification.md не изменены. World18/Rules production-recipes-1/
Session2/Envelope2 прежние. Python/JS не рассчитывают экономику.
CLI research flags и observer принадлежат composition layer Research.Runner.

ResearchWaitAtDecision-v1 останавливает дальнейшие шаги на время ответа,
сохраняя прежнюю очередь/fallback. Без нового флага старый CLI сохраняет
deadline timing. Исследовательские файлы не передаются в NPC/public UI.

## Фактически выполненные проверки

| Gate | Итог |
| --- | --- |
| Полная ordinary .NET suite |897passed,0failed,0skipped |
| Developer .NET suite |903passed,0failed,0skipped |
| Python laboratory tests |37passed |
| Полный scripts/check_headless.py |102exports,14replays,69comparison pairs; общий gate succeeded |
| Release runner и solution | Build прошёл |
| Godot .NET клиент | Debug build:0warnings/errors |
| Godot4.7.2 Mono/Linux smoke | PLATFORM/ELECTION/SUPPORT/PHYSICAL/TRADE/FOOD_PASS |
| Запускатель | Prepare/reopen/OS lock/Unicode/shutdown;20preset-map сочетаний,35arms через C# validators |
| Реальный Chromium + runner |600тиков/601кадр, формы/pausing/resume/cancel/archive/delete/profiles/files/ZIP/world/chart/640px/access guards |
| UI/CLI/observer/save/load/replay | Совпадение соответствующих полных экспортов и world-only replay |
| Конечные UI validation checks | Missing model/invalid seed/ошибочный economic arm отклонены; новая серия не публиковалась |
| Model routing HTTP fixture | Два model names у actor2/4, actor3 algorithmic, max concurrency1, точные prompt/reply, world wait, mixed load, replay без inference |
| Обе новые карты | Каждая600/seed42/4participants,601полный кадр, финальный hash совпал |

Это реальные новые запуски, а не числа из журнала прежней среды.
Полная обычная/Developer регрессия проверяла правила; поздние локальные
доработки presentation/progress/downsampling отдельно проверены итоговыми
browser/model/Python gates. Новые правила Core не добавлялись.

Godot import завершился exit0, но stderr содержал
EditorSettings not instantiated yet: export/android/shutdown_adb_on_exit.
Он не объявляется полностью чистым импортом; последующий smoke прошёл.

## Исправленные проблемы и неуспешные попытки

- Первый новый тест ошибочно добывал на некупленном частном ресурсе.
  Исправлен тестовый сценарий на публичную добычу; правило прав не ослаблено.
- В первой браузерной попытке повторное change количества NPC пересоздавало
  поле во время перехода фокуса. Добавлен guard неизменившегося состава.
- Проверка replay на финальном тике выборов выявила ожидание до подачи
  записанных бюллетеней. Теперь сначала подаются final-barrier inputs;
  повторная полная UI/CLI/load/replay проверка прошла.
- Подмена snapshot выявила неинициализированный cmd в ветке ошибки.
  Исправлено; unit boundary проверяет отказ без поломки drain.
- Ранние launcher-restore попытки возвращали exit1 без развёрнутого stderr.
  Подготовка теперь явно serial (--disable-parallel/-m:1); итоговый
  launcher gate с подготовкой/повторным запуском прошёл.
- Прореживание общего CSV могло оставить только одного actor при кратном
  stride. Оно выполняется по каждому actor; отдельный unit проверяет все
  линии и конечные точки.

## Ограничения

- HTTP fixture не является реальной нейросетью. Реальный runtime/веса,
  digest/скорость/качество inference и текущий P56 остаются локальной приёмкой.
- Cloud API и визуальный редактор карт не реализованы и не выданы за готовые.
- Windows EXE/Android-device/macOS hardware, FPS/звук/ввод на устройствах
  не проверялись этим Linux gate. Успешный Windows CI тоже не заменяет устройство.
- Равные расстояния и600тиков не доказывают долгосрочный баланс/ROI
  или преимущество Hard. P59/P60 целиком не закрыты.
- Прежняя полная история по-прежнему тяжёлая по RAM/save/load. Новая
  запись добавляет около31,5–31,7MB world-frames для601кадра новых карт
  в проверенном сценарии. Это измерение данного стенда, не универсальная оценка.
- Timeout включает время ручной паузы. Отмена сохраняет частичный результат,
  который не включается в успешную статистику.
- UI seed ограничен safe JS integer; CLI сохраняет uint64. Новые начальные
  настройки задают пустой рюкзак указанной вместимости, не произвольный товар.

## Как проверить вручную

1. После слияния обновить feat/economic-cycle-p00-p05 и открыть ResearchLab.cmd.
2. «Новое исследование» → «Быстрая проверка» → выбрать карту → «Начать».
3. В карточке открыть «Мир по шагам», затем «События и решения» и «Файлы».
4. Для проверки pause/resume выбрать более длинный опыт и достаточный timeout.
5. В игре «Сменить карту»: доступны прежние три, «Четыре квартала»,
   «Городской тракт». Выбор карты сам по себе партию не запускает.

[Полная инструкция](RESEARCH_WORKSPACE.md).
[Карты и расстояния](MAPS_RESEARCH.md).
[Компактная сводка gates](verification/research-workspace/gate-summary.json).
[Зафиксированные source hashes](verification/research-workspace/source-files.sha256).
Скриншоты: [каталог](verification/research-workspace/catalog.png),
[новый опыт](verification/research-workspace/new-experiment.png),
[мир](verification/research-workspace/world.png),
[узкое окно](verification/research-workspace/narrow.png).

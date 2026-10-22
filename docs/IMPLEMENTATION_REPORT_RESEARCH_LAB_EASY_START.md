# Упрощение запуска и использования исследовательской лаборатории

Автор: Шелестов Максим Евгеньевич.22.10.2026 (Europe/Samara).

База: актуальный слитый 25d48a2, PR50. До начала рабочее дерево чистое.
После прежнего 758ea62 слиты PR49 (обычный Godot Debug export с режимом бога)
и PR50 (заменяемые скины, десять образов, локальные appearance preferences,
обновлённые SVG). Проверены Git diff и реальные WorldSkins/AppearancePreferences;
исследовательская оболочка ими не упрощалась. Ветка этой задачи:
`feat/research-lab-easy-start`, создана от актуальной основной ветки.

## Изменения

- ResearchLab.cmd: двойной щелчок из корня, py -3/fallback python; ResearchLab.sh
  для Unix. Больше не нужно вручную собирать runner, указывать output и искать URL.
- --prepare проверяет установленный SDK из global.json, выполняет locked restore
  и инкрементальный Release build с hard timeout по180с и логами. Старый DLL не
  используется после ошибки подготовки; установки/смены toolchain нет.
- --open-browser/--port0: свободный порт, открытие браузера; повторный launcher
  проверяет identity уже работающего loopback host и открывает его же очередь.
  OS lock удерживается до завершения активного job/уже принятых HTTP-запросов.
- Четыре presets, три карты, формы тиков/seed/факторов, одна кнопка запуска.
  Базовый опыт120тиков/1seed/безLLM; расширенный JSON/CLI сохранён.
- Прогресс/ошибки по-русски, автоматические результаты последней серии, метрики
  с единицами; денежные Minor переводятся в монеты только при отображении.
  Открытые списки файлов сохраняются при polling, выбранные серии тоже.
- ZIP серии с планом/validated inputs/попытками/исходными экспортами/сводками/логами,
  на дисковом временном файле, без чтения всей истории в RAM сервера. Running
  блокирует архив, token/Host/Origin/workspace guards сохранены. Доступны логи
  неудавшихся попыток. Shutdown запрещает новые старты, ждёт активный bounded опыт.
- README, подробная RESEARCH_LAB, решения/архитектура/AGENTS и recovery context.
  Python unit/HTTP tests добавлены в существующий CI.

## Фактически запущено

- Bounded locked restore/build Delsk.sln Release: exit0; **891/891 .NET tests**,
  0 failed/skipped. Client/Core/save/policy не изменены.
- **23/23 Python tests**: прежняя очередь/метрики + SDK/restore failure,
  освобождение lock, reopen identity, presets, архив/active/path guards,
  логи failed попыток, privacy и запрет старта при shutdown.
- Настоящий launcher Linux: подготовка, два browser-open через записывающий
  opener, повторное открытие без rebuild, запрет второго CLI, Unicode/пробелы,
  остановка и освобождение lock. Все **12 сочетаний presets/карт**, **21 вариант**
  прошли реальные C# validate-only; это не57 выполненных экономических опытов.
- Настоящий Chromium→runner: отказ невалидному плану/пустому seed/missing model
  config, формы факторов/карты, queue/start/pause/resume, сохранение раскрытого
  списка файлов, CSV, quick preset одним нажатием, автоматический результат,
  скачивание и проверка ZIP, окно640px без горизонтального переполнения.
- В браузере выполнены2парных опыта +1quick. Дополнительно прямой CLI, save@59,
  load до120 и replay: **7 экспортов,4 полных/World-only сравнительных пары**.
  State hash UI/CLI: c49e925fefbf543b1b21a196db2dc9a76da3a8a3c492056b37fbb5f3cbe62b76.
  Проверены missing/wrong token, чужие Origin/Host403, перезапуск без повторения
  successful jobs. Новая экономика/модель/фейковая симуляция не вводились.

Компактные evidence, SHA исходников и лимиты:
[verification/research-lab-easy-start/gate-summary.json](verification/research-lab-easy-start/gate-summary.json).
Снимок: [laboratory-640.png](verification/research-lab-easy-start/laboratory-640.png).
Полные временные результаты/логи: /workspace/work/delsk-lab-easy (не коммитятся).

Windows double-click/.cmd и macOS непосредственно не запускались в этой Linux
среде. Godot export/hash gates не повторялись: изменений клиента/ядра нет;
это не утверждение о Windows/Android hardware или открытых P59/P60 gates.
Реальная модель не запускалась и для алгоритмических исследований не нужна.

## Передача

[Инструкция](RESEARCH_LAB.md): ResearchLab.cmd → «Быстрая проверка» →
«Начать исследование» → «Скачать серию ZIP». Результаты artifacts/lab.
Следующий ограниченный шаг — проверить двойной щелчок на машине Windows владельца,
затем выбрать содержательную парную серию для оставшихся P59/P60 вопросов.
Временная legacy-design ветка не включалась в изменения и ждёт подтверждения
скачивания перед удалением. PR этой задачи не сливать автоматически.

Основной commit: [89a6007](https://github.com/MaksShelest/Delsk/commit/89a600768179ef080ba44d3d224c033ec9a84c3a).
PR: [#51](https://github.com/MaksShelest/Delsk/pull/51), открыт без автоматического merge.

# PR64: причины красных checks и исправления

Автор: Шелестов Максим Евгеньевич.26.10.2026 Europe/Samara.

Исходный commit be695e0, PR64. Скачаны реальные журналы и artifacts GitHub,
не сделан вывод только по скриншоту. Сводка: verification/development-d2-ci/.

| Check | Что произошло | Исправление |
| --- | --- | --- |
| Core verify / PR, run40250853143 | Restore/build успешны. Базовый xUnit процесс не закончил1117тестов за120s; timeout124, ни одного assertion failure в журнале. Локально он занимал107–110s, другой push-run тех же исходников прошёл. | Три непересекающихся процесса:1062base/55integration/36D2. Те же1153теста; будущие классы автоматически вbase. TRX проверяет пропуски, пересечения и соответствие фильтрам. Каждый deadline120s сохранён. |
| Core verify / push, run40250783306 | Headless и все сценарии вплоть доD2успешны. Developer step отменён на15-минутном лимите всего job. | Developer — отдельный job со своим прежним15minлимитом и полным1160suite; процессы тоже разделены. Итоговый verify требует успеха обоих jobs, прежняя обязательная проверка не ослаблена. |
| Windows HUD/God, run40250853139 | Обычная сборка/audit/import/replay/Release audit успешны. Повторный импорт в Developer-stage завершил asset/editor layout, но процесс не вышел за120s. | Только для stageHUD переиспользуются импорты обычного проекта, уже успешно проверенные в том жеjob. Проверяются точные исходные байты, Godot source_md5, наличие всех remapped resources; не копируются managed assemblies/editor state. Rootimport и обаnativeDebug/ExportDebugcontrols остаются обязательными. |

Таймаут/отмена — настоящая незавершённая проверка, не доказательство ошибки игровых
правил и не успешный тест. Они не переименованы в успех и не игнорируются.
Сообщение Godot EditorSettings/android при завершении headless-editor — известный
отдельный notice из движка; на нём не основано предположение об ошибке Android игры.

При реальном запуске HUD после обхода editor shutdown обнаружена **ошибка нашего
кода**: DeveloperPanel без Development правил всё равно создавал XP OptionButton/
SpinBox/Label. Они не попадали в дерево и оставляли native RID/resources при выходе.
Теперь элементы создаются только при наличии отраслевого опыта; все созданные
элементы принадлежат дереву. Probe ждёт освобождения клиента перед Quit. Строгая
проверка ошибок Godot не ослаблена. Native regression проверяет отсутствие XP UI
в legacy и настоящие Set/Add XP с аудитом вD2, плюс очищенный выход.

Локально проверены все1153ordinary, вся runnerматрица102exports/14replay/69pairs,
20nativechecks вDebug и20вExportDebug с исходными props без CLI Developer flags.
7Python-тестов новой инфраструктуры проверяют exhaustive partitioning, реальные
TRX theory rows, запрет skip/duplicate, stale/missing imports и сохранение stageDLL.
Полный1160Developer прогон прошёл:1069base/55integration/36D2.
Результат повторногоGitHub запуска проверяется отдельно после push. Изменения игровых правил/баланса/D3 не выполнялись.

Зелёный блок GitHub «No conflicts» относится только к слиянию Git и не обещает
успех тестов. Автоматическое слияние PR не включать.

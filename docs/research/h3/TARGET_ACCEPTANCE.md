# H3: сборки и аппаратная приёмка

Автор: Шелестов Максим Евгеньевич.02.11.2026.

Источник — ветка `feat/vnext-h3-compact-acceptance`, PR77.
[Windows bundles](https://github.com/MaksShelest/Delsk/actions/workflows/h3-targets.yml)
содержат Developer и Release отдельно. Проверяйте исходный commit, успешный
`packages.json`, SHA256 и отсутствие failed/timeout проверок; диагностический
artifact не является успешной сборкой. Срок хранения Actions artifacts90дней.
Полная матрица результатов — [отчёт](../../IMPLEMENTATION_REPORT_H3.md).

## Воспроизводимая сборка

Нужны SDK10.0.401, Godot4.7.2 .NET и именно matching mono export templates.
Путь `godot-mono` ниже заменить реальным. Scripts создают отдельный stage;
повтор после ошибки выполнять в новом output, сохраняя старые логи.

```bash
python scripts/build_h3_targets.py --godot /path/to/godot-mono --platform windows --output artifacts/h3-windows
python scripts/build_h3_targets.py --godot /path/to/godot-mono --platform android --android-sdk /path/to/android-sdk --java-sdk /path/to/jdk --output artifacts/h3-android
```

Android требует platform36, build-tools36.1.0, platform-tools и JDK17+;
здесь подготовлен JDK21.0.12.1. Gradle8.11.1/Android plugin8.6.1 берутся из
официального Godot template. Обычный ExportDebug включает Developer согласно
действующим props; ExportRelease исключает Developer и Debug UI. Скрипт не
подменяет экономику и не внедряет probes в bundles. Использует прежние
restore/build180s, Godot import120s, export240s и metadata audit120s.

Android builds имеют test-only package IDs
`com.maksshelest.delsk.h3.developer` и `.release`, чтобы ставиться рядом с
прежними приложениями и не заменять их saves. Локальный test key генерируется
в ignored staging и не включается в artifact. Это сборка для проверки, без
подписи магазина. Для воспроизведения той же подписи сохранить свой исходный
ключ отдельно; другой ключ потребует нового package ID или удаления только
тестового приложения после резервного копирования его данных.

## Windows

Распаковать целиком выбранный `developer/` или `release/`, сохранив EXE, PCK и
`data_Delsk.Client_windows_x86_64` рядом. Runtime .NET включён в bundle. Запустить
`DelskProbe.exe` отдельно от Godot редактора. Наличие файла/успешный cross export
само по себе не подтверждает запуск EXE. Windows hosted VM обозначать отдельно
от компьютера пользователя.

1. Новая игра: по очереди3карты,3/6/10NPC и разные сложности. Убедиться, что
   «Малая долина» имеет описание/превью; обычный старт200монет, нет зданий/техники.
2. RU/EN, оба оформления, WASD/мышь, масштабы/выбор клеток, карта города,
   уведомления, помощь, планшеты и пауза. Проверить входы civic объектов,
   постоянство колей и обход работающей дороги.
3. Earn→land/import→factory/WIP/output/XP→warehouse→vehicle→несколько рейсов с
   оплатой→работы дороги/счёт. Записать фактические траты/таймеры, не использовать
   Developer для естественного прохождения. Prepared snapshots подписывать явно.
4. Сохранить опасные состояния и продолжить через load. Закрыть приложение,
   снова запустить, проверить сумму денег/товар/XP/награды/сроки. Сначала сделать
   резервную копию существующих saves; новая карта не мигрирует старые layouts.
5. В Developer проверить Меню→Debug→Режим бога отдельно. В Release кнопки/API
   вмешательств отсутствуют. Отдельный prepared God-mode run не является ordinary.
6. Для FPS/памяти указать CPU/GPU/ОС, размеры окна/карты, состав и tick;
   software-rendered покадровая запись не заменяет измерение производительности.

На Windows отдельно определить реальный установленный Ollama runtime и модель.
Следовать [LOCAL_MODEL_VALIDATION](../../LOCAL_MODEL_VALIDATION.md), записать
имя/digest, конфиг и настоящий inference в текущем commit. Если runtime/model
недоступны, оставить gate открытым; fake adapters и старые пилоты его не закрывают.
Не устанавливать/перенастраивать модель владельца без необходимости.

## Android: только алгоритмический режим

Установить APK arm64 на совместимое тестовое устройство, выбрать Algorithmic;
не включать local model. Записать модель устройства, Android, commit/SHA APK,
RAM/FPS/максимальное время кадра. Проверить RU/EN, touch movement и планшеты,
оба скина, узкий экран/масштаб, все3карты и состав3/6/10. Synthetic touch events
в Windows/Linux native scene не означают аппаратную мобильную эргономику.

Во время движения/добычи/WIP свернуть минимум на минуту и вернуть приложение:
мир, груз и сроки продолжаются с сохранённой точки, офлайн catch-up отсутствует.
Проверить короткую партию,1200/5000 и поздний реально достигнутый checkpoint;
память и load time измерять на устройстве. Новый ordinary maxTicks1m обеспечивает
технический горизонт, но не обещает мобильную производительность на277часах.
Сохранить evidence и оставшиеся дефекты до объявления аппаратного pass.

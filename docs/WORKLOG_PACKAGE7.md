# Пакет7 и планшет пекарни — восстановление работы

Автор: Шелестов Максим Евгеньевич.20.10.2026 (Europe/Samara).

## База и границы

PR#42 фактически слит:853b9935d8102289d6322e6520310b22fa8621b7. До начала чисто; новая ветка feat/package7-visual-audio-bakery. Нормативный пакет7 §12.1: **P30/P57**, не исследовательская лаборатория P58. Пользователь отдельно разрешил следующий пакет, поэтому прежние незакрытые model/Hard gates остаются честно открытыми; план явно допускает пакет7 раньше. Не переписывать экономику/контроллеров, не объявлять новую аппаратную/модельную приёмку.

Архив пользователя сохранён docs/design/bakery-v1/source.zip + оригинальный README. Извлечён с проверкой traversal/symlink/размера в /workspace/work/delsk-package7/reference/delsk_bakery_v1. Только сцена/artwork, demo.gd/project.godot/примерные числа не являются игровыми требованиями.

## Этапы

1. P30: собственный склад-площадка по actual stock/capacity/footprint/walls, не раскрывать чужие запасы; силуэты реально существующих уровней, кеширование моделей/ограниченные штабе́ли. Unit/privacy/save-load/replay и Godot проверка.
2. P57: краткие RU/EN реплики из публичных фактов, лимиты/приоритет/выключение; звук событий/кнопок + тихое окружение/опциональная музыка, отдельная громкость/источник и лицензия. Только presentation clock/state, без world PRNG/LLM/чужих условий; пауза/focus/ускорение без лавины. Meaningful tests и сводка/commit.
3. Пекарня: обобщить уже существующий presenter лесопилки минимально, подключить BakeryTablet.tscn к тем же actions/access, реальные рецепты/прогресс/численность/зарплата/локальные материалы/логистика, desktop RU/EN и compact. Лесопилка не регрессирует. Затем общий gate/docs/push/один PR без merge.

## Точка продолжения

Этап1 P30 реализован: уровни/own-only physical warehouse stock, bounded24 piles/mixed types, floor/walls по actual catalog, отдельный cache stock.6 целевых tests (включая PresentationTests) прошли, Debug клиент собирается. Тест поймал absolute-vs-relative footprint, исправлено до commit. Godot визуальные/cache gate предстоит в общем этапе3. Следующее действие — этап2 P57, уже начат в рабочем дереве (PresentationEffects, FeedbackAudio, Playfield.Feedback, оригинальные WAV и7 дополнительных тестов);13 целевых tests прошли. Исходная Presentation имеет только kind/label/id/owner, уровни/складские товары не передаёт; WorldModels Warehouse ещё закрытый дом. Stage пересоздаёт всю статическую сцену при изменении ключа. P57 звука/реплик ещё нет; public events уже фильтруют support/negotiation. Планшет лесопилки работает через HouseholdObjectCard, не дублировать actions.

Среда: source /workspace/work/delsk-package7/env.sh; .NET10.0.401, Godot4.7.2 Mono в /workspace/.tools/godot/. Все команды через scripts/run_check.py с timeout и scratch checks/. Full tests executable MTP, не пустой dotnet test; не запускать несколько полных suites вместе. База868 ordinary/874 Developer. Локальная сеть Godot/Xvfb и NuGet/git требуют execution network permission, не нового пользовательского согласования. Автор коммитов: Шелестов Максим Евгеньевич /314023140a@gmail.com. После каждого крупного этапа обновить этот файл и commit.

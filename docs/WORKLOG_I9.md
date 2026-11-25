# I9. Журнал работы

- Сверены `AGENTS.md`, `ARCHITECTURE.md`, `DECISIONS.md` и отчёты I6-C7.4, I6-C8C, I6-C8D и I8. Создана ветка `feat/vnext-i9-character-motion-npc` от `5a780c8ac0fbb5d96c720b4e2f496809efd758f5` после PR #112.
- Исправлены чтение завершающего отрезка пешего маршрута, поворот и фаза шага в Godot. Активное перемещение воспроизводится по состоянию маршрута в сохранении.
- Исправлены выбор участка и перепланирование покупки при конкуренции. Базовые прогоны воспроизвели по 25 `ParcelOccupied` для Hard с десятью NPC, seeds 17 и 42. Два набора используют профиль I6-C8D.
- Сцену I9 собирает отдельный нативный стенд. Он проверяет состав обычной сборки до установки тестового скрипта. Проба рисует через реальный `PresentationStage`, считывает координаты фигуры, носа и четырёх суставов, записывает кадры через Godot и собирает из них MP4.

## Headless

Собрать `Research.Runner` в Release с .NET SDK из `global.json`. Для одного прогона в новом каталоге:

```sh
runner=src/Research.Runner/bin/Release/net10.0/Research.Runner.dll
i9_work=/tmp/delsk-i9
mkdir -p "$i9_work"
dotnet "$runner" --export-i6c8-profiles config/vnext/economy.i4-mobile-equipment.json "$i9_work/profiles"
cp config/vnext/balance.b1.json "$i9_work/profiles/balance.b1.json"
printf '%s\n' '{"npcCount":10,"difficulty":"Hard","seed":17,"startingIndustry":"woodworking"}' > "$i9_work/options.json"
dotnet "$runner" --scenario scenarios/vnext/compact-valley-i6c8c.json \
    --economy "$i9_work/profiles/i6c8d.json" --new-game "$i9_work/options.json" --script baseline \
    --ticks 720 --output "$i9_work/export" --checkpoint "$i9_work/t720.save.json" \
    --economy-sample true --human-ballot self --compress-controller-turns true
python scripts/verify_results.py "$i9_work/export"
```

Повторить для Easy, Medium и Hard с seeds 17 и 42. Малая долина использует 3, 6 и 10 NPC, Лесной край (`woodland-region-i6c8c.json`) — 3 NPC. Флаг `--research true` в отдельном прогоне записывает `world-frames.jsonl` для подсчёта пройденных клеток. Продолжение до t3600 использует `--load "$i9_work/t720.save.json" --ticks 3600` с новым каталогом вывода и без переопределения сценария.

## Godot

Используется Godot Mono 4.7.2, Linux, Xvfb, OpenGL Compatibility с llvmpipe и FFmpeg. Каталог импорта подготавливают `scripts/prepare_godot_asset_import.py` и `godot --headless --editor --import`. `scripts/check_i9_native.py --help` перечисляет параметры. На вход `--scenario` подать `export/scenario.json` обычной Малой долины с десятью NPC, Hard, seed 17. Для сравнения её же подать изолированному исходному дереву `5a780c8` с `--source <дерево> --baseline`. Для маршрута автономного бота подать `--natural <checkpoint>` обычного Лесного края, три NPC, Hard, seed 17, сохранённого на t18. Стенд отделяет результаты по каталогам `capture` и `checks`.

# Дополнительная G2: пропорции мира и административный район

Автор: Шелестов Максим Евгеньевич. 29 октября 2026, Europe/Samara.
Runtime source `5af0573` (после уточнения стоянки купленной машины).
Основа `da213bf` — слитые G2/PR72 и H1/PR73; новая ветка
`feat/vnext-g2-world-proportions`. H1 и H2 не выполнялись.
[PR74](https://github.com/MaksShelest/Delsk/pull/74), без слияния.
Исторический [отчёт G2](IMPLEMENTATION_REPORT_PACKAGE_G2.md) сохраняется.

## Аудит требований и результат

Сверены vNext §3/4/18, план P68/P75/P79, AGENTS, реальный Playfield/
PresentationStage, каталог скинов, адаптер карт, Core navigation/Fleet и Load.
Требования в документации не считались доказательством внешнего вида.

| Требование | До итерации | Результат для новых партий |
|---|---|---|
| Ратуша: территория3×3, корпус2×2 | Геометрия уже была; одна коробка без часов | Два этажа, светлые стены, тёмная крыша, видимые часы с двумя стрелками, флаг |
| Передняя площадь3×1, боковая полоса1×2 | Открытые клетки уже были, благоустройства не было | Вход/козырёк к настоящему entry, две скамейки, два фонаря, озеленённая боковая полоса |
| Терминал2×3 и грузовой подъезд | Геометрия2×3; ящики на открытой площадке | Корпус2×2, открытая передняя полоса, читаемые ворота и навес, грузовой путь без ящиков |
| Автосалон5×5, небольшой павильон, четыре display cells и проезды | Корпус5×3, четыре клетки и настоящие6машин; модели пересекались | Павильон2×2, прежние четыре клетки, две обычные машины на клетку и отдельный укладчик; открыты11клеток проезда |
| Согласованные масштабы | Персонаж classic≈.72 / cozy≈.734 высотой; разные фиксированные коэффициенты | Все10моделей и classic помещаются в.30×.48×.30, ноги на земле; подпись не масштабируется вместе с mesh |
| Транспорт меньше клетки | Две машины шире парковочной клетки, визуальные offsets от физического центра | Обычный car≤.38×.46×.76; укладчик≤.76×.55×.86; legacy truck≤.42×.45×.75; equipment≤.72×.75×.82 |
| Два набора и каталог | Civic обходил каталог; старые карты рисовали отдельный generic capital | Общая геометрия по сохранённому layout, две палитры, world/people/per-model switching, отдельные terminal/dealership пункты |

Исходные PNG и результаты: [before](verification/world-proportions-g2/before/),
[after](verification/world-proportions-g2/after/). Это настоящие игровые кадры,
не галерея вручную созданных DTO. В основных кадрах новых карт обычный старт с11участниками
и конечным столичным автопарком; бесплатные предприятия/машины игроку не выдавались.
Отдельные `*-purchased` кадры — явно funded fixture после обычной покупки/посадки,
с4участниками: проверяют стоящую собственную машину рядом с оставшейся stock.
`*-adapter1` кадры — prepared compatibility fixture с купленной землёй,4участника;
свойства настоящих исходных saves с11участниками проверены отдельно старым бинарным кодом.

### Сравнительные скриншоты

[Полная таблица «до / после»: обе карты, оба скина и старые партии](verification/world-proportions-g2/COMPARISON.md).

| Объект, Речные кварталы / основной набор | До | После |
|---|---|---|
| Район, камера10 | [PNG](verification/world-proportions-g2/before/small-river/classic-district.png) | [PNG](verification/world-proportions-g2/after/small-river/classic-district.png) |
| Ратуша, камера4.2 | [PNG](verification/world-proportions-g2/before/small-river/classic-town_hall.png) | [PNG](verification/world-proportions-g2/after/small-river/classic-town_hall.png) |
| Терминал, камера4.2 | [PNG](verification/world-proportions-g2/before/small-river/classic-terminal.png) | [PNG](verification/world-proportions-g2/after/small-river/classic-terminal.png) |
| Автосалон, камера6 | [PNG](verification/world-proportions-g2/before/small-river/classic-dealership.png) | [PNG](verification/world-proportions-g2/after/small-river/classic-dealership.png) |
| Персонажи, камера4.2 | [PNG](verification/world-proportions-g2/before/small-river/classic-people.png) | [PNG](verification/world-proportions-g2/after/small-river/classic-people.png) |
| Дополнительные персонажи, камера4.2 | [PNG](verification/world-proportions-g2/before/small-river/cozy-people.png) | [PNG](verification/world-proportions-g2/after/small-river/cozy-people.png) |
| Большой округ / дополнительный район, камера10 | [PNG](verification/world-proportions-g2/before/large-districts/cozy-district.png) | [PNG](verification/world-proportions-g2/after/large-districts/cozy-district.png) |

В каждой папке есть оба набора и виды `people`, `town_hall`, `terminal`,
`dealership`, `district`, `overview`. Полные PNG1600×1000 сохранены без дорисовки.
Направление/FOV/точки камеры одинаковы между before/after. Камера в изолированной
probe кадрирует существующий мир; участники не переставляются ради композиции.
Общий вид использует57.6 /115.2 для small/large, допустимые в игровом zoom.

## Геометрия и совместимость

Новые JSON имеют `delsk-map-design-2`, resolved layout — `map-adapter-2`.
Это сознательная адаптация геометрии **нового старта**, а не смена коллизий скином.
Территории, их ID, четыре parking cells, дорожные коридоры, ресурсы, промышленные
контуры, рынок и spawns прежние. В обеих картах только:

- корпус автосалона5×3→2×2 в северо-западном углу своего участка, минус11blocked cells;
- entry ратуши сдвинут на1 по X внутри передней площади, дверь совпадает с ним;
- entry автосалона сдвинут на1 к северу, к передней стороне нового павильона;
- terminal body/entry прежние, теперь объявлены явно.

Открытые клетки автосалона реально доступны Core pathfinder и транспортным
командам; рисование не выдаёт проход через оставшуюся заблокированную коробку.
Здания используют сохранённый Body, не рассчитывают свою коллизию из mesh.
Скамейки/фонари/озеленение — периферийное оформление открытой площади, не новые
участки или отдельные жители. Центры пешеходных полос и входа свободны.

Старый `delsk-map-design-1` по-прежнему использует прежнее толкование.
Сохранённый `map-adapter-1` восстанавливает свои Body/Entry/Parking непосредственно
из checkpoint. Его большой павильон остаётся большим и визуально: освобождать
11клеток только на экране было бы неверно. Пять старых карт без Layout не получают
новую территорию поверх чужой земли/дороги: generic синий дом заменён компактным
муниципальным образом в прежней клетке терминала. Новых терминалов/автосалонов
в старые профили автоматически не добавляли.

Новые layout2 партии требуют новой сборки: их downgrade в клиент доPR74
не поддерживается. Совместимость направлена на продолжение старых saves новой
сборкой; сохранённые layout1 не преобразуются автоматически.

World28, session3, Rules и профиль экономики прежние. Новая принимаемая версия
Layout использует существующие поля. Сохранения не мигрируются, не заменяются
новым JSON по map ID; обычное предупреждение о замене партии сохранено.
Все config-файлы и нормативный BALANCE JSON сверены с базой:
[unchanged-economy.json](verification/world-proportions-g2/unchanged-economy.json).
В прежнем manifest82inputs совпали80; остальные2 — именно изменённые initial
map snapshots. Их JSON diff ограничен Body/Entry/BlockedCells и version/source metadata,
все экономические поля точны: [financial comparison](verification/world-proportions-g2/financial-source-comparison.json).
Это дополнительная проверка, а не замена внешней приёмки.

## Фактическая проверка

Финальный native gate: **7 из 7 сцен**, **80 PNG после / 32 PNG до**,
**678 исходных файлов совпали** с зафиксированной сценой. Оба скина в каждом случае.


Windows CI на runtime5af0573:
[40909280528](https://github.com/MaksShelest/Delsk/actions/runs/40909280528),
**оба jobs success**: прежний Windows core/replay и полный G2 UX gate.
Это дополнительная GUI-регрессия на Windows2025 VM; описанные ниже7новых
proportions-сцен сняты на Linux, не на физическом устройстве.
Core CI [40909280572](https://github.com/MaksShelest/Delsk/actions/runs/40909280572)
на момент фиксации отчёта ещё выполняется; полного CI pass пока не заявлено.

Итоговые машиночитаемые статусы и ограничения:
[gate.json](verification/world-proportions-g2/gate.json).
Подробный ход, найденные ошибки и исправления:
[WORKLOG_G2_WORLD_PROPORTIONS.md](WORKLOG_G2_WORLD_PROPORTIONS.md).

Реальная сцена: Godot4.7.2 .NET / SDK10.0.401, Linux/X11 dummy display,
llvmpipe, GL Compatibility, Dummy audio. До/после — отдельные изолированные
ordinary стадии с настоящим Playfield. AssemblyAudit выполняется **до** добавления
stage-only probe; WorldProportionsProbe запрещена в обычной сборке.
Asset-only import нормально завершается; byte-verified reuse без Mono editor
initialization. C# build и каждый native процесс ограничены180s/150s.

Проверяется не только хеш: реальные mesh bounds всех11участников и6машин,
совпадение корпусов с Body, оба скина и целая сцена на разных расстояниях,
выбор клетки настоящим ray pick, точное восстановление resolved config; у legacy fixtures настоящий Playfield.Load с диска. Скриншоты
просмотрены визуально; именно это выявило закрытые кровлей часы и козырьком
ворота после уже успешной проверки AABB. Эти проблемы исправлены.

Core-проверки отдельно используют настоящие команды: доступ от всех11spawns
ко всем открытым civic cells, покупка/посадка/выезд на машине и подъезд к терминалу,
negative geometry, сохранение приобретённой земли/коллизий/маршрутов старого
adapter1, точное продолжение; все пять прежних карт и их реальные terminal routes.
Обычный набор G2: **41 passed** (14+10+17), Developer G2: **41 passed**;
Сборка Developer в Release ожидаемо отклонена. После проверки стоянки
приобретённой машины новый geometry block повторён: **17 passed / 55.225 s**.
Python tooling: 28 passed. Дважды экспортированы обе JSON-карты, snapshots точны,
несовместимый старый профиль ожидаемо отклоняется.

Проверка настоящего исходного бинарного кода: da213bf собран из `git archive`,
обычный старт по10NPC на обеих картах, checkpoint на60, продолжение до120.
Новый бинарный код восстанавливает эти saves: все world/controller/export files
совпали байт в байт, независимый финансовый аудит прошёл; layout1/корпус15клеток
сохранены. Это120-tick compatibility smoke, не новая серия H1 и не измерение баланса.
[Подробные результаты](verification/world-proportions-g2/old-binary-compatibility.json).

Полный локальный набор обычной сборки не прошёл целиком: первый блок292pass, затем прежний
integration timeout120.033s/124. Deadline и состав блока не изменялись;
CI/остальные проверки указаны отдельно, этот результат не назван полным pass.

## Оставшиеся несоответствия и границы

1. Пять исторических карт без Layout не имеют полноценного нового района3×3/
   2×3/5×5. Сохранена их прежняя география; компактная ратуша — совместимое
   обозначение, а не заявление о полном выполнении §4 на этих картах.
2. В старых map-adapter-1 партиях автосалон сохраняет корпус5×3 и прежний вход.
   Новый малый павильон/11дополнительных проезжаемых клеток применяется только
   к новым партиям. Автоматической миграции старого мира нет.
3. Native software-renderer gate не закрывает физические Windows/Android,
   сенсор, аппаратный FPS/звук и всю P80. Нового аппаратного утверждения нет.
4. Эта итерация закрывает перечисленные визуальные требования §3/4/18 для новых
   карт, но не объявляет весь vNext/P79/P80 завершённым. Длительный баланс,
   естественное развитие доV, Hard/LLM и известные H1/H2-блокеры не исследовались.

## Воспроизведение

```text
python scripts/check_headless.py --phase tests --suite ux --output work/g2-units
python scripts/check_developer.py --suite ux --output work/g2-developer
python scripts/check_map_designs.py --output work/map-snapshots
python scripts/check_world_proportions.py --godot PATH_TO_GODOT_4_7_2_MONO --output work/g2-visual --verify
```

Для сохранения generated legacy fixtures: `DELSK_MAP_EVIDENCE=ABSOLUTE_DIRECTORY`
перед unit gate, затем `--legacy-save-dir ABSOLUTE_DIRECTORY` у visual runner.
Проверка машин после покупки добавляется `--fleet-save-dir ABSOLUTE_DIRECTORY`
(та же generated fixture directory). Денежная подготовка этих случаев не является
обычным стартом или измерением доходности.
Нужен графический дисплей, на Linux `DISPLAY` и программный OpenGL при отсутствии GPU.
Опциональный `--asset-cache` принимает только проверяемые идентичные assets.
Для проверки реально старого бинарного кода, а не только подготовленной
ScenarioDefinition: `python scripts/check_visual_legacy_compatibility.py --output work/old-binary`.
Он собирает исходный da213bf, создаёт обычные saves на обеих картах с10NPC,
затем продолжает их текущим бинарным кодом и сравнивает точные экспорты.
Это короткая проверка совместимости, не исследование H1/H2.

Новый output обязателен; неудачные попытки не перезаписываются.

| Финальная native сцена | Участников | Секунды | Результат |
|---|---:|---:|---|
| small-river | 11 | 21.454 | pass |
| large-districts | 11 | 36.74 | pass |
| first | 11 | 12.095 | pass |
| small-river-adapter1 | 4 | 27.8 | pass |
| large-districts-adapter1 | 4 | 49.672 | pass |
| small-river-purchased | 4 | 21.719 | pass |
| large-districts-purchased | 4 | 35.547 | pass |

# H3: компактная карта и приёмка после H2

Автор: Шелестов Максим Евгеньевич. 02.11.2026, Europe/Samara.

Работа продолжается. Полная версия не объявлена принятой: итог новой
экономической когорты, целевые экспорты и аппаратные gates ещё сверяются.
База `b87bc90a84845f509cf256bb227dbcf2952a43ad` содержит H2/PR76;
ветка `feat/vnext-h3-compact-acceptance`, [PR77](https://github.com/MaksShelest/Delsk/pull/77)
draft, без auto merge. Первичная реализация `ec0886b`, функциональный этап
`4175455`; ordinary21/21 и Developer21/21 после разделения большой prepared
траектории на независимые bounded phases. [Ход работы и все неудачи](WORKLOG_H3.md),
[воспроизведение](research/h3/README.md).

## Реализованный результат

«Малая долина» доступна в обычном меню с описанием и превью.32×27=864клетки,
28,125% площади «Речных кварталов»64×48; старые16×9/16×13 не вмещают полный
согласованный набор объектов и безопасные коридоры.24полных площадки2×3,
8ресурсных районов3×3,11стартов и11торговых мест. Основная грунтовка и
ветви занимают125клеток;354клетки защищены от покупки/застройки. Связность
проверена при блокировании всех24промышленных контуров, включая11стартов,
каждый публичный ресурс, торговлю и все входы. Ратуша3×3/корпус2×2,
терминал2×3, автосалон5×5/корпус2×2, четыре места показа и отдельный вход;
оба G2 оформления используют одну геометрию.

Обычный полный G1 профиль, Movement1/divisor32,200монет, пустой рюкзак,
никаких бесплатных зданий/машин. Географическое отличие раскрыто явно:
8public/64private ресурса против8/192наRiver и24/576наLarge; свойства узлов,
цены, XP и рецепты прежние.17исходных финансовых/config/map файлов побайтно
равны базе: [manifest](verification/h3/unchanged-economy.json).

Только новый `OrdinaryNewGame.Resolve` получает maxTicks1000000
(277ч46м40с активного времени). Это конечный горизонт, а не бесконечность.
Тик остаётся секундой; таймеры, скорость H2, World29/World28, Session4/1–3,
принятые условия и лимиты сохранения прежние. Сохранённый maxTicks5000 не
переписывается. На всех3картах проверена действительная победа человека,
ContinueAfterVictory→Sandbox, save/load и точное продолжение. Отдельная
естественная траектория пересекла5000без override.

Обнаружен и исправлен дефект Godot load: сохранённый runner-controller
`self-ballot-v1` ранее ошибочно передавался Algorithmic.Restore и отвергался
UnknownControllerVersion. Клиент теперь применяет существующий
SelfBallotController.Restore, сохраняя память. Natural save t5604загружен
в настоящую сцену; следующие3тика совпали по world/controller hashes.
Обычный новый human-controller клиента остаётся ручным.

## Матрица требований и доказательств

| Требование | Доказательство | Результат и граница |
|---|---|---|
| Компактная карта, полный профиль,3/6/10NPC×3сложности | CompactH3Tests; deterministic source/snapshot; unit-gate.json | Пройдено9вариантов ordinary resolve; free assets отсутствуют |
| Геометрия, максимальный состав, защищённые проезды | Полное блокирование24контуров; native compact-start | Проходимость всех11стартов и обязательных целей подтверждена |
| Длинная ordinary и песочница | HorizonH3Tests на3картах; actual natural save5604 | Новый горизонт1m, old5000сохранён, controlled human victory→Sandbox подтверждён |
| Импорт/земля/строительство/выпуск/XP/склад | H3Acceptance, ordinary3NPC seed42 | Фактические paid milestones; scripted Send, не ручной UI и не optimal timing |
| Транспорт,5рейсов, escrow, груз, оплата | prepared-gate.json:41процесс,21экспорт,18точных31-file comparisons,3карты | Подготовленная экспозиция, действительные правила и грузы; не natural ROI |
| Дорожные работы и объезд | H3RoadSceneTests; compact-road-work/finished | Реальное производство/работа paver, exact restore, защищённый обход |
| WIP/параллельность/upgrade/снос/overflow/пени/выборы/межкванты | Существующие неизменённые partitions D1–F2/H2 и полный Core CI | Итог текущего CI ожидается; отдельные engine fixtures не означают ручное прохождение всех3карт |
| Три карты, оба оформления, входы и пропорции |36PNG/3карты/11участников, source-inputs; natural-loaded | Настоящий Linux Godot/Xorg/llvmpipe; изображения просмотрены |
| Грунтовка и движение |12roadPNG,180viewport frames/60quanta/6активных секунд | Прямые/повороты/перекрёстки/объезд; запись144,863s/150, не FPS benchmark |
| RU/EN, карта города, масштаб и ввод | city-gate.json:3/6/10NPC,640RU/800EN/360EN | Native synthetic keyboard/mouse/touch, pause/help/filters/save; не physical mobile |
| Developer/Release и режим бога | ordinary21/Developer21, native Windows HUD, negative Release, published APK/Windows metadata | Пройдено;180/120s не повышены, исходный Developer timeout сохранён |
| Windows/Android и реальная Windows LLM | Windows1132hashes в каждом flavor, реальные ARM64 APK/подписи/CRC, [инструкция](research/h3/TARGET_ACCEPTANCE.md) | Windows VM и cross APK пройдены. Первый Release launch exit1 уточняется; physical device/живая модель открыты |
| Новые измерения после H2 | study-plan, отдельный frozen run41355595793 |180ordinary+12extended в работе, результаты не смешиваются с H1 |

## Открытые требования и ограничения

1. Новая естественная когорта и её поздние bounded continuations ещё выполняются.
   Нет основания заявлять20–35часов, развитиеII–V, полную окупаемость,
   достаточность XPот заданий либо устойчивое промышленное преимуществоHard.
2. Пользовательская цепочка выполняется scripted human через Core Send.
   Доставка и дороги проверены отдельно в явно подготовленных мирах;
   единое непрерывное ручное UI-прохождение всей цепочки пока не подтверждено.
3. Windows VM/сборка не подтверждает физический Windows GPU/управление/FPS.
   Нет Android устройства: алгоритмический lifecycle, ресурсы и длинная память
   на телефоне остаются открытыми. Нет доступного текущего Windows LLM runtime/
   модели: живой inference не заменён синтетическими ответами.
4. Соседние крыши терминала/автосалона визуально соприкасаются; footprints и
   входы разные, проезд доступен. Изменять замороженную географию исследования
   без реального дефекта оснований нет.

## Целевые сборки и дополнительные gates

[Windows bundles, source dafa477](https://github.com/MaksShelest/Delsk/actions/runs/41362439343/artifacts/13106117244)
экспортированы стандартными ExportDebug/ExportRelease, self-contained .NET10.
Developer EXE выполнил1132контрольных хеша/35file restores. Тот же Release EXE,
без пересборки, прошёл1132/35в fresh Windows VM: [diagnostic41363547781](https://github.com/MaksShelest/Delsk/actions/runs/41363547781),
8,453s/90; Actions archive SHA и EXE SHA проверены. Исходный быстрый exit1
на первой VM сохранён, причина ещё читается из оригинальных logs.
Это не физическая Windows приёмка.

Android ARM64 test APK: Developer102310949bytes,
SHA256`04e0b94e05fcecfd90c174121a2babea7438838e8d877206d86af97531686f39`;
Release95307498bytes,
SHA256`cbd11bceb9825b9f7507006680e59cfe68d31eb4b142bdaa3a3ae0a76eacb4b4`.
Оба имеют действительную test подпись, API24/target36, всеZIP CRC, managed client
и ARM64 engine; Release не debuggable и без Developer/Debug UI. Первый cold
Gradle export240s сохранён как timeout; реальные повторные кандидаты прошли
те же240s. Временные source/build caches удалены после проверки для освобождения
диска, APK/ключ/логи/исходные manifests сохранены. [Evidence](verification/h3/targets/android/packages.json).
[CI APK workflow](https://github.com/MaksShelest/Delsk/actions/workflows/h3-targets.yml)
добавляет воспроизводимую доставку; итог его ещё ожидается.

[21ordinary](verification/h3/unit-gate.json) и [21Developer](verification/h3/developer-gate.json)
прошли без skips. Большой service case разделён: current preparation→loaded645,
settlement→972и полный replay0→972, с обязательным совпадением исходных world/tape.
48fixture files совпали, кроме одного checkpoint: независимое декодирование
показало только epoch после restore; прочие поля Session4 равны. Пределы120/180s,
64/512MiB неизменны. Все37Python protocol checks пройдены.

[Короткое движение](verification/h3/native/compact-motion.mp4),
[обзор малой карты](verification/h3/native/compact-start/cozy-overview.png).
Финальные суммы CI и новая статистика будут внесены после проверки.

# Дополнительная визуальная итерация G2

Автор: Шелестов Максим Евгеньевич. 29.10.2026, Europe/Samara.
Основа: da213bf (merge PR73), чистая локальная ветка work; remote проверен fetch.
Рабочая ветка: feat/vnext-g2-world-proportions. H1/H2 не входят в работу.

## Исходный аудит

Сопоставлены §3/4/18 vNext, P68/P75/P79, AGENTS и отчёт G2/PR72 с фактическим
PresentationStage, WorldSkins, CivicSite, MapDesignReader, Fleet, сценариями.
G2 реализовал карту/UX, но не полную модель административного района.

- Обе designed maps уже имели территории 3×3/2×3/5×5, зал 2×2, 15 мест рынка,
  11 seeded spawns, защищённую землю и настоящие Core body/entry.
- Civic обходил WorldSkins: оба скина показывали одинаковые одноэтажные коробки.
  Не было часов, двух этажей, скамеек/фонарей/озеленения. Dealer body=5×3.
- Старые пять карт без Layout рисовали generic capital: маленький дом с синей крышей
  в classic либо отдельную baked модель. Они не резервировали новые 3×3 территории.
- Персонажи и техника использовали несогласованные постоянные коэффициенты.
  Dealer рисовал два автомобиля шире половины клетки; offsets сдвигали машины
  за центр клетки независимо от физического положения.
- Load читает сохранённую ScenarioDefinition, не PlayMaps.Resource. Этот путь сохранён.

## Решение

delsk-map-design-2 / map-adapter-2: явные building/entry; дилерский павильон2×2,
тот же участок5×5 и те же4 display cells. Hall entry сдвинут на одну клетку по X
в передней площади, dealer entry на одну клетку к северу. Terminal entry прежний.
Все прочие ID/координаты/ownership zoning/parking/spawns/дороги/ресурсы сохраняются.
Изменения только новых партий; map-adapter-1 читается без переинтерпретации.
World28/session3/Rules прежние, финансовые профили не изменены.

Общие procedural civic factories используют saved Body/Entry/Footprint и два набора
палитр. Настройки скинов сохранены, добавлены terminal/dealership overrides.
Person bounds≤.30×.48×.30; обычные dealer cars≤.38×.46×.76, tracked≤.76×.55×.86.
Размеры подгоняются по настоящему mesh AABB, без изменения Core travel/collision.

## Ход проверок и исправления

- Native «до»: реальный Playfield, обе designed maps и first, оба скина,
  1600×1000, close4.2 / district10 / overview, screenshots и clean exit.
  large-districts146.539s при deadline150s; timeout не был повышен.
- Geometry build155.038s/180 pass; visual build141s/180 pass.
- Первый MapDesign gate:31pass/1failure. Legacy compatibility fixture выбрала
  первый дальний plot; путь обратно на large достиг ordinary horizon5000 и Idle
  перестал продвигаться. Не менялись engine/horizon/deadlines: выбран ближайший
  реальный участок, по-прежнему BuyPlot обычной командой и проверка собственности.
- Повтор:34/34pass,51.910s; includes actual purchase/boarding/drive через дилерский
  участок и терминал, walking/access/negative geometry, adapter1 ownership/restore.
- Новые проверки выделены в WorldProportionsG2Tests и отдельную disjoint -g2-2
  partition; прежние partitions и120s deadlines сохраняются.
- Финальные native/checks и визуальная оценка будут перечислены в итоговом отчёте.

- Первый финальный native gate остановился на поиске Civic_town_hall/BuildingBody:
  RebuildStatic оставлял старые узлы дочерними до конца кадра, Godot переименовывал
  новые одинаковые имена при переключении скина. Старые static nodes теперь
  RemoveChild перед QueueFree. Failure/logs сохранены в work/after; повтор в
  отдельном work/after2, без ослабления проверки размеров или deadline.

- Второй native gate: строгий mesh/body bounds выявил эмблему-колесо автосалона,
  выступавшую на.0025 клетки за сохранённый Body. Диагностика дала фактический
  AABB; эмблема утоплена в фасад. Допуск/проверка не ослаблялись.

- Визуально просмотрены реальные close/district PNG обоих скинов после первых
  правок. AABB/хеш не выявляет закрытые крышей часы и скрытые козырьком ворота:
  башенка поднята над кровлей, козырёк уменьшен с.37 до.19 по глубине; дверь
  получила стекло/ручку. У выставленных машин скрыты тесные raw-ID captions,
  реальные listings по-прежнему доступны в карточке автосалона.

- Полный локальный ordinary aggregate: первый блок292pass, затем прежний
  integration partition timeout120.033s/124. Лимит и состав прежнего блока
  не менялись. Это не полный pass; исходная диагностика сохраняется.
- Scoped G2 ordinary:41pass=14+10+17, без пропусков. Новый блок включает все
  пять прежних карт, исходный map-design-1, приобретённую землю в adapter1 и
  реальную покупку/вождение в adapter2.
- Первый Developer restore остановился на NU1900: sandbox не смог прочитать
  разрешённый NuGet vulnerability endpoint. Повтор с сетевым доступом использует
  прежние locked-mode и vulnerability checks, без отключения аудита.
- Review вернул прежние смещения legacy truck/equipment: у старых зданий anchor
  проходим и занят символическим mesh; центрирование скрывало бы технику крышей.
  Новые физические Fleet машины остаются в настоящих центрах/parking slots.
  После этого проводится отдельный полный native rerun финального source5ef209b.
- Создан PR74 как draft до завершения проверок; merge/auto-merge не запускались.
- Проверка состояния после покупки выявила ещё один край: купленная машина всё
  ещё делит display cell с оставшейся stock машиной, но ForSale=false раньше
  сразу центрировал её. Общие display slots теперь применяются и к стоящей
  собственной машине. Native probe проверяет непересечение пар и загружает
  реальные funded purchase/boarding checkpoints обеих карт отдельно от ordinary.

## Финальный результат

Runtime5af0573:7/7native/80PNG после+32до, оба скина, обычные11участников,
отдельные prepared4 ownership и purchased/boarded cases;678inputs exact.
41ordinary+41Developer,17geometry retest;28Python; дважды exact map snapshots.
Actual da213bf binary checkpoints обе карты10NPC/t60→120: exact exports и
независимый accounting. Windows40909280528 обаjobs success на5af0573.
Core40909280572 ещё выполняется; локальный integration120s failure сохранён.
Никакого полного pass/physical-device/H1/H2/merge не заявлено.

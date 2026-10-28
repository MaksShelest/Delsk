# G2 — восстановление работы

Автор: Шелестов Максим Евгеньевич. 28.10.2026, Europe/Samara.

- Основа52cb5c0, merge PR71; содержит G1/runtime8da2218 и финальный report900e886.
  Git был чистым. Core40722536198 все27 jobs иWindows40722536200 success;
  G1 естественная матрица1800 ещё без предприятий, ограничения не отменяются.
- Ветка feat/vnext-g2-city-experience. Объём пользователя: P75/P79, необходимые
  UX/restore проверки. Не P77/полная аппаратная P80. PR без auto merge.
- Готовые решения: четырёхвкладочный PlayerTablet, own/bilateral PlayerView,
  Presentation public models, карточки/AccessFor, E2 roads, F2 services и E3 bills;
  help/tablet pause и focus-save. Миникарта DebugBoard не масштабируется.
- План: allowlist city-map DTO + viewport/preferences; native pan/zoom/select/layers
  и deep links; общий compact navigation, notification inbox и короткое обучение;
  устойчивые forms/safe areas/close/focus; читаемость входов/уровней, сохранение skins;
  meaningful headless privacy/state tests и native RU/EN/large11/touch scenarios,
  прежние regression/Windows gates, evidence/report/PR.
- Среда переиспользована: /workspace/work/e1/env.sh, SDK10.0.401/Godot4.7.2,
  Xorg/software Mesa. Cloud runtime skill/network policy прочитаны; сеть enforced.

- Реализованы allowlist CityMap/viewport memory, pan/zoom/select/filter, реальные
  чистые PreviewMovement через общий Core pathfinder (без Traffic lazy insertion),
  deep links к готовым own cards, отдельные notices/journey и сохраняемые UI read prefs.
  Просмотр bill/service идёт нормальным Send, но не блокирует Accept; таймер/пауза
  сохраняют обычные правила. Work/overflow read — cosmetic companion config.
- Сохранены входы/уровни и обе skin families; добавлены подписи входов и own status,
  плавное camera zoom. Карта/памятка/уведомления не пропускают WASD в world; ввод
  текста тоже блокирует игровой polling. Общий legacy Player tablet остаётся global.
- Source build4: Core27.832s/180, ordinary client22.247s/180.22 новых G2 tests прошли
  за23.240s/120. Матрица обе карты×3/6/10,privacy,real path/road contributions,
  invoice view!=payment,overflow rescue,construction notice,route/read-state restore.
  Source ошибок первых попыток: Godot Node.Owner naming, long notification constants,
  переименование method group; xUnit2031 исправлен без отключения analyzer.
- Затем обнаружены старые mobile width600 в selector и фиксированные catalog/skin
  dialogs. Они переведены на usable viewport, scrolling/flow и safe-area popup.
  Context actions тоже flow. Native проверки ещё выполняются, devices не заявлены.
- Новый suite ux (-g2 partitions) отделён от previous/E/F/G1, старые deadlines/counts
  сохранены; добавлены обязательные ordinary/Developer Core CI jobs.
- Native wrapper attempt1 создал Xorg log внутри output до строгой проверки пустоты;
  runner закономерно отказал до stage. Диагностика сохранена, log перенесён наружу.
  Native2 stage ordinary build/audit перед добавлением stage-only forbidden probe.

- Дополнительно23rd test: стабильные ключи plot/resource/capital переживают статус,
  бронь и обновление спроса; Holder отделён от legal Owner. Civic footprint выбирается
  целиком, маршрут ведёт к реальному входу. Road card перечисляет денежные вклады
  и права90/10, городскую основу100%; верхняя подпись не выдаёт одного владельца.
  Добавлены own paid pickup guides, active service/logistics/contract routes.
- ServicePanel: structural refresh вместо каждого WorkDone, живые progress labels,
  draft wage/text/focus/scroll, проверка актуального quote и AccessFor;20 заказов
  на страницу с deep link к старому. Holdings тоже20/page. Другие личные панели
  переиспользованы, payment amount не перезаписывается обновлениями.
- Core build6 31.540s,23 G2 tests16.813s passed; fixtures содержат только обычные
  команды и явные rich/staged worlds, включая25 historical requests. Не natural.
- Native small-river6/RU/640 прошёл все11 групп сценариев, в том числе реальные
  mouse/wheel/keyboard/pinch, filters, help, focus, skins, narrow dialogs, diskresume.
  Home/End не навигируют стандартное PopupMenu так, как ожидала ранняя probe;
  изменена только проверка на Up/Down+Enter. Второй fail focus сравнивал tick до
  закрытия/повторного открытия карты, когда время законно шло; baseline исправлен
  на момент непосредственно перед focus loss. Оба failure сохранены.
- Native prepared RU process выполнил invoice/read/partial pay, acceptance,
  history paging, overflow partial rescue, completed-work card, но strict logs
  обнаружили RID/font/texture exit leaks. До устранения не считать этот gate pass.
  Добавлен явный teardown probe/GC перед остановкой RenderingServer, повтор идёт.
- Среда пережила статус starting/ready; файлы и продолжающийся процесс сохранились.
  Физическое устройство не подключено к проверкам, adb в PATH/.tools не обнаружен.
- Реализация ещё в работе: итоговые large11/narrow/forms, полная регрессия/CI и
  final report/PR обязательны; текущие промежуточные passes не финальная приёмка.

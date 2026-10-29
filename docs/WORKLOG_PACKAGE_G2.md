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

- Опубликован первый source4ffb087 и draftPR72. Core40769557803 и platform40769557830
  запущены; их результаты ещё не финальная приёмка. Верхний report содержит WIP.
- Teardown probe не убрал3 leaked CanvasItem/10font textures. Найден реальный
  orphan: PlayerTablet всегда создаёт _serviceToggle, но не добавляет его в tree
  для старых profiles без Services. В трёх prepared non-service parties получается
  ровно3 таких объекта. Toggle теперь имеет скрытого родителя, чтобы штатно
  освобождаться; native repeat a6 проверяет эту гипотезу без подавления ошибок.

- Native a6 formsRU/formsEN и strict logs passed: скрытый parent исправил ровно3
  orphan buttons, exit RID/font/texture errors больше нет. ServiceRunProbe дальше
  выявил InvalidRoadMove при сохранении после cancel во время личной навигации.
  Ранний in-transit fixture G2 попал в состояние, не покрытое прежним F2 native.
  Исправлена проверка authenticated service navigation: прежний допустимый endpoint
  остаётся валиден при смене следующей цели на возврат. Физический путь/товар/
  денежный расчёт не менялись. Новая24th проверка включает exact continuation,
  движение до конца, затем реальный возврат/refund и replay. 24ordinary passed.
- Windows firstsource ordinary build/audit/import прошли, затем Python завершился
  до первого native case. Устранена зависимость read/write project.godot от cp1252:
  все текстовые файлы runner явно UTF-8. В CI добавлен вывод stderr при failure.
  Artifact download через доступную сеть вернул403; оригинал доступен в run artifact.
  Причину до повторного Windows gate считать гипотезой, не доказанным pass.
- Карта теперь сначала показывает само полотно, фильтры раскрываются отдельно;
  на узком окне controls не вытесняют карту. Native probe проверяет раскрытие.
- Опубликован source9cb5fc2, дерево локально совпадает. Core40771125303 и platform
  40771125313 проверяют эту версию; draftPR72 без merge. Native a7 all+prepared
  запущен на неизменных source bytes, ordinary-final fixtures получены24tests.

-29.10, Europe/Samara: core40771125303 все29jobs success, включая24ordinary и
  24Developer G2, прежние gate suites и6natural G1 starts. Windows replay/Release
  success; UI small прошёл, largeEN дошёл до restart и не нашёл English Continue:
  новый Playfield штатно начинает RU, а probe ожидала сохранённый язык. Это ошибка
  предположения проверки; язык можно выбрать обычной кнопкой перед продолжением.
- Native a8 на том же runtime прошёл small640 и strict logs. Диагностический assert
  открытия popup добавлен; предыдущий a7 filter failure в повторе не воспроизвёлся.
  Native a9 выполняет all+prepared; после завершения исправить launch-language
  предположение и проверить narrow footer bounds отдельным actual GUI assertion.
- Пользователь явно разрешил H1 после завершения G2; P77 больше не запрещён как
  следующий самостоятельный пакет. До финального report/PR G2 баланс не изменять.
- Native a9 large1100 diagnostic: rect=(34,464)/(1024,96), actual pointer=(550,360),
  popup не открыт. ParseInputEvent не синхронизировал OS pointer; probe теперь
  WarpMouse+2frames перед обычными событийными кликами. Это stage-only input,
  runtime не получает test APIs. Repeat a10 all+prepared; deadline600 прежний.
- Footer TabletPanel переходит на две строки при ширине<560, clock icon/label в
  одной строке, переключатель времени в следующей. Проверяется actual rect и
  всего планшета, и time control. EN restart явно выбирает English в launch screen.
- Финальный текущий sourcee9c82c6; Core40792820212/platform40792820234. После9cb
  изменены только TabletPanel и stage-only probe; все Core inputs9cb сохранились.

- a10 large сохранил popup failure даже после WarpMouse (pointer уже совпал512).
  Гипотеза одной лишь OS pointer синхронизации не подтвердилась полностью. Вместо
  длинного перекрывающего dropdown для11 участников сделан обычный HFlow из
  компактных toggle buttons Все/#1…#11, полный caption/tooltip и прежняя легенда.
  Проверка выбирает #11 реальным mouse input, Все — Enter. Общие меню не менялись.
- Текущий sourcecde7a49, native a11 all+prepared, Core40793629410/platform40793629397.
  Unit/financial rules прежние; два последних commits только client и probes.
- Native a11 small passed, large1100 a11 достиг map screenshot/inputs/help, но
  process180.168s/124 до конца сценария. Deadline180 не повышен; это не pass.
- Native a12 narrow360/large11 whole scenario118.434s, strict logs pass; RU forms
  50.959s, EN63.959s, strict logs pass. F2 native26.268s и strict logs pass:
  физические загрузка/движение/выгрузка/частичный платёж, diskresume, cancellation,
  реальный возврат/refund. Общий a12 gate success,679 matched inputs.
- Windowscde city-ux-ui остановился на Mono editor import shutdown120s, до UI;
  старый windows-core-replay success. Поэтому G2 runner переиспользует уже доказанный
  prepare_godot_asset_import/reuse_godot_imports путь старого platform gate: asset-only
  project, normal import exit (--quit), byte-verified96imports, ordinary audit и
  обычный Mono runtime. Timeout не принимается как pass, deadlines прежние.
- Source496fc77 — только эта правка runner послеcde, игровые bytes не изменены.
  Local a13 проверяет новый путь без готового cache; Windows повторяет весь UI.
- Asset-only fresh local a13/small640 прошёл с нормальным завершением importer,
  681 matched inputs. Windows496fc77: обаjobs success, вся6-case native matrix.
- Итоговая сверка обнаружила настоящий пробел уведомлений: PayRoadOrder оставляет
  Assignee0 для работы без принятого городского заказа; старый notice source только
  Orders.Assignee==actor пропускал такую завершённую дорогу. Теперь источник —
  реальные Contrib.Builder==actor; city commission не дублируется второй записью.
  Названия ступеней отображаются как Грунтовка/Дорога I/Магистраль II, не raw2/3.
- Расширена existing real-paver проверка: self-funded road completion→notice→read,
  hash/вклады неизменны, export road-built.24ordinary unit8 pass87.543s. Новый native
  RU/EN сценарий проходит actual notice→map→конкретная клетка→карточка с90/10 и
  городской100%, без второй награды. a14 prepared gate success,681matched inputs;
  физическийF2 native тоже прошёл. Вариант numeric wage поля в этих fixtures
  отсутствует, отдельно native wage-draft proof не заявлять.
- Source9ae94bf; Core40796389712/platform40796389702 ещё выполняются. После этой
  содержательной правки не менять source без выявленного failure; финализировать
  отчёт/PR по фактам, затем отдельная H1 ветка и исследования.

- Финал29.10: Core40796389712 все29jobs success, Windows40796389702 обаjobs success.
  Финальный20-process Windows native включает6GUI+6strict logs и bootstrap/audit;
  large11EN1100 101.297s, large11RU360 76.313s, formsRU42.157/EN39.453, physicalF2
  9.734s. Source9ae94bf; source900/financial82 unchanged. Документируются все24
  nonzero из170 local processes; Linuxwide180timeout не скрыт. G2 закрыт вsoftware
  границе, P77/P80 не закрыты. Следом явно разрешён H1 отдельной веткой/PR.

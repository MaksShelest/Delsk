# Приёмка B1/B2

Автор: Шелестов Максим Евгеньевич.24.10.2026.
Нормативные границы — PACKAGE_B_IMPLEMENTATION_GUIDE.md и отдельные полные prompts.
Статус означает программную приёмку в текущем supported subset, не будущие D/E.

| Требование | Доказательство |
|---|---|
| A2 prerequisite/default сохранён | fda7203/PR57; A1process43/A2process49; старый A2save50 exact hash |
| B1:3..10/default3, strict options, legacy5maps | OrdinaryNewGameTests, resolver и real CLI35checks |
| Независимые policies/IDs/кошельки, shared bounded Hybrid |30B1cases, ten-actor pending protocol fixture; не реальная модель |
|11участников: занятость/выборы/торговля/privacy | strict50%/majority, labor rotation,10suppliers, bilateral privacy tests |
| Menu keyboard/mouse/RU/EN/start/cancel/continue | OrdinarySetupProbe, native3/6/10, real disk load |
| B2 exact geometry/typed metadata/initial source hash | MapDesignTests, check_map_designs.py экспорт дважды, strict inputs |
|48/96lots,8/24districts,8types,15stalls/11spawns | exact fixture counts, preserved source IDs/координаты, generated C# snapshots |
| Защищённые corridor/civic/resource, городская dirt | Core ValidateMapLayout + existing BuyPlot/Build; no purchasable protected cells |
| Все11spawn→entries/stalls/public/terminal | actual GridMap BFS;9seed distance evidence, warehouse wall fixture |
| Eight actual sources/cash0 | Move/Harvest каждый public type, физическая доставка/Sell, no injected goods |
| Existing transport paths | legacy truck fixture on exact new obstacles/plots; loaded-trip restore, per-tick hash/replay |
| Runtime3/6/10 обе карты |64CLIprocess checks,180ticks; каждыйNPC commands; balances/directload/replay |
| New geometry/checkpoint provenance | MapRuntimeTests before/during move, harvest, backpack; typed config/hash/restore |
| Native maps/UI | обе×3/6/10,800×720RU/EN; civic approaches, City/voting/exchange/help, camera/minimap/lastNPC |
| Lab catalog/profile/recordings |37unit,28preset/map pairs/49actual C#arms; Chromium2×30tickexperiments, real recordings |
| Full software regression |1058ordinary/1064Developer,102exports/14replays/69pairs;1132Linux Godot hashes/35restores |

Прежние World18/Rules production-recipes-1/Session2/Envelope2: новые initial
config fields nullable/default-omitted, старые файлы не переписываются. Map adapter
version1 фиксирует интерпретацию civic entries, отсутствующих в design source;
подробности/команды/лимиты и реальные результаты — IMPLEMENTATION_REPORT_PACKAGE_B1/B2.md.

Открыто: полные будущие отрасли/производственные templates/XP/стройка C/D,
реальный автосалон/флот/дорожные вклады/налоги/доставка E, долгий баланс/HardwareFPS,
Windows/Android устройства, реальная модель. Руды не заменены wood и не обещают
ещё отсутствующие металлургические цеха. Seed rotation не доказывает экономическое
равенство мест. P61/P63 и весь vNext не объявляются завершёнными по факту карт.

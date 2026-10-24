# A2 — полное отображение B1 и границы исполнения

Автор: Шелестов Максим Евгеньевич.24.10.2026, Europe/Samara.

`config/vnext/balance.b1.json` — `delsk-vnext-balance-1`,15 typed sections +
`source_metadata` (все schema_version/status/date оригинала). Никаких JsonElement
для непрозрачных будущих разделов. A1 формат/CLI/catalog не изменены.
`extract_vnext_balance.py` — механический перенос; `--check` сравнивает точные
байты. Reader и экономические проверки только C#. B1 источник не исправлялся:
противоречий, блокирующих этот ограниченный статический анализ, не обнаружено.

Общие проверки: UTF8 JSON≤1MiB, depth32, decoded duplicate keys на любой глубине,
unknown/missing/null/type/integer/ID/enum/reference, checked Int64 sums/products.
Все обязательные поля новых DTO required; hidden defaults отсутствуют. ID regex
как A1, ordinal immutable maps/arrays. Full limits:256items/512recipes/32industries,
64vehicles/32resources/16kits/16overflow warnings, максимум5tiers/3versions/
3warehouses/3platforms/3roads/2modernizations. Это технические ограничения,
не требование именно34/23/6. Small fixture5/1/1 проходит.

Нормализация исходных пропусков: tierI price/materials/ticks=0/{}/0;
recipeV1 unlock_level=1; platformI price/materials/ticks=0/{}/0;
platformI/II service=0; у четырёх недорожных vehicle отсутствующий
`delivery_service_allowed`=true. В full-файле они явно записаны и обязательны.
Любая coins→minor ровно checked×100, не изменение цены. Иные исходные поля
сохраняются без изменения. Exact raw SHA не нормализует BOM/пробелы/переводы строк. Для исходного B1,
A1 catalog, full balance, supported profile и baseline economy закреплён LF в
.gitattributes: Windows checkout не меняет provenance; baseline JSON не редактирован.

## Runtime-проекция

`config/vnext/economy.b1-supported.json`, id `vnext-b1-supported-1`, выбирается
только через прежний `--economy`. Обычный `config/economy.json`/карты не изменены.
Adapter `VNextSupportedProfile.Create` принимает проверенный fullB1 + прежний
baseline profile, не использует экономические константы вместо JSON. Данные
источника подтверждаются source/full/A1 check до экспорта. Profile хранит
source/full/catalog/baseline SHA, version/capabilities/limitations и SHA
канонического world+npc+id payload. Изменённый payload со старой provenance
отклоняется. Редактируемый research profile должен убрать VNext metadata и
выбрать собственный id. Metadata не является криптографической подписью автора.

Активны34 ItemDefinition и23 V1 RecipeDefinition; Inputs=1..8 MaterialCost,
RequiredStaff=5, MinimumStaff=1, FullStaffTicks=T, WorkRateMultiplier=1 в smoke,
EnergyPerBatchMinor=T×service. При N=5 партия длится T, зарплата5×wage×T,
услуга service×T. На неполном N Core линейный, не staff_rate_bp B1. Услуга
начисляется floor(totalCost×done/required) с прежним целым остатком; это точная
пропорция накопленной работы, не отдельная энергия/вода. V2/V3 не исполняются.

Все восемь ResourceTypeBalance имеют настоящие item IDs, finite private/public
stock, regenerationQuantity=1 и собственный interval, purchase/lease data из B1.
Core имеет один HarvestTicksPerUnit; B1 даёт2 для всех. При разных длительностях
adapter явно откажет. Core ID ограничен50символами, catalog допускает64;
несовместимая проекция отвергается, ID не обрезаются. Техники в subset нет:
прежний equipment catalog требует map-specific nodes; B2 не должен заменять руду
или песок wood. Новый автопарк/ресурсные карты здесь не создаются.

Нет vNext production building templates: Core пока мгновенно строит из личного
инвентаря, не из bare ground70, и не имеет пятиступенчатого развития. Smoke
предоставляет готовое legacy Enterprise с5работниками и пустыми складами;
сырьё добывается/переносится обычными командами. Это подготовленная fixture,
не достижение фабрики с200 монет. Warehouse/shop и logistics/handling/movement,
labor/market/delivery/population/growth/NPC/price waves/maxTicks — явные значения
baseline в экспортированном JSON. Это не B1 хранилища/скорость/население.

Из B1 дополнительно активны start cash/personal capacity/plot price/reservation
fee+TTL/wage button step, capital period/bootstrap budget/coefficient, election
period. Demand11000bp сокращён точно до11/10 (Core numerator/denominator≤100).
Поддерживаемый профиль фиксирован на4участниках; бюджет20000 относится к ним;
иной состав получает явный отказ. Масштабирование/3–10NPC — будущий B1, не A2.

Manifest нового запуска: economyProvenance с точными source/full/catalog/baseline/
profile-file/payload SHA, active capabilities и ResolvedConfigHash. Raw profile
bytes хешируются в момент загрузки, не после прогона. ConfigHash считает прежний
resolved ScenarioDefinition; catalog SHA его не заменяет. Save/load/replay
используют сохранённый resolved config, не читают B1/profile повторно. Их новый
manifest фиксирует сохранённый ConfigHash/id; исторические source-file метаданные
не добавлялись в authoritative save и не восстанавливаются из внешнего файла.

## Семантическое покрытие и ограничения

15 строк coverage возвращает настоящий CLI. Ошибки структуры/ссылок/диапазонов,
размещения/противоречивых настроек/неподтверждённых обязательных зависимостей —
errors. Неположительная прямая маржа — warning; JSON не меняется. Scope=semantic,
`balanced=null`: нет заявления о темпе, ROI, конкуренции или аппаратной приёмке.

- Tiers1..5, XP/ёмкости/цеха монотонны,≤6клеток (2×3) и≤5цехов×5людей;
  capacity склада200/300/400 совпадает с дублирующим rules.
- Начальная стройка63/66/64/63/54/46≤70; расширение помещается в предыдущий input.
- V1/V2/V3 партии: input×множитель, output×множитель, work=ceil(T×num/den),
  точные checked числа/ёмкости доступного уровня. Проверены69партий.
- Маржа=reference output−reference inputs−5×wage×workTicks−service×workTicks.
  Полный норматив труда, без подмены валовой выручкой. Нет доставки/простоя/
  капитальных затрат/будущих счетов/налога/рыночного сбыта в этой марже.
- Граф ищет конечный свидетельствующий порядок30строек/расширений, затрачивает
  единый конечный пакет steel12/glass6/asphalt20. Только grounded recipes,
  ВСЕ входы которых восполнимы, помечают выход восполнимым. Запертая SCC не источник;
  SCC с реальным сырьевым входом/grounded recipe допускается. XP-технологический
  переход требует восполнимого доступного рецепта с положительным XP.
- Поиск не читает мир, не исполняет game economy.≤30000states; исчерпание бюджета
  отдельно GraphAnalysisLimit. Этот консервативный анализ не доказывает циклы,
  требующие временного преобразования конечных seeds между рецептами; если нет
  witness, успех не выдаётся. Доказательство для поставленного B1 реально найдено.
- Ресурсный источник восполним только при собственном resource profile. Import
  items — возобновляемый источник на неограниченном статическом горизонте с
  конечными партиями/refresh, не мгновенный бесконечный склад. На один construction
  witness берётся только ОДИН seed-пакет, без бесконечной стали/стекла. Нет проверки
  импортного расписания, цены конкуренции или fairness всех стартов одновременно.
- Outfits/modernizations/platforms/kits/roads/warehouses: поля/refs/checked цены,
  наборы материалов восполнимы. Установка, vehicle compatibility, traffic/ROI,
  услуги/счета будущих механизмов **не** заявлены семантически исполненными.

Критический старт: сталь12/стекло6 на пакет4участников, refresh1200, потолок2пакета
(§16); импорт125%, цены steel3125/glass4250minor. Ресурсы можно добыть без завода.
Первая лесопилка: земля10000+работы17000+30wood по500+3steel по3125=51375minor.
Собственная добыча30wood оставляет cash need36375, против start20000 дефицит16375:
33wood по справочной spot500 дают16500. Это статическая оценка, не длительность
и не гарантия реальной цены/объёма заказа; публичные источник и spot recovery
не требуют фабрики/лавки/залогового будущего договора. Финансовое восстановление
с cash0 отдельно выполнено через реальную добычу/spot-команду в unit test.

## Legacy mapping

| Сейчас | vNext значение | Действие A2 |
| --- | --- | --- |
| wood/grain/plank/bread/milk/beet/cheese/sugar/pastry | Те же9 ItemId | ID сохранены; новые цены/объёмы только при явном профиле |
| sawmill | woodworking | Отрасль, не переименование старого здания |
| bakery/sugar-factory/confectionery | food | Отрасль, не слияние старых корпусов/цехов |
| dairy | dairy | Отрасль, не смена RecipeId |
| warehouse/shop | инфраструктура | Не6/7 отрасль переработки |
| старые RecipeId/pinned WIP | прежние recipe/ingredients | Не переписываются новым каталогом; load/replay сохраняют config |

## Поле источника → контракт/единица/потребитель

В таблице `[]` означает все записи, `{id}` — все ключи соответствующей карты;
обобщения не пропускают дополнительные поля. Source SHA/source_metadata — выше.
Числа без отдельной денежной/временной/ratio единицы — целые количества/уровни/
объём; text/id/name/source — строки, delivery_service_allowed — bool.

| Source path | Единица / перенос | DTO / проверка | Runtime или следующий пакет |
| --- | --- | --- | --- |
| `rules.tick_seconds` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.money_minor_per_coin` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.min_bots` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.max_bots` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.default_bots` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.start_cash_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | World.StartingCashMinor |
| `rules.cell_price_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | World.PlotPriceMinor |
| `rules.reservation_extra_bp` | bp / integer,10000=1 | VNextRules; strict field/type/domain/reference/checked | World.Rules.ReservationFeeBasisPoints |
| `rules.reservation_ttl` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | World.LandReservationTicks |
| `rules.personal_capacity` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | World.PersonalInventoryCapacity |
| `rules.bare_cell_capacity` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.warehouse_capacities[]` | целое / без смены | VNextRules; integer/range | metadata; C–F (см. активные исключения) |
| `rules.staff_per_workshop` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | RecipeDefinition.RequiredStaff |
| `rules.staff_rate_bp[]` | bp / integer,10000=1 | VNextRules; integer/range | metadata; C–F (см. активные исключения) |
| `rules.wage_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.wage_button_step_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | World.Rules.PlayerWageStepMinor |
| `rules.demolition_recovery_bp` | bp / integer,10000=1 | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.demolition_cell_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.demolition_extra_cell_level_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.overflow_grace_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.overflow_warnings_remaining[]` | целое / без смены | VNextRules; integer/range | metadata; C–F (см. активные исключения) |
| `rules.delivery_accept_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_deadline_min_buffer` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_deadline_buffer_bp` | bp / integer,10000=1 | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_min_fee_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_fee_volume_block` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_distance_block` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_fixed_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.delivery_block_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.transport_invoice_period` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.invoice_grace` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.penalty_period` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.penalty_bp` | bp / integer,10000=1 | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.city_private_road_share_bp` | bp / integer,10000=1 | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.road_speed_caps[]` | целое / без смены | VNextRules; integer/range | metadata; C–F (см. активные исключения) |
| `rules.road_increment_light_minor` | minor / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.shop_update_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.shop_min_display_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.election_period` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | World.Elections.PeriodTicks |
| `rules.road_exit_hold_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.road_stop_warning_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.road_stop_release_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.autosave_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.quest_refresh_ticks` | целые тики / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.quest_active_limit` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.base_speed_units_per_cell_tick` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.offroad_vehicle_speed` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `rules.offroad_person_speed` | целое / без смены | VNextRules; strict field/type/domain/reference/checked | metadata; C–F (см. активные исключения) |
| `items[].id` | строка/ID/enum / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `items[].name` | строка/ID/enum / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `items[].unit_volume` | целое / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `items[].reference_price_minor` | minor / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `items[].xp_per_unit` | целое / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `items[].source` | строка/ID/enum / без смены | VNextCatalogItem; strict field/type/domain/reference/checked | ItemDefinition; XP/source → D |
| `recipes[].id` | строка/ID/enum / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].industry` | строка/ID/enum / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].unlock_level` | целое / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].inputs.{itemId}` | целое / без смены | VNextCatalogRecipe; quantity/ref/checked | V1 executor; industry/unlock → D |
| `recipes[].output` | строка/ID/enum / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].output_quantity` | целое / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].work_ticks_full_staff` | целые тики / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `recipes[].service_minor_per_work_tick` | minor / без смены | VNextCatalogRecipe; strict field/type/domain/reference/checked | V1 executor; industry/unlock → D |
| `vehicles[].id` | строка/ID/enum / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].name` | строка/ID/enum / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].speed` | целое / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].capacity_volume` | целое / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].price_minor` | minor / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].handling_max` | целое / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].class_factor_bp` | bp / integer,10000=1 | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `vehicles[].delivery_service_allowed` | bool / без смены | VNextVehicle; strict field/type/domain/reference/checked | metadata E |
| `industries[].id` | строка/ID/enum / без смены | VNextCatalogIndustry; strict field/type/domain/reference/checked | metadata D |
| `industries[].name` | строка/ID/enum / без смены | VNextCatalogIndustry; strict field/type/domain/reference/checked | metadata D |
| `industries[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextCatalogIndustry; strict field/type/domain/reference/checked | metadata D |
| `industries[].materials.{itemId}` | целое / без смены | VNextCatalogIndustry; quantity/ref/checked | metadata D |
| `enterprise_tiers[].level` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].xp` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].cells` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].workshops` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].input_capacity` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].output_capacity` | целое / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `enterprise_tiers[].materials.{itemId}` | целое / без смены | VNextEnterpriseTier; quantity/ref/checked | metadata D |
| `enterprise_tiers[].ticks` | целые тики / без смены | VNextEnterpriseTier; strict field/type/domain/reference/checked | metadata D |
| `warehouses[].level` | целое / без смены | VNextWarehouse; strict field/type/domain/reference/checked | metadata C |
| `warehouses[].capacity` | целое / без смены | VNextWarehouse; strict field/type/domain/reference/checked | metadata C |
| `warehouses[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextWarehouse; strict field/type/domain/reference/checked | metadata C |
| `warehouses[].materials.{itemId}` | целое / без смены | VNextWarehouse; quantity/ref/checked | metadata C |
| `warehouses[].ticks` | целые тики / без смены | VNextWarehouse; strict field/type/domain/reference/checked | metadata C |
| `recipe_versions[].version` | целое / без смены | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `recipe_versions[].input_multiplier` | точный integer/rational num/den | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `recipe_versions[].output_multiplier` | точный integer/rational num/den | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `recipe_versions[].work_multiplier_num` | точный integer/rational num/den | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `recipe_versions[].work_multiplier_den` | точный integer/rational num/den | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `recipe_versions[].unlock_level` | целое / без смены | VNextRecipeVersion; strict field/type/domain/reference/checked | metadata D |
| `resources.{id}.name` | строка/ID/enum / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `resources.{id}.private_stock_per_cell` | целое / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `resources.{id}.regen_one_every_ticks` | целые тики / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `resources.{id}.manual_ticks` | целые тики / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `resources.{id}.public_stock_per_cell` | целое / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `resources.{id}.public_regen_one_every_ticks` | целые тики / без смены | VNextResource; strict field/type/domain/reference/checked | ResourceTypeBalance/ResourceSiteRules; uniform manual ticks |
| `additional_workshop_outfits.{id}.coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextOutfit; strict field/type/domain/reference/checked | metadata D |
| `additional_workshop_outfits.{id}.materials.{itemId}` | целое / без смены | VNextOutfit; quantity/ref/checked | metadata D |
| `workshop_recipe_modernization[].version` | целое / без смены | VNextModernization; strict field/type/domain/reference/checked | metadata D |
| `workshop_recipe_modernization[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextModernization; strict field/type/domain/reference/checked | metadata D |
| `workshop_recipe_modernization[].materials.{itemId}` | целое / без смены | VNextModernization; quantity/ref/checked | metadata D |
| `workshop_recipe_modernization[].ticks` | целые тики / без смены | VNextModernization; strict field/type/domain/reference/checked | metadata D |
| `loading_platforms[].level` | целое / без смены | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `loading_platforms[].throughput` | целое / без смены | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `loading_platforms[].requires_staff` | целое / без смены | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `loading_platforms[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `loading_platforms[].materials.{itemId}` | целое / без смены | VNextLoadingPlatform; quantity/ref/checked | metadata E/F |
| `loading_platforms[].ticks` | целые тики / без смены | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `loading_platforms[].service_minor_per_active_tick` | minor / без смены | VNextLoadingPlatform; strict field/type/domain/reference/checked | metadata E/F |
| `vehicle_handling_kits[].id` | строка/ID/enum / без смены | VNextHandlingKit; strict field/type/domain/reference/checked | metadata E |
| `vehicle_handling_kits[].throughput` | целое / без смены | VNextHandlingKit; strict field/type/domain/reference/checked | metadata E |
| `vehicle_handling_kits[].requires_staff` | целое / без смены | VNextHandlingKit; strict field/type/domain/reference/checked | metadata E |
| `vehicle_handling_kits[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextHandlingKit; strict field/type/domain/reference/checked | metadata E |
| `vehicle_handling_kits[].materials.{itemId}` | целое / без смены | VNextHandlingKit; quantity/ref/checked | metadata E |
| `road_projects[].level` | целое / без смены | VNextRoadProject; strict field/type/domain/reference/checked | metadata E |
| `road_projects[].materials.{itemId}` | целое / без смены | VNextRoadProject; quantity/ref/checked | metadata E |
| `road_projects[].coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextRoadProject; strict field/type/domain/reference/checked | metadata E |
| `road_projects[].ticks` | целые тики / без смены | VNextRoadProject; strict field/type/domain/reference/checked | metadata E |
| `economy_globals.start_treasury_minor_per_4_participants` | minor / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.start_population_per_participant` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.max_population_per_participant` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.population_growth_ticks` | целые тики / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.resource_cell_coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextEconomyGlobals; strict field/type/domain/reference/checked | ResourceSiteRules.PurchasePriceMinor |
| `economy_globals.resource_lease_coins` | coins → price_minor (industries: construction_price_minor), checked×100 | VNextEconomyGlobals; strict field/type/domain/reference/checked | ResourceSiteRules.LeasePriceMinor |
| `economy_globals.resource_lease_ticks` | целые тики / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | ResourceSiteRules.LeaseTicks |
| `economy_globals.import_markup_bp` | bp / integer,10000=1 | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.import_refresh_ticks` | целые тики / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.import_stock_per_additive_per_4_participants` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.bootstrap_steel` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.bootstrap_glass` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.bootstrap_asphalt` | целое / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.bootstrap_asphalt_refresh_ticks` | целые тики / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | metadata B–F (см. активные исключения) |
| `economy_globals.capital_demand_period` | целые тики / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | World.CapitalDemand.PeriodTicks |
| `economy_globals.capital_bootstrap_budget_minor_per_4_participants` | minor / без смены | VNextEconomyGlobals; strict field/type/domain/reference/checked | World.CapitalDemand.BootstrapBaseBudgetMinor (fixed4) |
| `economy_globals.capital_demand_coefficient_bp` | bp / integer,10000=1 | VNextEconomyGlobals; strict field/type/domain/reference/checked | World.CapitalDemand exact reduced numerator/denominator |

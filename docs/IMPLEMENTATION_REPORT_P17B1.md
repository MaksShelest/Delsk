# P17b1 — улучшение транспорта

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.

Основа: merge P17a `197ac3a`, отдельная ветка `feat/logistics-upgrade-p17b1`.

## Объём

Первая ограниченная часть P17b: команда UpgradeLogistics, второй уровень системы, атомарная оплата денег и локальных материалов, сохранение/валидация уровня, повышенные cargo/crew limits и полный физический рейс улучшенного транспорта. Очереди/общие направления/полнота независимых stops/controlled late-return/crew-perturbation остаются обязательными следующими частями P17b. P17 целиком не закрыт.

## Технические решения

- Сценарий явно задаёт nullable LogisticsSettings.Upgrade. Null означает, что сценарий содержит только базовую систему; никаких требований к несуществующим товарам в старых каталожных fixtures. В logistics-upgrade.json сохранены исходные допущения: цена30000minor +10plank, cargo20 вместо10, min/capacity crew3 вместо2, прежняя скорость1cell/tick и ставка группы.
- Только владелец, свободный и физически у предприятия, может улучшить выключенную систему без начатого рейса. Производство управляется отдельно. Деньги идут в казну, свободные материалы берутся из Input, затем Output именно этого предприятия. Нельзя потратить зарезервированный товар/личный инвентарь; вся transaction откатывается при отказе. ConstructionInput и события учитывают необратимые материалы.
- Сохраняются ID системы/груза/группы, контракт, зарплата, текущая численность. Меняются только Level, cargo capacity и числовые рабочие места. Найм остаётся общим/постепенным; минимум3 проверяется при отправлении, покупка/upgrade не создают людей. Проверяются переполнения резервирования рейса с текущей и pending зарплатой.
- ImmutableArray<MaterialCost> сохраняет immutable controller allowlist. Чужие private routes/cargo по-прежнему скрыты. Характеристики уровня применяются к planning, payroll/readiness и checkpoint validation.
- World6/Rules logistics-2, Session2/Envelope1. World1–5 отклоняются явно, миграции нет; прежние saves не удалять, открывать прежним кодом. Godot автоматически использует отдельное имя save по RulesVersion.

## Проверки

**CI исходного кода 0fdd7e05 завершён успешно:** [Core](https://github.com/MaksShelest/Delsk/actions/runs/38736700071), [Godot/Windows](https://github.com/MaksShelest/Delsk/actions/runs/38736700069). Locked restore/Release build прошли; TRX: **308 executed/passed,0 failed,0 skipped**,27 новых случаев. Все12 сценариев и9 save/load пар прошли; после скачивания artifacts локально независимо проверены30 exports и18 continuous-vs-saved/loaded сравнений13 файлов. Windows actual log подтверждает62 checkpoints и load12/34/55/60. Старые platform hashes сохранились: basic fixture не содержит LogisticsSettings и систем, поэтому новые поля не изменили этот hash. Перегенерация не потребовалась.

Полный новый цикл реально занял176 ticks: upgrade166, prepare168, load169, receipt172, empty return175, final176. Cargo20 и minCrew3 использованы настоящим roundtrip; одновременные выплаты не дублируются. Компактные результаты и exit codes — [checks.json](verification/p17b1/checks.json). Полные журналы/выгрузки — artifacts Core CI. Следующий коммит меняет только документы/evidence, не исходники.

В этом новом Work-окружении dotnet/Godot отсутствуют. Закреплённый SDK10.0.401 не скачался с двух официальных адресов: proxy CONNECT timeout10/15s. Повторные установки/смена toolchain не выполнялись. Публикация использует GitHub connector: обычный git push dry-run не авторизован. Не заявляется локальный build/test/Godot pass.

Локально выполнены Python syntax check и git diff --check. Добавлены meaningful xUnit для financial/item/population conservation, upgrade source/rollback, access/location/max/dedup, unsafe state, minimum crew, overflow, unknown/inconsistent level и exact load на границах upgrade/outbound/return. Новый CLI scenario включён в CI и headless regression.

Подтверждённые независимым расчётом и фактическим запуском итоги нового полного цикла:60wood→30plank,10plank на upgrade,20 доставлено одним рейсом. Supplier105050/buyer40000/Treasury53000/WagesSink1950=200000minor; logistics payroll450, production payroll1500. Все итоговые суммы совпали с заранее заданными независимыми числовыми ожиданиями.

## Локальный запуск после проверки CI

```powershell
dotnet restore Delsk.sln --locked-mode
dotnet build Delsk.sln -c Release --no-restore
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/logistics-upgrade.json --script logistics-upgrade --output artifacts/logistics-upgrade
```

Выгрузки summary.json/scenario.json/logistics.json/labor.csv/ledger.jsonl показывают уровень, штат, расходы, товар и состояние рейса. Для автоматической полной регрессии: `python scripts/check_headless.py --output artifacts/headless-p17b1`.

Android device gate остаётся открыт. Новые APK/EXE, реальный телефон, реальные модели/научные серии не проверялись. Следующий ограниченный шаг — P17b2: исходящая доставка в собственный неподвижный склад/лавочку до конечного заполнения, с реальными reserve/путём/возвратом/save-load; весь месячный план сохраняется.

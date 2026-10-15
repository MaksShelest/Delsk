# P17b2 — отправка продукции в собственный склад/лавочку

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.
Основа: merge P17b1 `2e7e9e8`, ветка `feat/logistics-own-output-p17b2`.

## Объём

Одно исходящее назначение системы: собственный неподвижный Warehouse/Shop.Input, режим UntilFull. Используются тот же slot, числовая EmploymentGroup, физический roundtrip и полный payroll budget. Контрактный маршрут и upgrade P17b1 сохранены. Incoming, общая очередь/priorities, следующий production destination, полнота independent stops и controlled late-return/crew-perturbation gates остаются обязательными частями P17b. Full P17 не закрыт.

## Реализация

SetLogisticsStorage принимает только system ID/destination inventory/enabled; товар берётся из рецепта предприятия, количество из free output/cargo volume/free destination space. Ручные item/quantity/contract отклоняются. Назначение только владельцу, только свой Warehouse/Shop.Input; personal/managed cargo/production input/чужое/неизвестное отклоняются одинаковым NotAvailable. Команда remote management, как SetLogisticsContract. Переназначать начатый рейс нельзя; повтор или stop/resume того же назначения не дублирует его.

Prepare phase4 атомарно резервирует source stock, destination capacity и верхний payroll. На следующем phase2 source goods переходят в exclusive cargo; освободившееся место source резервируется под возврат. Реальный путь и empty return прежние. Приёмка phase3 превращает destination hold в stock и освобождает source return place. Это внутреннее перемещение без выручки, escrow, контракта или эмиссии; зарплата начисляется за реальные рабочие тики. Enabled=false запрещает новые trips, начатый оплачивается/заканчивается. При полном складе — NoDestinationSpace/неоплачиваемое ожидание и постепенный уход; после освобождения места маршрут сам восстанавливает готовность/найм и возобновляется.

Destination сохранён на системе и отдельно в immutable Trip. Контракт и своё назначение взаимоисключающие. Validator проверяет владельца/kind/path endpoints, stock/cargo/return/payroll/destination holds и reverse references; cargo/численность не копируются. World7/Rules logistics-3, Session2/Envelope1; World1–6 отклоняются явно без миграции, старые saves/ветки не удалять. Godot path по RulesVersion отдельный.

## Проверки

Статус на момент подготовки: CI ожидается. В Work по-прежнему нет локального SDK/Godot, предыдущая ограниченная проверка загрузки SDK завершилась proxy timeout; установку не повторяли. Не заявляется локальный build/test/device pass. Локально Python syntax/git diff --check.

Новый fixture own-storage-cycle:60wood→30plank; два real trips10+5 в собственный склад15, source остаётся15, idle wages0. Заранее рассчитанные ожидания: supplier74900/другой участник100000/Treasury23000/WagesSink2100=200000, transport payroll600/production payroll1500; никаких trade receipts или внешних потоков.

Добавлен31 test case: full cycle/stock/money/population, reserved destination vs manual fill, partial cargo/volume, stock restart, stop/in-flight/paused save, own warehouse/shop permissions, retarget/duplicate, payroll boundary cash299/300, NoPath, corruption/reverse holds, privacy и old schema rejection. CI/headless расширены до13 scenarios/10checkpoint pairs. Фактические результаты будут записаны после выполнения.

## Запуск

```powershell
dotnet restore Delsk.sln --locked-mode
dotnet build Delsk.sln -c Release --no-restore
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-storage-cycle.json --script own-storage --output artifacts/own-storage
```

Смотреть summary.json/logistics.json/final-state.json/labor.csv/ledger.jsonl. `python scripts/check_headless.py --output artifacts/headless-p17b2` проверяет всю реализованную регрессию; Python не исполняет игровые правила.

Следующий ограниченный шаг: входящая поставка из своего склада на предприятие с независимым stop, общим transport slot/числовой группой и реальными reserves/путём/save-load. Общая queue/fairness/full late-return остаются в P17b, весь месячный объём сохранён. APK/EXE/Android device/реальные модели/диссертационные серии не запускались, device gate открыт.

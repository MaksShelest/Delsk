# Доставка продукта в следующее собственное производство — часть P17

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.

## Исходное состояние и выбранная граница

Начало от `170654bb24c8195cdaf0289db6e232a5fa60793a`, ветка `work`, чистое рабочее дерево. Remote HEAD и `origin/feat/economic-cycle-p00-p05` указывали на тот же commit. Прочитаны AGENTS, полная спецификация0.3, ARCHITECTURE, DECISIONS и IMPLEMENTATION_PLAN; история содержит предыдущие PR логистики. Исходная Release build и393/393 теста прошли локально.

Фактический код уже содержит покупку/upgrade, контрактные рейсы, Warehouse/Shop UntilFull, входящие поставки и поздний физический возврат. `IsOwnLogisticsStorage` исключал вход следующего производства. Выбрана только эта следующая часть P17 из конца IMPLEMENTATION_PLAN. Более ранний аппаратный gate P06 остаётся открыт: в этой среде нет реального телефона. Общая очередь, nearest deadline/fairness и оставшиеся сценарии contract quota не объявляются выполненными.

Рабочая ветка: `feat/logistics-own-production`.

## Реализация

- `SetLogisticsProduction(Logistics,Destination,Enabled)` — remote phase1, только input другого собственного Enterprise. Чужой/несуществующий объект, свой input, output, cargo, carrier и Warehouse/Shop этим действием не принимаются. Warehouse/Shop сохраняют прежнюю команду.
- При включении текущий output item источника должен совпадать с input item рецепта получателя. Quantity и item выводит ядро; ручные quantity/item/contract отклоняются. Production target использует прежний `OwnDestination`, без нового транспорта, группы занятости, договора или escrow.
- Планирование использует прежние UntilFull/cargo/UnitVolume, exclusive stock, destination capacity, return place и full payroll hold. Receipt в phase3 перемещает cargo во вход и освобождает обратное место; никакой выручки/эмиссии. Пустая машина возвращается физически и получает последний рабочий платёж.
- Остановка исходящего маршрута не отменяет начатый рейс; производство и входящие поставки сохраняют независимые настройки. Переназначение занятого output запрещено. Заполненный вход ждёт без зарплаты, освобождение места восстанавливает готовность и наём.
- Смена рецепта влияет на следующий рейс. Подготовленный груз, его товар, приёмное место и оплата остаются прежними даже после изменения рецепта получателя. Новые несовместимые рейсы ждут с `IncompatibleRecipe`; выключение маршрута разрешено и при несовместимом рецепте.
- Структурный validator/load допускает только собственный правильный input и сохраняет прежние проверки пути, времени, cargo, обратного владения резервами и payroll. Намеренная несовместимость текущего рецепта не делает законный committed trip повреждённым.

RulesVersion=`logistics-6`; форма DTO прежняя: World8/Session2/Envelope1. Старые Rules и World1–7 отклоняются без миграции. Старые файлы не перезаписываются; Godot использует путь с новой RulesVersion. Own projections доступны только владельцу; исследовательские экспорты остаются полными.

## Проверяемый сценарий

`scenarios/own-production-cycle.json` и `--script own-production` — явно scripted fixture: два заранее подготовленных собственных предприятия, начальный штат0, настоящий наём, population6, step1/mobility1 для короткого контроля. Дополнительный рецепт plank→crate служит проверке последовательной переработки, не меняет штатный баланс или исходную спецификацию.

60 вручную добытых wood →30 plank. Остановленный downstream получает10+5 и ждёт заполнения. Output stop запрещает новую доставку, downstream самостоятельно нанимает группу и расходует14 plank→7 crate. После его остановки output resume перевозит10+4. Финал tick212: input15 plank, исходный output1 plank, downstream output7 crate,4 completed trips, без активного груза/резервов.

Один рейс на3 клетки туда и обратно требует6 оплаченных интервалов:2×25×6=300minor. Четыре рейса=1200; полный WagesSink3050, Treasury23350, cash владельца73600 и второго участника100000, сумма200000. Внутренняя доставка не создаёт продаж, escrow или внешних денег.

## Проверки и воспроизведение

Все процессы запускались через `scripts/run_check.py` с hard timeout, отдельными stdout/stderr и metadata/exit code. Среда: Linux, SDK10.0.401/runtime10.0.12; Godot .NET4.7.2. Использован уже установленный toolchain и NuGet cache, переустановки нет. Первые неуспешные команды restore/build без `DOTNET_CLI_HOME` столкнулись с read-only `/home/agent/.dotnet`; после указания существующих `/workspace/.tools/dotnet-home` и NuGet cache исходные проверки прошли. Это ошибка запуска среды, не test pass.

27 новых xUnit проверяют конкретные количества/деньги, движение до приёмки, incoming capacity против ручного Transfer, права/тип назначения/recipe, stop и full wait/resume, UnitVolume, запрет retarget/dedup, конкуренцию двух систем за один конечный input, сохранение после изменения рецепта, dangerous checkpoint cuts и отказ повреждённых refs/holds/старых Rules. Прежние393 теста сохранены.

```sh
python scripts/check_headless.py --output artifacts/headless-own-production
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-production-cycle.json --script own-production --output artifacts/own-production
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/own-production-cycle.json --script own-production --checkpoint artifacts/own-production.save.json --checkpoint-at 170 --output artifacts/own-production-saved
dotnet run --project src/Research.Runner -c Release --no-build -- --load artifacts/own-production.save.json --output artifacts/own-production-loaded
```

Фактический итог локальной проверки:

| Проверка | Результат |
| --- | --- |
| Locked restore / Release build | exit0,0 warnings/errors |
| xUnit |420/420 passed,0 failed/skipped;27 новых |
| Runner |15 обычных сценариев и test-only late-return fixture прошли |
| Direct load |12 обычных пар +1 controlled pair;13 файлов на сравнение,25 побайтных сравнений |
| Независимый Python verifier |42 выгрузки: money/item/capacity/population/settlements, exit0 |
| Godot Linux |client restore/build, import/smoke/strict verify exit0;62 hashes, restore12/34/55/60 |
| Спецификация |SHA-256 b86447e9311ab676538e1edbf7fd217c5a09896c3fc0d5edda7569ae088e9376 прежний |

Первый Godot import/smoke вернул0 и report passed, но строгий verifier корректно отклонил ошибки сокетов sandbox. Повтор с доступом к локальным сокетам прошёл без этих ошибок. Platform fixture после явной перегенерации совпала с прежней: старые денежные/товарные ожидания и62 хеша не изменились. Windows и Android/device в этой итерации локально не запускались; предыдущие отчёты не являются новой проверкой.

Точные результаты и компактные metadata — `docs/verification/own-production/checks.json`. Полные крупные журналы и выгрузки не коммитятся. CI расширен новым runner/save/load/independent verification и minimum420 tests.

Следующий ограниченный шаг P17: общая сохраняемая очередь заданий одного transport slot с приоритетом ближайшего contract deadline, input shortage и освобождения output, воспроизводимым равенством; проверить pause/load очереди. P18, политика, LLM и игровой UI в этой итерации не добавлены. Android device gate остаётся открыт.

# G1 — NPC/P76

Автор: Шелестов Максим Евгеньевич. 28.10.2026, Europe/Samara.

Основа0511375 — merge PR70 с полным F2/6e7d43c и проверенным96ebac6.
Ветка feat/vnext-g1-npc-economy. Чистое дерево; .NET10.0.401/Godot4.7.2 имеются.
Явное задание разрешает G1 вопреки историческому стопу. По содержанию это P76:
P75 в текущем плане — карта города и сюда не включается. Балансовый P77 не начинать.

Исходная политика уже умеет harvest/sell, production, wages, workshops, quests,
imports, development, own logistics, roads и carrier-side F1/F2. Проверить/исправить
связность, полную стоимость инвестиций, overflow, account и customer-side services.
Новое opt-in поведение с сохраняемой памятью; старые контроллеры/профили не подменять.
Игрок/NPC — одни права и команды, only immutable own/public observation.

Первый естественный baseline: small-river/3 Medium/seed42/1800 ticks,55.269s,
успешное завершение,0 buildings/0 groups,4 BuyImport, cash NPC13776/13735/4797
при стартовых20000. Это наблюдение, не готовая причина и не сравнительный результат.
Продолжить baseline матрицу и отделить её от prepared fixtures семейств действий.

Новая политика: полный строительный бюджет до импорта; whole-trip заработок,
многопредприятийное снабжение/найм/сбыт, own overflow, полезные оплаченные quotes,
бюджетная добывающая техника, funded road inputs, bounded failed-intent memory,
Hard executable long intent вне короткого forecast. Core — только новый allowlist
и достоверная оценка пешего времени; финансовые/производственные правила F2 прежние.

Попытки build1/2 (ошибки dispatch), build5 (internal hash helper) сохранены.
Unit1–6: исправлены оценка пешего пути/объёма получателя, некорректные prepared
fixtures (добыча производственных компонентов делала её выгоднее производства),
assert названия wage ledger и ссылочное равенство ImmutableArray. Unit7:16/16.
Unit8:18/19, workshop fixture не имел реального спроса. Unit9:18/19, статический
столичный пункт попал на частный footprint; перенесён на свободную клетку.
Отказы политики не скрывать снятием ограничений валидатора.

Pilot6000 завершился ошибкой MaxTicks5000 (213.356s), не timeout. Pilot4500 успешно
191.449s:0 предприятий, cash35083/35638/34708,0 rejected;314 EarnConstructionReserve,
118 SellCarried,51 Harvest/61 actual sale. Старт20000. Это ещё не доказательство
промышленного развития. Для длинного исследования можно увеличить только MaxTicks
копии сценария, отдельно обозначив это; никакие деньги/ресурсы не выдаются.

Python Checks28 прошли. Первые Lab37:7 ошибок localhost sandbox; повтор с network
permission37/37,4.416s. Local SDK/env прежние. Новый NPC CI отделён от прежних gates:
ordinary, Developer, prepared actions, natural2maps×3counts (Medium,seed42,t1800).

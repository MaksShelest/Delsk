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

Дополнительные фактические дефекты и проверки:
- wide/10/t900: CheckpointTooLarge при неизменных лимитах64MiB/512MiB.
  Для G1 добавлен typed field pool immutable observation history, session schema3;
  старые1/2 читаются, old non-G1 writer остаётся2. Полная история не отбрасывается.
 3 новых archive tests, references/layout corruption rejected. Unit16:27/27.
  wide/10/t1800 теперь save/load/replay/finance/privacy succeeded, checkpoint1.7MiB.
- Сборщик статистики держал1.1GiB решений как объекты, ~6.4GiB RAM; прошёл653.854s,
  затем заменён потоковым. Повтор только анализа14.232s; семь игровых checks уже0.
- Независимый public audit ранее не учитывал additional outfit/WIP/монтаж: исправлен
  по экспортированным workshops, без изменения Core. Prepared14 cases/28exports,
 46 processes,177.170/780s: все audits/continuations/profile reproduction passed.
  Developer27/27,220.996s. Windows CI131 success; более поздний полный CI ожидается.
- Long12000: первая попытка меняла template MaxTicks, profile Apply возвращал5000.
  После разрешения initial profile/setup MaxTicks изменяется только в resolved copy.
  Следующая попытка получила900s timeout, сохранённый checkpoint6000; не повышать
  уже объявленный deadline и не объявлять этот run успешным.
- Funded prepared start100000 у NPC2 (остальные20000): сначала строил завод+магазин,
  но0 batches/NoStaff. Причина: объявленная покупка/не выигранная ставка ошибочно
  вычитались как обеспеченное поступление. G1 теперь считает только paid claims и
  принятые contracts, обслуживает primary перед следующей инвестицией и перепроверяет
  оставшийся план. В paid services исключён параллельный manual pickup/delivery;
  готовая физическая F2 выполняет купленную перевозку оплаченной чужой партии.
- Generic legacy extractor purchase отключён для G1 в пользу budgeted variant.
  Regression обнаружил legacy primary WorkshopId0: старое Resume не проверяло его
  прибыльность и спорило с Pause. Исправлено; bounded recovery scenario прошёл.
- Funded start после supply fix:1 batch, Wages1440,0 rejects. Малый остаток и короткое
  столичное предложение ещё мешали сбыту. Добавлен real transfer+walk к бирже и
  bounded300 active-tick ожидание публичного спроса, с checkpoint memory и без
  прогноза будущих offers. Prepared short-offer test: продажа и второй batch;5/5
  production tests,53.425s. Итоговый набор30 и повтор funded start выполняются.

- Финальная проверка ожидания обнаружила преждевременный сброс MarketItem до
  погрузки StageOutputAtExchange. Ожидание теперь начинается после исполнения
  плана. Fixture короткого спроса явно задаёт и PeriodTicks, и OrderTtlTicks=20
  (одна смена периода не меняла прежний TTL180). Проверяется capture/restore
  при ненулевом marketWaitUntil. Дополнительный assert прежнего source-output
  оказался слишком узким: NPC законно перевёз выпуск собственной логистикой
  к терминалу. Проверяется фактическая погрузка plank в carrier и продажа.
- Неудачные production29/30/31 и build32 сохранены. Build32 поймал опечатку
  имени Participant.InventoryId в новом assert; она исправлена. Последующий
  production32 после неудачного build исполнял старую assembly и не является
  проверкой исправленного исходника. Новый build/test выполняются отдельно.

- Финальный runtime/test/CI8da2218958ad02b6dd74b7abb4a02294eaf11076:
  production33 5/5,50.587s; Release build21.494s. Funded старт100000 только NPC2:
  t4500,3batches,9planks/8482minor,4320wages,0rejects;195.168s/300.
  На checkpoint2940 память marketItem=plank/marketWaitUntil3224, уже SellCarried;
  load88.115s/300, exact0.968s, accounting1.469s/public0.366s. В конце производство
  paused по спросу,0staff; это не естественная калибровка.
- Native на том же source:659matched runtime/assets/config inputs, изолированный
  существующий OrdinarySetupProbe с G1/v4 assertions. small-river6 30.019s,
  large-districts10 37.854s; реальные RU/EN/keyboard/mouse, старт/cancel/diskresume.
  Final large10 screenshot просмотрен; обычный HUD/карта и последний NPC. Единственный
  runtime stderr — software VSync warning. Count3 старого probe ждёт до-E1 dealer
  info и не заявлен как новый civic UI gate. Probe не добавлен в shipped client.
- Core40722536198 все27jobs success иWindows40722536200 success на8da2218,
  включая неизменённые старые gates,30/30 G1,17prepared cases/34exports/17exactpairs
  и повтор всей natural матрицы2maps×3/6/10 Medium/42/t1800/cut900. Natural показатели
  совпали с прошлым source, предприятий ещё0. Windows Debug/Release metadata,
  62/40/120 checkpoint states и20nativeHUD/God controls passed.
- Локальный дополнительный unit34/actions34 был подготовлен, но не запускался:
  после успешного обязательного полного final-source CI повтор без нового риска
  не нужен. Локальные подробные30-case counters до последнего market guard имеют
  provenance bcf36b1; изменённый5-case production partition прошёл на8da2218,
  полный тот же30-case suite прошёл на8da2218 в CI. Evidence не подменяет provenance.
- Финальные документы фиксируют884source inputs/82financial unchanged и52nonzero
  bounded check records. Исторический900s timeout не превращён в success.
  G1/P76 software завершён; P75/P77/G2 не начинались. PR71 без auto merge.

# Codex: D3 — полный контент, модернизации и режим бога

Запускать после проверенных C1/C2/D1/D2. Скопируйте блок полностью. Это завершение производственного пакета D; автопарк/дороги/услуги остаются E/F.

```text
Реализуй в MaksShelest/Delsk только D3: работающий новый производственный
контент шести отраслей, ограниченный импорт/bootstrap, локальные V2/V3,
Developer XP и немедленное Apply в открытом окне режима бога.

1. Прочитай актуальные AGENTS/архитектуру/решения, весь
   docs/vnext/PACKAGE_CD_IMPLEMENTATION_GUIDE.md, спецификацию0.6 §5–10/18
   и Developer правила, план/typed баланс/CONTENT_TABLES/mapping A2,
   реальные отчёты A2/B1/B2/C1/C2/D1/D2. Проверь HEAD/status/diff/remote/PR.
   D2 должен реально иметь XP/quests/expansion/assembly и C2 ledger/salvage;
   только документов недостаточно. Основная ветка feat/economic-cycle-p00-p05.
   Отдельная ветка, чужую работу сохранить. Уже реализованное не дублировать.

2. Составь active coverage matrix полного B1 после D2: source поле ->
   потребитель -> текущее состояние -> D3/отложенный E/F/G/H. A2 supported
   профиль пока имеет34items/23V1/8resources, но не полный runtime B1.
   Переиспользуй строгие typed readers/semantic checks/extraction и D1/D2
   executor/project/XP, а не вручную повторяй числа/создавай вторую экономику.
   Для новых editable definitions расширь schema/projection/digest строго
   и воспроизводимо, старый supported profile ID/bytes/provenance сохранить.

3. Создай явно выбираемый versioned производственный профиль, содержащий
   реальные6industry building templates,23V1 и34items из полного баланса:
   woodworking/metallurgy/materials/food/dairy/machinery. Industry mapping
   не переименовывает здания/RecipeId/WIP старого save. В новом профиле
   food объединяет последовательные рецепты муки/хлеба/сахара/выпечки,
   dairy сыр/творог/сметану; у каждого цеха один active recipe.
   Начальные/дальнейшие unlock_level строго из данных, одна выпечка с
   разными рецептами, не несколько выдуманных товаров. Не открывай все
   рецепты после покупки здания, не строй отдельный тип завода на каждый.

4. Полный новый старт:200монет/пустой backpack/нетпредприятия, земля100
   отдельно. Initial prices/materials по industries, в том числе лесопилка
   170+30wood+3steel, общий initial набор63объёма. Материалы заранее на
   ground70; начальная стройка60тиков через реальные timed projects.
   Первый workshop включён, новые нанятые люди не возникают из покупки.
   После старта расхода отмена по C2, не полный бесплатный refund.
   Начальные input/output80 с фильтром D1; дальнейшие уровни/оснащение
   и локальные Lcell/вложения по D2. Цена земли/корпуса/материалов/оснащения
   в карточке разделена. Готовая fixture не считается настоящим start.

5. Реальный конечный импорт в терминале, чтобы промышленность могла
   заработать: coke/soda/limestone/cement/bitumen; bootstrap steel/glass/
   asphalt. Stock/лимиты/наценка12500bp/refresh1200 и отдельный период
   bootstrap asphalt2400, начальные steel12/glass6/asphalt20 сверить с
   актуальными полями полного B1. Масштабирование по участникам применять
   только где предусмотрено, с exact rounding и сохранённым remainder.
   Импортная цена=reference*markup с объявленным minor rounding; steel
   при reference25монет/125% даёт31.25, не округляй до31монеты.
   Bootstrap — настоящий finite общий stock, не бесконечная seed запись.
   Повторный load/refresh не выдаёт его снова. Явно учитывай поступление
   извне как import flow/source; оплаченный товар требует реального
   stock/rights и физической загрузки/пути, не teleport AddStock у завода.
   Конкуренция нескольких покупателей обрабатывается общим fair окном,
   не первым вызванным NPC. Cash/stock holds/settlement/cancel/expiry/replay
   прежними механизмами, ограниченные capital budgets не эмитируются дважды.
   Ручная публичная добыча/наличная продажа столице приcash0 по-прежнему
   доступны без лавочки/залога/новой покупки. Динамический баланс всех
   отраслей не обещай лишь из-за static bootstrap witness.

6. Подвижная добывающая техника§7 не должна пропасть между пакетами:
   активируй поддерживаемые реальные каталоги малой/большой техники на
   прежнем P41 executor:150/600монет,2/6единиц за2тика,storage70/200,
   auto crew2/5 по данным/документированным definitions. Только своя
   купленная совместимая ресурсная клетка, не public/аренда; personal
   требует присутствия, automatic числовую группу. Нет двойного выпуска,
   бесплатных людей/неоплаченного перемещения, при полном storage ждёт
   и возобновляет работу после вывоза. Сохраняй тип/stock/crew/route/holds.
   Ферма не перемещается как лесная машина. Если конкретный type технически
   неподдержан — точный остаток в P63, не фиктивная отметка «всё готово».
   Пять новых машин E и заправку не реализуй.

7. Локальные recipe upgrades: unlockIV открывает право V2, V открывает V3,
   но каждый workshop отдельно модернизируется за деньги/materials/time.
   V2:300монет+4steel+1motor/120тиков; V3:900+6steel+1conveyor/120.
   Порядок/предусловия перехода явно задокументируй и проверь по source;
   обычный переход последовательный, без бесплатной V2 от sharedXP.
   Начатый WIP блокирует unsafe retrofit/recipe switch, не переписывается.
   Вход/выход/целые quantities для базовыхI,Q,T:
   V1:I->Q,T; V2:4I->5Q,ceil(9*T/2); V3:2I->3Q,ceil(5*T/2).
   Checked/Int128 до ceil, никаких float/округлённого двигателя1->2.
   Сохрани полный pinned derived recipe в WIP, finite input/output hold.
   При недостаточной ёмкости увеличенная партия недоступна, V1/доступные
   старые версии остаются. Вложения модернизации в C2 ledger ровно один раз.
   XP за реально произведённый output умножается на xp_per_unit один раз,
   сервис пропорционален full-staff work из D1, не стоимости старой партии.
   Генератор derive на23рецепта даёт69variants без ручного копирования.

8. Production UI RU/EN: все6отраслей, реальные рецепты/разблокировки/
   входы/выход/скорость/затраты/версии, цеха/локальная модернизация и сроки.
   Переиспользуй ProductionTabletPresenter с корректным Supports/generic
   fallback; не маскируй металлургию экраном хлеба. Таблицы/labels/icons
   получают реальные34товара, unknown-safe fallback допустим, отдельный
   большой art pipeline G не обязателен. Реальное меню карты/profile
   поддерживает совместимый выбор нового производственного профиля;
   старые карты/default config/economy.json и A2subset остаются доступны.
   Не переписывай незаметно начатую партию и её economy hash.
   NPC минимально умеют построить/нанять/выбрать доступный recipe,
   приобрести нужный импорт и выполнить production/quest обычными командами.
   Достаточно рабочих цепочек, не обещай оптимального planner/Hard efficacy;
   полную стратегию/полировку оставь G. No foreign stock/quests/plans в UI.

9. Developer XP: target owner+industry, показать current/target/delta,
   целочисленный допустимый диапазон. Это XP участника отрасли, не отдельная
   несогласованная копия на каждом здании. Apply меняет XP, пересчитывает
   доступность технологий, не строит/оснащает фабрику и не нанимает людей.
   Уменьшение XP не уничтожает уже оплаченные здания/WIP/груз; правило
   будущих unlock операций явно и детерминированно. Всё только Developer
   files/API/изолированная flavour, audit old/new/input ordinal и replay.
   Ordinary/Release не получают cheat ActionKind/API/hidden handlers.

10. Переделай God Apply также для существующих money/stock controls:
    открыть->изменить->«Применить»->сразу увидеть результат без закрытия
    окна/ручного X/выхода в Debug. Общая кнопка применяет выбранные изменения
    атомарно либо объясняет отказ без скрытой половины операции. Отдельные
    прежние row Apply могут использовать тот же путь. Выполнять безопасную
    control transaction на существующей разрешённой границе БЕЗ economic
    Step: Tick/production/wages/invoices/quests/deadlines/RNG не продвигаются
    ради применения. При voting/travel/busy boundary — ясный отказ или
    видимое ожидание допустимой границы, не скрытая перемотка. Не обходить
    reserved cash/stock/capacity/cargo/privacy guards. Developer изменения
    помечают run/save modified и не попадают в чистую исследовательскую
    статистику. Сохранить стандартную галочку Export With Debug, forbidden
    Developer Release, audit assemblies/PCK/cleanup и нынешний build workflow.

11. Приёмка:34/23/6 и все69derived вариантов по независимым количествам/
    работе/XP/service; реальные multi-stage пищевые/металл/стройматериалы/
    machinery chains через прежний executor; уровни/технологии/локальный
    retrofit/вместимость/V1 fallback; finite import/buy race/budget/refresh/
    stock restore; настоящий cash0 recovery и ручной старт новой лесопилки
    без бесплатного предприятия/сырья; no double staff/stock/refund/reward.
    Short runs3/6/10 на обеихB2картах, каждый NPC действует; подготовленные
    высокие ступени отдели от реального старта, годы XP не накручивай
    и не выдавай Developerrun за доказательство темпа обычной игры.
    Checkpoint в импортной выдаче/refresh, цепочкеWIP, модернизации/между
    completion иXP, после Apply: continuous/directload/replay совпадают.
    Native Godot RU/EN/узкое окно: actual build/recipe/upgrade/quest,
    God money/stock/XP Apply на паузе с доказанно прежним Tick/зарплатой/
    hash незатронутых подсистем и сохранёнными holds. Негативный ordinary/
    Release metadata audit и реальные17+17/актуальные Developer probes.

Прогони общие bounded gates guide: Core ordinary/Developer, headless,
A1/A2 strict CLI/extraction/provenance, B1/B2/legacy/save/replay, native
client. Не отключай отрицательные tests ради новых DTO; версии миграций
и fixtures обновляй после проверки причины. Измерь новые startup/step/
memory/save расходы, но не обещай hardware60FPS/20–35часов по короткому smoke.

Вне D3: автопарк/реальный автосалон/дорожные полосы/объезд/вклады/налоги/
счета/сторонние услуги E/F, большая карта-слои/полировка G, длительный H.
Полный P61/P63/P64–P66/пакетD отмечай по фактической coverage; неуделанные
согласованные механики явно открыты, не списаны в «всё готово».
Отчёт docs/IMPLEMENTATION_REPORT_PACKAGE_D3.md, обновлённые usage/content
tables/mapping/semantic checks/versions/план/hashes, реальные результаты,
ограничения. Отдельный commit/PR, без auto merge. После D3 остановись
перед E1. Финал: что активно/что metadata, проверки и branch/commit/PR.
```

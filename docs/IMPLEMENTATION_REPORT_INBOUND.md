# P17 «Логистика»: входящие поставки на предприятие

Автор: Шелестов Максим Евгеньевич. Дата: 15.10.2026.
Технический ID итерации: P17b3. Основа: merge P17b2 `689df2b`, ветка `feat/logistics-inbound-p17b3`.

## Объём

Один входящий источник — свой Warehouse/Shop.Input — и один прежний исходящий маршрут используют единственную систему/машину/числовую Logistics group предприятия. Входящие поставки включаются независимо от производства/выхода. Общие очереди произвольного числа работ, fairness/nearest deadline среди нескольких обязательств, следующий production destination и controlled late-return/crew perturbation gate остаются обязательными частями P17. Полный P17 ещё не завершён.

## Правила и реализация

SetLogisticsInbound(System,Source,Enabled) — remote phase1. Источник принадлежит владельцу и является неподвижным Warehouse/Shop.Input; item берётся из recipe.InputItem, объём из free source/cargo/free enterprise input с UnitVolume. Manual item/quantity/foreign source отклоняются. Настройка входа не заменяет выходной контракт/склад и наоборот. Нельзя переназначить уже начатый входящий рейс; отключение того же источника не прерывает поездку. Для upgrade выключить оба направления и дождаться Trip=null.

В prepare source stock/destination input place/full payroll резервируются атомарно. Loading — общий барьер подготовки; на первом пути машина едет пустой FetchingEmpty, товары всё ещё у источника под stock hold. В ArrivalTick у источника phase3 физически загружает cargo, освобождённое место источника резервируется под возврат. ReturningInput везёт груз на предприятие; в EndTick phase3 destination hold становится реальным входным товаром, source return place освобождается. Последний payroll phase4 списывается до завершения рейса. Legacy поле LoadTick для входа означает барьер отправления пустой машины, не момент выдачи товара на удалённом складе. Длительности/параметры/ставка фиксированы, отпуск/телепортация товара отсутствуют.

Один вход/выход выбираются так: готовый контрактный рейс, вход при нехватке сырья на одну партию, готовая исходящая продукция, дополнительный вход. Каждый рейс переиспользует общий payroll/labor allocator и slot. Это ограниченный selector двух настроек, не завершённая произвольная очередь/fairness всех P17. Груз/деньги/штат не дублируются, нового жителя или экипажа на каждое направление нет.

InboundSource/InboundEnabled системы и immutable Trip.InboundSource сохраняются; источник товара для stock/return holds учитывается отдельно от дома машины. Validator проверяет fetching/return states, путь/квоту/cargo/payroll/input/source holds; reverse hold ownership расширен. World8/Rules logistics-4, Session2/Envelope1. World1–7 отклоняются без миграции; прежние saves/ветки сохранять, новый Godot path по RulesVersion.

## Проверки

CI ожидается. В Work локального .NET SDK/Godot нет, повторную сетевую установку после известных proxy timeouts не запускали. Локально Python syntax/git diff --check; local build/test/device pass не заявляется.

Новый scenario inbound-cycle:30wood физически доставлены человеком в свой склад; входящие рейсы10+5 заполняют input15; вход остановлен,7production batches потребляют14wood,7plank одним исходящим рейсом едут в тот же склад. Заранее рассчитанные ожидания: source warehouse15wood+7plank, enterprise input1wood/output0, transport wages900/production wages350, supplier78050/other100000/treasury20700/wages1250=200000. Никакой продажи/эмиссии; один numeric crew на оба направления.

Добавлены30 meaningful cases: полный цикл, пустая дорога/реальная погрузка/загруженный возврат, независимые switches/один slot/приоритет при shortage, source и destination exclusive holds, cash299/300, permissions/item/quota, pause/save/resume, структурная порча и privacy/old-schema rejection. Ожидаемый полный набор369. CI/headless расширены до14scenarios/11load pairs; это ожидаемые размеры до результата запуска.

## Запуск

```powershell
dotnet restore Delsk.sln --locked-mode
dotnet build Delsk.sln -c Release --no-restore
dotnet run --project src/Research.Runner -c Release --no-build -- --scenario scenarios/inbound-cycle.json --script inbound --output artifacts/inbound
```

Смотреть logistics.json/events.jsonl/labor.csv/ledger.jsonl/final-state.json. Полная bounded регрессия: `python scripts/check_headless.py --output artifacts/headless-inbound`. Python запускает проверки/анализ, игровые правила по-прежнему C#.

Следующий ограниченный шаг P17: контролируемая просрочка груза и физический возврат с освобождением места/денег и perturbations экипажа; общая очередь/fairness и остальные направления не вычёркиваются. Android device/новые APK/EXE/реальная модель/научные серии не проверены. Игровой UI остаётся P26.

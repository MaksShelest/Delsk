# Покрытие команд и правил доступа — пакет1

Пакет4: `ProposeTrade` / `CounterTrade` / `AcceptTrade` / `RejectTrade` — Remote. Первые три требуют built shops обеих сторон, последняя оставляет возможность отказаться/освободить переговоры. Человек использует глобальный планшет → Переговоры, NPC тот же валидатор. Обычные publication/bid/buyoffer/support также проверяют shop access. Sale source выбирается среди own building input/output; pickup guide выдаётся только покупателю конкретного claim. Количество — slider/+/−/точный ввод. [Процедура](PARTICIPANT_TRADE.md).


Пакет3 добавил обычные `BuyResource`/`LeaseResource` (AtLocation), `StopTransfer`/`MoveEquipment`/`SetExtractionRoute`/`SetTransportRoute` (Remote). Первые требуют местной ресурсной клетки; остальные проверяют своё владение/безопасное состояние/совместимость в Core. UI — контекст ресурса и карточки/редактор маршрута; NPC имеет те же права. Transfer/CollectPurchase/DeliverContract теперь физический процесс с holds/progress/Stop. Build проверяет walls/committed paths до оплаты. Подробности — [PHYSICAL_AUTOMATION.md](PHYSICAL_AUTOMATION.md). Ниже историческая матрица пакета1, не полный актуальный enum.

Пакет2: при активной стадии голосования Core блокирует остальные команды; SetVoteChoice принимается на той же временной границе один раз и соблюдает signed binding. Developer-вмешательства отсутствуют в ActionKind и ordinary binaries; [отдельная процедура](DEVELOPER_TOOLS.md).

Автор: Шелестов Максим Евгеньевич. 19.10.2026.

Источник геометрии — Core `ActionPolicy.Reach`/`AccessFor`; обработчики повторяют её перед изменением мира после established privacy/busy/deadline checks. `Allowed` означает только разрешённую геометрию; владение, деньги, запас, сроки и безопасное состояние остаются обязательными. Локальная кнопка и Core используют один reason, человек и NPC — один валидатор.

В графе NPC указано право отправить тот же intent, а не обещание, что текущая utility policy самостоятельно выбирает любую команду. P51–P53 не реализуются здесь. Актуальное P45 добавляет версионированные переговоры; детали выше исторической матрицы.

| ActionKind | Политика | Цель/дополнительное право | Человек: реальный интерфейс | NPC |
| --- | --- | --- | --- | --- |
| `MoveTo` | Remote | Ход к целевой клетке | WASD/контекст и «Подойти» в планшете | Те же права |
| `Harvest` | AtLocation | Разрешённый ресурс или собственная машина | Контекст/личная добыча в карточке техники | Те же права |
| `Transfer` | AtLocation | Свой carrier + свой запас в одной точке | Запасы карточки; своя секция компенсации; лавка | Те же права |
| `SetProductionEnabled` | Remote | Собственное производство | Карточка → Работа | Те же права |
| `SellAvailableToCapital` | AtLocation | Опубликованный терминал; лавочка не нужна | Биржа столицы | Те же права |
| `CancelCapitalSale` | Remote | Собственная spot-заявка | Биржа столицы → отмена | Те же права |
| `SetTravelHeading` | Remote | Аутентифицированный собственный ход | WASD; отдельный существующий travel input, не Send | Те же права |
| `ReservePlot` | AtLocation | Публичная клетка участка, права/лимит отдельно | Контекст участка → бронь | Те же права |
| `BuyPlot` | AtLocation | Публичная клетка участка, права/бронь отдельно | Контекст участка → покупка | Те же права |
| `CancelPlotReservation` | Remote | Собственная действующая бронь | Контекст/Переговоры → снять бронь | Те же права |
| `Build` | AtLocation | Anchor всей своей свободной площади | Контекст своего участка → постройка | Те же права |
| `UpgradeBuilding` | AtLocation | Anchor собственного безопасно остановленного здания | Карточка → Работа и улучшения | Те же права |
| `SetRecipe` | Remote | Собственный enterprise; unlocked recipe, нет WIP | Карточка → разрешённый рецепт | Те же права |
| `SetWage` | AtLocation | Своя production/logistics база или extraction машина | Карточка → Работники и зарплата | Те же права |
| `BuyEquipment` | AtLocation | Target ресурсной клетки соответствующего типа | Моё хозяйство → купить технику | Те же права |
| `UpgradeEquipment` | AtLocation | Своя машина, Off, без WIP/личной работы | Карточка техники → улучшить | Те же права |
| `SetEquipmentMode` | Remote | Собственная машина; совместимое WIP-состояние | Карточка техники → Off/Personal/Automatic | Те же права |
| `CreateSellOffer` | Remote | Свой Shop.Input, реальный свободный stock | Планшет лавки → публикация лота | Те же права |
| `SubmitBuyRequest` | Remote | Опубликованный лот; бюджет/лимит | Планшет рынка участников → заявка | Те же права |
| `RaiseBid` | Remote | Собственная ставка/актуальный раунд/лимит | Планшет рынка участников → повышение | Те же права |
| `WithdrawBid` | Remote | Собственная ставка/актуальный раунд | Планшет рынка участников → выход | Те же права |
| `CollectPurchase` | AtLocation | Источник своего pickup-договора у продавца | Планшет лавки продавца → получить | Те же права |
| `CreateBuyOffer` | Remote | Input собственного здания, обеспеченный бюджет | Переговоры → товар/количество/цена/получатель | Те же права |
| `AcceptDeliveryQuantity` | Remote | Опубликованный buy/capital offer, залог/окно | Переговоры → принять поставку | Те же права |
| `WithdrawSupplyRequest` | Remote | Собственная supply request и её окно | Переговоры → снять заявку поставки | Те же права |
| `CancelBuyOffer` | Remote | Собственный незаключённый остаток предложения | Переговоры → отменить закупку | Те же права |
| `DeliverContract` | AtLocation | Destination своего supplier-договора | Переговоры → подойти/передать по договору | Те же права |
| `BuyLogistics` | AtLocation | Своя enterprise база | Карточка производства → купить грузовик | Те же права |
| `SetLogisticsContract` | Remote | Своя система и supplier-договор; quantity из квоты | Карточка → отправлять по договору | Те же права |
| `UpgradeLogistics` | AtLocation | Своя база; оба направления выключены, без рейса | Карточка → улучшить грузовик | Те же права |
| `SetLogisticsStorage` | Remote | Своя система и Warehouse/Shop.Input | Карточка → отправлять в хранилище | Те же права |
| `SetLogisticsInbound` | Remote | Своя система и Warehouse/Shop.Input источник | Карточка → подвозить сырьё | Те же права |
| `SetLogisticsProduction` | Remote | Своя система и совместимый input своего следующего производства | Карточка → отправлять в производство | Те же права |
| `QueueLogisticsJob` | Remote | Своя система, разрешённый contract/destination/source | Карточка → очередь/отключение задания | Те же права |
| `SetLogisticsEnabled` | Remote | Своя система; independent direction | Карточка → вход/выход отдельно | Те же права |
| `SetVoteChoice` | Remote | Текущий election/candidate; обязательный договор задаёт итог | Переговоры → голосовать за кандидата, включая себя | Те же права |
| `ContinueAfterVictory` | Remote | Первый победитель/режим Victory | Переговоры → продолжить после победы | Те же права |
| `ProposeSupport` | Remote | Собственные материалы/кандидат и конкретные выборы | Переговоры → предложить компенсацию за поддержку | Те же права |
| `AcceptSupport` | Remote | Собственная сторона-продавец, unsigned proposal | Переговоры → принять поддержку | Те же права |
| `RejectSupport` | Remote | Любая собственная сторона unsigned proposal | Переговоры → отклонить | Те же права |
| `SignSupport` | AtLocation | Общая торговая точка; bilateral, полная actual compensation | Переговоры → доставить в секцию/подписать | Те же права |
| `StartHarvesting` | AtLocation | Ресурс текущего вида | Контекст «Добывать» | Те же права |
| `StopHarvest` | Remote | Собственная активная добыча | Контекст «Остановить добычу» | Те же права |

Неизвестный/чужой private ID: `NotAvailable` без координат. Правильный own/bilateral ID издалека: `NotAtLocation` (старые Harvest/BuyEquipment сохраняют `NoResourceNode`/`NotAtResourceNode`). Busy/Closed/ManagedCargo и другие предметные проверки не заменяются геометрическим отказом. Публичная клетка участка не раскрывает секретное имущество.

Проверки: HouseholdManagementTests покрывает enum/человеческий путь, равные human/NPC права, базы трёх ролей, privacy неизвестного/чужого ID, настоящий persisted маршрут NPC, глобальный и локальный контексты. Прежние unit/integration сценарии продолжают проверять каждое предметное семейство. Реальная оконная приёмка и непроверенные платформы — [отчёт](IMPLEMENTATION_REPORT_PACKAGE1.md).

### D1

| Действие | Доступ | Дополнительные условия Core |
| --- | --- | --- |
| OutfitWorkshop | AtLocation: собственное предприятие | Готовое место, idle участник, реальные деньги/материалы в input; нет подготовки к сносу |
| AssignWorkshopWorkers | Remote | Источник и получатель — свои цеха одного предприятия; только существующий штат и свободные места |
| SetProductionEnabled / SetRecipe с WorkshopId | Remote | Обычный owner validator; рецепт только без WIP и при остановке выбранного цеха |
| SetWage группы дополнительного цеха | AtLocation: предприятие | Прежняя общая проверка ставки/границы партии; без бесплатного найма |

D2: `AcceptIndustryQuest`, `AbandonIndustryQuest`, `PrepareEnterpriseExpansion`,
`CancelExpansionPreparation` — Remote, только собственные договоры/фабрики.
`BuyExpansionLand` — AtLocation собственной фабрики, покупает конкретную клетку
своего контура прежней транзакцией земли. `UpgradeBuilding` сохраняет AtLocation,
для D2production требует явный target level в Quantity против устаревшего клика.
Initial Build — AtLocation anchor; материалы ground70, остальные условия прежние.
Developer XP отсутствует в ActionKind и доступен только conditional intervention.

### D3

| Действие | Доступ | Условия |
| --- | --- | --- |
| BuyImport | AtLocation: терминал | Idle, текущий конечный запас и свободные деньги; оплаченный товар остаётся в собственном пункте выдачи |
| Transfer из пункта импорта | AtLocation: терминал | Только собственный рюкзак/товар, время обработки и реальная вместимость; обратная выкладка запрещена |
| ModernizeWorkshop | AtLocation: своё предприятие | Конкретный stopped/noWIP цех, следующий local V2/V3, глобальная techIV/V, реальные input materials/деньги, отсутствие строительства/подготовки |
| SetRecipe V2/V3 | Remote | Старые owner/noWIP/stop проверки +global unlock и отдельно оплаченная версия выбранного цеха |

Industry XP в режиме бога остаётся conditional research intervention,
не обычной ActionKind. Money/stock/XP не требуют Step на паузе, не обходят holds.

### E1

| Действие | Доступ | Условия |
| --- | --- | --- |
| BuyVehicle | AtLocation: вход автосалона | Idle, конечный доступный экземпляр, свободные деньги; общий inclusive5tick window и реальный money hold для всех участников |
| BoardVehicle | AtLocation: своя машина | Свободна от водителя/передачи/монтажа/рейса; оба направления связанной автоматики выключены |
| LeaveVehicle | Remote: текущая управляемая машина | Нет незавершённой работы/передачи; позиция и груз остаются у машины |
| AssignVehicle | AtLocation: своё предприятие | Пустая машина там же, без driver/монтажа/передачи/назначения; прежний executor stopped/noTrip; Тракт исключён |
| InstallVehicleKit | AtLocation: своя машина | Совместимая модель, idle, реальные материалы в кузове/деньги;60 активных тиков |
| BuildLoadingPlatform | AtLocation: своё действующее здание | Допустимый уровень и stopped project, реальные input materials/деньги; I не создаёт штат, II/III ждут монтаж |
| Transfer с кузовом | AtLocation: общая клетка | Свои права на товар/место, реальный объём/holds, материалы Тракта; несовместимые задачи машины блокируются |

Для E1 BuyLogistics возвращает VehicleRequired: выбирается купленная машина,
а не второй скрытый грузовик. Остальные прежние команды очереди/маршрутов/Stop
используются после AssignVehicle. Старые профили сохраняют BuyLogistics.
Человек и NPC проходят один ActionAccess и обычные команды. Полный будущий
ассортимент/PRNG/чужие holds не попадают в ControllerObservation.

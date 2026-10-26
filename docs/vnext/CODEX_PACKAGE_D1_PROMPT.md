# Codex: D1 — параллельные цеха и числовой персонал

Запускать после проверенных C1/C2. Подготовлено25.10.2026; это задание, не готовая реализация. Скопируйте блок полностью.

```text
Реализуй в MaksShelest/Delsk только D1: параллельные производственные цеха,
общий конечный склад, независимые партии и корректное распределение работников.
Опыт/задания/покупка пяти ступеней — D2, полный контент/модернизации — D3.

1. Прочитай актуальные AGENTS/архитектуру/решения, весь
   docs/vnext/PACKAGE_CD_IMPLEMENTATION_GUIDE.md, спецификацию0.6 §6/8/9,
   план/typed баланс/CONTENT_TABLES/mapping A2 и отчёты A2/B1/B2/C1/C2.
   Git HEAD/status/diff/remote/PR проверить, основа feat/economic-cycle-p00-p05;
   нужные C1/C2 должны реально присутствовать. Не дублируй уже сделанный D1.
   Отдельная ветка, чужую работу сохранить. Сначала краткий план точек
   изменения/версий/сохранений, затем доведи его до проверенного результата.

2. Исследуй EnterpriseState/ProductionAndTrade/BuildingState/LaborWorkplaces/
   LaborGroups/Logistics/WIP/input-output holds/Capital/ControllerObservation.
   Один предыдущий executor с ингредиентами и pinned WIP уже существует.
   Расширь его для workshop, не создавай второй производственный мир.
   Для старого default профиля сохраняй линейный штат и старую семантику;
   новое поведение выбирается versioned настройкой/профилем явно.

3. Enterprise содержит stable workshop IDs (1..5 по каталогу и реально
   подготовленным местам). Workshop: chosen recipe/version, enabled/reason,
   оснащённость, numeric staff assignment, pinned batch/inputs/output/hold,
   work/service remainder и счётчики. Input/output предприятия общие и
   конечные. Empty place не производит, наличие unlocked slot не дарит
   оснащение или людей. В этой итерации многолинейную работу доказывай
   prepared fixtures; не выдавай ordinary игроку пять мест бесплатно,
   не обходи будущие XP/проекты D2. Минимальный реальный профиль/fixture
   использует данные B1, не ручные альтернативные цены в коде.

4. Производительность по численности0/1/2/3/4/5:
   0/25/45/65/85/100%. Минимум1, максимум5, не linear N/5.
   Progress целочисленный, например basis points: target=T*10000;
   один работник добавляет2500work/tick, пять10000. Никаких sub-ticks.
   Сервис0.02монеты на эквивалент полного рабочего тика по накопленной
   работе, exact remainder; не взимай полную full-staff услугу за каждый
   замедленный wall tick. Платить wages только реально работающему
   числу людей; NoStaff/NoCash/NoInput/NoSpace/UserPaused объясняют простой.
   Начальная ставка/шаг/кадровый рост из профиля, не мгновенный найм.

5. Выбери одну согласованную схему staff: несколько числовых workplace
   groups или общая group с assignments. Опиши почему и реализуй весь
   путь: вакансии/readiness/наём/уход/payroll/pending wage/голоса/total
   population/public employment. Один человек не даёт work двум цехам
   и не считается одновременно логистикой/добычей. Переназначение staff
   сохраняет число людей, ограничения target/source и не платит дважды
   на границе кадрового/производственного тика. Group capacity только
   реально оснащённых готовых мест. Stop цеха не обнуляет численность.
   Общий бюджет одного owner учитывает все цеха и другие группы;
   конкурентный start/payroll не пропускает отрицательный available cash.

6. Партия начинает расход только при полном наборе всех inputs,
   полном output capacity hold и оплате допустимого рабочего шага.
   Inputs уходят в pinned WIP ровно один раз, completion добавляет только
   правильный output ровно один раз. Параллельные старты при нехватке
   общих ресурсов/денег/места имеют воспроизводимую fairness, не постоянный
   первый workshop ID. Несколько завершений в одном тике корректны.
   Выбор recipe только из допустимого списка конкретного workshop.
   Stop сохраняет WIP и рецепт/версию, resume продолжает его; смена рецепта
   не переписывает начатые inputs/work/output. Одноразовая отмена/снос не
   возвращает WIP скрыто. Без сырья/денег один цех не замораживает другой.

7. Входной склад нового предприятия принимает ингредиенты допустимых
   рецептов и текущих строительных/оснастительных проектов; output —
   продукцию. Внедри фильтр на авторитетном admission пути, включая
   manual/автоматические поставки, с безопасной обработкой старого stock
   и уже committed грузов/holds. Нельзя использовать дешёвую фабрику как
   универсальный склад. Не сломай C1 overflow/warehouse или старый профиль.
   Минимальные own inbound/outbound selectors видят потребность нескольких
   цехов, не забивают вход одним товаром за счёт остальных. Не создавай
   копии старой машины на каждый цех; fleet E пока не существует.

8. C2 integration: preparation-to-demolish запрещает новые партии всех
   цехов, но даёт WIP завершиться. Снос проверяет каждый workshop/group/
   hold, возвращает staff без потери population и не дублирует оборудование.
   Фактические новые расходы/оснащение используют тот же investment ledger.
   XP ещё не начисляй заглушкой и не создавай future quests state.

9. Snapshot/restore/hash/invariants/NextId/exports: workshops, pinned batch,
   work/service remainders, payroll/assignment/rotation, references и общие
   inventories. Контроллер/PlayerView получают разрешённое представление,
   чужой raw state не раскрывается. Не переименовывай legacy RecipeIds
   и начатый WIP при адаптации старого enterprise. Изменение формы state
   требует версий/compatibility уже сейчас, не в H.

10. UI RU/EN: список цехов, включение/остановка каждого, текущий recipe,
    штат/назначение/скорость/reason/прогресс и общий input/output. Выбор
    сохраняется при refresh; последняя строка видна в узком окне.
    Реальные commands, не локальные counters. Минимальный алгоритмический
    executor может управлять несколькими доступными workshop той же
    командой/правами; полноценное планирование специализации остаётся G.

11. Независимая приёмка: T16/один сотрудник64рабочихтика, пять16;
    ноль progress0;2/3/4workers соответствуют45/65/85%; переменный штат
    и exact work/service remainder. Два разных рецепта параллельно,
    одинаковое simultaneous completion, scarce input/output/cash fairness;
    NoCash не расходует inputs/зарплату/hold частично; pause/resume одного;
    переключение приWIP reject; staff redistribution без двойного worker;
    public totals/голоса/readiness/budget; фильтры и committed incoming cargo;
    снос/подготовка после нескольких WIP. Конкретные ожидаемые числа
    выводи независимо от production helpers. Fixture с большим оснащением
    назови prepared, не доказательством его приобретения с200монетами.
    Checkpoint с двумя WIP и разными остатками, перед одновременным
    completion/переназначением staff: continuous/directload/replay equality.

Общие gates guide обязательны: relevant ordinary/Developer/Core/headless,
A1/A2 и setup/maps/legacy, native RU/EN/узкое окно. Измерь bounded расходы
параллельности, не обещай hardwareFPS/баланс по smoke. Сохрани стандартный
Export With Debug и ordinary/Release без вмешательств.
Вне D1: реальный XP/quests, пять expansion projects/автоматическое открытие,
V2/V3, весь импорт/контент D3, God Apply/XP, транспорт/дороги E/F.
Отчёт docs/IMPLEMENTATION_REPORT_PACKAGE_D1.md, usage/mapping/версии/план/hashes,
реальные проверки и границы. P64 по факту, P65/P66/D целиком не закрывать.
Отдельный commit/PR без auto merge. После D1 остановись перед D2.
```

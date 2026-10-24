# Новая обычная партия

Автор: Шелестов Максим Евгеньевич.

В меню выберите карту, число ботов от3 до10 (по умолчанию3), сложность и,
при необходимости, существующий Hybrid. Всего участников — боты плюс один
игрок. Preview не запускает мир. Смена языка/карты сохраняет настройки.
Start использует прежнее подтверждение замены партии; Cancel сохраняет
текущий мир. Continue загружает сохранённый состав, а не настройки новой игры.
В запущенной партии число участников не меняется.

Клиент и runner используют `OrdinaryNewGame.Resolve` в Hosting. Конкретные
политики создаёт прежний Algorithmic factory. Поля options JSON для CLI:
`npcCount` integer3..10, `difficulty` Easy/Medium/Hard, optional `seed` unsigned,
`humanActorId` default1 (известный участник исходного шаблона).

```json
{"npcCount":10,"difficulty":"Medium","seed":17}
```

```bash
dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --scenario scenarios/presentation-map-one-v3.json --economy config/economy.json --new-game /tmp/new-game.json --script baseline --output /tmp/delsk-new --ticks 180
```

`--new-game` разрешён только для новой baseline/hybrid с явным `--economy`;
несовместим с `--load`, `--replay`, `--seed`, `--research-setup`.
Обычный research/explicit scenario не ограничивается этим диапазоном.
Hybrid сохраняет общий bounded runtime; модель необязательна.

Порядок: проверить options → один EconomyProfile.Apply → назначить roster/
spawn/агрегаты → ScenarioValidation → создать мир/отдельные controllers.
Legacy N=4/default seed сохраняет стартовые позиции. Другие составы используют
версионированную SHA seed rotation без расхода runtime PRNG. Пул старых карт
детерминированно дополнен до11, торговые общие места — до числа участников.
Земля и ресурсы не выдаются в собственность бесплатно.

Масштабируются только население, optional maximum population, начальная казна
и bootstrap budget столицы: `ceil(base * participants / 4)` с checked Int128.
Личные деньги/вместимость и физические stock не умножаются. Legacy null max
означает прежний unlimited, казна0 остаётся0. Для явного A2 supported profile
начальное/max население берётся из проверенного полного balance.b1.json:
20/60 на участника; казна500000minor на4 с той же формулой. Полный профиль
проверяется по SHA из provenance; текущий default economy-v6 не заменён.

Сохранение содержит resolved config/NewGame provenance и реальные controller
states. Загрузка не обращается к внешнему профилю и не пересоздаёт roster.
Старые поля по умолчанию не сериализуются; старые сохранения не переписываются.

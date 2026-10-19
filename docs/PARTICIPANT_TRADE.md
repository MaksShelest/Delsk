# Лавочки, переговоры и получение — пакет4

Автор: Шелестов Максим Евгеньевич. 19.10.2026.

Для новых правил нужна новая партия World17/participant-trade-1. Предыдущие сохранения World16 сохраняются, но не загружаются этим движком. Ordinary profile presentation-economy-v5 меняет capacity лавочкиL1 на200 (как warehouseL1), не стоимость строительства/материалы. Сохранённая партия хранит свой config.

Постройте свою лавочку: бронь или купленный участок не дают торгового доступа. Контрагенту тоже нужна лавочка, включая сделки за поддержку. До постройки можно добывать/продавать столице и голосовать.

В глобальном планшете → «Переговоры»:

1. Выберите тип/контрагента. «Продажа наличия» фиксирует свой запас в лавке/складе/входе или выходе производства и точку самовывоза. Для публичного лота используйте «Опубликовать выбранное на общем рынке» или карточку лавочки с тем же выбором источника.
2. Количество задают ползунок,−/+,точный ввод. Цена — за единицу, срок исполнения — в тиках после согласия. «Закупка»/«Поставка» означают будущую физическую доставку в собственный input получателя. Если поставщик ещё не знает destination, покупатель уточняет его встречной версией.
3. Выберите переписку и «Показать последнюю версию». Проверьте стороны/товар/цену/компенсацию/точку/срок. Принять/отклонить/предложить встречную версию может только другая сторона. При изменении условий старое согласие блокируется. Срок переговоров не продлевается каждым ответом.
4. Для поддержки checkbox определяет продажу собственного блока либо покупку блока контрагента. В форме задаётся один компонент компенсации; полные полученные terms отображаются списком. Встречная форма заменяет компенсацию выбранным товаром. Обычный transfer к собственной секции общей торговой точки и SignSupport после полной доставки остаются обязательными. Accept переговоров сам по себе голос не связывает.
5. На паузе команды подготовлены и ждут тика. Разрешите время переключателем, чтобы применить их; пауза останавливает весь мир. Меню/справка/фокус независимы от открытого глобального планшета.

После покупки наличия блок «Активные покупки и путь получения» показывает только ваши требования: quantity/collected, остаток срока, клетку выдачи и путь с общими препятствиями. «Подойти» готовит обычную команду движения, время запускается переключателем. На месте выберите количество либо «всё по вместимости». Товар передаётся постепенно; Stop сохраняет уже выданные единицы. Возвращайтесь за остатком несколькими рейсами. Истечение закрывает невыданный остаток со штатным settlement, не отнимает собранное. Право получения не открывает остальные чужие запасы.

Future DeliveryContract можно назначить существующему supplier truck в карточке производства/прежней очереди. Перевозка buyer stock-pickup нынешней ролью грузовика не поддерживается. Production/output/inbound остаются отдельными переключателями.

## Воспроизведение проверок

Требуются SDK10.0.401,Python3,Godot4.7.2 Mono. Каждый внешний процесс ограничен `run_check.py`; данные пишутся вне исходников.

```bash
python3 scripts/run_check.py --name headless --timeout 780 --log-directory work/package4-checks -- python3 scripts/check_headless.py --output work/package4-headless
python3 scripts/run_check.py --name developer --timeout 600 --log-directory work/package4-checks -- python3 scripts/check_developer.py --output work/package4-developer
python3 scripts/run_check.py --name maps --timeout 600 --log-directory work/package4-checks -- python3 scripts/check_physical_automation.py --output work/package4-maps
```

Отдельный короткий сценарий (после Release build) и сохранение на встречной версии:

```bash
python3 scripts/run_check.py --name trade --timeout 30 --log-directory work/package4-checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --scenario scenarios/participant-trade.json --script participant-trade --output work/trade --checkpoint work/trade.save.json --checkpoint-at 33
python3 scripts/run_check.py --name trade-load --timeout 30 --log-directory work/package4-checks -- dotnet src/Research.Runner/bin/Release/net10.0/Research.Runner.dll --load work/trade.save.json --output work/trade-loaded
python3 scripts/run_check.py --name trade-compare --timeout 15 --log-directory work/package4-checks -- python3 scripts/verify_checkpoint_exports.py work/trade work/trade-loaded
```

Godot (подставьте путь своего editor):

```bash
python3 scripts/run_check.py --name client-restore --timeout 180 --log-directory work/package4-checks -- dotnet restore src/Client.Godot/Delsk.Client.csproj --locked-mode --disable-parallel -m:1
python3 scripts/run_check.py --name client-build --timeout 180 --log-directory work/package4-checks -- dotnet build src/Client.Godot/Delsk.Client.sln -c Debug --no-restore --disable-build-servers -m:1
python3 scripts/run_check.py --name import --timeout 120 --log-directory work/package4-checks -- Godot --headless --path src/Client.Godot --import
python3 scripts/run_check.py --name smoke --timeout 45 --log-directory work/package4-checks -- Godot --headless --path src/Client.Godot -- --smoke --report work/platform.json
python3 scripts/verify_platform_probe.py --report work/platform.json --log-prefix work/package4-checks/import --log-prefix work/package4-checks/smoke
```

Probe включает220 trade tick hashes; перегенерация goldens только из независимо проверенного runner output через `update_platform_fixture.py --trade-directory ...`. Не заменяйте ожидаемые hashes ради прохождения ошибки.

Research export `negotiations.json` содержит приватные terms/history. Не передавайте research artifacts контроллерам других участников. Истинные критерии/непройденные аппаратные gates — в [отчёте](IMPLEMENTATION_REPORT_PACKAGE4.md).

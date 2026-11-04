# I3 ordinary progression

Автор: Шелестов Максим Евгеньевич.

Скрипт создаёт обычную партию с 200 монетами, без имущества и бесплатных
материалов, с тремя действующими NPC. Все действия проходят через Hosting.
Это отдельная проверка от подготовленных I3 unit/native fixtures.

```sh
dotnet restore tools/I3Acceptance/I3Acceptance.csproj --locked-mode
dotnet build tools/I3Acceptance/I3Acceptance.csproj -c Release --no-restore
python scripts/run_check.py --name ordinary-i3 --timeout 600 --log-directory work/i3-natural/checks -- dotnet tools/I3Acceptance/bin/Release/net10.0/I3Acceptance.dll work/i3-natural/segment compact-valley
```

Выходной каталог должен быть новым. Для продолжения передайте третьим аргументом
`last-safe.save.json` предыдущего сегмента. Карты: `compact-valley`, `small-river`,
`large-districts`. Сохранение проверяется на совпадение ordinary-конфигурации;
восстанавливается память настоящих NPC. Каждые 1000 тиков пишется checkpoint.
SIGTERM сохраняет безопасную границу и фиксирует `interrupted`, а не успех.

Текущая траектория сначала продаёт публично добытую медь, затем строит комбинат
и продаёт произведённый щебень. Это не доказанная оптимальная стратегия. Покупка
укладчика за 12 000 при старте в 200 монет требует длительного накопления.
Полный сценарий ещё не завершён; сборка этого инструмента не является приёмкой.
Результаты и ограничения — `docs/IMPLEMENTATION_REPORT_I3.md`.

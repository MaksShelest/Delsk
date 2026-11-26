# I10. Журнал

- Ветка создана от `14c766b` после PR #113. Проверены отчёты I8, I9, R1, R2, исходники выборов, Developer и лаборатории.
- Обычный `SetVoteChoice` обслуживает ручной и автоматический голос. Добавлен аудированный переход после победы, поддерживающий replay. При первом запуске проверка Core отклоняла переход на тике победы. Повторный запуск использует условие, допускающее этот тик только с записью исследовательского перехода.
- Текущие исследования разрешают I6-C8D фабрикой обычной партии. Исторический I6-C7.2 остаётся отдельным выбором. Очередь сохраняет карту, баланс, профиль и политику в валидированном снимке.
- Прогресс на вкладке Developer обновляется отдельно от полной отрисовки карты и журнала решений. Контроллеры и экономика по-прежнему выполняют каждый шаг.
- Прогоны I10 сохраняют результаты по выбранной группе и seed. Сравнения относятся к этим входам, горизонту и среде.

## Команды

```sh
sh ResearchLab.sh
python scripts/check_research_lab_city.py --ticks 120 --pause --output artifacts/i10-city
python -m unittest discover -s tests/Research.Lab
```

В браузере открыть «Научные эксперименты» → «Сравнение сложности алгоритмических NPC». В условиях оставить одинаковыми профиль, карту, стартовую отрасль, политику человека и автоголосование. Выбрать Medium и Hard, Лесной край, 3 NPC, 10 000 тиков, первый seed 17 и два независимых seed. Для Малой долины форма города принимает 10 NPC, Hard, seed 17 и 10 000 тиков.

Планы для запуска из корня репозитория включают те же входные условия. В опубликованном плане Лесного края расширен только перечень разведочных показателей отчёта. Хеши первоначальных планов, снимков входов и бинарей записаны рядом с результатами. [Входы, Runner и хеши мира](verification/i10/provenance.json).

```sh
python scripts/research_lab.py --prepare --plan docs/verification/i10/woodland-plan.json --output artifacts/i10-woodland --run
python scripts/research_lab.py --prepare --plan docs/verification/i10/compact-plan.json --output artifacts/i10-compact --run
python scripts/research_lab.py --output artifacts/i10-woodland --port 0 --open-browser
```

В открытой очереди вкладки «Сравнение условий», «Отдельные прогоны» и «Отчёт и файлы» используют сохранённые экспорты. Полный исходный экспорт остаётся в `runs/<id-прогона>/<номер-попытки>/` внутри папки серии. В `docs/verification/i10` приложены сводные CSV, результаты выборов и самостоятельный HTML-отчёт. Для проверки экспортов укажите каталоги завершённых попыток в `python scripts/verify_results.py`.

Для отдельной проверки восстановления задайте `i10_attempt` как каталог `1` прогона `arm-0-s17-r0`. Соседний файл `1-world.save.json` содержит контрольную точку t3000. Исходные пути отчёта из команд ниже выводятся в `artifacts/`.

```sh
i10_attempt=$(find artifacts/i10-woodland -path '*/runs/arm-0-s17-r0/1' -type d -print -quit)
i10_runner=src/Research.Runner/bin/Release/net10.0/Research.Runner.dll
dotnet "$i10_runner" --load "$i10_attempt/../1-world.save.json" \
  --output artifacts/i10-t3300 --ticks 3300 --economy-sample true --host-profile true \
  --progress true --compress-controller-turns true --research-human ballot-only \
  --research-ballot self --research-continue-after-victory true
dotnet "$i10_runner" --scenario artifacts/i10-t3300/scenario.json \
  --replay artifacts/i10-t3300/commands.jsonl \
  --research-modifications artifacts/i10-t3300/research-modifications.json \
  --ticks 3300 --output artifacts/i10-replay --compress-controller-turns true
python scripts/verify_results.py artifacts/i10-t3300 artifacts/i10-replay
```

Runner принимает те же явные флаги политики в новом алгоритмическом опыте с `--new-game`. Голос за NPC задаётся ID участника. Воспроизведение читает записанные команды и аудированный переход.

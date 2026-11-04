# I3 evidence

Автор: Шелестов Максим Евгеньевич. Все exit codes — фактические.

- `final-unit`, `final-developer`: 21/21 каждый, без skips, прежние deadlines.
  Запрет Developer Release подтверждён ожидаемым exit1.
- `complete-unit`: первоначальные 20 ordinary I3 cases; компактный speed/billing сценарий после
  этого расширен и отдельно повторён (`checks/i3-map-speed.*`).
- `roads-regression`, `i2-regression`: сохранённые suites E2 и I2.
- `native-4`: полная ordinary сборка и audit исходных runtime references.
- `native-5`: финальная UI-поправка локализации; upstream DLL frozen из native-4,
  ordinary client audit, import, smoke, 3 native cases и 18 PNG.
- `complete-developer`: исходный timeout сборки 180s при конкуренции за CPU.
  Повтор выполняется отдельно с тем же лимитом; исходный отказ не удаляется.
- `complete-developer-2`: сборка прошла за 51,46s, но составной тест большой
  карты превысил 180s на полном replay после строительства. Проверки разделены:
  подготовка заново сравнивает точные мир и tape с исходным checkpoint;
  отдельная partition воспроизводит весь исходный путь от t0. Никакая проверка
  не удалена, прежние 120/180s сохранены. Происхождение frozen fixture —
  `district-frozen-origin.json`; это явно подготовленный диагностический мир.
  Первоначальный Developer checkpoint корректно отвергнут ordinary loader;
  frozen fixture взят из независимо повторённого ordinary пути с тем же t767
  hash и tape, без изменения каких-либо флагов или ослабления проверки пакета.
- `final-runner`: 23 процесса, 14 экспортов, 7 точных пар и независимые audits.
- `checks`: исходные неуспешные native попытки, Python sandbox/partition отказы
  и исправленные повторные проверки.
- `natural-*`: реальный ordinary-прогресс, статус `interrupted`, без выдачи
  средств. Это не законченная приёмка строительства.
- `large-artifacts.json`: SHA256/размер локальных больших сохранений, которые
  намеренно не добавляются в Git. CI заново формирует диагностические fixtures.

Не считать Linux software GL, synthetic touch и подготовленные ресурсы
физическими Windows/Android испытаниями или естественным накоплением.

Воспроизведение: `check_headless.py --phase tests --suite i3`, затем
`check_i3_runner.py --fixtures <output>/i3-fixtures --output <new-output>`.
Developer: `check_developer.py --suite i3`. Native: `check_i3_native.py`
с `--fixtures`, `--output`, `--godot`; нужен Xorg dummy и разрешённый localhost.
Все прежние deadlines сохраняются. Подробности — итоговый отчёт I3.

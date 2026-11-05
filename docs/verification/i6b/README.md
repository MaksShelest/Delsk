# I6-B evidence

`native/` — успешный итоговый процесс native-12: все 11 фаз, 227 assertions, 10 настоящих PNG, строгий журнал и 0 orphan nodes. `images.json` содержит размеры и SHA-256 изображений. `attempts.json` — честный индекс всех 12 попыток; `attempts.tar.gz` хранит исходные receipts, входные SHA и сжатые журналы, включая отказы.

`prepared.save.json` — явно подготовленный мир compact-valley / 3 NPC до оснащения второго цеха; не обычный старт. SHA — в `prepared.sha256`. Цены, профили, движение и контроллеры не изменены. `runtime-references.json` фиксирует DLL, использованные изолированным probe после обычного AssemblyAudit.

Для повторения с установленными .NET 10.0.401 и Godot .NET 4.7.2:

```sh
python scripts/check_i6b_ui.py \
  --output /tmp/delsk-i6b-check \
  --godot /path/to/Godot_v4.7.2-stable_mono_linux.x86_64 \
  --fixture docs/verification/i6b/prepared.save.json \
  --case from-navigation
```

Нужны Xorg с dummy driver и software GL, как в существующем драйвере I6-A. Output должен быть новым: отказы не перезаписываются. Без `--prepared` собираются обычные текущие references; с `--prepared` разрешены только изменения Client, а с `--probe-only` игровые исходники должны побайтно совпадать. Probe лежит за пределами shipped client и запрещён в обычном AssemblyAudit.

Linux/synthetic input не является аппаратной приёмкой Windows/Android. Полный отчёт — [IMPLEMENTATION_REPORT_I6B](../../IMPLEMENTATION_REPORT_I6B.md).

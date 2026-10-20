# Пакет7 и планшет пекарни — восстановление работы

Автор: Шелестов Максим Евгеньевич.20.10.2026 (Europe/Samara).

## База и границы

PR#42 фактически слит:853b9935d8102289d6322e6520310b22fa8621b7. До начала чисто; новая ветка feat/package7-visual-audio-bakery. Нормативный пакет7 §12.1: **P30/P57**, не исследовательская лаборатория P58. Пользователь отдельно разрешил следующий пакет, поэтому прежние незакрытые model/Hard gates остаются честно открытыми; план явно допускает пакет7 раньше. Не переписывать экономику/контроллеров, не объявлять новую аппаратную/модельную приёмку.

Архив пользователя сохранён docs/design/bakery-v1/source.zip + оригинальный README. Извлечён с проверкой traversal/symlink/размера в /workspace/work/delsk-package7/reference/delsk_bakery_v1. Только сцена/artwork, demo.gd/project.godot/примерные числа не являются игровыми требованиями.

## Этапы

1. P30: собственный склад-площадка по actual stock/capacity/footprint/walls, не раскрывать чужие запасы; силуэты реально существующих уровней, кеширование моделей/ограниченные штабе́ли. Unit/privacy/save-load/replay и Godot проверка.
2. P57: краткие RU/EN реплики из публичных фактов, лимиты/приоритет/выключение; звук событий/кнопок + тихое окружение/опциональная музыка, отдельная громкость/источник и лицензия. Только presentation clock/state, без world PRNG/LLM/чужих условий; пауза/focus/ускорение без лавины. Meaningful tests и сводка/commit.
3. Пекарня: обобщить уже существующий presenter лесопилки минимально, подключить BakeryTablet.tscn к тем же actions/access, реальные рецепты/прогресс/численность/зарплата/локальные материалы/логистика, desktop RU/EN и compact. Лесопилка не регрессирует. Затем общий gate/docs/push/один PR без merge.

## Итог и точка продолжения

- Этап1 P30: commit45174b4. Actual level/relative catalog footprint/walls/own-only physical stock, mixed bounded24 piles/4 layers, stock cache отдельно от static. Unit tests поймали absolute-vs-relative footprint и это исправлено; global hashes не менялись.
- Этап2 P57: commit2047e2d. PresentationEffects только public facts/cosmetic clock/hash;2 simultaneous/3.2s/8s cooldown и разные высоты, bounded sound pool, own WAV/CC0,3 volumes/speech preferences вне checkpoint. Выключение и сохранение prefs проверены настоящим scene restart. Godot audio probe прошёл с Dummy output, без claim динамиков.
- Этап3: общий ProductionTabletPresenter вместо Sawmill..., bakery named-node map Grain/Bread, Upgrade CostPlanks=boards, reuse identical Nunito/OFL. Other/multi-input recipe generic fallback. Actual bakery RU/EN/800×600, Start/Stop, paused queue, truck purchase, pending wage и independent inbound прошли; sawmill regression прошёл.
- Финальные проверки:879/879 ordinary и885/885 Developer,0 failed/skipped; полный headless102exports/14replay/69pairs, privacy/balances/invariants/S7; ordinary+isolated Developer Godot1132 hashes + stopped10/35 restores; Debug/ExportRelease0 warnings/errors,5 DLL audit, PCK без Developer/test gallery/obj, со сценами/audio/licenses. Exit0 с ошибкой disconnect в smoke пойман log verifier; guard подписки исправлен и оба smoke перепроверены.
- Gallery из tests/Client.Probes/VisualModelsProbe.cs собирается только в isolated stage. Она проверяет controlled visual DTOs, не достижения NPC: empty/partial/full mixed yard/L1/L2/purple shop/2 captions;100 unchanged frames не rebuild, stock меняет один root. Код этого probe отсутствует в ordinary metadata/PCK.
- Доказательства/source hashes/screenshots: docs/verification/package7. Полный отчёт docs/IMPLEMENTATION_REPORT_PACKAGE7.md. Спецификация SHA e416b331a8353d5f39d8ff042288ad2493d39aae2c506a40ecc3f30de56fd56c неизменна; World18/Rules production-recipes-1/Session2/Envelope2 прежние.
- Scratch: source /workspace/work/delsk-package7/env.sh; SDK10.0.401, Godot4.7.2 Mono. checks/ содержит bounded stdout/stderr/exit; final879.trx, developer/tests.trx, headless/, bakery-final/, sawmill-final/, models-delivery/, platform-final.json, developer-platform-final.json. Новый run_check без нужды не повторять; использовать последние успешные имена из gate-summary.
- Следующие действия git: финальный commit/push/один PR в feat/economic-cycle-p00-p05, без merge. Финальный hash определить через git log, после публикации дописать PR link.
- После публикации не начинать заново: открыть ветку/PR и эту сводку. Следующий пакет8 P58–P60. Предыдущие real-model P56/Hard efficacy/P27/device gates остаются открытыми; Windows EXE/Android/динамики/60FPS/длинная партия здесь не проверены. По текущему заданию P58+ не начинался.

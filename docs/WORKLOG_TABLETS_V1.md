# Переработка планшетов — контекст и этапы

Автор: Шелестов Максим Евгеньевич.20.10.2026 (Europe/Samara).

База `77d40e9`: PR#44 и#46 слиты; PR#45 открыт Draft (e195be1), не часть актуальной базы. PR45 содержит отдельную карту и аудит, редактор F04 не закончен и не восстанавливается в рамках этой задачи. PR46 добавил ResourceInventoryHud с положительными physical goods/парными цепочками и Developer-only окно из Debug; сохранить partial hooks/паузы/audit. Ветка feat/tablets-design-v1, исходное дерево чистое.

Архив delsk_tablets_godot_v1.zip (205files,48scenes) — референс представления. В нём demo.gd/фиктивные данные, не игровые инструкции. Никакой замены project.godot/экономики/прав из архива. Исходник/лицензии сохранить, reuse одинаковые Nunito/старые approved sawmill/bakery. Живые данные только через прежние PlayerView/обработчики и валидаторы.

1. Общий светлый каркас и производства: общая тема/иконки/адаптивность, milk/cheese, beet/sugar, multi-input pastry; безопасная общая привязка к HouseholdObjectCard actions, сохранение всех подробных действий/routes/recipes.
2. Биржа/планшет игрока/справка: крупные группы экранов с единым стилем; существующие фильтры/цены/аукционы/выдача/приватность/переговоры/город. Справка11тем, краткие шаги+полный текст, прежние pause/restore semantics. Developer/HUD не перекрашивать глобальной темой игры.
3. Реальные Godot probes: состояния stock/reservations/WIP/pending wage/no truck/independent directions, market/negotiations/access, RU/EN1440/1280/800 и узкие окна; normal+Developer gates/audit/hash/runner по риску. Обновить docs, commits/push/один PR без merge.

Scratch /workspace/work/delsk-tablets-v1 (env.sh готов). Godot4.7.2/.NET10.0.401, только bounded scripts/run_check.py. Не считать static DTO screenshot живой экономической приёмкой; новые вмешательства/тестовые классы только isolated stage и вне ordinary audit/PCK. По завершении каждого этапа дополнять этот файл результатом/командами/оставшимся. Старые Hard/model/device/history gates остаются открытыми.

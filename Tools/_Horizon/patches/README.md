# Три патча оптимизации сервера StarHorizon

Все изменения кода из ветки `ccr-777a0c21-8mid2y`, разбитые на три независимых патча.
База для патчей игры — `claude/bold-ritchie-hq4nob`; для патча движка — RobustToolbox `feb9e1db` (267.3.0).

| Патч | Куда применять | Что внутри |
|---|---|---|
| `0001-engine-server-parallel-manager.patch` | каталог `RobustToolbox` | Серверный диспетчер параллельных задач: игровой поток участвует в работе. Только `Robust.Server`. |
| `0002-game-optimizations.patch` | корень репозитория игры | Оптимизации и исправления кода игры, найденные профилированием. |
| `0003-monolith14-port.patch` | корень репозитория игры | Приёмы из Monolith14: сон NPC вдали от игроков, уборка мусора в космосе, очередь удаления обломков. |

Применение:

    git -C RobustToolbox apply "$PWD/Tools/_Horizon/patches/0001-engine-server-parallel-manager.patch"
    git apply Tools/_Horizon/patches/0002-game-optimizations.patch
    git apply Tools/_Horizon/patches/0003-monolith14-port.patch

Патчи не зависят друг от друга: их можно применять по отдельности и в любом порядке.
В самой ветке `ccr-777a0c21-8mid2y` всё это уже применено (патч движка — в форке
`lAstronautl/RobustToolbox`, ветка `horizon/267.3.0`).

В патчи не входит только переключение сабмодуля движка на форк (`.gitmodules`).
Стенд замеров, скрипты и отчёт из ветки удалены; они остались в истории git (коммит `802b3f7f`).

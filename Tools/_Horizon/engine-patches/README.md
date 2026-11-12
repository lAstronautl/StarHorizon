# Патчи движка RobustToolbox (StarHorizon)

Правки исходников движка, измеренные на нагрузочном стенде (`Tools/_Horizon/perf`).

Сабмодуль `RobustToolbox` указывает на форк `lAstronautl/RobustToolbox`, ветка `horizon/267.3.0`.
Патчи ниже **уже применены в этой ветке**; здесь они лежат как справка — для переноса на
следующую версию движка при апстриме.

Базовый коммит движка: `feb9e1db69ab97acfd07580918f5d20ffaa6b770`.

Применение на чистый апстримный движок (из корня репозитория игры):

    git -C RobustToolbox apply ../Tools/_Horizon/engine-patches/0001-parallel-manager-caller-participation.patch

Откат: `git -C RobustToolbox checkout -- .`

| Патч | Что делает |
|---|---|
| 0001 | `ParallelManager.ProcessNow`: вызывающий (игровой) поток сам выполняет пачки параллельной задачи вместе с пулом потоков, а не спит на kernel-хэндле. |

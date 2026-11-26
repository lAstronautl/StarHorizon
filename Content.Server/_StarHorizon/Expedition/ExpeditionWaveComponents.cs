namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Волны врагов на карте планетной экспедиции. Каждая волна тратит случайную долю mobBudget сложности и спавнит
/// мобов фракции задания кольцом вокруг игроков.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionWavesComponent : Component
{
    /// <summary>Когда придёт следующая волна. Null: на карте ещё нет игроков.</summary>
    [ViewVariables] public TimeSpan? NextWave;

    [DataField] public float FirstWaveMin = 45f;
    [DataField] public float FirstWaveMax = 75f;
    [DataField] public float IntervalMin = 90f;
    [DataField] public float IntervalMax = 180f;

    /// <summary>Доля mobBudget сложности на одну волну: случайно от 40% до 60%.</summary>
    [DataField] public float BudgetFractionMin = 0.4f;
    [DataField] public float BudgetFractionMax = 0.6f;

    /// <summary>Потолок бюджета одной волны (по умолчанию не ограничен, ограничивает MaxAliveWaveMobs).</summary>
    [DataField] public float MaxWaveBudget = 10000f;

    /// <summary>Сколько мобов падает в одном дроп-поде.</summary>
    [DataField] public int MobsPerPod = 6;

    /// <summary>Где упали первые поды: позиция первого игрока на карте. Запоминается один раз.</summary>
    [ViewVariables] public System.Numerics.Vector2? DropAnchor;

    /// <summary>Сколько мобов в текущей волне (для выключения музыки, когда осталось 10%).</summary>
    [ViewVariables] public int WaveTotal;

    /// <summary>Невидимая сущность в якоре: держит землю вокруг подгруженной, пока игроков рядом нет.</summary>
    [ViewVariables] public EntityUid? AnchorLoader;

    /// <summary>Сколько мобов волн может жить на карте одновременно, дальше волна пропускается.</summary>
    [DataField] public int MaxAliveWaveMobs = 60;

    /// <summary>
    /// Кольцо падения дроп-подов вокруг якоря (места, где начал первый игрок), метры. Если у якоря нет подгруженной
    /// земли (биом подгружается только вокруг игроков), поды падают в таком же кольце вокруг ближайшего игрока.
    /// </summary>
    [DataField] public float DropRingMin = 5f;
    [DataField] public float DropRingMax = 14f;

    /// <summary>Музыка волны выключается, когда живых мобов волны осталось столько долей от всех.</summary>
    [DataField] public float MusicOffFraction = 0.1f;

    /// <summary>Сколько после появления волны клиентам играет боевая музыка (волна считается идущей).</summary>
    [DataField] public TimeSpan WaveMusicDuration = TimeSpan.FromSeconds(120);

    /// <summary>За сколько до конца экспедиции волны прекращаются.</summary>
    [DataField] public TimeSpan StopBefore = TimeSpan.FromSeconds(30);
}

/// <summary>Моб волны: всегда знает, где ближайший игрок, и идёт к нему.</summary>
[RegisterComponent]
public sealed partial class PursuePlayerComponent : Component
{
    [ViewVariables] public EntityUid? Target;

    /// <summary>Если моб волны не двигается дольше этого (застрял в щели, за камнем) и игроков рядом нет, он удаляется.</summary>
    [DataField] public TimeSpan StuckTime = TimeSpan.FromSeconds(60);

    /// <summary>Сдвиг меньше этого за проверку считается стоянием на месте, метры.</summary>
    [DataField] public float StuckDistance = 0.5f;

    /// <summary>Игрок ближе этого (дальше дальности стрельбы 10 м): моб дерётся или стреляет, на месте стоять ему можно.</summary>
    [DataField] public float PlayerNearDistance = 15f;

    /// <summary>
    /// Дальше этого моб идёт к игроку по «ножкам» вдоль поля расстояний (через двери и коридоры): A* у ИИ ограничен 512 узлами и до далёкой цели путь не находит (моб
    /// стоит на месте), поэтому цель ставится на <see cref="LegLength"/> метров вперёд по открытой земле.
    /// </summary>
    [DataField] public float DirectRange = 8f;

    /// <summary>Длина ножки по полю расстояний до игроков, тайлы.</summary>
    [DataField] public int LegTiles = 12;

    /// <summary>Длина одной ножки пути к далёкому игроку, метры.</summary>
    [DataField] public float LegLength = 18f;

    /// <summary>Цель текущей ножки (в координатах карты) и когда выбрана.</summary>
    [ViewVariables] public System.Numerics.Vector2? Waypoint;

    [ViewVariables] public TimeSpan WaypointSet;

    /// <summary>С какого угла пробовать ножку: растёт, когда к выбранной цели пути нет (NoPath), чтобы пойти в обход.</summary>
    [ViewVariables] public int AngleStart;

    /// <summary>Какая цель сейчас стоит в блэкборде моба (чтобы перепланировать только при смене).</summary>
    [ViewVariables] public (EntityUid? Player, System.Numerics.Vector2? Waypoint) Followed;

    [ViewVariables] public System.Numerics.Vector2? LastPosition;

    [ViewVariables] public TimeSpan StillSince;
}

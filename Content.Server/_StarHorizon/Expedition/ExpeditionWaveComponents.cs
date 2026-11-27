namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Волны врагов на карте планетной экспедиции. Каждая волна тратит случайную долю mobBudget сложности и спавнит
/// мобов фракции задания в дроп-подах на полу данжа рядом с игроком.
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

    /// <summary>Доля mobBudget сложности на одну волну: случайно от 30% до 50%.</summary>
    [DataField] public float BudgetFractionMin = 0.3f;
    [DataField] public float BudgetFractionMax = 0.5f;

    /// <summary>Потолок бюджета одной волны (по умолчанию не ограничен, ограничивает MaxAliveWaveMobs).</summary>
    [DataField] public float MaxWaveBudget = 10000f;

    /// <summary>
    /// Во время эвакуации (шаттл вызван) обычные волны не идут, вместо них часто и без объявления падают маленькие:
    /// эта доля mobBudget раз в <see cref="ExtractionIntervalMin"/>..<see cref="ExtractionIntervalMax"/> секунд.
    /// </summary>
    [DataField] public float ExtractionBudgetFraction = 0.1f;
    [DataField] public float ExtractionIntervalMin = 20f;
    [DataField] public float ExtractionIntervalMax = 40f;

    /// <summary>За сколько до конца эвакуационные волны прекращаются (под падает ~10 с, позже мобы не успеют).</summary>
    [DataField] public TimeSpan ExtractionStopBefore = TimeSpan.FromSeconds(15);

    [ViewVariables] public TimeSpan? NextExtractionWave;

    /// <summary>Сколько мобов падает в одном дроп-поде.</summary>
    [DataField] public int MobsPerPod = 6;

    /// <summary>Сколько мобов в текущей волне (для выключения музыки, когда осталось 10%).</summary>
    [ViewVariables] public int WaveTotal;

    /// <summary>Сколько мобов волн может жить на карте одновременно, дальше волна пропускается.</summary>
    [DataField] public int MaxAliveWaveMobs = 60;

    /// <summary>
    /// Кольцо падения дроп-подов вокруг случайного живого игрока, метры. Поды садятся на пол данжа в этом кольце, а
    /// если данжа рядом нет, на любой свободный тайл.
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
    /// Дальше этого моб идёт к игроку по «ножкам»: A* у ИИ ограничен 512 узлами и до далёкой цели путь не находит (моб
    /// стоит на месте), поэтому цель ставится на <see cref="LegLength"/> метров вперёд по открытой земле.
    /// </summary>
    [DataField] public float DirectRange = 25f;

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

    /// <summary>Где моб был, когда последний раз продвинулся (для перестройки пути, если он встал).</summary>
    [ViewVariables] public System.Numerics.Vector2? ProgressPosition;
    [ViewVariables] public TimeSpan ProgressSince;

    /// <summary>
    /// Если моб столько не продвинулся к игроку (а до игрока далеко), путь сбрасывается и строится заново: иначе после
    /// неудачного поиска пути (дверь, пробка) он так и стоял бы, даже когда путь освободился.
    /// </summary>
    [DataField] public TimeSpan RepathAfter = TimeSpan.FromSeconds(4);

    [ViewVariables] public TimeSpan StillSince;
}

/// <summary>Клетки комплекса (данжа) карты экспедиции: если игрок в комплексе, капсулы волн падают только в него.</summary>
[RegisterComponent]
public sealed partial class ExpeditionDungeonTilesComponent : Component
{
    [ViewVariables] public HashSet<Robust.Shared.Maths.Vector2i> Tiles = new();
}

/// <summary>
/// Маркер места падения капсул волн. Ставится в атласах комнат комплекса: если до маркера от игрока можно дойти,
/// капсулы падают на маркеры в первую очередь.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionDropSiteMarkerComponent : Component
{
}

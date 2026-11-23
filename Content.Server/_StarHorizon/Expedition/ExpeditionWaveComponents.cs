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

    /// <summary>Доля mobBudget сложности на одну волну (случайно между).</summary>
    [DataField] public float BudgetFractionMin = 0.2f;
    [DataField] public float BudgetFractionMax = 0.5f;

    /// <summary>Потолок бюджета одной волны (у Inferno mobBudget = 240).</summary>
    [DataField] public float MaxWaveBudget = 40f;

    /// <summary>Сколько мобов волн может жить на карте одновременно, дальше волна пропускается.</summary>
    [DataField] public int MaxAliveWaveMobs = 60;

    /// <summary>
    /// Кольцо появления вокруг игрока, метры. Биом подгружается только в ~16 м вокруг игроков (чанки), дальше пустота,
    /// поэтому кольцо держим в пределах подгруженной земли: за пределами экрана, но на тайлах.
    /// </summary>
    [DataField] public float RingMin = 15f;
    [DataField] public float RingMax = 30f;

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
}

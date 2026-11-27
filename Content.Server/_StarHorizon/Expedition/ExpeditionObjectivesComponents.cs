namespace Content.Server._StarHorizon.Expedition;

/// <summary>Задание «Квота добычи»: сдать <see cref="Quota"/> руды в приёмник экспедиции.</summary>
[RegisterComponent]
public sealed partial class SalvageMiningExpeditionComponent : Component
{
    [ViewVariables] public int Quota = 30;
    [ViewVariables] public int Collected;
}

/// <summary>Задание «Сбор образцов»: образцы разбросаны по комнатам данжа, сдать <see cref="Required"/> в приёмник.</summary>
[RegisterComponent]
public sealed partial class SalvageSamplingExpeditionComponent : Component
{
    [ViewVariables] public int Required = 4;
    [ViewVariables] public int Collected;
    [ViewVariables] public List<EntityUid> Samples = new();
}

/// <summary>
/// Приёмник экспедиции на карте задания с квотой или образцами: его сбрасывают в капсуле рядом с первым игроком,
/// руду и образцы сдают в него (по одному или сразу из сумки).
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionDepotHolderComponent : Component
{
    [ViewVariables] public EntityUid? Depot;

    /// <summary>Когда сбросить приёмник (через несколько секунд после появления первого игрока).</summary>
    [ViewVariables] public TimeSpan? DropAt;

    [ViewVariables] public bool Dropped;

    /// <summary>Задание выполнено и объявлено.</summary>
    [ViewVariables] public bool Done;
}

/// <summary>Сам приёмник: к какой карте экспедиции он относится.</summary>
[RegisterComponent]
public sealed partial class ExpeditionDepotComponent : Component
{
    [ViewVariables] public EntityUid Expedition;
}

/// <summary>Образец для задания «Сбор образцов».</summary>
[RegisterComponent]
public sealed partial class ExpeditionSampleComponent : Component
{
}

public enum ExpeditionRepairStage : byte
{
    /// <summary>Шаттл ещё не сел или узлы чинятся.</summary>
    Repairing,

    /// <summary>Узлы починены, системы запускаются: идут волны, шаттл надо удержать.</summary>
    Defending,

    /// <summary>Шаттл готов к вылету.</summary>
    Done,
}

/// <summary>
/// Задание «Ремонт шаттла»: эвакуационный шаттл в начале задания жёстко садится на планету. Надо починить его узлы,
/// затем продержаться, пока запускаются системы. Если шаттл к эвакуации не готов, присылают резервный, а задание
/// проваливается.
/// </summary>
[RegisterComponent]
public sealed partial class SalvageRepairExpeditionComponent : Component
{
    [ViewVariables] public int Total = 3;
    [ViewVariables] public List<EntityUid> Points = new();
    [ViewVariables] public bool PointsSpawned;
    [ViewVariables] public ExpeditionRepairStage Stage;

    [DataField] public TimeSpan DefenseDuration = TimeSpan.FromMinutes(2);
    [DataField] public TimeSpan DefenseWaveInterval = TimeSpan.FromSeconds(40);
    [ViewVariables] public TimeSpan DefenseStart;
    [ViewVariables] public TimeSpan DefenseEnd;
    [ViewVariables] public TimeSpan NextDefenseWave;
}

/// <summary>Повреждённый узел разбитого эвакуационного шаттла.</summary>
[RegisterComponent]
public sealed partial class ExpeditionRepairPointComponent : Component
{
    [ViewVariables] public EntityUid Expedition;
    [ViewVariables] public bool Repaired;
    [DataField] public TimeSpan RepairTime = TimeSpan.FromSeconds(8);
}

/// <summary>Эвакуационный шаттл разбит: пока висит этот компонент, он не улетает.</summary>
[RegisterComponent]
public sealed partial class ExpeditionBrokenShuttleComponent : Component
{
}

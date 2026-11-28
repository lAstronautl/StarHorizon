using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.Expedition;

/// <summary>
/// Данные интерфейса планетной экспедиции (как в Deep Rock Galactic): задачи с прогрессом, время до эвакуации и где
/// стоит эвакуационный шаттл. Висит на карте экспедиции, сервер обновляет, клиент рисует, пока игрок на этой карте.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExpeditionHudComponent : Component
{
    /// <summary>Конец экспедиции: в этот момент эвакуационный шаттл уходит в БСС.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan EndTime;

    /// <summary>Когда вызывается эвакуационный шаттл (за несколько минут до конца).</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ExtractionAt;

    [DataField, AutoNetworkedField]
    public List<ExpeditionObjective> Objectives = new();

    /// <summary>Все задачи выполнены.</summary>
    [DataField, AutoNetworkedField]
    public bool Completed;

    /// <summary>Идёт волна врагов: сколько её мобов ещё в строю и сколько было.</summary>
    [DataField, AutoNetworkedField]
    public int WaveFighting;

    [DataField, AutoNetworkedField]
    public int WaveTotal;

    /// <summary>Эвакуационный шаттл (грид), когда он есть.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Shuttle;

    /// <summary>Шаттл сел на карту экспедиции: к нему можно идти.</summary>
    [DataField, AutoNetworkedField]
    public bool ShuttleLanded;

    /// <summary>Центр шаттла в координатах карты (шаттл может быть вне PVS игрока, поэтому позиция передаётся явно).</summary>
    [DataField, AutoNetworkedField]
    public Vector2 ShuttlePosition;

    /// <summary>Куда показывает компас (координаты карты экспедиции) и что там.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 CompassTarget;

    [DataField, AutoNetworkedField]
    public ExpeditionCompassTarget CompassKind;
}

/// <summary>На что указывает компас интерфейса экспедиции.</summary>
[Serializable, NetSerializable]
public enum ExpeditionCompassTarget : byte
{
    /// <summary>Цели нет: компас показывает только стороны света.</summary>
    None,

    /// <summary>Данж: цели задания надо найти в нём.</summary>
    Dungeon,

    /// <summary>Место задания, которое не надо искать (разбитый шаттл, активированный чёрный ящик).</summary>
    Objective,

    /// <summary>Приёмник руды и образцов.</summary>
    Depot,

    /// <summary>Эвакуационный шаттл (летит или сел).</summary>
    Shuttle,
}

[Serializable, NetSerializable]
public enum ExpeditionObjectiveKind : byte
{
    /// <summary>Уничтожить строения.</summary>
    Destroy,

    /// <summary>Убить мегафауну.</summary>
    Eliminate,

    /// <summary>Активировать чёрный ящик.</summary>
    Activate,

    /// <summary>Скачать данные чёрного ящика: Done/Total в процентах, <see cref="ExpeditionObjective.Paused"/> если в зоне никого.</summary>
    Download,

    /// <summary>Квота добычи: сдано руды в приёмник.</summary>
    Quota,

    /// <summary>Сдано образцов в приёмник.</summary>
    Samples,

    /// <summary>Починено узлов разбитого шаттла.</summary>
    Repair,

    /// <summary>Оборона шаттла: Done/Total в секундах.</summary>
    Defend,
}

/// <summary>Узел разбитого эвакуационного шаттла: починен или нет (экран разбит / работает).</summary>
[Serializable, NetSerializable]
public enum ExpeditionRepairVisuals : byte
{
    Repaired,
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class ExpeditionObjective : IEquatable<ExpeditionObjective>
{
    [DataField] public ExpeditionObjectiveKind Kind;

    /// <summary>Название цели (имя сущности).</summary>
    [DataField] public string Name = string.Empty;

    [DataField] public int Total;

    [DataField] public int Done;

    /// <summary>Прогресс стоит (скачивание: в зоне ящика никого нет).</summary>
    [DataField] public bool Paused;

    public bool Finished => Total > 0 && Done >= Total;

    public bool Equals(ExpeditionObjective? other)
    {
        return other != null && Kind == other.Kind && Name == other.Name && Total == other.Total && Done == other.Done && Paused == other.Paused;
    }

    public override bool Equals(object? obj) => Equals(obj as ExpeditionObjective);

    public override int GetHashCode() => HashCode.Combine(Kind, Name, Total, Done, Paused);
}

/// <summary>
/// Зона чёрного ящика: пока идёт скачивание данных, клиент рисует вокруг ящика круг радиуса <see cref="Radius"/>.
/// Данные качаются, только пока в круге есть живой игрок.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExpeditionBlackBoxZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public float Radius = 5f;

    /// <summary>В зоне есть живой игрок (скачивание идёт).</summary>
    [DataField, AutoNetworkedField]
    public bool Occupied;
}

/// <summary>Чёрный ящик: ждёт активации (красный мигает), качает данные (оранжевый), данные скачаны (зелёный).</summary>
[Serializable, NetSerializable]
public enum ExpeditionBlackBoxVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ExpeditionBlackBoxState : byte
{
    Idle,
    Downloading,
    Done,
}

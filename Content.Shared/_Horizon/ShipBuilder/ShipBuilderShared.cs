using System.Numerics;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.ShipBuilder;

/// <summary>
/// Консоль верфи: при активации открывает окно строительства шаттла на отдельной карте.
/// Набор доступного для постройки задаётся в прототипе консоли.
/// </summary>
[RegisterComponent]
public sealed partial class ShipBuilderConsoleComponent : Component
{
    /// <summary>Тайлы, доступные всегда.</summary>
    [DataField]
    public List<ProtoId<ContentTileDefinition>> Tiles = new();

    /// <summary>Предметы и структуры, доступные всегда (как записи спавн-меню).
    /// Остальное открывается чертежами на сервере верфи.</summary>
    [DataField]
    public List<EntProtoId> Entities = new();

    // Рабочая область текущего пользователя (только сервер, не сохраняется и не синхронизируется).
    [ViewVariables]
    public EntityUid Map;

    [ViewVariables]
    public EntityUid Grid;

    [ViewVariables]
    public EntityUid Camera;

    [ViewVariables]
    public EntityUid? User;
}

/// <summary>
/// Сервер верфи (копия R&amp;D-сервера): хранит список разблокированных предметов.
/// Консоль использует сервер, находящийся на том же гриде.
/// </summary>
[RegisterComponent]
public sealed partial class ShipBuilderServerComponent : Component
{
    [DataField]
    public HashSet<EntProtoId> Unlocked = new();
}

/// <summary>
/// Чертёж: при применении к серверу верфи (или консоли) разблокирует перечисленные предметы.
/// </summary>
[RegisterComponent]
public sealed partial class ShipBuilderBlueprintComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Unlocks = new();
}

[Serializable, NetSerializable]
public enum ShipBuilderUiKey : byte
{
    Key,
}

public static class ShipBuilderPalette
{
    /// <summary>Максимальное удаление от центра карты (в тайлах).</summary>
    public const int MaxExtent = 48;

    /// <summary>Предел полёта камеры (в тайлах). Практически не ограничивает обзор.</summary>
    public const int MaxCameraExtent = 512;

    /// <summary>Максимальное число тайлов в одном шаттле.</summary>
    public const int MaxTiles = 1500;

    /// <summary>Максимальное число объектов на гриде.</summary>
    public const int MaxObjects = 3000;
}

public enum ShipBuilderTool : byte
{
    Tile,
    Entity,
    Erase,
}

[Serializable, NetSerializable]
public sealed class ShipBuilderBuiState(
    NetEntity? camera,
    int tiles,
    int objects,
    List<string> tileIds,
    List<string> entityIds,
    bool serverLinked) : BoundUserInterfaceState
{
    public readonly NetEntity? Camera = camera;
    public readonly int Tiles = tiles;
    public readonly int Objects = objects;

    /// <summary>Доступные тайлы.</summary>
    public readonly List<string> TileIds = tileIds;

    /// <summary>Доступные предметы: базовые из прототипа консоли и разблокированные на сервере.</summary>
    public readonly List<string> EntityIds = entityIds;

    public readonly bool ServerLinked = serverLinked;
}

/// <summary>Клик по карте. <see cref="World"/> - координаты на карте строительства.</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderPlaceMessage(Vector2 world, ShipBuilderTool tool, string id, int rotation) : BoundUserInterfaceMessage
{
    public readonly Vector2 World = world;
    public readonly ShipBuilderTool Tool = tool;
    public readonly string Id = id;

    /// <summary>Поворот в градусах, кратен 90.</summary>
    public readonly int Rotation = rotation;
}

/// <summary>Сдвиг камеры (в мировых единицах).</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderMoveCameraMessage(Vector2 delta) : BoundUserInterfaceMessage
{
    public readonly Vector2 Delta = delta;
}

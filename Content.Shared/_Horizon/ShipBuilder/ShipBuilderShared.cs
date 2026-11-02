using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.ShipBuilder;

/// <summary>
/// Консоль верфи: при активации открывает окно строительства шаттла на отдельной карте.
/// </summary>
[RegisterComponent]
public sealed partial class ShipBuilderConsoleComponent : Component
{
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

[Serializable, NetSerializable]
public enum ShipBuilderUiKey : byte
{
    Key,
}

/// <summary>
/// Наборы разрешённых для постройки тайлов и стен. Сервер проверяет выбор клиента по этим спискам.
/// </summary>
public static class ShipBuilderPalette
{
    public static readonly string[] Tiles =
    {
        "Plating",
        "FloorSteel",
        "FloorSteelDirty",
        "FloorDark",
        "FloorWhite",
        "FloorTechMaint",
        "FloorBlue",
    };

    public static readonly string[] Walls =
    {
        "WallSolid",
        "WallReinforced",
        "Window",
        "ReinforcedWindow",
    };

    /// <summary>Максимальное удаление от центра карты (в тайлах).</summary>
    public const int MaxExtent = 48;

    /// <summary>Предел полёта камеры (в тайлах). Практически не ограничивает обзор.</summary>
    public const int MaxCameraExtent = 512;

    /// <summary>Максимальное число тайлов в одном шаттле.</summary>
    public const int MaxTiles = 1500;
}

public enum ShipBuilderTool : byte
{
    Tile,
    Wall,
    Erase,
}

[Serializable, NetSerializable]
public sealed class ShipBuilderBuiState(NetEntity? camera, int tiles, int walls) : BoundUserInterfaceState
{
    public readonly NetEntity? Camera = camera;
    public readonly int Tiles = tiles;
    public readonly int Walls = walls;
}

/// <summary>Клик по карте. <see cref="World"/> - координаты на карте строительства.</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderPlaceMessage(Vector2 world, ShipBuilderTool tool, string id) : BoundUserInterfaceMessage
{
    public readonly Vector2 World = world;
    public readonly ShipBuilderTool Tool = tool;
    public readonly string Id = id;
}

/// <summary>Сдвиг камеры (в мировых единицах).</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderMoveCameraMessage(Vector2 delta) : BoundUserInterfaceMessage
{
    public readonly Vector2 Delta = delta;
}

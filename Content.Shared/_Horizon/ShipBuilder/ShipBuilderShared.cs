using System.Numerics;
using Content.Shared.Eui;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Horizon.ShipBuilder;

/// <summary>
/// Консоль верфи: при активации открывает окно строительства шаттла на отдельной карте.
/// </summary>
[RegisterComponent]
public sealed partial class ShipBuilderConsoleComponent : Component;

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
public sealed class ShipBuilderEuiState(NetEntity? camera, GameTick tick, int tiles, int walls) : EuiStateBase
{
    public readonly NetEntity? Camera = camera;

    /// <summary>Нужен, чтобы клиент дождался применения игрового состояния перед поиском камеры.</summary>
    public readonly GameTick Tick = tick;

    public readonly int Tiles = tiles;
    public readonly int Walls = walls;
}

/// <summary>Клик по карте. <see cref="World"/> - координаты на карте строительства.</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderPlaceMessage(Vector2 world, ShipBuilderTool tool, string id) : EuiMessageBase
{
    public readonly Vector2 World = world;
    public readonly ShipBuilderTool Tool = tool;
    public readonly string Id = id;
}

/// <summary>Сдвиг камеры (в мировых единицах).</summary>
[Serializable, NetSerializable]
public sealed class ShipBuilderMoveCameraMessage(Vector2 delta) : EuiMessageBase
{
    public readonly Vector2 Delta = delta;
}

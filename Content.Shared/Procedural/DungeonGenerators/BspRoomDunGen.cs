using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Generates rectangular rooms without any atlas: the area is recursively split in two (BSP) and every leaf
/// gets a randomly sized room, so rooms can never overlap. Only floors are placed, walls, doors, corridors and
/// contents come from the layers that run after it (Corridor, RoomEntrance, BoundaryWall, RoomFurnish, ...).
/// </summary>
public sealed partial class BspRoomDunGen : IDunGenLayer
{
    /// <summary>
    /// Total area (in tiles) that gets partitioned, centered on the dungeon position.
    /// </summary>
    [DataField]
    public Vector2i Size = new(64, 64);

    [DataField]
    public Vector2i MinRoom = new(5, 5);

    [DataField]
    public Vector2i MaxRoom = new(14, 12);

    /// <summary>
    /// Free tiles kept between a room and the edge of its leaf. Two neighbouring rooms end up at least
    /// 2 * Padding apart, which has to fit both room walls and a corridor.
    /// </summary>
    [DataField]
    public int Padding = 2;

    /// <summary>
    /// Chance to keep splitting a region that is already small enough for a room. Lower values give fewer, bigger rooms.
    /// </summary>
    [DataField]
    public float SplitChance = 0.6f;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile;
}

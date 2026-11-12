using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Generates rectangular rooms without any atlas: the area is recursively split in two (BSP) and every leaf becomes
/// one room that fills the whole leaf, so the area is packed with no wasted space. Neighbouring rooms are separated by
/// a single shared wall tile, and doors are cut straight through it following the BSP tree (every room is reachable,
/// no corridors needed). Only floors are placed; walls, door contents and furnishing come from the layers after it
/// (BoundaryWall, RoomEntrance, RoomFurnish, ...).
/// With <see cref="CorridorWidth"/> above 0 the rooms are instead separated by corridor bands carved along every split
/// (a station layout: main hallways with side branches), and each room gets a door onto a corridor.
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
    /// Chance to keep splitting a region that is already small enough for a room. Lower values give fewer, bigger rooms.
    /// </summary>
    [DataField]
    public float SplitChance = 0.6f;

    /// <summary>
    /// Width of the corridors that run along every BSP split, like the hallways of a space station with the rooms
    /// opening onto them. 0 = no corridors: neighbouring rooms share one wall tile and doors are cut through it.
    /// </summary>
    [DataField]
    public int CorridorWidth = 0;

    /// <summary>
    /// Chance that two neighbouring rooms which are not connected through the BSP tree also get a door between them
    /// (loops, so the complex is not a pure tree).
    /// </summary>
    [DataField]
    public float LoopChance = 0.25f;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile;
}

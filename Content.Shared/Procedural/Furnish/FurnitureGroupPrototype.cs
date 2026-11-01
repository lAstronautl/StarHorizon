using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.Furnish;

/// <summary>
/// Where in a room a <see cref="FurnitureGroupPrototype"/> anchors itself.
/// </summary>
public enum FurnishPlacement : byte
{
    /// <summary>
    /// Anchor tile touches a wall, the group faces away from it.
    /// </summary>
    Wall,

    /// <summary>
    /// Anchor tile touches two perpendicular walls.
    /// </summary>
    Corner,

    /// <summary>
    /// Anchor tile has no wall within one tile (including diagonals). Facing is random.
    /// </summary>
    Center,

    /// <summary>
    /// Wall placement, but only near a doorway (guards, door-side lockers, etc).
    /// </summary>
    DoorSide,

    /// <summary>
    /// Any free tile, random facing.
    /// </summary>
    Free,
}

/// <summary>
/// One entity inside a <see cref="FurnitureGroupPrototype"/>.
/// </summary>
[DataDefinition]
public sealed partial class FurnitureMember
{
    [DataField(required: true)]
    public EntProtoId Proto;

    /// <summary>
    /// Offset relative to the group's anchor tile in the group's local frame:
    /// X runs along the wall (to the right when you look into the room), Y runs away from the wall into the room.
    /// </summary>
    [DataField]
    public Vector2i Offset;

    /// <summary>
    /// Chance this member is part of the group at all. Rolled once per group placement.
    /// </summary>
    [DataField]
    public float Chance = 1f;

    /// <summary>
    /// Rotate the entity so its front (south of the sprite) faces the group's facing direction.
    /// </summary>
    [DataField]
    public bool Rotate = true;

    /// <summary>
    /// Blocking members occupy their tile: nothing else is placed there and the walkability check treats it as solid.
    /// Non-blocking ones (rugs, floor markings) only need to be inside the room.
    /// </summary>
    [DataField]
    public bool Blocking = true;

    /// <summary>
    /// Optional items that are spawned on top of this member (e.g. medkits on a table).
    /// This is how loot is tied to the thing that holds it instead of being scattered around.
    /// </summary>
    [DataField]
    public ProtoId<EntityTablePrototype>? OnTop;

    /// <summary>
    /// Mount on the wall tile instead of the floor (posters, APCs, wall cabinets).
    /// The position (usually offset 0,-1) must be an unused wall tile around the room.
    /// </summary>
    [DataField]
    public bool OnWall;
}

/// <summary>
/// A floor decal that belongs to a <see cref="FurnitureGroupPrototype"/> (hazard box around a bench,
/// loading marks under crates, ...). Uses the same local frame as <see cref="FurnitureMember.Offset"/>.
/// </summary>
[DataDefinition]
public sealed partial class FurnishGroupDecal
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public Vector2i Offset;

    [DataField]
    public Color? Tint;

    /// <summary>
    /// Rotate the decal so it follows the group's facing.
    /// </summary>
    [DataField]
    public bool Rotate = true;
}

/// <summary>
/// A small meaningful composition of entities (a weapon rack with an ammo crate, a bed with a locker, ...)
/// with a placement rule. The unit that <see cref="RoomArchetypePrototype"/> asks to be placed.
/// </summary>
[Prototype]
public sealed partial class FurnitureGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField]
    public FurnishPlacement Placement = FurnishPlacement.Wall;

    /// <summary>
    /// How many tiles in front of every blocking member must stay walkable.
    /// </summary>
    [DataField]
    public int Clearance = 1;

    [DataField(required: true)]
    public List<FurnitureMember> Members = new();

    /// <summary>
    /// Decals laid on the floor together with the group, outlining it.
    /// </summary>
    [DataField]
    public List<FurnishGroupDecal> Decals = new();
}

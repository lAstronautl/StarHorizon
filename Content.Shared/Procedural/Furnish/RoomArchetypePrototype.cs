using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.Furnish;

/// <summary>
/// Request for a number of <see cref="FurnitureGroupPrototype"/> instances inside a room.
/// </summary>
[DataDefinition]
public sealed partial class FurnishEntry
{
    [DataField(required: true)]
    public ProtoId<FurnitureGroupPrototype> Group;

    /// <summary>
    /// Minimum amount to place. Entries are placed in list order, so put the important ones first.
    /// </summary>
    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;

    /// <summary>
    /// If above 0, adds room tiles / PerTiles more instances so bigger rooms get proportionally more content.
    /// </summary>
    [DataField]
    public int PerTiles;

    /// <summary>
    /// Chance to place the entry at all when <see cref="Min"/> is 0.
    /// </summary>
    [DataField]
    public float Chance = 1f;
}

public enum FurnishDecalMode : byte
{
    /// <summary> Random decal from <see cref="FurnishDecalRule.Decals"/> on a fraction of the tiles (dirt, stains). </summary>
    Scatter,

    /// <summary> Every room tile gets a decal from <see cref="FurnishDecalRule.Decals"/> (floor patterns). </summary>
    Fill,

    /// <summary> Floor trim along the walls: <see cref="FurnishDecalRule.Cardinal"/> for one wall, <see cref="FurnishDecalRule.Corner"/> for two. </summary>
    Skirting,

    /// <summary> Marking in front of every doorway, picked by the side the door is on (<see cref="FurnishDecalRule.Cardinal"/>). </summary>
    DoorMarks,
}

/// <summary>
/// Floor decals applied to a whole room, so rooms get the dirt, trim and markings that atlas rooms had painted in.
/// </summary>
[DataDefinition]
public sealed partial class FurnishDecalRule
{
    [DataField]
    public FurnishDecalMode Mode = FurnishDecalMode.Scatter;

    /// <summary>
    /// Decal ids for Scatter / Fill. Repeat an id to make it more likely.
    /// </summary>
    [DataField]
    public List<string> Decals = new();

    /// <summary>
    /// Chance per tile for Scatter.
    /// </summary>
    [DataField]
    public float Chance = 0.3f;

    [DataField]
    public Color? Tint;

    [DataField]
    public bool RandomRotation;

    /// <summary>
    /// Mapping rules require every dirt decal to be cleanable, so players can mop it away.
    /// </summary>
    [DataField]
    public bool Cleanable;

    /// <summary>
    /// Skirting: decal for a wall on that side. DoorMarks: decal for a door on that side.
    /// </summary>
    [DataField]
    public Dictionary<DirectionFlag, string> Cardinal = new();

    /// <summary>
    /// Skirting: decal for a tile with walls on two adjacent sides.
    /// </summary>
    [DataField]
    public Dictionary<DirectionFlag, string> Corner = new();
}

/// <summary>
/// What kind of room this is: when it may be used and what it must contain.
/// </summary>
[Prototype]
public sealed partial class RoomArchetypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>
    /// Free-form tags (e.g. Objective) so mission setup can look up rooms by role.
    /// </summary>
    [DataField]
    public List<string> Tags = new();

    [DataField]
    public float Weight = 1f;

    [DataField]
    public int MinTiles = 0;

    [DataField]
    public int MaxTiles = int.MaxValue;

    [DataField]
    public int MinDoors = 0;

    [DataField]
    public int MaxDoors = int.MaxValue;

    /// <summary>
    /// 0 = room closest to the dungeon origin (where the ship lands), 1 = the farthest one.
    /// </summary>
    [DataField]
    public float MinDepth = 0f;

    [DataField]
    public float MaxDepth = 1f;

    /// <summary>
    /// Guaranteed amount of rooms with this archetype per dungeon (if suitable rooms exist).
    /// </summary>
    [DataField]
    public int MinPerDungeon = 0;

    [DataField]
    public int MaxPerDungeon = int.MaxValue;

    [DataField(required: true)]
    public List<FurnishEntry> Entries = new();

    /// <summary>
    /// Floor decals for the room.
    /// </summary>
    [DataField]
    public List<FurnishDecalRule> Decals = new();
}

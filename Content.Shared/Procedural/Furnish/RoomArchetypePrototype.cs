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
}

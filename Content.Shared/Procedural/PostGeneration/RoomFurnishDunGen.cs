using Content.Shared.Procedural.Furnish;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Gives every room of the dungeon a role (<see cref="RoomArchetypePrototype"/>) and furnishes it with
/// <see cref="FurnitureGroupPrototype"/>s that follow wall/corner/center rules and never block doorways.
/// Run it after walls and doors exist (after BoundaryWall / RoomEntrance).
/// </summary>
public sealed partial class RoomFurnishDunGen : IDunGenLayer
{
    /// <summary>
    /// Archetypes that may be assigned.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<RoomArchetypePrototype>> Archetypes = new();

    /// <summary>
    /// Used for rooms that no archetype fits. Rooms stay empty if null.
    /// </summary>
    [DataField]
    public ProtoId<RoomArchetypePrototype>? Fallback;

    /// <summary>
    /// Point (in grid tiles) used to measure room depth. In expeditions the ship lands near the grid origin.
    /// </summary>
    [DataField]
    public Vector2i Origin = Vector2i.Zero;
}

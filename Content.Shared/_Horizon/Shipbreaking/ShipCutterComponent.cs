using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// A battery powered cutter for shipbreaking. Clicking a wall cuts it loose so it can be moved with a gravity gun,
/// clicking the seam between two floor tiles cuts a slit along it (see <see cref="GridCutsComponent"/>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShipCutterComponent : Component
{
    /// <summary>
    /// What counts as a wall that can be cut loose.
    /// </summary>
    [DataField]
    public EntityWhitelist? WallWhitelist;

    [DataField]
    public float WallDelay = 4f;

    [DataField]
    public float TileDelay = 3f;

    /// <summary>
    /// Charge taken from the power cell for cutting a wall loose.
    /// </summary>
    [DataField]
    public float WallCharge = 40f;

    /// <summary>
    /// Charge taken from the power cell for one cut between tiles.
    /// </summary>
    [DataField]
    public float TileCharge = 25f;

    /// <summary>
    /// How close to a tile edge the click has to be to count as a click on the seam, in tiles.
    /// </summary>
    [DataField]
    public float EdgeTolerance = 0.15f;

    /// <summary>
    /// Played when a cut starts.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/welder.ogg");

    /// <summary>
    /// Played when a cut is done.
    /// </summary>
    [DataField]
    public SoundSpecifier? DoneSound = new SoundPathSpecifier("/Audio/Items/welder2.ogg");
}

/// <summary>
/// One thing to cut: a wall, or an edge of a tile on a grid.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ShipCutterTarget(NetEntity Entity, Vector2i Tile, CutEdges Edge)
{
    /// <summary>
    /// A wall is a target without an edge, the entity is the wall. For a seam the entity is the grid.
    /// </summary>
    public bool IsWall => Edge == CutEdges.None;

    public static ShipCutterTarget Wall(NetEntity wall) => new(wall, default, CutEdges.None);

    public static ShipCutterTarget Seam(NetEntity grid, Vector2i tile, CutEdges edge) => new(grid, tile, edge);
}

[Serializable, NetSerializable]
public sealed partial class ShipCutterDoAfterEvent : DoAfterEvent
{
    public ShipCutterTarget Cut;

    private ShipCutterDoAfterEvent()
    {
    }

    public ShipCutterDoAfterEvent(ShipCutterTarget cut)
    {
        Cut = cut;
    }

    public override DoAfterEvent Clone() => this;
}

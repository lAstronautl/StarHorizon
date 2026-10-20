using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// A cutter for shipbreaking. Using it on a wall cuts the wall loose so it can be moved with a gravity gun.
/// Using it on the seam between two floor tiles cuts a slit along it (see <see cref="GridCutsComponent"/>).
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
    /// Fuel taken from the cutter (it is a welder) for cutting a wall loose.
    /// </summary>
    [DataField]
    public float WallFuel = 3f;

    /// <summary>
    /// Fuel taken from the cutter (it is a welder) for one cut between tiles.
    /// </summary>
    [DataField]
    public float TileFuel = 2f;

    /// <summary>
    /// How close to a tile edge the click has to be to count as a click on the seam, in tiles.
    /// </summary>
    [DataField]
    public float EdgeTolerance = 0.15f;

    /// <summary>
    /// Played when the cutting starts. When it finishes the usual tool sound of the cutter plays.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/welder.ogg");
}

[Serializable, NetSerializable]
public sealed partial class ShipCutterWallDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ShipCutterTileDoAfterEvent : DoAfterEvent
{
    public NetEntity Grid;
    public Vector2i Tile;
    public CutEdges Edge;

    private ShipCutterTileDoAfterEvent()
    {
    }

    public ShipCutterTileDoAfterEvent(NetEntity grid, Vector2i tile, CutEdges edge)
    {
        Grid = grid;
        Tile = tile;
        Edge = edge;
    }

    public override DoAfterEvent Clone() => this;
}

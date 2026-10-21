using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// A breaching charge. Clicking the edge of a tile with it (also the edge of a tile with a wall on it) hangs it there;
/// after the fuse it cuts a line of seams along that edge, see <see cref="GridCutsComponent"/>. It does not break anything.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BreachChargeComponent : Component
{
    /// <summary>
    /// How many seams are cut in a row along the edge, centred on the charge.
    /// </summary>
    [DataField]
    public int Segments = 5;

    [DataField]
    public float PlaceDelay = 2f;

    /// <summary>
    /// How close to a tile edge the click has to be to pick that edge, in tiles. Half a tile always picks the nearest edge.
    /// </summary>
    [DataField]
    public float EdgeTolerance = 0.5f;

    /// <summary>
    /// What is left hanging on the edge.
    /// </summary>
    [DataField]
    public EntProtoId ArmedPrototype = "BreachChargeArmed";

    [DataField]
    public SoundSpecifier? PlaceSound = new SoundPathSpecifier("/Audio/Items/Handcuffs/rope_start.ogg");
}

/// <summary>
/// A charge that hangs on an edge and counts down.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ArmedBreachChargeComponent : Component
{
    /// <summary>
    /// The grid, the tile and the edge it hangs on. Only the server sets them.
    /// </summary>
    public EntityUid Grid;
    public Vector2i Tile;
    public CutEdges Edge;
    public int Segments = 5;

    [DataField]
    public TimeSpan FuseTime = TimeSpan.FromSeconds(10);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ExplodeAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBeep;

    [DataField]
    public SoundSpecifier? BeepSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier? ExplodeSound = new SoundPathSpecifier("/Audio/Effects/explosion1.ogg");
}

[Serializable, NetSerializable]
public sealed partial class BreachChargePlaceDoAfterEvent : DoAfterEvent
{
    public NetEntity Grid;
    public Vector2i Tile;
    public CutEdges Edge;

    private BreachChargePlaceDoAfterEvent()
    {
    }

    public BreachChargePlaceDoAfterEvent(NetEntity grid, Vector2i tile, CutEdges edge)
    {
        Grid = grid;
        Tile = tile;
        Edge = edge;
    }

    public override DoAfterEvent Clone() => this;
}

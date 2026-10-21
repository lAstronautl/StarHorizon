using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared._Horizon.Shipbreaking.Derelict;

/// <summary>
/// A derelict shuttle sold by the derelict yard. The yard takes <see cref="Price"/> from the station account;
/// what is made of the wreck by the shipbreaking machines pays back into the same account.
/// </summary>
[Prototype("derelictVessel")]
public sealed partial class DerelictVesselPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField(required: true)]
    public int Price;

    /// <summary>
    /// A grid file with a single shuttle grid.
    /// </summary>
    [DataField(required: true)]
    public ResPath ShuttlePath;

    /// <summary>
    /// How long the shuttle lasts after it was bought.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(30);
}

/// <summary>
/// The console of the derelict yard. Opens <see cref="DerelictYardUiKey.Key"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DerelictYardConsoleComponent : Component
{
    /// <summary>
    /// The wrecks the console offers.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<DerelictVesselPrototype>> Vessels = new();
}

/// <summary>
/// On the grid of a bought derelict (and on every piece cut off it). When the time is up all of these grids are deleted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class DerelictLeaseComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Expires;

    /// <summary>
    /// The pieces share one lease: warnings are sent once for all of them.
    /// </summary>
    /// <summary>
    /// The grid that was bought and the station to announce to. Pieces cut off it carry the same values.
    /// </summary>
    public EntityUid Group;
    public EntityUid? Station;

    [DataField]
    public bool Warned5;

    [DataField]
    public bool Warned1;
}

[Serializable, NetSerializable]
public enum DerelictYardUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class DerelictVesselEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Price;
    public TimeSpan Lifetime;
}

[Serializable, NetSerializable]
public sealed class DerelictYardState : BoundUserInterfaceState
{
    public List<DerelictVesselEntry> Vessels = new();

    /// <summary>
    /// The balance of the station account, null if the console has no station.
    /// </summary>
    public int? Balance;
}

/// <summary>
/// The client wants to buy a wreck.
/// </summary>
[Serializable, NetSerializable]
public sealed class DerelictPurchaseMessage : BoundUserInterfaceMessage
{
    public string Id;

    public DerelictPurchaseMessage(string id)
    {
        Id = id;
    }
}

/// <summary>
/// The client wants the preview of a wreck.
/// </summary>
[Serializable, NetSerializable]
public sealed class DerelictPreviewRequestMessage : BoundUserInterfaceMessage
{
    public string Id;

    public DerelictPreviewRequestMessage(string id)
    {
        Id = id;
    }
}

public enum DerelictPreviewKind : byte
{
    Floor,
    Window,
    Machine,
    Door,
    Wall,
}

/// <summary>
/// A view from above on the shuttle: one coloured cell per tile.
/// </summary>
[Serializable, NetSerializable]
public sealed class DerelictPreviewMessage : BoundUserInterfaceMessage
{
    public string Id;
    public Vector2i[] Tiles;
    public DerelictPreviewKind[] Kinds;

    public DerelictPreviewMessage(string id, Vector2i[] tiles, DerelictPreviewKind[] kinds)
    {
        Id = id;
        Tiles = tiles;
        Kinds = kinds;
    }
}

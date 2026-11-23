using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Horizon.Freight;

[DataDefinition]
public sealed partial class FreightManifestEntry
{
    /// <summary>Crate entity to spawn (e.g. a TradeGoods* crate).</summary>
    [DataField(required: true)]
    public EntProtoId Crate;

    [DataField]
    public int Count = 1;

    [DataField(required: true)]
    public ProtoId<FreightRecipientPoolPrototype> Pool;

    /// <summary>Optional credit price per crate (rate on the paper); overrides the recipient's Credits.</summary>
    [DataField]
    public int? Price;
}

/// <summary>
/// The paper slip: addressee, cargo list, pools of destinations and rates.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FreightManifestComponent : Component
{
    /// <summary>Localized or plain name of whoever issued the manifest.</summary>
    [DataField]
    public string Addressee = string.Empty;

    [DataField]
    public List<FreightManifestEntry> Entries = new();
}

/// <summary>
/// Marks a station or grid as a named freight destination.
/// </summary>
[RegisterComponent]
public sealed partial class FreightDestinationComponent : Component
{
    [DataField(required: true)]
    public ProtoId<FreightDestinationPrototype> Id;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class FreightConsoleComponent : Component
{
    public const string ManifestSlotId = "freight-console-manifestSlot";

    [DataField]
    public ItemSlot ManifestSlot = new();

    [DataField]
    public ProtoId<StackPrototype> CashType = "Credit";

    [DataField]
    public ProtoId<StackPrototype> TokenType = "TradeToken";

    [DataField]
    public float PalletDistance = 8f;

    /// <summary>Own destination of this console, used if neither the grid nor the station has FreightDestination.</summary>
    [DataField]
    public ProtoId<FreightDestinationPrototype>? Destination;

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}

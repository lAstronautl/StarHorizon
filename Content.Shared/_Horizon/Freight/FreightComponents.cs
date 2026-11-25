using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Horizon.Freight;

/// <summary>
/// The paper slip printed by a freight console. It has its own address: it must be inserted into a
/// freight console at <see cref="Source"/> to release the crates.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FreightManifestComponent : Component
{
    /// <summary>Loading point: where the manifest has to be handed in.</summary>
    [DataField]
    public ProtoId<FreightDestinationPrototype>? Source;

    [DataField]
    public string Addressee = string.Empty;

    [DataField]
    public List<FreightResolvedEntry> Entries = new();
}

/// <summary>
/// Marks a station or grid as a named freight address.
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

    /// <summary>Own address of this console, used if neither the grid nor the station defines one.</summary>
    [DataField]
    public ProtoId<FreightDestinationPrototype>? Destination;

    /// <summary>Paper prototype printed for a taken order.</summary>
    [DataField]
    public EntProtoId ManifestPrototype = "PaperFreightManifest";

    [DataField]
    public int OfferCount = 3;

    [DataField]
    public TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan PrintDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    // Runtime state (server only).
    [ViewVariables]
    public List<FreightOffer> Offers = new();

    [ViewVariables]
    public TimeSpan NextRefresh;

    [ViewVariables]
    public TimeSpan NextPrintTime;
}

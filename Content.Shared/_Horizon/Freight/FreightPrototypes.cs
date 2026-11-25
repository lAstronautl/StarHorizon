using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Freight;

/// <summary>
/// Named freight address (e.g. "Точка Продажи A", "Эдисон", "ДШ"). Ids mirror the NF trade destination ids
/// (CargoA..CargoH, Edison, ...) so depot letters assigned by the POI generator map onto them.
/// </summary>
[Prototype]
public sealed partial class FreightDestinationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localized name shown on the console and on manifests.</summary>
    [DataField(required: true)]
    public LocId Name;
}

/// <summary>
/// Pool of possible recipients for a crate. One recipient is picked (by weight) per crate when an order is offered.
/// Recipients whose destination does not exist in the sector (or equals the loading point) are skipped.
/// </summary>
[Prototype]
public sealed partial class FreightRecipientPoolPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<FreightRecipient> Recipients = new();
}

[DataDefinition]
public sealed partial class FreightRecipient
{
    [DataField]
    public float Weight = 1f;

    [DataField(required: true)]
    public ProtoId<FreightDestinationPrototype> Destination;

    /// <summary>Credits paid to the seller.</summary>
    [DataField]
    public int Credits;

    /// <summary>Trade tokens paid to the seller.</summary>
    [DataField]
    public int Tokens;

    /// <summary>Credits deposited into the ATM bank account of the delivery grid (if it has a StationBankATM).</summary>
    [DataField]
    public int BankPayout;
}

[DataDefinition]
public sealed partial class FreightOrderEntry
{
    [DataField(required: true)]
    public EntProtoId Crate;

    [DataField]
    public int Count = 1;

    [DataField(required: true)]
    public ProtoId<FreightRecipientPoolPrototype> Pool;
}

/// <summary>
/// A delivery order template. Offers shown on the freight console are generated from these.
/// </summary>
[Prototype]
public sealed partial class FreightOrderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField]
    public float Weight = 1f;

    /// <summary>Allowed loading points. Empty = any existing address other than the console's own.</summary>
    [DataField]
    public List<ProtoId<FreightDestinationPrototype>> Sources = new();

    [DataField(required: true)]
    public List<FreightOrderEntry> Entries = new();
}

/// <summary>
/// A crate group with its delivery address and rates already resolved. Lives on manifests and in offers.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class FreightResolvedEntry
{
    [DataField(required: true)]
    public EntProtoId Crate;

    [DataField]
    public int Count = 1;

    [DataField(required: true)]
    public ProtoId<FreightDestinationPrototype> Destination;

    /// <summary>Per crate.</summary>
    [DataField]
    public int Credits;

    [DataField]
    public int Tokens;

    [DataField]
    public int BankPayout;
}

/// <summary>An order offered by a console: where to load, what, and where to deliver.</summary>
[Serializable, NetSerializable]
public sealed class FreightOffer
{
    public string OrderId = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public ProtoId<FreightDestinationPrototype> Source;
    public List<FreightResolvedEntry> Entries = new();
}

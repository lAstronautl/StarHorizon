using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Horizon.Freight;

/// <summary>
/// Named delivery point (e.g. a deport). Matched against <see cref="FreightDestinationComponent"/>.
/// </summary>
[Prototype]
public sealed partial class FreightDestinationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localized name shown on the console.</summary>
    [DataField(required: true)]
    public LocId Name;
}

/// <summary>
/// Pool of possible addressees for a crate. One recipient is picked (by weight) per spawned crate;
/// it defines where the crate must be delivered and what/whom it pays.
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

    /// <summary>
    /// Where the crate must be sold. Null = any grid with a freight console.
    /// </summary>
    [DataField]
    public ProtoId<FreightDestinationPrototype>? Destination;

    /// <summary>Credits paid to the seller. Overridden by the manifest entry price if set.</summary>
    [DataField]
    public int Credits;

    /// <summary>Trade tokens paid to the seller.</summary>
    [DataField]
    public int Tokens;

    /// <summary>
    /// Credits deposited into the ATM bank account of the selling grid (if the grid has a StationBankATM).
    /// </summary>
    [DataField]
    public int BankPayout;
}

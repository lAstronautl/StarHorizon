using Content.Shared._Horizon.Freight;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.Freight;

/// <summary>Added at runtime to crates spawned from a manifest; stores where they go and what they pay.</summary>
[RegisterComponent]
public sealed partial class FreightCrateComponent : Component
{
    /// <summary>Null = any freight console grid.</summary>
    [DataField]
    public ProtoId<FreightDestinationPrototype>? Destination;

    [DataField]
    public int Credits;

    [DataField]
    public int Tokens;

    [DataField]
    public int BankPayout;
}

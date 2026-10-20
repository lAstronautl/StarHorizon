using Content.Shared._NF.Bank.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// A machine that buys loose entities that touch it (stuff coming off a conveyor, thrown or pulled in)
/// for their appraised price and pays the money into a sector bank account, deleting them afterwards.
/// Used for the shipbreaking processor (walls) and barge (furniture), differing only by <see cref="Whitelist"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScrapProcessorComponent : Component
{
    /// <summary>
    /// What may be processed. Entities that are not on the whitelist are left alone.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// What may never be processed, even if it is on the whitelist.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Multiplier applied to the appraised price of the entity.
    /// </summary>
    [DataField]
    public float PayoutMultiplier = 1f;

    /// <summary>
    /// The bank account that receives the money.
    /// </summary>
    [DataField]
    public SectorBankAccount Account = SectorBankAccount.Frontier;

    /// <summary>
    /// Played when something is processed.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;
}

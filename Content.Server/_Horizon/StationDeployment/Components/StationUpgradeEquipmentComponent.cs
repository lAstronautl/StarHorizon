using Robust.Shared.Audio;

namespace Content.Server._Horizon.StationDeployment.Components;

/// <summary>
/// Marks a piece of station upgrade equipment (bought via the station control console) that must be
/// activated with the station owner's ID card before it works, and only works while it stays on the
/// grid it was bought for.
/// </summary>
[RegisterComponent]
public sealed partial class StationUpgradeEquipmentComponent : Component
{
    /// <summary>
    /// The grid this equipment was purchased for - it only functions while parented to this grid.
    /// </summary>
    [DataField]
    public EntityUid? BoundGrid;

    [DataField]
    public bool Installed;

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg");

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");
}

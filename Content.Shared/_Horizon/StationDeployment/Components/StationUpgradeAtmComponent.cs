using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Horizon.StationDeployment.Components;

/// <summary>
/// A standalone ATM appliance purchasable as station upgrade equipment - reads/writes the same
/// per-station bank account as the station control console, once installed (see
/// StationUpgradeEquipmentComponent).
/// </summary>
[RegisterComponent]
public sealed partial class StationUpgradeAtmComponent : Component
{
    [DataField]
    public ProtoId<StackPrototype> CashType = "Credit";

    public static string CashSlotId = "station-upgrade-atm-cashSlot";

    [DataField]
    public ItemSlot CashSlot = new();

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}

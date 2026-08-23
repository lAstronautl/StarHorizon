using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Horizon.StationDeployment;

/// <summary>
/// Wires up the purchasable ATM's cash slot - mirrors StationControlConsoleCashSlotSystem. Shared so
/// the slot's backing container exists and syncs client-side too.
/// </summary>
public sealed class StationUpgradeAtmCashSlotSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationUpgradeAtmComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<StationUpgradeAtmComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(EntityUid uid, StationUpgradeAtmComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, StationUpgradeAtmComponent.CashSlotId, component.CashSlot);
    }

    private void OnComponentRemove(EntityUid uid, StationUpgradeAtmComponent component, ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(uid, component.CashSlot);
    }
}

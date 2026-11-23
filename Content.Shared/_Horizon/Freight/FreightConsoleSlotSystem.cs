using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Horizon.Freight;

/// <summary>Registers the manifest slot of the freight console (shared so the container exists on the client too).</summary>
public sealed class FreightConsoleSlotSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FreightConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<FreightConsoleComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, FreightConsoleComponent comp, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, FreightConsoleComponent.ManifestSlotId, comp.ManifestSlot);
    }

    private void OnRemove(EntityUid uid, FreightConsoleComponent comp, ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(uid, comp.ManifestSlot);
    }
}

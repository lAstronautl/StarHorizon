using Content.Client.Items;
using Content.Shared._Horizon.Shipbreaking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._Horizon.Shipbreaking;

/// <summary>
/// Client side of the ship tool: shows a ghost of the target under the cursor while the tool is held,
/// and makes the mode visible: the sprite of the item and the line under the hand follow it.
/// </summary>
public sealed class ShipCutterSystem : SharedShipCutterSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ShipCutterGhostOverlay(EntityManager));

        SubscribeLocalEvent<ShipCutterComponent, AfterAutoHandleStateEvent>(OnState);
        Subs.ItemStatus<ShipCutterComponent>(ent => new ShipToolStatusControl(ent));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ShipCutterGhostOverlay>();
    }

    // Also runs for the first state, so a tool picked up in either mode looks right.
    private void OnState(Entity<ShipCutterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        var look = ent.Comp.Mode == ShipToolMode.Cut ? ent.Comp.CutSprite : ent.Comp.WeldSprite;
        if (look != null && TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.LayerSetSprite((ent, sprite), 0, look);
    }
}

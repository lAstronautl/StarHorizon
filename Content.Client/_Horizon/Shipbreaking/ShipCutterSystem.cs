using Content.Shared._Horizon.Shipbreaking;
using Robust.Client.Graphics;

namespace Content.Client._Horizon.Shipbreaking;

/// <summary>
/// Client side of the ship cutter: shows a ghost of the cut under the cursor while the cutter is held.
/// </summary>
public sealed class ShipCutterSystem : SharedShipCutterSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ShipCutterGhostOverlay(EntityManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ShipCutterGhostOverlay>();
    }
}

using System.Numerics;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.GravityGun;

/// <summary>
/// Client side of the gravity gun: tells the server where the cursor is while something is held, and turns the
/// mouse wheel into holding the target further away or closer.
/// </summary>
public sealed class GravityGunSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private MapCoordinates _lastSent;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new GravityGunOverlay(EntityManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<GravityGunOverlay>();
    }

    private bool TryGetHeldGun(out EntityUid gun)
    {
        gun = default;

        if (_player.LocalEntity is not { } player ||
            !_hands.TryGetActiveItem(player, out var item) ||
            !TryComp<GravityGunComponent>(item, out var comp) ||
            comp.Held == null)
            return false;

        gun = item.Value;
        return true;
    }

    /// <summary>
    /// Called by the game viewport when the wheel turns. Returns whether the gun used it.
    /// </summary>
    public bool OnWheel(float delta)
    {
        if (delta == 0f || !TryGetHeldGun(out _))
            return false;

        RaiseNetworkEvent(new GravityGunDistanceEvent { Delta = delta });
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted || !TryGetHeldGun(out _))
            return;

        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return;

        var mapPos = _eye.PixelToMap(mouse);
        if (mapPos.MapId == MapId.Nullspace)
            return;

        // Only tell the server when the cursor actually moved.
        if (mapPos.MapId == _lastSent.MapId && (mapPos.Position - _lastSent.Position).LengthSquared() < 0.01f)
            return;

        _lastSent = mapPos;

        var coords = _mapManager.TryFindGridAt(mapPos, out var gridUid, out _)
            ? _transform.ToCoordinates(gridUid, mapPos)
            : _transform.ToCoordinates(_map.GetMap(mapPos.MapId), mapPos);

        RaiseNetworkEvent(new GravityGunAimEvent { Coordinates = GetNetCoordinates(coords) });
    }
}

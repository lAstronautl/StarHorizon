using Content.Shared._Horizon.GravityGun;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Horizon.GravityGun;

/// <summary>
/// Draws a line from a gravity gun to what it is holding.
/// </summary>
public sealed class GravityGunOverlay : Overlay
{
    private static readonly Color BeamColor = Color.Cyan.WithAlpha(0.8f);
    private static readonly Color HeavyColor = Color.Orange.WithAlpha(0.8f);

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;

    public GravityGunOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var query = _entManager.EntityQueryEnumerator<GravityGunComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gun, out var xform))
        {
            if (gun.Held is not { } held || xform.MapID != args.MapId || !_entManager.TryGetComponent(held, out TransformComponent? heldXform))
                continue;

            var from = _transform.GetWorldPosition(uid);
            var to = System.Numerics.Vector2.Transform(gun.HeldPoint, _transform.GetWorldMatrix(heldXform));
            args.WorldHandle.DrawLine(from, to, gun.UserIsMoved ? HeavyColor : BeamColor);
        }
    }
}

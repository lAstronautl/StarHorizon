using System.Numerics;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.GravityGun;

/// <summary>
/// The griddrag command for whoever holds a <see cref="GridDragGunComponent"/>: same requests as
/// <see cref="GridDraggingSystem"/>, but any holder may send them, and only for a grid near them.
/// </summary>
public sealed class GridDragGunSystem : SharedGridDragGunSystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GridDragRequestPosition>(OnRequestDrag);
        SubscribeNetworkEvent<GridDragVelocityRequest>(OnRequestVelocity);
    }

    /// <summary>
    /// Whether the sender holds the gun and the grid is within reach of them (a grid far from the user is not theirs
    /// to move, a client could otherwise take any grid anywhere).
    /// </summary>
    private bool CanDrag(EntitySessionEventArgs args, EntityUid grid, Vector2 point)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !_hands.TryGetActiveItem(user, out var item) ||
            !TryComp<GridDragGunComponent>(item, out var gun) ||
            !HasComp<PhysicsComponent>(grid) || !CanGrab(user, gun, grid))
            return false;

        var reach = gun.MaxDistance;
        var userPos = _transform.GetWorldPosition(user);
        return (point - userPos).Length() <= reach && (_transform.GetWorldPosition(grid) - userPos).Length() <= reach * 3f;
    }

    private void OnRequestVelocity(GridDragVelocityRequest ev, EntitySessionEventArgs args)
    {
        var grid = GetEntity(ev.Grid);
        if (!CanDrag(args, grid, _transform.GetWorldPosition(grid)))
            return;

        _physics.SetLinearVelocity(grid, ev.LinearVelocity);
        _physics.SetAngularVelocity(grid, 0f);
    }

    private void OnRequestDrag(GridDragRequestPosition msg, EntitySessionEventArgs args)
    {
        var grid = GetEntity(msg.Grid);
        if (!CanDrag(args, grid, msg.WorldPosition))
            return;

        _transform.SetWorldPosition(grid, msg.WorldPosition);
    }
}

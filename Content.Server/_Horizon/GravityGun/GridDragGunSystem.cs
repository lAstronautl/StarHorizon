using System.Numerics;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.GravityGun;

/// <summary>
/// The griddrag command for whoever holds a <see cref="GridDragGunComponent"/>: same requests as
/// <see cref="GridDraggingSystem"/>, but any holder may send them, and only for a grid near them.
/// </summary>
public sealed class GridDragGunSystem : SharedGridDragGunSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
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
    private bool CanDrag(EntitySessionEventArgs args, EntityUid grid, Vector2 point, out Entity<GridDragGunComponent> gun)
    {
        gun = default;

        if (args.SenderSession.AttachedEntity is not { } user ||
            !_hands.TryGetActiveItem(user, out var item) ||
            !TryComp<GridDragGunComponent>(item, out var comp) ||
            !HasComp<PhysicsComponent>(grid) || !CanGrab(user, comp, grid))
            return false;

        var reach = comp.MaxDistance;
        var userPos = _transform.GetWorldPosition(user);
        gun = (item.Value, comp);
        return (point - userPos).Length() <= reach && (_transform.GetWorldPosition(grid) - userPos).Length() <= reach * 3f;
    }

    /// <summary>
    /// The end of a throw: the grid goes on with the speed of the hand, and is not coloured any more.
    /// </summary>
    private void OnRequestVelocity(GridDragVelocityRequest ev, EntitySessionEventArgs args)
    {
        var grid = GetEntity(ev.Grid);
        if (!CanDrag(args, grid, _transform.GetWorldPosition(grid), out _))
            return;

        _physics.SetLinearVelocity(grid, ev.LinearVelocity);
        _physics.SetAngularVelocity(grid, 0f);
        RemComp<GridDraggedComponent>(grid);
    }

    /// <summary>
    /// The grid is pulled towards the requested place and does not jump to it: its speed moves a part of the way to
    /// the wanted one with each request, so it lags behind the cursor and carries on after it.
    /// </summary>
    private void OnRequestDrag(GridDragRequestPosition msg, EntitySessionEventArgs args)
    {
        var grid = GetEntity(msg.Grid);
        if (!CanDrag(args, grid, msg.WorldPosition, out var gun))
            return;

        var wanted = (msg.WorldPosition - _transform.GetWorldPosition(grid)) * gun.Comp.Gain;
        if (wanted.Length() > gun.Comp.MaxSpeed)
            wanted = Vector2.Normalize(wanted) * gun.Comp.MaxSpeed;

        var speed = _physics.GetMapLinearVelocity(grid);
        _physics.SetLinearVelocity(grid, speed + (wanted - speed) * gun.Comp.Blend);
        _physics.SetAngularVelocity(grid, 0f);

        var dragged = EnsureComp<GridDraggedComponent>(grid);
        if (dragged.Gun != gun.Owner)
        {
            dragged.Gun = gun.Owner;
            Dirty(grid, dragged);
        }

        dragged.LastRequest = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The gun lets go when nobody asks for the grid any more (dropped, or the key let go).
        var query = EntityQueryEnumerator<GridDraggedComponent>();
        while (query.MoveNext(out var uid, out var dragged))
        {
            if (_timing.CurTime - dragged.LastRequest > TimeSpan.FromSeconds(0.5))
                RemComp<GridDraggedComponent>(uid);
        }
    }
}

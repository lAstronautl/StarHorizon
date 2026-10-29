using System.Numerics;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Weapons.Misc;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GridDragRequestPosition>(OnRequestDrag);
        SubscribeNetworkEvent<GridDragVelocityRequest>(OnRequestVelocity);
        SubscribeLocalEvent<GridDraggedComponent, ComponentShutdown>(OnDraggedShutdown);
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
    /// Whether the grid, going on with this speed for the lookahead time, would have a tile over a tile of another grid
    /// anywhere on its way (not only at the end: a fast grid would jump over a thin one). A grid that is over another
    /// one already is let go on, so it can get out.
    /// Grids do not push each other out of the way hard enough to count on it, so a grid is never let into another.
    /// </summary>
    private bool WouldOverlap(EntityUid gridUid, Vector2 speed, float lookahead)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var xform = Transform(gridUid);
        var matrix = _transform.GetWorldMatrix(xform);
        var shift = speed * lookahead;
        var half = grid.TileSize * 0.48f;

        // Points of every tile in the grid, once: the centre, the corners and the middles of the edges.
        var points = new List<Vector2>();
        foreach (var tile in _maps.GetAllTiles(gridUid, grid))
        {
            var centre = new Vector2(tile.GridIndices.X + 0.5f, tile.GridIndices.Y + 0.5f) * grid.TileSize;
            foreach (var offset in Offsets)
            {
                points.Add(Vector2.Transform(centre + offset * half, matrix));
            }
        }

        if (OverlapsAt(gridUid, xform.MapID, points, Vector2.Zero))
            return false;

        // A step is shorter than a tile, so no tile of the other grid is skipped.
        var steps = Math.Max(1, (int) MathF.Ceiling(shift.Length() / 0.4f));
        for (var i = 1; i <= steps; i++)
        {
            if (OverlapsAt(gridUid, xform.MapID, points, shift * (i / (float) steps)))
                return true;
        }

        return false;
    }

    private bool OverlapsAt(EntityUid gridUid, MapId map, List<Vector2> points, Vector2 shift)
    {
        var others = new List<Entity<MapGridComponent>>();

        foreach (var local in points)
        {
            var point = local + shift;

            others.Clear();
            _mapManager.FindGridsIntersecting(map, Box2.CenteredAround(point, new Vector2(0.1f)), ref others);

            foreach (var other in others)
            {
                if (other.Owner == gridUid)
                    continue;

                var indices = _maps.WorldToTile(other.Owner, other.Comp, point);
                if (_maps.TryGetTile(other.Comp, indices, out var otherTile) && !otherTile.IsEmpty)
                    return true;
            }
        }

        return false;
    }

    private static readonly Vector2[] Offsets =
    {
        Vector2.Zero, new(1, 1), new(1, -1), new(-1, 1), new(-1, -1), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>
    /// The end of a throw: the grid goes on with the speed of the hand, and is not coloured any more.
    /// </summary>
    private void OnRequestVelocity(GridDragVelocityRequest ev, EntitySessionEventArgs args)
    {
        var grid = GetEntity(ev.Grid);
        if (!CanDrag(args, grid, _transform.GetWorldPosition(grid), out _))
            return;

        // A throw into another grid is no throw at all.
        var thrown = WouldOverlap(grid, ev.LinearVelocity, 1f) ? Vector2.Zero : ev.LinearVelocity;
        _physics.SetLinearVelocity(grid, thrown);
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

        // Somebody got on the grid, the user included: the gun lets go of it.
        if (IsHoldingGun(args, out var holder, out var held) && IsDragged(grid) && !CanGrab(holder, held.Comp, grid))
        {
            LetGo(grid);
            return;
        }

        if (!CanDrag(args, grid, msg.WorldPosition, out var gun))
            return;

        var wanted = (msg.WorldPosition - _transform.GetWorldPosition(grid)) * gun.Comp.Gain;
        if (wanted.Length() > gun.Comp.MaxSpeed)
            wanted = Vector2.Normalize(wanted) * gun.Comp.MaxSpeed;

        var speed = _physics.GetMapLinearVelocity(grid);
        var next = speed + (wanted - speed) * gun.Comp.Blend;

        // Where the pull would take the grid into another grid it stops instead, like against a wall.
        if (WouldOverlap(grid, next, 0.25f))
            next = Vector2.Zero;

        _physics.SetLinearVelocity(grid, next);
        _physics.SetAngularVelocity(grid, 0f);

        var dragged = EnsureComp<GridDraggedComponent>(grid);
        if (dragged.Gun != gun.Owner)
        {
            dragged.Gun = gun.Owner;
            Dirty(grid, dragged);

            // The same humming as the tether of the gun makes, for as long as the grid is held.
            _audio.Stop(dragged.Stream);
            if (TryComp<BaseForceGunComponent>(gun.Owner, out var force))
                dragged.Stream = _audio.PlayPvs(force.Sound, gun.Owner)?.Entity;
        }

        dragged.LastRequest = _timing.CurTime;
    }

    private void OnDraggedShutdown(Entity<GridDraggedComponent> ent, ref ComponentShutdown args)
    {
        _audio.Stop(ent.Comp.Stream);
    }

    private bool IsDragged(EntityUid grid) => HasComp<GridDraggedComponent>(grid);

    private bool IsHoldingGun(EntitySessionEventArgs args, out EntityUid user, out Entity<GridDragGunComponent> gun)
    {
        user = default;
        gun = default;

        if (args.SenderSession.AttachedEntity is not { } attached ||
            !_hands.TryGetActiveItem(attached, out var item) ||
            !TryComp<GridDragGunComponent>(item, out var comp))
            return false;

        user = attached;
        gun = (item.Value, comp);
        return true;
    }

    /// <summary>
    /// The gun lets go of the grid: it stops where it is and is not coloured any more.
    /// </summary>
    private void LetGo(EntityUid grid)
    {
        _physics.SetLinearVelocity(grid, Vector2.Zero);
        _physics.SetAngularVelocity(grid, 0f);
        RemComp<GridDraggedComponent>(grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GridDraggedComponent>();
        while (query.MoveNext(out var uid, out var dragged))
        {
            // Nobody asks for the grid any more (dropped, or the key let go).
            if (_timing.CurTime - dragged.LastRequest > TimeSpan.FromSeconds(0.5))
            {
                // It goes on with its speed, so it is not let drift into another grid either.
                if (WouldOverlap(uid, _physics.GetMapLinearVelocity(uid), 1f))
                    _physics.SetLinearVelocity(uid, Vector2.Zero);

                RemComp<GridDraggedComponent>(uid);
                continue;
            }

            // Whoever holds the gun, or anything alive, standing on the grid breaks the hold, even if no request comes.
            if (TryComp<GridDragGunComponent>(dragged.Gun, out var gun) &&
                !CanGrab(Transform(dragged.Gun).ParentUid, gun, uid))
                LetGo(uid);
        }
    }
}

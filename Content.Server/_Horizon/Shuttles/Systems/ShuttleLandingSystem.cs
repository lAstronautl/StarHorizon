using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared._Horizon.Shuttles.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shuttles.Systems;

public sealed class ShuttleLandingSystem : SharedShuttleLandingSystem
{
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const int GridBit = (int) CollisionGroup.MapGrid;
    private TimeSpan _nextMaskCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextMaskCheck)
            return;

        _nextMaskCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        // Grid fixtures are rebuilt when tiles change, which resets their masks, so keep re-applying.
        var shuttles = EntityQueryEnumerator<LandableShuttleComponent, FixturesComponent>();
        while (shuttles.MoveNext(out var uid, out _, out var fixtures))
            IgnoreGridCollision(uid, fixtures);

        var pads = EntityQueryEnumerator<LandingPadComponent, FixturesComponent>();
        while (pads.MoveNext(out var uid, out _, out var fixtures))
            IgnoreGridCollision(uid, fixtures);
    }

    /// <summary>
    /// Removes the grid-vs-grid bit from the collision mask of the grid fixtures so that tiles can overlap.
    /// Wall and other entity fixtures are untouched and keep colliding.
    /// </summary>
    private void IgnoreGridCollision(EntityUid uid, FixturesComponent fixtures)
    {
        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            if ((fixture.CollisionLayer & GridBit) == 0 || (fixture.CollisionMask & GridBit) == 0)
                continue;

            _physics.SetCollisionMask(uid, id, fixture, fixture.CollisionMask & ~GridBit, fixtures);
        }
    }

    /// <summary>
    /// Tries to land the shuttle on the landing pad grid it overlaps.
    /// </summary>
    public bool TryLand(Entity<LandableShuttleComponent?> shuttle, out string reason)
    {
        reason = string.Empty;

        if (!Resolve(shuttle, ref shuttle.Comp, false) ||
            !TryComp<MapGridComponent>(shuttle, out var shuttleGrid) ||
            !TryComp<PhysicsComponent>(shuttle, out var body))
        {
            reason = "Not a landable shuttle.";
            return false;
        }

        if (shuttle.Comp.State == LandState.Landed)
        {
            reason = "Already landed.";
            return false;
        }

        if (body.LinearVelocity.Length() > shuttle.Comp.MaxLandingSpeed)
        {
            reason = "Moving too fast.";
            return false;
        }

        var shuttleXform = Transform(shuttle);
        var shuttleMatrix = _transform.GetWorldMatrix(shuttleXform);
        var corners = new Box2Rotated(shuttleGrid.LocalAABB, Angle.Zero);

        var pads = EntityQueryEnumerator<LandingPadComponent, MapGridComponent, TransformComponent>();
        while (pads.MoveNext(out var padUid, out var pad, out var padGrid, out var padXform))
        {
            if (padUid == shuttle.Owner || padXform.MapID != shuttleXform.MapID)
                continue;

            var zone = pad.Zone ?? padGrid.LocalAABB;
            var toPad = _transform.GetInvWorldMatrix(padXform) * shuttleMatrix;

            // The whole shuttle must be inside the landing zone.
            var inside = true;
            foreach (var corner in new[] { corners.BottomLeft, corners.BottomRight, corners.TopLeft, corners.TopRight })
            {
                if (!zone.Contains(Vector2.Transform(corner, toPad)))
                {
                    inside = false;
                    break;
                }
            }

            if (!inside)
                continue;

            _physics.SetLinearVelocity(shuttle, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(shuttle, 0f, body: body);
            _shuttle.Disable(shuttle);

            shuttle.Comp.State = LandState.Landed;
            shuttle.Comp.Pad = padUid;
            Dirty(shuttle);
            return true;
        }

        reason = "Not inside a landing zone.";
        return false;
    }

    public bool TryTakeOff(Entity<LandableShuttleComponent?> shuttle)
    {
        if (!Resolve(shuttle, ref shuttle.Comp, false) || shuttle.Comp.State != LandState.Landed)
            return false;

        _shuttle.Enable(shuttle);
        shuttle.Comp.State = LandState.Flying;
        shuttle.Comp.Pad = null;
        Dirty(shuttle);
        return true;
    }
}
